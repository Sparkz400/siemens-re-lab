using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace TiaProject;

public sealed class FormatSnapshot
{
    public string Created { get; set; } = DateTime.UtcNow.ToString("O");
    public string Project { get; set; } = "";
    public string? FileFormatVersion { get; set; }
    public int IdxEntrySize { get; set; } = 38;
    public int IdxEntries { get; set; }
    public int CommitPoints { get; set; }
    public Dictionary<string, int> PlfTypes { get; set; } = [];
    public string? XrefSchemaHash { get; set; }
    public Dictionary<string, long> XrefCounts { get; set; } = [];
    public string? ExportRoot { get; set; }
    public string? ExportVersion { get; set; }
    public Dictionary<string, string> FileHashes { get; set; } = [];
}

public sealed class DriftReport
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public string AProject { get; set; } = "";
    public string BProject { get; set; } = "";
    public string Severity { get; set; } = "none";
    public List<string> Breaking { get; set; } = [];
    public List<string> Additive { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}

public static class FormatDrift
{
    public static FormatSnapshot Snapshot(ProjectPaths paths, string exportPath)
    {
        var plf = PlfReader.Read(paths.Plf, verifyHashes: false);
        var idx = PlfReader.ReadIndex(paths.Idx);
        var export = ObjectExport.Read(exportPath);
        var snap = new FormatSnapshot
        {
            Project = paths.Root,
            FileFormatVersion = plf.Header.FileFormatVersion,
            IdxEntries = idx.Entries.Count,
            CommitPoints = idx.CommitPoints.Count,
            PlfTypes = plf.Objects.GroupBy(o => o.TypeId.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            ExportRoot = "exportdata",
            ExportVersion = export.FileVersion
        };
        snap.FileHashes["plf"] = PlatformIo.HashFile(paths.Plf);
        snap.FileHashes["idx"] = PlatformIo.HashFile(paths.Idx);
        snap.FileHashes["xref"] = PlatformIo.HashFile(paths.XrefDb);
        snap.FileHashes["export"] = PlatformIo.HashFile(exportPath);

        var temp = FileUtil.CopyToTemp(paths.XrefDb, Path.Combine(Path.GetTempPath(), "tia-project", "drift"));
        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = temp, Mode = SqliteOpenMode.ReadOnly }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();
            var schema = new StringBuilder();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT type,name,sql FROM sqlite_master WHERE type IN ('table','index') ORDER BY name";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    schema.Append(r.GetString(0)).Append('|').Append(r.GetString(1)).Append('|').Append(r.IsDBNull(2) ? "" : r.GetString(2)).Append('\n');
            }
            snap.XrefSchemaHash = EquivalenceCertifier.Hash(schema.ToString());
            foreach (var table in new[] { "parts", "objs", "rels", "addrs" })
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
                snap.XrefCounts[table] = Convert.ToInt64(cmd.ExecuteScalar());
            }
        }
        finally
        {
            FileUtil.TryDelete(temp);
            FileUtil.TryDeleteDirectory(Path.GetDirectoryName(temp)!);
        }
        return snap;
    }

    public static DriftReport Compare(FormatSnapshot a, FormatSnapshot b)
    {
        var r = new DriftReport { A = a.Created, B = b.Created, AProject = a.Project, BProject = b.Project };
        if (a.FileFormatVersion != b.FileFormatVersion)
            r.Breaking.Add($"PLF file format version changed: {a.FileFormatVersion} -> {b.FileFormatVersion}");
        if (a.IdxEntrySize != b.IdxEntrySize)
            r.Breaking.Add($"IDX entry size changed: {a.IdxEntrySize} -> {b.IdxEntrySize}");
        if (a.XrefSchemaHash != b.XrefSchemaHash)
            r.Breaking.Add("XRef SQLite schema changed");
        foreach (var kv in a.PlfTypes)
        {
            if (!b.PlfTypes.ContainsKey(kv.Key))
                r.Breaking.Add($"PLF object type removed: {kv.Key} ({kv.Value} records)");
        }
        foreach (var kv in b.PlfTypes)
        {
            if (!a.PlfTypes.ContainsKey(kv.Key))
                r.Additive.Add($"PLF object type added: {kv.Key} ({kv.Value} records)");
        }
        foreach (var table in new[] { "parts", "objs", "rels", "addrs" })
        {
            a.XrefCounts.TryGetValue(table, out var av);
            b.XrefCounts.TryGetValue(table, out var bv);
            if (av != bv) r.Notes.Add($"XRef {table} count: {av} -> {bv}");
        }
        if (a.ExportVersion != b.ExportVersion)
            r.Notes.Add($"FS2XML file version: {a.ExportVersion} -> {b.ExportVersion}");
        r.Severity = r.Breaking.Count > 0 ? "breaking" : r.Additive.Count > 0 ? "additive" : "none";
        return r;
    }
}

