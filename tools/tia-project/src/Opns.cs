using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TiaProject;

public sealed class ExtractedSchema
{
    public string Assembly { get; set; } = "";
    public string ResourceName { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class SchemaExtractionResult
{
    public string BinPath { get; set; } = "";
    public string OutPath { get; set; } = "";
    public List<ExtractedSchema> Schemas { get; set; } = [];
    public List<string> IncludesResolved { get; set; } = [];
    public List<string> MissingIncludes { get; set; } = [];
}

public static class SchemaExtractor
{
    private sealed record Spec(string Assembly, string[] Patterns);

    private static readonly Spec[] Specs =
    [
        new("Siemens.Simatic.Lang.BlockLogic.dll",
            ["Step7OpennessBody", "SW.PlcBlocks."]),
        new("Siemens.Simatic.Lang.IPI.dll",
            ["Step7Internal", "SW.Common", "SW.InterfaceSections"]),
        new("Siemens.Automation.ObjectFrame.Meta.dll",
            ["Automation-ObjectFrame-MetaData"]),
        new("Siemens.Simatic.Lang.BlockInterface.dll",
            ["Values"])
    ];

    public static SchemaExtractionResult Extract(string tiaBin, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var result = new SchemaExtractionResult
        {
            BinPath = Path.GetFullPath(tiaBin),
            OutPath = Path.GetFullPath(outDir)
        };
        var byFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var spec in Specs)
        {
            var dll = Path.Combine(tiaBin, spec.Assembly);
            if (!File.Exists(dll)) continue;
            using var fs = File.OpenRead(dll);
            using var pe = new PEReader(fs);
            var mr = pe.GetMetadataReader();
            foreach (var handle in mr.ManifestResources)
            {
                var resource = mr.GetManifestResource(handle);
                var name = mr.GetString(resource.Name);
                if (!name.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase)) continue;
                if (!spec.Patterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;

                var file = ShortName(name);
                if (!byFile.TryAdd(file, dll))
                {
                    // Two assemblies expose the same file name; keep the first but still record the second.
                    file = Path.GetFileNameWithoutExtension(name) + "-" + Path.GetFileNameWithoutExtension(spec.Assembly) + ".xsd";
                    byFile[file] = dll;
                }
                var resourceOffset = pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress + (int)resource.Offset;
                var block = pe.GetSectionData(resourceOffset);
                var reader = block.GetReader();
                int resourceLength = reader.ReadInt32();
                var bytes = block.GetContent(4, resourceLength).ToArray();
                var path = Path.Combine(outDir, file);
                File.WriteAllBytes(path, bytes);
                result.Schemas.Add(new ExtractedSchema
                {
                    Assembly = spec.Assembly,
                    ResourceName = name,
                    FileName = file,
                    Length = bytes.Length,
                    Sha256 = FileUtil.Sha256Hex(bytes)
                });
            }
        }

        ResolveIncludes(outDir, result);
        File.WriteAllText(Path.Combine(outDir, "schema-map.json"),
            JsonUtil.Serialize(result), new UTF8Encoding(false));
        return result;
    }

    private static string ShortName(string resourceName)
    {
        foreach (var marker in new[] { "._Resource.", "._XMLSchemas.", "._Schema.", ".Schema.", ".Schemas.", "._schemas.", "._met." })
        {
            var i = resourceName.LastIndexOf(marker, StringComparison.Ordinal);
            if (i >= 0) return resourceName[(i + marker.Length)..];
        }
        return Path.GetFileName(resourceName);
    }

    private static void ResolveIncludes(string outDir, SchemaExtractionResult result)
    {
        var files = Directory.GetFiles(outDir, "*.xsd").Select(Path.GetFileName).Where(f => f is not null).Cast<string>().ToList();
        var regex = new Regex("schemaLocation\\s*=\\s*\"([^\"]+)\"", RegexOptions.Compiled);
        foreach (var xsd in Directory.GetFiles(outDir, "*.xsd"))
        {
            var text = File.ReadAllText(xsd);
            foreach (Match m in regex.Matches(text))
            {
                var target = m.Groups[1].Value;
                if (target.Contains('/') || target.Contains('\\')) continue;
                var targetPath = Path.Combine(outDir, target);
                if (File.Exists(targetPath)) continue;

                var stem = Path.GetFileNameWithoutExtension(target);
                var match = files.FirstOrDefault(f =>
                    f.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase))
                    ?? files.FirstOrDefault(f =>
                        f.StartsWith(stem + "-", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    File.Copy(Path.Combine(outDir, match), targetPath, overwrite: true);
                    result.IncludesResolved.Add($"{Path.GetFileName(xsd)} -> {target} (from {match})");
                }
                else
                {
                    result.MissingIncludes.Add($"{Path.GetFileName(xsd)} -> {target}");
                }
            }
        }
    }
}

public sealed class ApiType
{
    public string Namespace { get; set; } = "";
    public string Name { get; set; } = "";
    public string FullName => Namespace.Length == 0 ? Name : Namespace + "." + Name;
    public List<string> Methods { get; set; } = [];
    public List<string> Properties { get; set; } = [];
    public List<string> Fields { get; set; } = [];
}

public sealed class ApiAssembly
{
    public string File { get; set; } = "";
    public List<ApiType> Types { get; set; } = [];
}

public sealed class ApiIndex
{
    public string BinPath { get; set; } = "";
    public List<ApiAssembly> Assemblies { get; set; } = [];
}

public static class ApiIndexer
{
    private static readonly string[] Assemblies =
    [
        "Siemens.Simatic.Lang.Openness.dll",
        "Siemens.Simatic.Lang.Openness.Internal.dll",
        "Siemens.Automation.Opns.ObjectModel.dll",
        "Siemens.Automation.Opns.Services.dll",
        "Siemens.Automation.Opns.Mapper.dll",
        "Siemens.Automation.XRef.Openness.dll"
    ];

