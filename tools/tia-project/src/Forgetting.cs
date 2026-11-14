using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TiaProject;

public sealed class HistoryState
{
    public int Savepoint { get; set; }
    public Dictionary<string, HistoryVersion> Versions { get; set; } = [];
}

public sealed class ForgettingTombstone
{
    public int Savepoint { get; set; }
    public string? CommitGuid { get; set; }
    public int Live { get; set; }
    public string StateId { get; set; } = "";
    public List<string> SampleObjects { get; set; } = [];
}

public sealed class ForgettingResult
{
    public string Source { get; set; } = "";
    public string Output { get; set; } = "";
    public bool Pass { get; set; }
    public int OriginalRecords { get; set; }
    public int OriginalSavepoints { get; set; }
    public int OriginalLive { get; set; }
    public int KeptSavepoints { get; set; }
    public int RemovedSavepoints { get; set; }
    public int NewRecords { get; set; }
    public int NewSavepoints { get; set; }
    public int NewLive { get; set; }
    public long NewPlfLength { get; set; }
    public long NewIdxLength { get; set; }
    public List<int> Kept { get; set; } = [];
    public List<int> Removed { get; set; } = [];
    public List<ForgettingTombstone> Tombstones { get; set; } = [];
    public List<string> Verification { get; set; } = [];
}

public static class HistoryStateBuilder
{
    public static List<HistoryState> Build(HistoryResult history)
    {
        var states = new List<HistoryState>();
        var live = new Dictionary<string, HistoryVersion>(StringComparer.Ordinal);
        foreach (var sp in history.Savepoints.OrderBy(s => s.Index))
        {
            foreach (var v in history.History.Where(v => v.Savepoint == sp.Index).OrderBy(v => v.Index))
            {
                if (v.Deleted) live.Remove(v.Key);
                else live[v.Key] = v;
            }
            states.Add(new HistoryState { Savepoint = sp.Index, Versions = new Dictionary<string, HistoryVersion>(live, StringComparer.Ordinal) });
        }
        return states;
    }
}