public sealed class CorpusExpected
{
    public int Records { get; set; }
    public int Live { get; set; }
    public int XrefParts { get; set; }
    public int XrefRelations { get; set; }
    public List<string> Blocks { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public List<string> Screens { get; set; } = [];
    public Dictionary<string, string> Hashes { get; set; } = [];
}

public sealed class CorpusManifest
{
    public string Name { get; set; } = "golden";
    public string Created { get; set; } = DateTime.UtcNow.ToString("O");
    public CorpusExpected Expected { get; set; } = new();
}

public sealed class CorpusVerifyResult
{
    public bool Pass { get; set; }
    public List<string> Checks { get; set; } = [];
    public List<string> Failures { get; set; } = [];
}

public static class GoldenCorpus
{
    public static CorpusManifest Build(ProjectPaths paths, string exportPath, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var plf = PlfReader.Read(paths.Plf, verifyHashes: false);
        PlfReader.AttachIndex(plf, PlfReader.ReadIndex(paths.Idx));
        var xref = XrefReader.Read(paths.XrefDb, plf);
        var export = ObjectExport.Read(exportPath);
        var manifest = new CorpusManifest
        {
            Name = Path.GetFileName(paths.Root),
            Expected = new CorpusExpected
            {
                Records = plf.Objects.Count,
                Live = plf.LiveObjects.Count,
                XrefParts = xref.Summary.PartCount,
                XrefRelations = xref.Summary.RelationCount,
                Blocks = export.Objects.Where(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal))
                    .Select(o => o.Name ?? "").Where(n => n.Length > 0).OrderBy(n => n).ToList(),
                Tags = export.Objects.Where(o => o.ClassName.EndsWith("EAMTZTagData", StringComparison.Ordinal))
                    .Select(o => o.Name ?? "").Where(n => n.Length > 0).OrderBy(n => n).ToList(),
                Screens = export.Objects.Where(o => o.ClassName.EndsWith("HmiScreenData", StringComparison.Ordinal))
                    .Select(o => o.Name ?? "").Where(n => n.Length > 0).OrderBy(n => n).ToList()
            }
        };
        manifest.Expected.Hashes["plf"] = PlatformIo.HashFile(paths.Plf);
        manifest.Expected.Hashes["idx"] = PlatformIo.HashFile(paths.Idx);
        manifest.Expected.Hashes["xref"] = PlatformIo.HashFile(paths.XrefDb);
        manifest.Expected.Hashes["export"] = PlatformIo.HashFile(exportPath);
        File.WriteAllText(Path.Combine(outDir, "manifest.json"), JsonUtil.Serialize(manifest), new UTF8Encoding(false));
        return manifest;
    }

