using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace TiaProject;

public sealed class ReleaseFixtureResult
{
    public string Name { get; set; } = "";
    public string ProjectRoot { get; set; } = "";
    public string ExportPath { get; set; } = "";
    public string FormatVersion { get; set; } = "";
    public string? AddedTag { get; set; }
    public string PlfHash { get; set; } = "";
    public string IdxHash { get; set; } = "";
    public string ExportHash { get; set; } = "";
    public int Records { get; set; }
    public int Live { get; set; }
    public string Created { get; set; } = DateTime.UtcNow.ToString("O");
}

public static class ReleaseFixture
{
    public static ReleaseFixtureResult Create(string projectRoot, string exportPath, string outRoot,
        string name, string formatVersion = "0.1400.2.0", bool addTag = true)
    {
        if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        Directory.CreateDirectory(outRoot);
        CopyProject(projectRoot, outRoot);

        var plfPath = Path.Combine(outRoot, "System", "PEData.plf");
        PatchHeader(plfPath, formatVersion);
        var idxPath = Path.Combine(outRoot, "System", "PEData.idx");
        if (File.Exists(idxPath)) PatchHeader(idxPath, formatVersion);

        var exportOut = Path.Combine(outRoot, "export", "project.objectframe.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(exportOut)!);
        var doc = XDocument.Load(exportPath);
        var addedTag = addTag ? CloneTag(doc, name) : null;
        doc.Save(exportOut);

        var paths = ProjectPaths.Resolve(outRoot);
        var plf = PlfReader.Read(paths.Plf, verifyHashes: false);
        PlfReader.AttachIndex(plf, PlfReader.ReadIndex(paths.Idx));
        return new ReleaseFixtureResult
        {
            Name = name,
            ProjectRoot = paths.Root,
            ExportPath = Path.GetFullPath(exportOut),
            FormatVersion = plf.Header.FileFormatVersion,
            AddedTag = addedTag,
            PlfHash = PlatformIo.HashFile(paths.Plf),
            IdxHash = PlatformIo.HashFile(paths.Idx),
            ExportHash = PlatformIo.HashFile(exportOut),
            Records = plf.Objects.Count,
            Live = plf.LiveObjects.Count
        };
    }

    private static void PatchHeader(string path, string version)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 98) throw new CliException("file is smaller than a 98-byte header: " + path);
        var parts = version.Split('.');
        for (int i = 0; i < 4; i++)
        {
            var value = i < parts.Length && int.TryParse(parts[i], out var parsed) ? parsed : 0;
            BitConverter.TryWriteBytes(bytes.AsSpan(40 + i * 4, 4), value);
        }
        var hash = SHA256.HashData(bytes.AsSpan(0, 65));
        hash.CopyTo(bytes, 65);
        bytes[97] = 0xFF;
        File.WriteAllBytes(path, bytes);
    }

    private static string? CloneTag(XDocument doc, string name)
    {
        var objects = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "objects");
        if (objects is null) return null;
        var tag = objects.Elements().FirstOrDefault(e => e.Name.LocalName == "StorageObject"
            && e.Attribute("name")?.Value.EndsWith("EAMTZTagData", StringComparison.Ordinal) == true);
        if (tag is null) return null;

        var maxInstance = objects.Elements()
            .Select(e => int.TryParse(e.Attribute("instId")?.Value, out var v) ? v : 0)
            .DefaultIfEmpty(0).Max();
        var clone = new XElement(tag);
        clone.SetAttributeValue("instId", (maxInstance + 1).ToString());
        clone.SetAttributeValue("guid", Guid.NewGuid().ToString());
        var tagName = name + "_Tag";
        var nameAttrib = clone.Descendants().FirstOrDefault(e => e.Name.LocalName == "attrib" && e.Attribute("name")?.Value == "Name");
        if (nameAttrib?.Elements().FirstOrDefault() is { } nameValue) nameValue.Value = tagName;
        var addressAttrib = clone.Descendants().FirstOrDefault(e => e.Name.LocalName == "attrib" && e.Attribute("name")?.Value == "LogicalAddress");
        if (addressAttrib?.Elements().FirstOrDefault() is { } addressValue) addressValue.Value = "%M2.0";
        objects.Add(clone);
        objects.SetAttributeValue("objectCount", objects.Elements().Count(e => e.Name.LocalName == "StorageObject"));
        objects.SetAttributeValue("lastInstanceId", maxInstance + 1);
        return tagName;
    }

    private static void CopyProject(string source, string dest)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            File.WriteAllBytes(Path.Combine(dest, Path.GetFileName(file)), FileUtil.ReadAllBytesShared(file));

        var dirs = new[] { "System", "XRef", "Vci", "AdditionalFiles", "src", "UserFiles" };
        foreach (var dirName in dirs)
        {
            var dir = Path.Combine(source, dirName);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, file);
                var dst = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.WriteAllBytes(dst, FileUtil.ReadAllBytesShared(file));
            }
        }
    }
}
