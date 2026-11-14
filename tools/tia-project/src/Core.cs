using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace TiaProject;

public sealed class CliException(string message) : Exception(message);

public static class FileUtil
{
    public static byte[] ReadAllBytesShared(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            return ms.ToArray();
        }
        catch (IOException)
        {
            // TIA may hold the file without sharing. Copy through a shared handle into temp.
            var tmp = Path.Combine(Path.GetTempPath(), "tia-project", Guid.NewGuid().ToString("N") + Path.GetExtension(path));
            Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
            using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = File.Create(tmp))
                src.CopyTo(dst);
            try { return File.ReadAllBytes(tmp); }
            finally { TryDelete(tmp); }
        }
    }

    public static string CopyToTemp(string path, string? directory = null, string? fileName = null)
    {
        directory ??= Path.Combine(Path.GetTempPath(), "tia-project");
        Directory.CreateDirectory(directory);
        var dstPath = Path.Combine(directory, fileName ?? (Guid.NewGuid().ToString("N") + Path.GetExtension(path)));
        using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var dst = File.Create(dstPath);
        src.CopyTo(dst);
        return dstPath;
    }

    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string? FindTiaBin(string? explicitPath = null)
    {
        var candidates = new List<string?>();
        if (!string.IsNullOrWhiteSpace(explicitPath)) candidates.Add(explicitPath);
        var env = Environment.GetEnvironmentVariable("TIA_BIN");
        if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env);
        candidates.Add(@"C:\Program Files\Siemens\Automation\Portal V21\Bin");
        candidates.Add(@"C:\Program Files\Siemens\Automation\Portal V20\Bin");
        candidates.Add(@"C:\Program Files\Siemens\Automation\Portal V19\Bin");
        return candidates.Where(p => !string.IsNullOrWhiteSpace(p))
                         .Select(p => Path.GetFullPath(p!))
                         .FirstOrDefault(p => File.Exists(Path.Combine(p, "Siemens.Automation.ObjectFrame.FileStorage.FS2XML.exe")));
    }

    public static string? FindOpennessSchemas(string? tiaBin = null)
    {
        var env = Environment.GetEnvironmentVariable("TIA_OPENNESS_SCHEMAS");
        if (!string.IsNullOrWhiteSpace(env)
            && File.Exists(Path.Combine(env, "SW.PlcBlocks.SCL_v4.xsd")))
            return Path.GetFullPath(env);

        var bin = FindTiaBin(tiaBin);
        if (bin is null) return null;
        var root = Path.GetFullPath(Path.Combine(bin, ".."));
        var candidates = new[]
        {
            Path.Combine(root, "PublicAPI", "V21", "Schemas"),
            Path.Combine(root, "PublicAPI", "V20", "Schemas"),
            Path.Combine(root, "PublicAPI", "Schemas"),
            Path.Combine(bin, "Schemas")
        };
        foreach (var candidate in candidates)
            if (File.Exists(Path.Combine(candidate, "SW.PlcBlocks.SCL_v4.xsd")))
                return Path.GetFullPath(candidate);
        return null;
    }

    public static string? FindSqlCmd()
    {
        var candidates = new[]
        {
            @"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE",
            @"C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE"
        };
        foreach (var c in candidates) if (File.Exists(c)) return c;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                var p = Path.Combine(dir, "sqlcmd.exe");
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }
}

public static class JsonUtil
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize(object value) => JsonSerializer.Serialize(value, Options);

    public static void Write(object value, string? path)
    {
        var json = Serialize(value);
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine(json);
            return;
        }
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, json, new UTF8Encoding(false));
        Console.Error.WriteLine($"wrote {full} ({json.Length:N0} chars)");
    }
}

public sealed class ProjectPaths
{
    public required string Root { get; init; }
    public required string Ap21 { get; init; }
    public required string Plf { get; init; }
    public required string Idx { get; init; }
    public required string XrefDb { get; init; }
    public required string VciDb { get; init; }
    public string? HmiBackup { get; init; }