    public static CorpusVerifyResult Verify(string corpusDir, ProjectPaths paths, string exportPath)
    {
        var manifest = JsonSerializer.Deserialize<CorpusManifest>(File.ReadAllText(Path.Combine(corpusDir, "manifest.json")), JsonUtil.Options)!;
        var actual = Build(paths, exportPath, Path.Combine(Path.GetTempPath(), "tia-project", "corpus-verify"));
        var r = new CorpusVerifyResult { Pass = true };
        void Check(string name, object? expected, object? got)
        {
            var ok = Equals(expected?.ToString(), got?.ToString());
            r.Checks.Add($"{name}: expected={expected} got={got} {(ok ? "OK" : "FAIL")}");
            if (!ok) { r.Pass = false; r.Failures.Add(name); }
        }
        Check("records", manifest.Expected.Records, actual.Expected.Records);
        Check("live", manifest.Expected.Live, actual.Expected.Live);
        Check("xref-parts", manifest.Expected.XrefParts, actual.Expected.XrefParts);
        Check("xref-relations", manifest.Expected.XrefRelations, actual.Expected.XrefRelations);
        Check("blocks", string.Join(",", manifest.Expected.Blocks), string.Join(",", actual.Expected.Blocks));
        Check("tags", string.Join(",", manifest.Expected.Tags), string.Join(",", actual.Expected.Tags));
        Check("screens", string.Join(",", manifest.Expected.Screens), string.Join(",", actual.Expected.Screens));
        foreach (var kv in manifest.Expected.Hashes)
        {
            actual.Expected.Hashes.TryGetValue(kv.Key, out var got);
            Check("hash-" + kv.Key, kv.Value, got);
        }
        return r;
    }

    public static List<string> Mutate(string exportPath, string outDir, int count)
    {
        Directory.CreateDirectory(outDir);
        var doc = XDocument.Load(exportPath);
        var files = new List<string>();
        var objects = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "objects");
        if (objects is null) throw new CliException("no <objects> in export");
        var candidates = objects.Elements().Where(e => e.Name.LocalName == "StorageObject").ToList();
        for (int i = 0; i < count; i++)
        {
            var clone = new XDocument(doc);
            var objs = clone.Root!.Elements().First(e => e.Name.LocalName == "objects");
            var list = objs.Elements().Where(e => e.Name.LocalName == "StorageObject").ToList();
            if (list.Count == 0) break;
            var target = list[Random.Shared.Next(list.Count)];
            var kind = i % 3;
            if (kind == 0)
            {
                var nameAttr = target.Descendants().FirstOrDefault(e => e.Name.LocalName == "attrib" && e.Attribute("name")?.Value == "Name");
                if (nameAttr?.Elements().FirstOrDefault() is { } val) val.Value += "_MUT" + i;
            }
            else if (kind == 1)
            {
                var addr = target.Descendants().FirstOrDefault(e => e.Name.LocalName == "attrib" && e.Attribute("name")?.Value == "LogicalAddress");
                if (addr?.Elements().FirstOrDefault() is { } val) val.Value = "%M9." + i;
            }
            else
            {
                target.Remove();
            }
            var file = Path.Combine(outDir, $"mutant-{i:D3}.xml");
            clone.Save(file);
            files.Add(file);
        }
        File.WriteAllText(Path.Combine(outDir, "mutants.json"), JsonUtil.Serialize(files.Select(f => new { file = f, hash = PlatformIo.HashFile(f) })), new UTF8Encoding(false));
        return files;
    }
}

public sealed class CorpusProjectSpec
{
    public string Name { get; set; } = "";
    public string Project { get; set; } = "";
    public string Export { get; set; } = "";
}

public sealed class CorpusSpec
{
    public List<CorpusProjectSpec> Projects { get; set; } = [];
}

public sealed class CorpusIndexEntry
{
    public string Name { get; set; } = "";
    public string Project { get; set; } = "";
    public string Export { get; set; } = "";
    public string Manifest { get; set; } = "";
    public int Records { get; set; }
    public int Live { get; set; }
    public int Blocks { get; set; }
    public int Tags { get; set; }
    public int Screens { get; set; }
    public int XrefParts { get; set; }
    public int XrefRelations { get; set; }
    public bool VerifyPass { get; set; }
}

public sealed class CorpusIndex
{
    public string Created { get; set; } = DateTime.UtcNow.ToString("O");
    public int Projects { get; set; }
    public long TotalRecords { get; set; }
    public long TotalLive { get; set; }
    public long TotalBlocks { get; set; }
    public long TotalTags { get; set; }
    public long TotalScreens { get; set; }
    public long TotalXrefParts { get; set; }
    public long TotalXrefRelations { get; set; }
    public bool Pass { get; set; }
    public List<CorpusIndexEntry> Entries { get; set; } = [];
}

