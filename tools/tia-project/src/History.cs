using System.Text;

namespace TiaProject;

public sealed class HistoryVersion
{
    public int Savepoint { get; set; }
    public int Index { get; set; }
    public int TypeId { get; set; }
    public long InstanceId { get; set; }
    public Guid Guid { get; set; }
    public int Position { get; set; }
    public int Size { get; set; }
    public byte Flags { get; set; }
    public bool Deleted { get; set; }
    public string? ClassName { get; set; }
    public string? Name { get; set; }
    public string Key => $"{TypeId}/{InstanceId}";
}

public sealed class HistorySavepoint
{
    public int Index { get; set; }
    public int CommitIndex { get; set; }
    public Guid? CommitGuid { get; set; }
    public long CommitInstanceId { get; set; }
    public int Live { get; set; }
    public int New { get; set; }
    public int Changed { get; set; }
    public int Deleted { get; set; }
    public List<string> Samples { get; set; } = [];
}

public sealed class HistoryResult
{
    public string ProjectPath { get; set; } = "";
    public string OutPath { get; set; } = "";
    public int Records { get; set; }
    public int Versions { get; set; }
    public int CommitPoints { get; set; }
    public List<HistorySavepoint> Savepoints { get; set; } = [];
    public List<HistoryVersion> History { get; set; } = [];
    public string? MarkdownFile { get; set; }
}

public static class HistoryBuilder
{
    public static HistoryResult Build(string projectRoot, string plfPath, string idxPath, string outDir,
        ObjectExport? export = null, bool extract = false)
    {
        Directory.CreateDirectory(outDir);
        var plf = PlfReader.Read(plfPath, verifyHashes: false);
        var idx = PlfReader.ReadIndex(idxPath);
        PlfReader.AttachIndex(plf, idx);
        if (export is not null) plf.EnrichWith(export.ByGuid);

        var result = new HistoryResult
        {
            ProjectPath = projectRoot,
            OutPath = Path.GetFullPath(outDir),
            Records = plf.Objects.Count,
            CommitPoints = idx.CommitPoints.Count
        };

        var events = new List<(int Index, PlfIndexEntry? Entry, PlfCommitPoint? Commit)>();
        foreach (var e in idx.Entries) events.Add((e.Index, e, null));
        foreach (var c in idx.CommitPoints) events.Add((c.Index, null, c));
        events.Sort((a, b) => a.Index.CompareTo(b.Index));

        var savepoints = new List<HistorySavepoint> { new() { Index = 0 } };
        int current = 0;
        foreach (var ev in events)
        {
            if (ev.Commit is not null)
            {
                current++;
                savepoints.Add(new HistorySavepoint
                {
                    Index = current,
                    CommitIndex = ev.Commit.Index,
                    CommitGuid = ev.Commit.Guid,
                    CommitInstanceId = ev.Commit.InstanceId
                });
                continue;
            }
            var e2 = ev.Entry!;
            var obj = plf.Objects.FirstOrDefault(o => o.Offset == e2.Position);
            var v = new HistoryVersion
            {
                Savepoint = current,
                Index = e2.Index,
                TypeId = e2.TypeId,
                InstanceId = e2.InstanceId,
                Guid = e2.Guid,
                Position = e2.Position,
                Size = obj?.Size ?? 0,
                Flags = e2.Flags,
                Deleted = e2.IsDeleted || e2.IsDestroyed || e2.Position < 0,
                ClassName = obj?.ClassName,
                Name = obj?.Name
            };
            result.History.Add(v);
        }

        var state = new Dictionary<string, HistoryVersion>(StringComparer.Ordinal);
        var snapshots = new List<Dictionary<string, HistoryVersion>>();
        foreach (var sp in savepoints)
        {
            foreach (var v in result.History.Where(v => v.Savepoint == sp.Index).OrderBy(v => v.Index))
            {
                if (v.Deleted) state.Remove(v.Key);
                else state[v.Key] = v;
            }
            snapshots.Add(new Dictionary<string, HistoryVersion>(state, StringComparer.Ordinal));
            sp.Live = state.Count;
            var prev = snapshots.Count >= 2 ? snapshots[^2] : new Dictionary<string, HistoryVersion>();
            foreach (var kv in state)
            {
                if (!prev.TryGetValue(kv.Key, out var old))
                {
                    sp.New++;
                    if (sp.Samples.Count < 12) sp.Samples.Add("+ " + (kv.Value.Name ?? kv.Key));
                }
                else if (old.Guid != kv.Value.Guid || old.Position != kv.Value.Position)
                {
                    sp.Changed++;
                    if (sp.Samples.Count < 12) sp.Samples.Add("~ " + (kv.Value.Name ?? kv.Key));
                }
            }
            foreach (var key in prev.Keys)
            {
                if (state.ContainsKey(key)) continue;
                sp.Deleted++;
                if (sp.Samples.Count < 12) sp.Samples.Add("- " + (prev[key].Name ?? key));
            }
        }
        result.Savepoints = savepoints;
        result.Versions = result.History.Count;

        if (extract)
        {
            var bytes = FileUtil.ReadAllBytesShared(plfPath);
            foreach (var v in result.History.Where(v => !v.Deleted && v.Size > 0))
            {
                var dir = Path.Combine(outDir, $"sp{v.Savepoint:D2}");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, SafeFileName($"{v.TypeId}-{v.InstanceId}-{v.Index}") + ".bin");
                var data = new byte[v.Size];
                Array.Copy(bytes, v.Position, data, 0, v.Size);
                File.WriteAllBytes(file, data);
            }
        }

        var md = BuildMarkdown(result);
        result.MarkdownFile = Path.Combine(outDir, "timeline.md");
        File.WriteAllText(result.MarkdownFile, md, new UTF8Encoding(false));
        return result;
    }

    private static string BuildMarkdown(HistoryResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Project history");
        sb.AppendLine();
        sb.AppendLine($"PLF records: {result.Records}  |  index versions: {result.Versions}  |  commit points: {result.CommitPoints}");
        sb.AppendLine();
        sb.AppendLine("| Savepoint | Commit | Live | New | Changed | Deleted | Samples |");
        sb.AppendLine("|---:|---|---:|---:|---:|---:|---|");
        foreach (var sp in result.Savepoints)
        {
            var commit = sp.CommitGuid?.ToString() ?? (sp.Index == 0 ? "initial" : "-");
            sb.AppendLine($"| {sp.Index} | {commit} | {sp.Live} | {sp.New} | {sp.Changed} | {sp.Deleted} | {string.Join("<br>", sp.Samples.Select(Escape))} |");
        }
        return sb.ToString();
    }

    private static string Escape(string s) => s.Replace("|", "\\|");
    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