    private static readonly string[] NamespacePrefixes =
    [
        "Siemens.Simatic.Lang.Openness",
        "Siemens.Automation.Opns",
        "Siemens.Automation.Openness"
    ];

    public static ApiIndex Index(string tiaBin) => Index(tiaBin, Assemblies, NamespacePrefixes);

    public static ApiIndex Index(string tiaBin, IEnumerable<string> assemblies, IEnumerable<string> namespacePrefixes)
    {
        var prefixes = namespacePrefixes.ToArray();
        var index = new ApiIndex { BinPath = Path.GetFullPath(tiaBin) };
        foreach (var name in assemblies)
        {
            var dll = Path.Combine(tiaBin, name);
            if (!File.Exists(dll)) continue;
            using var fs = File.OpenRead(dll);
            using var pe = new PEReader(fs);
            var mr = pe.GetMetadataReader();
            var asm = new ApiAssembly { File = name };
            foreach (var handle in mr.TypeDefinitions)
            {
                var td = mr.GetTypeDefinition(handle);
                var ns = mr.GetString(td.Namespace);
                if (!prefixes.Any(p => ns.StartsWith(p, StringComparison.Ordinal))) continue;
                var t = new ApiType
                {
                    Namespace = ns,
                    Name = mr.GetString(td.Name)
                };
                foreach (var mh in td.GetMethods())
                {
                    var m = mr.GetMethodDefinition(mh);
                    var mn = mr.GetString(m.Name);
                    if (!mn.StartsWith("get_", StringComparison.Ordinal) && !mn.StartsWith("set_", StringComparison.Ordinal))
                        t.Methods.Add(mn);
                }
                foreach (var ph in td.GetProperties())
                    t.Properties.Add(mr.GetString(mr.GetPropertyDefinition(ph).Name));
                foreach (var fh in td.GetFields())
                    t.Fields.Add(mr.GetString(mr.GetFieldDefinition(fh).Name));
                asm.Types.Add(t);
                if (asm.Types.Count >= 10000) break;
            }
            index.Assemblies.Add(asm);
        }
        return index;
    }
}