public static class GoldenCorpusIndex
{
    public static CorpusIndex Build(CorpusSpec spec, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var index = new CorpusIndex();
        foreach (var project in spec.Projects)
        {
            var paths = ProjectPaths.Resolve(project.Project);
            var dir = Path.Combine(outDir, Safe(project.Name));
            var manifest = GoldenCorpus.Build(paths, project.Export, dir);
            var verify = GoldenCorpus.Verify(dir, paths, project.Export);
            index.Entries.Add(new CorpusIndexEntry
            {
                Name = project.Name,
                Project = paths.Root,
                Export = Path.GetFullPath(project.Export),
                Manifest = Path.Combine(dir, "manifest.json"),
                Records = manifest.Expected.Records,
                Live = manifest.Expected.Live,
                Blocks = manifest.Expected.Blocks.Count,
                Tags = manifest.Expected.Tags.Count,
                Screens = manifest.Expected.Screens.Count,
                XrefParts = manifest.Expected.XrefParts,
                XrefRelations = manifest.Expected.XrefRelations,
                VerifyPass = verify.Pass
            });
        }
        index.Projects = index.Entries.Count;
        index.TotalRecords = index.Entries.Sum(e => (long)e.Records);
        index.TotalLive = index.Entries.Sum(e => (long)e.Live);
        index.TotalBlocks = index.Entries.Sum(e => (long)e.Blocks);
        index.TotalTags = index.Entries.Sum(e => (long)e.Tags);
        index.TotalScreens = index.Entries.Sum(e => (long)e.Screens);
        index.TotalXrefParts = index.Entries.Sum(e => (long)e.XrefParts);
        index.TotalXrefRelations = index.Entries.Sum(e => (long)e.XrefRelations);
        index.Pass = index.Entries.Count >= 2 && index.Entries.All(e => e.VerifyPass);
        JsonUtil.Write(index, Path.Combine(outDir, "index.json"));
        return index;
    }

    private static string Safe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "project" : name;
    }
}
public sealed class FleetFailurePattern
{
    public string Pattern { get; set; } = "";
    public int Count { get; set; }
    public string? First { get; set; }
    public string? Last { get; set; }
    public List<string> Severities { get; set; } = [];
    public List<string> Tags { get; set; } = [];
}

public sealed class FleetRecord
{
    public string Name { get; set; } = "";
    public string ProjectHash { get; set; } = "";
    public string Created { get; set; } = DateTime.UtcNow.ToString("O");
    public int Blocks { get; set; }
    public int Tags { get; set; }
    public int Screens { get; set; }
    public int Devices { get; set; }
    public int HighFindings { get; set; }
    public string? FileFormatVersion { get; set; }
    public string? FailureSource { get; set; }
    public List<FleetFailurePattern> FailurePatterns { get; set; } = [];
}

public sealed class FleetPatternSummary
{
    public string Pattern { get; set; } = "";
    public int Plants { get; set; }
    public int Count { get; set; }
    public List<string> PlantNames { get; set; } = [];
    public List<string> Tags { get; set; } = [];
}

public sealed class FleetReport
{
    public int Records { get; set; }
    public double AvgBlocks { get; set; }
    public double AvgTags { get; set; }
    public double AvgScreens { get; set; }
    public int HighFindings { get; set; }
    public int TotalPatterns { get; set; }
    public int CrossPlantPatterns { get; set; }
    public int PlantsWithPatterns { get; set; }
    public Dictionary<string, int> ByFormat { get; set; } = [];
    public List<FleetPatternSummary> Patterns { get; set; } = [];
    public List<FleetProjectSummary> Projects { get; set; } = [];
}

public sealed class FleetProjectSummary
{
    public string Name { get; set; } = "";
    public string ProjectHash { get; set; } = "";
    public int Blocks { get; set; }
    public int Tags { get; set; }
    public int Screens { get; set; }
    public int Devices { get; set; }
    public int HighFindings { get; set; }
    public string? FileFormatVersion { get; set; }
    public int FailurePatterns { get; set; }
}