    public static ProjectPaths Resolve(string input)
    {
        var path = Path.GetFullPath(input);
        string root;
        if (File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            root = ext switch
            {
                ".ap21" => Path.GetDirectoryName(path)!,
                ".plf" => Path.GetDirectoryName(Path.GetDirectoryName(path)!)!,
                _ => Path.GetDirectoryName(path)!
            };
        }
        else if (Directory.Exists(path))
        {
            root = path;
        }
        else
        {
            throw new CliException($"project path not found: {input}");
        }

        var ap21 = Directory.EnumerateFiles(root, "*.ap21", SearchOption.TopDirectoryOnly).FirstOrDefault()
                   ?? throw new CliException($"no .ap21 file in {root}");

        var systemDir = Path.Combine(root, "System");
        var plf = (Directory.Exists(systemDir)
                      ? Directory.EnumerateFiles(systemDir, "*.plf", SearchOption.TopDirectoryOnly).FirstOrDefault()
                      : null)
                  ?? Directory.EnumerateFiles(root, "*.plf", SearchOption.AllDirectories).FirstOrDefault()
                  ?? throw new CliException($"no PEData .plf in {root}");

        var idx = Path.ChangeExtension(plf, ".idx");
        if (!File.Exists(idx))
        {
            idx = Directory.EnumerateFiles(Path.GetDirectoryName(plf)!, "*.idx", SearchOption.TopDirectoryOnly).FirstOrDefault()
                  ?? throw new CliException($"no PEData .idx next to {plf}");
        }

        var xref = Path.Combine(root, "XRef", "XRef.db");
        if (!File.Exists(xref))
            xref = Directory.EnumerateFiles(root, "XRef.db", SearchOption.AllDirectories).FirstOrDefault()
                   ?? throw new CliException($"no XRef.db in {root}");

        var vci = Path.Combine(root, "Vci", "Vci.db");
        if (!File.Exists(vci))
        {
            var vciDir = Path.Combine(root, "Vci");
            vci = (Directory.Exists(vciDir)
                      ? Directory.EnumerateFiles(vciDir, "Vci.db", SearchOption.TopDirectoryOnly).FirstOrDefault()
                      : null)
                  ?? Directory.EnumerateFiles(root, "Vci.db", SearchOption.AllDirectories).FirstOrDefault()
                  ?? throw new CliException($"no Vci.db in {root}");
        }

        string? hmi = null;
        var im = Path.Combine(root, "IM", "HMI");
        if (Directory.Exists(im))
        {
            hmi = Directory.EnumerateFiles(im, "*.bak", SearchOption.AllDirectories)
                .OrderByDescending(p => Path.GetFileName(p).StartsWith("CS_", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        return new ProjectPaths
        {
            Root = root,
            Ap21 = ap21,
            Plf = plf,
            Idx = idx,
            XrefDb = xref,
            VciDb = vci,
            HmiBackup = hmi
        };
    }
}

public sealed class Ap21Info
{
    public string? RootElement { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? BrowsableType { get; set; }
    public string? ProjectCompatibilityVersion { get; set; }
    public string? ProjectVersion { get; set; }
    public long FileLength { get; set; }
    public string? Sha256 { get; set; }

    public static Ap21Info Read(string path)
    {
        var bytes = FileUtil.ReadAllBytesShared(path);
        var doc = XDocument.Parse(Encoding.UTF8.GetString(bytes));
        var root = doc.Root ?? throw new CliException($"empty XML in {path}");
        return new Ap21Info
        {
            RootElement = root.Name.LocalName,
            Type = root.Attribute("Type")?.Value,
            Name = root.Attribute("Name")?.Value,
            BrowsableType = root.Attribute("BrowsableType")?.Value,
            ProjectCompatibilityVersion = root.Attribute("ProjectCompatibilityVersion")?.Value,
            ProjectVersion = root.Element(root.Name.Namespace + "ProjectVersion")?.Value,
            FileLength = bytes.Length,
            Sha256 = FileUtil.Sha256Hex(bytes)
        };
    }
}

public sealed class ProductInfo
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? TechnicalVersion { get; set; }
}

public sealed class ProjectInfo
{
    public string? MinimumVersion { get; set; }
    public string? MinimumTechnicalVersion { get; set; }
    public string? LastUsedVersion { get; set; }
    public string? LastUsedTechnicalVersion { get; set; }
    public List<ProductInfo> Products { get; set; } = [];

    public static ProjectInfo Read(string path)
    {
        var info = new ProjectInfo();
        string? section = null;
        ProductInfo? current = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line is "TiaPortal:" or "UsedProducts:") { section = line; current = null; continue; }
            if (line is "MinimumRequired:" or "LastUsed:") { section = line; current = null; continue; }
            if (line.StartsWith("- Name:", StringComparison.OrdinalIgnoreCase))
            {
                current = new ProductInfo { Name = line[7..].Trim() };
                info.Products.Add(current);
                continue;
            }
            var idx = line.IndexOf(':');
            if (idx < 0) continue;
            var key = line[..idx].TrimStart('-', ' ');
            var value = line[(idx + 1)..].Trim();
            if (current is not null && section == "UsedProducts:")
            {
                if (key.Equals("Version", StringComparison.OrdinalIgnoreCase)) current.Version = value;
                else if (key.Equals("TechnicalVersion", StringComparison.OrdinalIgnoreCase)) current.TechnicalVersion = value;
            }
            else if (section == "MinimumRequired:")
            {
                if (key.Equals("Version", StringComparison.OrdinalIgnoreCase)) info.MinimumVersion = value;
                else if (key.Equals("TechnicalVersion", StringComparison.OrdinalIgnoreCase)) info.MinimumTechnicalVersion = value;
            }
            else if (section == "LastUsed:")
            {
                if (key.Equals("Version", StringComparison.OrdinalIgnoreCase)) info.LastUsedVersion = value;
                else if (key.Equals("TechnicalVersion", StringComparison.OrdinalIgnoreCase)) info.LastUsedTechnicalVersion = value;
            }
        }
        return info;
    }
}

public static class TextUtil
{
    public static string? AsciiOrHex(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        var printable = bytes.All(b => b is >= 0x20 and < 0x7F || b is 0x09 or 0x0A or 0x0D);
        return printable ? Encoding.UTF8.GetString(bytes) : Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string? DecodeCoreBlob(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            var bytes = Convert.FromHexString(hex);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.All(b => b is >= 0x09 and <= 0x0D || b is >= 0x20 and < 0x7F))
                return Encoding.UTF8.GetString(bytes);
            return null;
        }
        catch
        {
            return null;
        }
    }
}