public static class Forgetting
{
    public static ForgettingResult Execute(ProjectPaths paths, string exportPath, string outRoot,
        int keepLast = 2, IEnumerable<string>? validatedGuids = null, bool fullCopy = false)
    {
        var export = ObjectExport.Read(exportPath);
        var history = HistoryBuilder.Build(paths.Root, paths.Plf, paths.Idx,
            Path.Combine(Path.GetTempPath(), "tia-project", "forget-history"), export, false);
        var states = HistoryStateBuilder.Build(history);
        var stateBySavepoint = states.ToDictionary(s => s.Savepoint);
        var prune = Retention.Prune(history, keepLast, validatedGuids ?? []);
        var kept = prune.KeptSavepoints.OrderBy(i => i).ToList();
        var removed = prune.RemovedSavepoints.OrderBy(i => i).ToList();

        var plfBytes = FileUtil.ReadAllBytesShared(paths.Plf);
        var idxBytes = FileUtil.ReadAllBytesShared(paths.Idx);
        var plf = PlfReader.Read(paths.Plf, verifyHashes: true);
        var idx = PlfReader.ReadIndex(paths.Idx);
        var entryByIndex = idx.Entries.ToDictionary(e => e.Index);
        var objByOffset = plf.Objects.ToDictionary(o => o.Offset);
        var commitByIndex = idx.CommitPoints.ToDictionary(c => c.Index);
        var commitPayloads = ReadCommitPayloads(idxBytes);

        var retainedVersions = states.Where(s => kept.Contains(s.Savepoint))
            .SelectMany(s => s.Versions.Values)
            .GroupBy(v => v.Position)
            .Select(g => g.OrderBy(v => v.Index).First())
            .OrderBy(v => v.Index)
            .ToList();

        var plfOut = new MemoryStream();
        plfOut.Write(new byte[98]);
        var newPosByOldPos = new Dictionary<int, int>();
        foreach (var v in retainedVersions)
        {
            if (!objByOffset.TryGetValue(v.Position, out var obj))
                throw new CliException($"retained object version at {v.Position} not found in PLF");
            var bytes = plfBytes.AsSpan(v.Position, obj.Size).ToArray();
            var newOffset = (int)plfOut.Position;
            plfOut.Write(bytes);
            plfOut.Write(SHA256.HashData(bytes));
            newPosByOldPos[v.Position] = newOffset;
        }

        var newCommitPos = new Dictionary<int, int>();
        foreach (var spIndex in kept.Where(i => i > 0))
        {
            var sp = history.Savepoints.First(s => s.Index == spIndex);
            if (sp.CommitIndex == 0 || !commitByIndex.TryGetValue(sp.CommitIndex, out var cp))
                throw new CliException($"commit point for savepoint {spIndex} not found");
            if (!objByOffset.TryGetValue(cp.Position, out var cpObj))
                throw new CliException($"commit record at {cp.Position} not found");
            var bytes = plfBytes.AsSpan(cp.Position, cpObj.Size).ToArray();
            var newOffset = (int)plfOut.Position;
            plfOut.Write(bytes);
            plfOut.Write(SHA256.HashData(bytes));
            newCommitPos[spIndex] = newOffset;
        }

        var plfHeader = plfBytes.AsSpan(0, 98).ToArray();
        var plfHeaderHash = SHA256.HashData(plfHeader.AsSpan(0, 65));
        plfHeaderHash.CopyTo(plfHeader, 65);
        plfHeader[97] = 0xFF;
        plfOut.Position = 0;
        plfOut.Write(plfHeader);
        var newPlfBytes = plfOut.ToArray();

        var idxOut = new MemoryStream();
        idxOut.Write(new byte[98]);
        foreach (var spIndex in kept)
        {
            if (spIndex > 0)
            {
                var sp = history.Savepoints.First(s => s.Index == spIndex);
                var cp = commitByIndex[sp.CommitIndex];
                var payload = commitPayloads[sp.CommitIndex];
                WriteIndexEntry(idxOut, 458772, cp.InstanceId, cp.Guid, newCommitPos[spIndex], cp.Cluster, 0);
                idxOut.Write(payload);
            }
            var state = stateBySavepoint[spIndex];
            foreach (var v in state.Versions.Values.OrderBy(v => v.Index))
            {
                var entry = entryByIndex[v.Index];
                WriteIndexEntry(idxOut, v.TypeId, v.InstanceId, v.Guid, newPosByOldPos[v.Position], entry.Cluster, entry.Flags);
            }
        }
        var idxHeader = idxBytes.AsSpan(0, 98).ToArray();
        var idxHeaderHash = SHA256.HashData(idxHeader.AsSpan(0, 65));
        idxHeaderHash.CopyTo(idxHeader, 65);
        idxHeader[97] = 0xFF;
        idxOut.Position = 0;
        idxOut.Write(idxHeader);
        var newIdxBytes = idxOut.ToArray();

        CopyProject(paths.Root, outRoot, fullCopy);
        Directory.CreateDirectory(Path.Combine(outRoot, "System"));
        File.WriteAllBytes(Path.Combine(outRoot, "System", "PEData.plf"), newPlfBytes);
        File.WriteAllBytes(Path.Combine(outRoot, "System", "PEData.idx"), newIdxBytes);

        var latestOriginal = states.Last();
        var tombstones = new List<ForgettingTombstone>();
        foreach (var spIndex in removed)
        {
            var sp = history.Savepoints.First(s => s.Index == spIndex);
            var state = stateBySavepoint[spIndex];
            tombstones.Add(new ForgettingTombstone
            {
                Savepoint = spIndex,
                CommitGuid = sp.CommitGuid?.ToString(),
                Live = state.Versions.Count,
                StateId = SavepointStateBuilder.Build(history).First(s => s.Index == spIndex).StateId,
                SampleObjects = state.Versions.Values.Select(v => v.Name ?? v.Key).Take(8).ToList()
            });
        }

        var result = new ForgettingResult
        {
            Source = Path.GetFullPath(paths.Root),
            Output = Path.GetFullPath(outRoot),
            OriginalRecords = plf.Objects.Count,
            OriginalSavepoints = history.Savepoints.Count,
            OriginalLive = latestOriginal.Versions.Count,
            KeptSavepoints = kept.Count,
            RemovedSavepoints = removed.Count,
            Kept = kept,
            Removed = removed,
            Tombstones = tombstones,
            NewPlfLength = newPlfBytes.Length,
            NewIdxLength = newIdxBytes.Length
        };

        var newPaths = ProjectPaths.Resolve(outRoot);
        var newPlf = PlfReader.Read(newPaths.Plf, verifyHashes: true);
        var newIdx = PlfReader.ReadIndex(newPaths.Idx);
        PlfReader.AttachIndex(newPlf, newIdx);
        var newHistory = HistoryBuilder.Build(outRoot, newPaths.Plf, newPaths.Idx,
            Path.Combine(Path.GetTempPath(), "tia-project", "forget-verify"), export, false);
        var newStates = HistoryStateBuilder.Build(newHistory);
        var latestNew = newStates.Last();

        result.NewRecords = newPlf.Objects.Count;
        result.NewSavepoints = newHistory.Savepoints.Count;
        result.NewLive = newPlf.LiveObjects.Count;

        void Check(string name, bool ok, string detail)
        {
            result.Verification.Add($"{name}: {(ok ? "PASS" : "FAIL")} {detail}");
            if (!ok) result.Pass = false;
        }
        result.Pass = true;
        Check("plf-hashes", newPlf.HashFailures == 0, $"{newPlf.HashFailures} failures");
        Check("idx-trailing", newIdx.TrailingBytes == 0, $"{newIdx.TrailingBytes} trailing bytes");
        Check("idx-guid", newPlf.IndexGuidMismatches == 0, $"{newPlf.IndexGuidMismatches} mismatches");
        Check("index-missing", newPlf.MissingIndexEntries.Count == 0, $"{newPlf.MissingIndexEntries.Count} missing");
        Check("savepoint-count", newHistory.Savepoints.Count == kept.Count, $"{newHistory.Savepoints.Count} vs kept {kept.Count}");
        Check("latest-live", newPlf.LiveObjects.Count == latestOriginal.Versions.Count, $"{newPlf.LiveObjects.Count} vs original {latestOriginal.Versions.Count}");
        Check("history-live", newHistory.Savepoints.Last().Live == latestOriginal.Versions.Count, $"{newHistory.Savepoints.Last().Live} vs original {latestOriginal.Versions.Count}");
        Check("latest-state-keys", latestNew.Versions.Keys.OrderBy(k => k).SequenceEqual(latestOriginal.Versions.Keys.OrderBy(k => k)), $"{latestNew.Versions.Count} keys");
        Check("index-entries", newIdx.Entries.Count == newHistory.History.Count, $"{newIdx.Entries.Count} entries, {newHistory.History.Count} versions");
        Check("new-savepoints", newHistory.Savepoints.Count == result.KeptSavepoints, $"{newHistory.Savepoints.Count} vs {result.KeptSavepoints}");

        Directory.CreateDirectory(Path.Combine(outRoot, "System", "Forgetting"));
        File.WriteAllText(Path.Combine(outRoot, "System", "Forgetting", "forgetting-manifest.json"),
            JsonUtil.Serialize(result), new UTF8Encoding(false));
        return result;
    }