public static class FleetCorpus
{
    public static FleetRecord Record(ProjectPaths paths, string exportPath, string? patternsCsv = null)
    {
        var plf = PlfReader.Read(paths.Plf, verifyHashes: false);
        var export = ObjectExport.Read(exportPath);
        var gate = GateEngine.Compare(export, export);
        var patterns = patternsCsv is null ? [] : FleetPatternReader.Read(export, patternsCsv);
        return new FleetRecord
        {
            Name = new DirectoryInfo(paths.Root).Name,
            FailureSource = patternsCsv is null ? null : Path.GetFullPath(patternsCsv),
            FailurePatterns = patterns,
            ProjectHash = EquivalenceCertifier.Hash(paths.Root + "|" + PlatformIo.HashFile(paths.Ap21)),
            Blocks = export.Objects.Count(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)),
            Tags = export.Objects.Count(o => o.ClassName.EndsWith("EAMTZTagData", StringComparison.Ordinal)),
            Screens = export.Objects.Count(o => o.ClassName.EndsWith("HmiScreenData", StringComparison.Ordinal)),
            Devices = export.Objects.Count(o => o.ClassName.Contains("DeviceData", StringComparison.Ordinal)),
            HighFindings = gate.High,
            FileFormatVersion = plf.Header.FileFormatVersion
        };
    }

    public static FleetReport Report(string dir)
    {
        var records = Directory.GetFiles(dir, "*.fleet.json")
            .Select(f => JsonSerializer.Deserialize<FleetRecord>(File.ReadAllText(f), JsonUtil.Options)!)
            .ToList();
        var patterns = records
            .SelectMany(r => r.FailurePatterns.Select(p => (Record: r, Pattern: p)))
            .GroupBy(x => x.Pattern.Pattern, StringComparer.OrdinalIgnoreCase)
            .Select(g => new FleetPatternSummary
            {
                Pattern = g.Key,
                Plants = g.Select(x => x.Record.ProjectHash).Distinct().Count(),
                Count = g.Sum(x => x.Pattern.Count),
                PlantNames = g.Select(x => x.Record.Name).Where(n => !string.IsNullOrEmpty(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList(),
                Tags = g.SelectMany(x => x.Pattern.Tags).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(t => t).ToList()
            })
            .OrderByDescending(p => p.Plants).ThenByDescending(p => p.Count)
            .ToList();
        return new FleetReport
        {
            Records = records.Count,
            AvgBlocks = records.Count == 0 ? 0 : records.Average(r => r.Blocks),
            AvgTags = records.Count == 0 ? 0 : records.Average(r => r.Tags),
            AvgScreens = records.Count == 0 ? 0 : records.Average(r => r.Screens),
            HighFindings = records.Sum(r => r.HighFindings),
            ByFormat = records.GroupBy(r => r.FileFormatVersion ?? "?").ToDictionary(g => g.Key, g => g.Count()),
            Projects = records.Select(r => new FleetProjectSummary
            {
                Name = r.Name,
                ProjectHash = r.ProjectHash,
                Blocks = r.Blocks,
                Tags = r.Tags,
                Screens = r.Screens,
                Devices = r.Devices,
                HighFindings = r.HighFindings,
                FileFormatVersion = r.FileFormatVersion,
                FailurePatterns = r.FailurePatterns.Count
            }).ToList(),
            TotalPatterns = patterns.Count,
            CrossPlantPatterns = patterns.Count(p => p.Plants >= 2),
            PlantsWithPatterns = records.Count(r => r.FailurePatterns.Count > 0),
            Patterns = patterns
        };
    }
}

public static class FleetPatternReader
{
    public static List<FleetFailurePattern> Read(ObjectExport export, string csvPath)
    {
        var rows = RecordedCsv.Read(csvPath);
        if (rows.Count < 2) throw new CliException("failure pattern CSV has no data rows");
        var header = rows[0].Select(h => h.Trim()).ToArray();
        string[] required = ["timestamp", "code", "severity", "message", "tag", "pattern"];
        foreach (var name in required)
            if (!header.Any(h => h.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new CliException("failure pattern CSV missing column: " + name);
        int Col(string name) => Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        var knownTags = export.Objects
            .Where(o => o.ClassName.EndsWith("EAMTZTagData", StringComparison.Ordinal))
            .Select(o => o.Name).Where(n => !string.IsNullOrEmpty(n)).Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var byPattern = new Dictionary<string, List<(DateTimeOffset Time, string Code, string Severity, string Tag)>>(StringComparer.OrdinalIgnoreCase);
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Length != header.Length)
                throw new CliException($"failure pattern row {r + 1} has {row.Length} columns, expected {header.Length}");
            var timestampText = row[Col("timestamp")];
            DateTimeOffset timestamp;
            try { timestamp = RecordedCsv.ParseIso(timestampText); }
            catch (FormatException) { throw new CliException($"invalid failure timestamp at row {r + 1}: {timestampText}"); }
            var code = row[Col("code")].Trim();
            var severity = row[Col("severity")].Trim().ToLowerInvariant();
            var message = row[Col("message")].Trim();
            var tag = row[Col("tag")].Trim();
            var pattern = row[Col("pattern")].Trim();
            if (code.Length == 0 || message.Length == 0)
                throw new CliException($"failure pattern row {r + 1} has an empty code or message");
            if (pattern.Length == 0)
                throw new CliException($"failure pattern row {r + 1} has an empty pattern");
            if (severity is not ("low" or "medium" or "high" or "critical"))
                throw new CliException($"failure pattern row {r + 1} has invalid severity: {severity}");
            if (tag.Length > 0 && !knownTags.Contains(tag))
                throw new CliException($"failure pattern row {r + 1} references unknown tag: {tag}");
            if (!byPattern.TryGetValue(pattern, out var entries))
                byPattern[pattern] = entries = [];
            entries.Add((timestamp, code, severity, tag));
        }

        return byPattern.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv =>
        {
            var ordered = kv.Value.OrderBy(e => e.Time).ToList();
            return new FleetFailurePattern
            {
                Pattern = kv.Key,
                Count = ordered.Count,
                First = RecordedCsv.Iso(ordered.First().Time),
                Last = RecordedCsv.Iso(ordered.Last().Time),
                Severities = ordered.Select(e => e.Severity).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s).ToList(),
                Tags = ordered.Select(e => e.Tag).Where(t => t.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList()
            };
        }).ToList();
    }
}

