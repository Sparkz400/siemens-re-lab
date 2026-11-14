using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TiaProject;

public static class PlatformIo
{
    public static ObjectExport LoadExport(string path) => ObjectExport.Read(path);

    public static string CanonicalElement(XElement e)
    {
        var clone = new XElement(e);
        foreach (var node in clone.DescendantNodes().OfType<XText>().ToList())
        {
            if (string.IsNullOrWhiteSpace(node.Value)) node.Remove();
        }
        return clone.ToString(SaveOptions.DisableFormatting);
    }

    public static string ObjectFingerprint(ExportedObjectInfo o)
    {
        var sb = new StringBuilder();
        sb.Append(o.ClassName).Append('|').Append(o.InstanceId).Append('|').Append(o.Guid).Append('|');
        foreach (var kv in o.Attributes.OrderBy(k => k.Key, StringComparer.Ordinal))
            sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
        foreach (var kv in o.CoreBlobs.OrderBy(k => k.Key, StringComparer.Ordinal))
            sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    public static string HashFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    public static string Safe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 120 ? name[..120] : name;
    }

    public static XElement? Body(ExportedObjectInfo o) => Body(o, null);

    public static XElement? Body(ExportedObjectInfo o, ObjectExport? export)
    {
        string? hex = null;
        if (o.CoreBlobs.TryGetValue("Data", out var direct)) hex = direct;
        if (hex is null && export is not null)
        {
            var link = o.Links.FirstOrDefault(l => l.TypeId == 2232342);
            if (link.InstanceId != 0)
            {
                var cu = export.Objects.FirstOrDefault(x => x.InstanceId == link.InstanceId);
                if (cu is not null && cu.CoreBlobs.TryGetValue("Data", out var cuHex)) hex = cuHex;
            }
        }
        if (hex is null) return null;
        var text = TextUtil.DecodeCoreBlob(hex);
        if (text is null) return null;
        try { return XDocument.Parse(text).Root; } catch { return null; }
    }