    private static void WriteIndexEntry(Stream s, int type, long instance, Guid guid, int position, int cluster, byte flags)
    {
        Span<byte> buf = stackalloc byte[38];
        BitConverter.TryWriteBytes(buf[..4], type);
        BitConverter.TryWriteBytes(buf.Slice(4, 8), instance);
        guid.TryWriteBytes(buf.Slice(12, 16));
        buf[28] = 0;
        BitConverter.TryWriteBytes(buf.Slice(29, 4), position);
        BitConverter.TryWriteBytes(buf.Slice(33, 4), cluster);
        buf[37] = flags;
        s.Write(buf);
    }

    private static Dictionary<int, byte[]> ReadCommitPayloads(byte[] idx)
    {
        var result = new Dictionary<int, byte[]>();
        int offset = 98, index = 0;
        while (offset + 38 <= idx.Length)
        {
            int type = BitConverter.ToInt32(idx, offset);
            offset += 38;
            if (type == 458772)
            {
                int length = BitConverter.ToInt32(idx, offset);
                if (length < 4 || offset + length > idx.Length)
                    throw new CliException("invalid commit point payload length");
                var payload = new byte[length];
                Array.Copy(idx, offset, payload, 0, length);
                result[index] = payload;
                offset += length;
            }
            index++;
        }
        return result;
    }

    private static void CopyProject(string source, string dest, bool full)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            File.WriteAllBytes(Path.Combine(dest, Path.GetFileName(file)), FileUtil.ReadAllBytesShared(file));

        var dirs = full
            ? Directory.EnumerateDirectories(source).Where(d => !Path.GetFileName(d).Equals("Logs", StringComparison.OrdinalIgnoreCase)
                                                                 && !Path.GetFileName(d).Equals("TMP", StringComparison.OrdinalIgnoreCase))
            : Directory.EnumerateDirectories(source).Where(d => new[] { "System", "XRef", "Vci", "AdditionalFiles", "src", "UserFiles" }
                .Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase));
        foreach (var dir in dirs)
        {
            var name = Path.GetFileName(dir);
            if (name.Equals("System", StringComparison.OrdinalIgnoreCase))
                CopyDirectory(dir, Path.Combine(dest, name), skipPlf: true);
            else
                CopyDirectory(dir, Path.Combine(dest, name), skipPlf: false);
        }
    }

    private static void CopyDirectory(string source, string dest, bool skipPlf)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            if (skipPlf && (Path.GetFileName(file).Equals("PEData.plf", StringComparison.OrdinalIgnoreCase)
                            || Path.GetFileName(file).Equals("PEData.idx", StringComparison.OrdinalIgnoreCase)))
                continue;
            var dst = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllBytes(dst, FileUtil.ReadAllBytesShared(file));
        }
    }
}