public sealed class FleetSpecProject
{
    public string Name { get; set; } = "";
    public string Project { get; set; } = "";
    public string Export { get; set; } = "";
    public string? Patterns { get; set; }
}

public sealed class FleetSpec
{
    public List<FleetSpecProject> Projects { get; set; } = [];
}

public static class FleetAggregate
{
    public static FleetReport Run(FleetSpec spec, string outDir)
    {
        Directory.CreateDirectory(outDir);
        foreach (var project in spec.Projects)
        {
            var paths = ProjectPaths.Resolve(project.Project);
            var record = FleetCorpus.Record(paths, project.Export, project.Patterns);
            if (!string.IsNullOrWhiteSpace(project.Name)) record.Name = project.Name;
            var safe = project.Name;
            foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            JsonUtil.Write(record, Path.Combine(outDir, (string.IsNullOrWhiteSpace(safe) ? "project" : safe) + ".fleet.json"));
        }
        var report = FleetCorpus.Report(outDir);
        JsonUtil.Write(report, Path.Combine(outDir, "report.json"));
        return report;
    }
}

public sealed class FleetPatternSelfTestResult
{
    public bool Pass { get; set; }
    public FleetReport Report { get; set; } = new();
    public List<string> Checks { get; set; } = [];
    public List<string> NegativeChecks { get; set; } = [];
}

