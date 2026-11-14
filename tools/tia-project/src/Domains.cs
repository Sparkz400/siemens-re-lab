using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TiaProject;

public sealed class DomainObject
{
    public string ClassName { get; set; } = "";
    public long InstanceId { get; set; }
    public Guid Guid { get; set; }
    public string? Name { get; set; }
    public string? Subtype { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = [];
    public List<string> Links { get; set; } = [];
}

public sealed class DomainResult
{
    public string Kind { get; set; } = "";
    public string ExportPath { get; set; } = "";
    public string OutPath { get; set; } = "";
    public int Count { get; set; }
    public List<DomainObject> Objects { get; set; } = [];
    public string? MarkdownFile { get; set; }
}

public static class DomainExporter
{
    public static List<DomainObject> Select(ObjectExport export, string kind)
    {
        Func<string, bool> match = kind switch
        {
            "safety" => c => c.Contains("Safety", StringComparison.OrdinalIgnoreCase)
                              || c.Contains("FailSafe", StringComparison.OrdinalIgnoreCase),
            "umac" => c => c.Contains("Umac", StringComparison.OrdinalIgnoreCase)
                            || c.Contains("AccessControl", StringComparison.OrdinalIgnoreCase)
                            || c.Contains("SystemRole", StringComparison.OrdinalIgnoreCase)
                            || c.Contains("SystemUser", StringComparison.OrdinalIgnoreCase)
                            || c.Contains("DeviceFunctionRight", StringComparison.OrdinalIgnoreCase),
            "drives" => c => c.Contains("MC.Drives", StringComparison.OrdinalIgnoreCase)
                              || c.Contains("Sinamics", StringComparison.OrdinalIgnoreCase)
                              || c.Contains("Telegram", StringComparison.OrdinalIgnoreCase)
                              || c.Contains("DriveData", StringComparison.OrdinalIgnoreCase)
                              || c.Contains("DriveObject", StringComparison.OrdinalIgnoreCase)
                              || c.Contains("AxisData", StringComparison.OrdinalIgnoreCase),
            _ => _ => false
        };

        return export.Objects.Where(o => match(o.ClassName)).Select(o => new DomainObject
        {
            ClassName = o.ClassName,
            InstanceId = o.InstanceId,
            Guid = o.Guid,
            Name = o.Name,
            Subtype = o.Subtype,
            Attributes = o.Attributes,
            Links = o.Links.Select(l => $"{l.TypeId}-{l.InstanceId}").ToList()
        }).ToList();
    }

    public static DomainResult Export(ObjectExport export, string kind, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var result = new DomainResult { Kind = kind, ExportPath = export.FilePath, OutPath = Path.GetFullPath(outDir) };
        result.Objects = Select(export, kind);
        result.Count = result.Objects.Count;

        var md = new StringBuilder();
        md.AppendLine($"# {kind} objects");
        md.AppendLine();
        md.AppendLine($"Count: {result.Count}");
        md.AppendLine();
        md.AppendLine("| Name | Class | Subtype |");
        md.AppendLine("|---|---|---|");
        foreach (var o in result.Objects.OrderBy(o => o.Name))
            md.AppendLine($"| {o.Name} | {o.ClassName} | {o.Subtype} |");
        result.MarkdownFile = Path.Combine(outDir, kind + ".md");
        File.WriteAllText(result.MarkdownFile, md.ToString(), new UTF8Encoding(false));
        return result;
    }
}

public sealed class VciEntryResult
{
    public string? RelativePath { get; set; }
    public string? FullPath { get; set; }
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "";
    public string? Error { get; set; }
}

public sealed class VciVerifyResult
{
    public string ProjectPath { get; set; } = "";
    public string VciPath { get; set; } = "";
    public int Total { get; set; }
    public int Ok { get; set; }
    public int Mismatch { get; set; }
    public int Missing { get; set; }
    public int Errors { get; set; }
    public List<VciEntryResult> Entries { get; set; } = [];
}

public static class VciVerifier
{
    public static VciVerifyResult Verify(string projectRoot, string vciPath)
    {
        var result = new VciVerifyResult { ProjectPath = Path.GetFullPath(projectRoot), VciPath = Path.GetFullPath(vciPath) };
        var temp = FileUtil.CopyToTemp(vciPath, Path.Combine(Path.GetTempPath(), "tia-project", "vci-verify"));
        try
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = temp,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private
            }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT RelativeFilePath, FileHash, FileHashDescription FROM FileRecord";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var rel = r.IsDBNull(0) ? null : r.GetString(0);
                var expected = r.IsDBNull(1) ? null : r.GetString(1);
                var desc = r.IsDBNull(2) ? null : r.GetString(2);
                var entry = new VciEntryResult { RelativePath = rel, Expected = expected, Description = desc };
                result.Total++;
                if (string.IsNullOrWhiteSpace(rel))
                {
                    entry.Status = "error";
                    entry.Error = "empty relative path";
                    result.Errors++;
                    result.Entries.Add(entry);
                    continue;
                }
                var full = Path.GetFullPath(Path.Combine(projectRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
                entry.FullPath = full;
                if (!File.Exists(full))
                {
                    entry.Status = "missing";
                    result.Missing++;
                    result.Entries.Add(entry);
                    continue;
                }
                try
                {
                    var actual = HashFile(full, desc);
                    entry.Actual = actual;
                    if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Status = "ok";
                        result.Ok++;
                    }
                    else
                    {
                        entry.Status = "mismatch";
                        result.Mismatch++;
                    }
                }
                catch (Exception ex)
                {
                    entry.Status = "error";
                    entry.Error = ex.Message;
                    result.Errors++;
                }
                result.Entries.Add(entry);
            }
            return result;
        }
        finally
        {
            FileUtil.TryDelete(temp);
            FileUtil.TryDeleteDirectory(Path.GetDirectoryName(temp)!);
        }
    }

    private static string HashFile(string path, string? description)
    {
        var d = (description ?? "").ToUpperInvariant();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        byte[] hash;
        if (d.Contains("SHA512")) hash = SHA512.HashData(stream);
        else if (d.Contains("SHA1")) hash = SHA1.HashData(stream);
        else if (d.Contains("MD5")) hash = MD5.HashData(stream);
        else hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