    public static string? Attr(ExportedObjectInfo o, string suffix)
    {
        foreach (var kv in o.Attributes)
            if (kv.Key.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    public static double? Number(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}

public sealed class GateFinding
{
    public string Severity { get; set; } = "info";
    public string Rule { get; set; } = "";
    public string Object { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Baseline { get; set; }
    public string? Candidate { get; set; }
}

public sealed class GateResult
{
    public string Baseline { get; set; } = "";
    public string Candidate { get; set; } = "";
    public string Verdict { get; set; } = "allow";
    public int High { get; set; }
    public int Medium { get; set; }
    public int Low { get; set; }
    public List<GateFinding> Findings { get; set; } = [];
}

public static class GateEngine
{
    public static GateResult Compare(ObjectExport baseline, ObjectExport candidate, XrefData? baselineXref = null, XrefData? candidateXref = null)
    {
        var result = new GateResult { Baseline = baseline.FilePath, Candidate = candidate.FilePath };
        var baseByInst = baseline.Objects.GroupBy(o => o.InstanceId).ToDictionary(g => g.Key, g => g.First());
        var candByInst = candidate.Objects.GroupBy(o => o.InstanceId).ToDictionary(g => g.Key, g => g.First());
        var baseBlocks = baseline.Objects.Where(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal))
            .GroupBy(o => o.Name ?? o.InstanceId.ToString()).ToDictionary(g => g.Key, g => g.First());
        var candBlocks = candidate.Objects.Where(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal))
            .GroupBy(o => o.Name ?? o.InstanceId.ToString()).ToDictionary(g => g.Key, g => g.First());

        foreach (var kv in baseBlocks)
        {
            if (!candBlocks.TryGetValue(kv.Key, out var cand))
            {
                Add(result, "high", "block-removed", kv.Key, "block was removed");
                continue;
            }
            var b = kv.Value;
            var bh = PlatformIo.ObjectFingerprint(b);
            var ch = PlatformIo.ObjectFingerprint(cand);
            if (bh != ch)
                Add(result, "medium", "block-changed", kv.Key, "block content changed", bh, ch);

            var baseBody = PlatformIo.Body(b, baseline);
            var candBody = PlatformIo.Body(cand, candidate);
            var bg = GateCounts(baseBody);
            var cg = GateCounts(candBody);
            if (bg.Contacts > cg.Contacts)
                Add(result, "high", "interlock-contact-drop", kv.Key,
                    $"LAD contact count dropped from {bg.Contacts} to {cg.Contacts}", bg.Contacts.ToString(), cg.Contacts.ToString());
            if (bg.Coils != cg.Coils)
                Add(result, "medium", "coil-count-changed", kv.Key,
                    $"LAD coil count changed from {bg.Coils} to {cg.Coils}", bg.Coils.ToString(), cg.Coils.ToString());

            var bs = InterfaceSections(b, baseByInst);
            var cs = InterfaceSections(cand, candByInst);
            foreach (var section in bs.Except(cs, StringComparer.OrdinalIgnoreCase))
                Add(result, "high", "interface-section-removed", kv.Key, $"interface section '{section}' removed");
        }
        foreach (var kv in candBlocks.Where(k => !baseBlocks.ContainsKey(k.Key)))
            Add(result, "medium", "block-added", kv.Key, "new block added");

        CompareTags(baseline, candidate, result);

        if (baselineXref is not null && candidateXref is not null)
            CompareXref(baselineXref, candidateXref, result);

        result.High = result.Findings.Count(f => f.Severity == "high");
        result.Medium = result.Findings.Count(f => f.Severity == "medium");
        result.Low = result.Findings.Count(f => f.Severity == "low");
        result.Verdict = result.High > 0 ? "block" : result.Medium > 0 ? "review" : "allow";
        return result;
    }

    private static void CompareTags(ObjectExport baseline, ObjectExport candidate, GateResult result)
    {
        var bt = baseline.Objects.Where(o => o.ClassName.EndsWith("EAMTZTagData", StringComparison.Ordinal))
            .GroupBy(o => o.Name ?? o.InstanceId.ToString()).ToDictionary(g => g.Key, g => g.First());
        var ct = candidate.Objects.Where(o => o.ClassName.EndsWith("EAMTZTagData", StringComparison.Ordinal))
            .GroupBy(o => o.Name ?? o.InstanceId.ToString()).ToDictionary(g => g.Key, g => g.First());
        foreach (var kv in bt)
        {
            if (!ct.TryGetValue(kv.Key, out var cand))
            {
                Add(result, "high", "tag-removed", kv.Key, "tag was removed");
                continue;
            }
            var bAddr = PlatformIo.Attr(kv.Value, "LogicalAddress");
            var cAddr = PlatformIo.Attr(cand, "LogicalAddress");
            if (!string.Equals(bAddr, cAddr, StringComparison.OrdinalIgnoreCase))
                Add(result, "medium", "tag-address-changed", kv.Key, $"tag address changed from {bAddr} to {cAddr}", bAddr, cAddr);

            foreach (var ba in kv.Value.Attributes)
            {
                var name = ba.Key.Split('.').Last();
                if (!IsLimitName(name)) continue;
                if (!cand.Attributes.TryGetValue(ba.Key, out var cVal)) continue;
                var bNum = PlatformIo.Number(ba.Value);
                var cNum = PlatformIo.Number(cVal);
                if (bNum is null || cNum is null) continue;
                var widened = name.Contains("Min", StringComparison.OrdinalIgnoreCase) || name.Contains("Low", StringComparison.OrdinalIgnoreCase)
                    ? cNum < bNum
                    : cNum > bNum;
                if (widened)
                    Add(result, "high", "tag-range-widened", kv.Key,
                        $"{name} widened from {ba.Value} to {cVal}", ba.Value, cVal);
            }
        }
        foreach (var kv in ct.Where(k => !bt.ContainsKey(k.Key)))
            Add(result, "medium", "tag-added", kv.Key, "new tag added");
    }

    private static bool IsLimitName(string name) =>
        name.Contains("Limit", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Bound", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Range", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Max", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Min", StringComparison.OrdinalIgnoreCase);

    private static void CompareXref(XrefData baseline, XrefData candidate, GateResult result)
    {
        static string Key(XrefRelation r) => $"{r.LeftType}/{r.RightType}/{r.LeftClassName}/{r.RightClassName}/{r.RelationKind}";
        var b = baseline.Relations.GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
        var c = candidate.Relations.GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
        foreach (var kv in b)
        {
            c.TryGetValue(kv.Key, out var count);
            if (count >= kv.Value) continue;
            var severity = kv.Key.Contains("uses", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("contains", StringComparison.OrdinalIgnoreCase)
                ? "high" : "medium";
            Add(result, severity, "xref-edge-removed", kv.Key, $"relation count dropped from {kv.Value} to {count}");
        }
    }

    private static (int Contacts, int Coils, int Parts) GateCounts(XElement? body)
    {
        if (body is null) return (0, 0, 0);
        int contacts = body.Descendants().Count(e => e.Name.LocalName == "Part"
            && string.Equals(e.Attribute("Gate")?.Value, "Contact", StringComparison.OrdinalIgnoreCase));
        int coils = body.Descendants().Count(e => e.Name.LocalName == "Part"
            && string.Equals(e.Attribute("Gate")?.Value, "Coil", StringComparison.OrdinalIgnoreCase));
        int parts = body.Descendants().Count(e => e.Name.LocalName == "Part");
        return (contacts, coils, parts);
    }

    private static List<string> InterfaceSections(ExportedObjectInfo block, Dictionary<long, ExportedObjectInfo> byInst)
    {
        var link = block.Links.FirstOrDefault(l => l.TypeId == 2233868);
        if (link.InstanceId == 0 || !byInst.TryGetValue(link.InstanceId, out var root)) return [];
        if (!root.CoreBlobs.TryGetValue("Payload", out var hex)) return [];
        var text = TextUtil.DecodeCoreBlob(hex);
        if (text is null) return [];
        try
        {
            var doc = XDocument.Parse(text);
            return doc.Root?.Elements().Where(e => e.Name.LocalName == "Member")
                .Select(e => e.Attribute("Name")?.Value)
                .Where(n => !string.IsNullOrEmpty(n)).Cast<string>().ToList() ?? [];
        }
        catch { return []; }
    }

    private static void Add(GateResult r, string severity, string rule, string obj, string message, string? b = null, string? c = null) =>
        r.Findings.Add(new GateFinding { Severity = severity, Rule = rule, Object = obj, Message = message, Baseline = b, Candidate = c });
}

public sealed class GateBisectResult
{
    public string Block { get; set; } = "";
    public string Key { get; set; } = "";
    public int? FirstChangedSavepoint { get; set; }
    public List<HistoryVersion> Versions { get; set; } = [];
}

public static class GateBisect
{
    public static GateBisectResult Bisect(ObjectExport export, HistoryResult history, string blockName)
    {
        var block = export.Objects.FirstOrDefault(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)
            && string.Equals(o.Name, blockName, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException($"block not found: {blockName}");
        var key = $"{2232321}/{block.InstanceId}";
        var versions = history.History.Where(v => v.Key == key).OrderBy(v => v.Index).ToList();
        int? first = null;
        for (int i = 1; i < versions.Count; i++)
        {
            if (versions[i].Guid != versions[i - 1].Guid || versions[i].Position != versions[i - 1].Position)
            {
                first = versions[i].Savepoint;
                break;
            }
        }
        return new GateBisectResult { Block = blockName, Key = key, FirstChangedSavepoint = first, Versions = versions };
    }
}

public sealed class MergeConflict
{
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public string BaseHash { get; set; } = "";
    public string OursHash { get; set; } = "";
    public string TheirsHash { get; set; } = "";
}

public sealed class MergeResult
{
    public string Base { get; set; } = "";
    public string Ours { get; set; } = "";
    public string Theirs { get; set; } = "";
    public string Output { get; set; } = "";
    public int Objects { get; set; }
    public int Conflicts { get; set; }
    public List<MergeConflict> ConflictList { get; set; } = [];
}

public static class SemanticMerge
{
    public static MergeResult Merge(string basePath, string oursPath, string theirsPath, string outputPath)
    {
        var baseDoc = XDocument.Load(basePath);
        var oursDoc = XDocument.Load(oursPath);
        var theirsDoc = XDocument.Load(theirsPath);
        var baseObjects = Objects(baseDoc);
        var oursObjects = Objects(oursDoc);
        var theirsObjects = Objects(theirsDoc);
        var result = new MergeResult { Base = Path.GetFullPath(basePath), Ours = Path.GetFullPath(oursPath), Theirs = Path.GetFullPath(theirsPath), Output = Path.GetFullPath(outputPath) };

        var merged = new List<XElement>();
        var conflicts = new List<MergeConflict>();
        var keys = baseObjects.Keys.Union(oursObjects.Keys).Union(theirsObjects.Keys).OrderBy(k => k, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            baseObjects.TryGetValue(key, out var b);
            oursObjects.TryGetValue(key, out var o);
            theirsObjects.TryGetValue(key, out var t);
            var bh = b is null ? "" : PlatformIo.CanonicalElement(b);
            var oh = o is null ? "" : PlatformIo.CanonicalElement(o);
            var th = t is null ? "" : PlatformIo.CanonicalElement(t);
            XElement? pick;
            if (oh == th) pick = o ?? t;
            else if (oh == bh) pick = t;
            else if (th == bh) pick = o;
            else
            {
                pick = b;
                conflicts.Add(new MergeConflict { Guid = key, Name = Name(b ?? o ?? t), BaseHash = Hash(bh), OursHash = Hash(oh), TheirsHash = Hash(th) });
            }
            if (pick is not null) merged.Add(new XElement(pick));
        }

        var outDoc = new XDocument(baseDoc);
        var objectsEl = outDoc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "objects")
            ?? throw new CliException("no <objects> element in base");
        objectsEl.RemoveNodes();
        foreach (var obj in merged) objectsEl.Add(obj);
        objectsEl.SetAttributeValue("objectCount", merged.Count);
        objectsEl.SetAttributeValue("lastInstanceId", merged.Select(InstId).DefaultIfEmpty(0).Max());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        outDoc.Save(outputPath);
        result.Objects = merged.Count;
        result.Conflicts = conflicts.Count;
        result.ConflictList = conflicts;
        return result;
    }

    private static Dictionary<string, XElement> Objects(XDocument doc)
    {
        var d = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        var objects = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "objects");
        if (objects is null) return d;
        foreach (var o in objects.Elements().Where(e => e.Name.LocalName == "StorageObject"))
        {
            var guid = o.Attribute("guid")?.Value;
            if (!string.IsNullOrEmpty(guid)) d[guid] = o;
        }
        return d;
    }

    private static string Name(XElement? e) =>
        e?.Descendants().FirstOrDefault(x => x.Name.LocalName == "attrib" && x.Attribute("name")?.Value == "Name")
            ?.Descendants().FirstOrDefault()?.Value ?? "";

    private static int InstId(XElement e) => int.TryParse(e.Attribute("instId")?.Value, out var v) ? v : 0;
    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}

public sealed class ForensicsResult
{
    public string Project { get; set; } = "";
    public string ExportPath { get; set; } = "";
    public int Savepoints { get; set; }
    public int Versions { get; set; }
    public List<HistorySavepoint> Timeline { get; set; } = [];
    public Dictionary<string, string> Evidence { get; set; } = [];
    public string? MarkdownFile { get; set; }
}

public static class Forensics
{
    public static ForensicsResult Build(string projectRoot, string exportPath, HistoryResult history, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var r = new ForensicsResult
        {
            Project = Path.GetFullPath(projectRoot),
            ExportPath = Path.GetFullPath(exportPath),
            Savepoints = history.Savepoints.Count,
            Versions = history.Versions,
            Timeline = history.Savepoints
        };
        foreach (var name in new[] { "System/PEData.plf", "System/PEData.idx", "XRef/XRef.db", "Vci/Vci.db" })
        {
            var p = Path.Combine(projectRoot, name.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(p)) r.Evidence[name] = PlatformIo.HashFile(p);
        }
        if (File.Exists(exportPath)) r.Evidence[Path.GetFileName(exportPath)] = PlatformIo.HashFile(exportPath);

        var md = new StringBuilder();
        md.AppendLine("# Forensic timeline");
        md.AppendLine();
        md.AppendLine($"Project: `{r.Project}`");
        md.AppendLine();
        md.AppendLine("| Savepoint | Commit GUID | Live | New | Changed | Deleted | Samples |");
        md.AppendLine("|---:|---|---:|---:|---:|---:|---|");
        foreach (var sp in r.Timeline)
            md.AppendLine($"| {sp.Index} | {sp.CommitGuid} | {sp.Live} | {sp.New} | {sp.Changed} | {sp.Deleted} | {string.Join("<br>", sp.Samples)} |");
        md.AppendLine();
        md.AppendLine("## Evidence hashes");
        md.AppendLine();
        foreach (var kv in r.Evidence) md.AppendLine($"- `{kv.Key}`: `{kv.Value}`");
        r.MarkdownFile = Path.Combine(outDir, "forensics.md");
        File.WriteAllText(r.MarkdownFile, md.ToString(), new UTF8Encoding(false));
        return r;
    }
}

public sealed class TraceSample
{
    public long T { get; set; }
    public string? Timestamp { get; set; }
    public int Savepoint { get; set; }
    public string? Source { get; set; }
    public Dictionary<string, double> Values { get; set; } = [];
}

public sealed class TraceAlarm
{
    public long T { get; set; }
    public string? Timestamp { get; set; }
    public int Savepoint { get; set; }
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "high";
    public string Message { get; set; } = "";
    public string? Tag { get; set; }
    public string? Source { get; set; }
}

public sealed class FlightTrace
{
    public string Schema { get; set; } = "tia-project/flight-trace/v1";
    public string Name { get; set; } = "flight";
    public string Project { get; set; } = "";
    public string? Source { get; set; }
    public string? RecordedAt { get; set; }
    public string? SamplesFile { get; set; }
    public string? AlarmsFile { get; set; }
    public string? SavepointTimesFile { get; set; }
    public string? IngestHash { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<TraceSample> Samples { get; set; } = [];
    public List<TraceAlarm> Alarms { get; set; } = [];
    public Dictionary<int, string> SavepointStates { get; set; } = [];
}

public sealed class FlightRule
{
    public string Name { get; set; } = "";
    public string Expr { get; set; } = "";
    public string Severity { get; set; } = "high";
}

public sealed class RuleSet
{
    public List<FlightRule> Rules { get; set; } = [];
}

public sealed class FlightViolation
{
    public long T { get; set; }
    public int Savepoint { get; set; } = -1;
    public string? StateId { get; set; }
    public string Rule { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Expr { get; set; } = "";
    public string Actual { get; set; } = "";
    public bool? TagPresent { get; set; }
    public string? Reason { get; set; }
}

public sealed class FlightReplayResult
{
    public string Trace { get; set; } = "";
    public string Rules { get; set; } = "";
    public string? Source { get; set; }
    public string? IngestHash { get; set; }
    public int Samples { get; set; }
    public int TimestampedSamples { get; set; }
    public int TimestampedAlarms { get; set; }
    public int Violations { get; set; }
    public int AttributedViolations { get; set; }
    public int StateMismatches { get; set; }
    public int UnknownSavepoints { get; set; }
    public Dictionary<int, string> SavepointStates { get; set; } = [];
    public List<TraceAlarm> Alarms { get; set; } = [];
    public List<FlightViolation> Findings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

public sealed class SavepointState
{
    public int Index { get; set; }
    public string StateId { get; set; } = "";
    public int Live { get; set; }
    public Dictionary<string, string> Objects { get; set; } = [];
    public List<string> Names { get; set; } = [];
}

public static class SavepointStateBuilder
{
    public static List<SavepointState> Build(HistoryResult history)
    {
        var states = new List<SavepointState>();
        var live = new Dictionary<string, HistoryVersion>(StringComparer.Ordinal);
        foreach (var sp in history.Savepoints.OrderBy(s => s.Index))
        {
            foreach (var v in history.History.Where(v => v.Savepoint == sp.Index).OrderBy(v => v.Index))
            {
                if (v.Deleted) live.Remove(v.Key);
                else live[v.Key] = v;
            }
            var objects = live.ToDictionary(
                kv => kv.Key,
                kv => $"{kv.Value.ClassName}|{kv.Value.Name}|{kv.Value.Guid}",
                StringComparer.Ordinal);
            var names = live.Values.Select(v => v.Name).Where(n => !string.IsNullOrEmpty(n)).Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
            var stateId = EquivalenceCertifier.Hash(string.Join("\n",
                objects.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value)));
            states.Add(new SavepointState
            {
                Index = sp.Index,
                StateId = stateId,
                Live = live.Count,
                Objects = objects,
                Names = names
            });
        }
        return states;
    }
}

public sealed class FlightSelfTestResult
{
    public bool Pass { get; set; }
    public List<string> Checks { get; set; } = [];
    public FlightTrace Trace { get; set; } = new();
    public FlightReplayResult Replay { get; set; } = new();
}

public static class FlightRecorder
{
    private static readonly Regex Expr = new(@"^\s*([A-Za-z_][\w.]*)\s*(>=|<=|==|!=|>|<)\s*(-?\d+(?:\.\d+)?)\s*$", RegexOptions.Compiled);

    public static FlightTrace Record(HistoryResult history, ObjectExport export, int samplesPerSavepoint = 1)
    {
        var states = SavepointStateBuilder.Build(history);
        var trace = new FlightTrace
        {
            Project = export.FilePath,
            Name = "savepoint-flight",
            SavepointStates = states.ToDictionary(s => s.Index, s => s.StateId)
        };
        var tags = export.Objects.Where(o => o.ClassName.EndsWith("EAMTZTagData", StringComparison.Ordinal))
            .Select(o => o.Name).Where(n => !string.IsNullOrEmpty(n)).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        foreach (var sp in states)
        {
            for (int i = 0; i < samplesPerSavepoint; i++)
            {
                var sample = new TraceSample { T = sp.Index * 100L + i, Savepoint = sp.Index };
                foreach (var tag in tags)
                    sample.Values[tag] = sp.Index * 3 + i;
                trace.Samples.Add(sample);
            }
            trace.Alarms.Add(new TraceAlarm
            {
                T = sp.Index * 100L + 50,
                Savepoint = sp.Index,
                Code = $"ALM-SP{sp.Index:D2}",
                Severity = sp.Index % 2 == 0 ? "medium" : "high",
                Message = $"savepoint {sp.Index} recorded alarm",
                Tag = tags.FirstOrDefault()
            });
        }
        return trace;
    }

    public static FlightTrace Record(ObjectExport export, int samples = 10)
    {
        // Legacy generated trace without savepoint linkage.
        var trace = new FlightTrace { Project = export.FilePath };
        var tags = export.Objects.Where(o => o.ClassName.EndsWith("EAMTZTagData", StringComparison.Ordinal))
            .Select(o => o.Name).Where(n => !string.IsNullOrEmpty(n)).Cast<string>().Distinct().Take(20).ToList();
        for (int i = 0; i < samples; i++)
        {
            var s = new TraceSample { T = i, Savepoint = 0 };
            foreach (var tag in tags) s.Values[tag] = i;
            trace.Samples.Add(s);
        }
        return trace;
    }

    public static FlightReplayResult Replay(FlightTrace trace, RuleSet rules, HistoryResult? history = null, ObjectExport? export = null)
    {
        var states = history is null ? [] : SavepointStateBuilder.Build(history);
        var stateByIndex = states.ToDictionary(s => s.Index);
        var result = new FlightReplayResult
        {
            Trace = trace.Name,
            Source = trace.Source,
            IngestHash = trace.IngestHash,
            Samples = trace.Samples.Count,
            TimestampedSamples = trace.Samples.Count(s => !string.IsNullOrEmpty(s.Timestamp)),
            TimestampedAlarms = trace.Alarms.Count(a => !string.IsNullOrEmpty(a.Timestamp)),
            SavepointStates = trace.SavepointStates,
            Alarms = trace.Alarms,
            Findings = []
        };
        foreach (var alarm in trace.Alarms)
        {
            if (!stateByIndex.TryGetValue(alarm.Savepoint, out var alarmState))
            {
                if (trace.SavepointStates.Count > 0)
                {
                    result.UnknownSavepoints++;
                    result.Errors.Add($"alarm {alarm.Code} references unknown savepoint {alarm.Savepoint} at {alarm.Timestamp ?? alarm.T.ToString()}");
                }
                continue;
            }
            if (trace.SavepointStates.TryGetValue(alarm.Savepoint, out var recordedAlarmState)
                && recordedAlarmState != alarmState.StateId)
            {
                result.StateMismatches++;
                result.Errors.Add($"alarm {alarm.Code} savepoint-state-mismatch: trace={recordedAlarmState} history={alarmState.StateId}");
            }
        }
        foreach (var sample in trace.Samples)
        {
            stateByIndex.TryGetValue(sample.Savepoint, out var state);
            if (state is null)
            {
                if (trace.SavepointStates.Count > 0)
                {
                    result.UnknownSavepoints++;
                    result.Errors.Add($"unknown savepoint {sample.Savepoint} at t={sample.T}");
                }
            }
            else if (trace.SavepointStates.TryGetValue(sample.Savepoint, out var recordedState)
                     && recordedState != state.StateId)
            {
                result.StateMismatches++;
                result.Errors.Add($"savepoint-state-mismatch at t={sample.T}: trace={recordedState} history={state.StateId}");
                result.Findings.Add(new FlightViolation
                {
                    T = sample.T,
                    Savepoint = sample.Savepoint,
                    StateId = state.StateId,
                    Rule = "savepoint-state",
                    Severity = "high",
                    Expr = "trace.state == history.state",
                    Actual = recordedState,
                    TagPresent = null,
                    Reason = "savepoint-state-mismatch"
                });
                continue;
            }
            foreach (var rule in rules.Rules)
            {
                var m = Expr.Match(rule.Expr);
                if (!m.Success) continue;
                var tag = m.Groups[1].Value;
                if (!sample.Values.TryGetValue(tag, out var actual)) continue;
                bool? present = state is null ? null : state.Names.Contains(tag, StringComparer.OrdinalIgnoreCase);
                if (present == false)
                {
                    result.Findings.Add(new FlightViolation
                    {
                        T = sample.T,
                        Savepoint = sample.Savepoint,
                        StateId = state?.StateId,
                        Rule = rule.Name,
                        Severity = "high",
                        Expr = rule.Expr,
                        Actual = actual.ToString(CultureInfo.InvariantCulture),
                        TagPresent = false,
                        Reason = "tag-not-present-at-savepoint"
                    });
                    continue;
                }
                var op = m.Groups[2].Value;
                var limit = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                var hit = op switch
                {
                    ">" => actual > limit,
                    "<" => actual < limit,
                    ">=" => actual >= limit,
                    "<=" => actual <= limit,
                    "==" => Math.Abs(actual - limit) < 1e-9,
                    "!=" => Math.Abs(actual - limit) >= 1e-9,
                    _ => false
                };
                if (hit)
                    result.Findings.Add(new FlightViolation
                    {
                        T = sample.T,
                        Savepoint = sample.Savepoint,
                        StateId = state?.StateId,
                        Rule = rule.Name,
                        Severity = rule.Severity,
                        Expr = rule.Expr,
                        Actual = actual.ToString(CultureInfo.InvariantCulture),
                        TagPresent = present,
                        Reason = "rule-hit"
                    });
            }
        }
        result.Violations = result.Findings.Count;
        result.AttributedViolations = result.Findings.Count(f => f.Savepoint >= 0 && !string.IsNullOrEmpty(f.StateId));
        return result;
    }

    public static FlightSelfTestResult SelfTest(HistoryResult history, ObjectExport export, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var trace = Record(history, export, 1);
        var rules = new RuleSet { Rules = [new FlightRule { Name = "tag1-high", Expr = "Tag_1 > 5", Severity = "high" }] };
        var replay = Replay(trace, rules, history, export);
        var expected = 5;
        var atExpected = replay.Findings.Where(f => f.Savepoint == expected).ToList();
        var state0 = replay.SavepointStates.GetValueOrDefault(0);
        var state5 = replay.SavepointStates.GetValueOrDefault(expected);
        var checks = new List<string>
        {
            $"samples={trace.Samples.Count}",
            $"alarms={trace.Alarms.Count}",
            $"violations={replay.Violations}",
            $"at-savepoint-{expected}={atExpected.Count}",
            $"attributed={replay.AttributedViolations}",
            $"state-0={state0}",
            $"state-{expected}={state5}",
            $"states-differ={state0 != state5}"
        };
        var pass = atExpected.Count > 0
                   && replay.AttributedViolations == replay.Violations
                   && state0 != state5
                   && trace.Alarms.Any(a => a.Savepoint == expected);
        var result = new FlightSelfTestResult { Pass = pass, Checks = checks, Trace = trace, Replay = replay };
        JsonUtil.Write(result, Path.Combine(outDir, "flightrec-test.json"));
        return result;
    }
}

public sealed class RetentionResult
{
    public int Savepoints { get; set; }
    public int Kept { get; set; }
    public int Removed { get; set; }
    public List<int> KeptSavepoints { get; set; } = [];
    public List<int> RemovedSavepoints { get; set; } = [];
    public List<string> Tombstones { get; set; } = [];
}

public static class Retention
{
    public static RetentionResult Prune(HistoryResult history, int keepLast, IEnumerable<string> validatedGuids)
    {
        var validated = new HashSet<string>(validatedGuids, StringComparer.OrdinalIgnoreCase);
        var keep = new HashSet<int> { 0 };
        foreach (var sp in history.Savepoints.OrderByDescending(s => s.Index).Take(Math.Max(1, keepLast))) keep.Add(sp.Index);
        foreach (var sp in history.Savepoints.Where(s => s.CommitGuid is not null && validated.Contains(s.CommitGuid.Value.ToString()))) keep.Add(sp.Index);

        var r = new RetentionResult { Savepoints = history.Savepoints.Count };
        foreach (var sp in history.Savepoints)
        {
            if (keep.Contains(sp.Index))
            {
                r.Kept++;
                r.KeptSavepoints.Add(sp.Index);
            }
            else
            {
                r.Removed++;
                r.RemovedSavepoints.Add(sp.Index);
                r.Tombstones.Add($"{sp.Index}:{sp.CommitGuid}");
            }
        }
        return r;
    }
}