public static class FleetPatternSelfTest
{
    public static FleetPatternSelfTestResult Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var dir = Path.Combine(outDir, "records");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        void Add(string name, params (string Pattern, int Count, string Tag)[] patterns)
        {
            var record = new FleetRecord
            {
                Name = name,
                ProjectHash = IrExporter.Hash(name),
                FailurePatterns = patterns.Select(p => new FleetFailurePattern
                {
                    Pattern = p.Pattern,
                    Count = p.Count,
                    Tags = [p.Tag]
                }).ToList()
            };
            JsonUtil.Write(record, Path.Combine(dir, name + ".fleet.json"));
        }
        Add("PlantA", ("high-level", 2, "Tag_1"), ("overload", 1, "Tag_2"));
        Add("PlantB", ("overload", 3, "Fault_B"), ("interlock-open", 1, "Stop_B"));
        Add("PlantC", ("high-level", 1, "Level_High"), ("overload", 2, "Pump_Fault"), ("interlock-open", 1, "Valve_Open"));
        Add("PlantD", ("overheat", 2, "Temp_High"), ("overload", 1, "Fan_Trip"), ("interlock-open", 2, "Door_Open"));
        var report = FleetCorpus.Report(dir);
        JsonUtil.Write(report, Path.Combine(outDir, "synthetic-report.json"));

        var checks = new List<string>
        {
            $"records={report.Records}",
            $"patterns={report.TotalPatterns}",
            $"crossPlant={report.CrossPlantPatterns}",
            $"plantsWithPatterns={report.PlantsWithPatterns}",
            $"overload.plants={report.Patterns.FirstOrDefault(p => p.Pattern == "overload")?.Plants}",
            $"overload.count={report.Patterns.FirstOrDefault(p => p.Pattern == "overload")?.Count}",
            $"high-level.plants={report.Patterns.FirstOrDefault(p => p.Pattern == "high-level")?.Plants}",
            $"overheat.plants={report.Patterns.FirstOrDefault(p => p.Pattern == "overheat")?.Plants}"
        };
        var overload = report.Patterns.FirstOrDefault(p => p.Pattern == "overload");
        var high = report.Patterns.FirstOrDefault(p => p.Pattern == "high-level");
        var overheat = report.Patterns.FirstOrDefault(p => p.Pattern == "overheat");

        var negative = new List<string>();
        var export = new ObjectExport { FilePath = "synthetic" };
        export.Objects.Add(new ExportedObjectInfo
        {
            ClassName = "Siemens.Automation.DomainModel.EAMTZTagData",
            Name = "Tag_1"
        });
        void ExpectFailure(string name, string csv, string expected)
        {
            var path = Path.Combine(outDir, "negative-" + name + ".csv");
            File.WriteAllText(path, csv);
            try
            {
                FleetPatternReader.Read(export, path);
                negative.Add($"{name}=false expected={expected}");
            }
            catch (CliException ex)
            {
                negative.Add($"{name}={ex.Message.Contains(expected, StringComparison.OrdinalIgnoreCase)} expected={expected} actual={ex.Message}");
            }
        }
        ExpectFailure("unknown-tag",
            "timestamp,code,severity,message,tag,pattern\n2026-11-13T14:46:00Z,1,high,x,Bogus,overload\n", "unknown tag");
        ExpectFailure("bad-severity",
            "timestamp,code,severity,message,tag,pattern\n2026-11-13T14:46:00Z,1,urgent,x,Tag_1,overload\n", "invalid severity");
        ExpectFailure("empty-pattern",
            "timestamp,code,severity,message,tag,pattern\n2026-11-13T14:46:00Z,1,high,x,Tag_1,\n", "empty pattern");
        ExpectFailure("bad-timestamp",
            "timestamp,code,severity,message,tag,pattern\nnot-a-time,1,high,x,Tag_1,overload\n", "invalid failure timestamp");

        var result = new FleetPatternSelfTestResult
        {
            Report = report,
            Checks = checks,
            NegativeChecks = negative
        };
        result.Pass = report.Records == 4
                      && report.TotalPatterns == 4
                      && report.CrossPlantPatterns == 3
                      && report.PlantsWithPatterns == 4
                      && overload?.Plants == 4
                      && overload?.Count == 7
                      && high?.Plants == 2
                      && overheat?.Plants == 1
                      && negative.All(n => n.Contains("=True", StringComparison.Ordinal));
        JsonUtil.Write(result, Path.Combine(outDir, "fleet-pattern-test.json"));
        return result;
    }
}
