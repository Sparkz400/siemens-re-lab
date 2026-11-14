using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TiaProject;

public sealed class SimaticMlBlock
{
    public string Name { get; set; } = "";
    public string Element { get; set; } = "";
    public string? Subtype { get; set; }
    public int? Number { get; set; }
    public string? Language { get; set; }
    public string File { get; set; } = "";
    public bool WellFormed { get; set; }
    public string? BodyRoot { get; set; }
    public List<string> Sections { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class SimaticMlResult
{
    public string ExportPath { get; set; } = "";
    public string OutPath { get; set; } = "";
    public List<SimaticMlBlock> Blocks { get; set; } = [];
}

public static class SimaticMlExporter
{
    private static readonly XNamespace Blocks = "http://www.siemens.com/automation/Openness/SW/Blocks/v5";
    private static readonly XNamespace Interface = "http://www.siemens.com/automation/Openness/SW/Interface/v5";
    private static readonly XNamespace DocumentNs = "http://www.siemens.com/automation/Openness/SW/Document/v5";

    private static readonly string[] SectionOrder =
    [
        "Input", "Output", "InOut", "Temp", "Return", "Static", "Constant"
    ];

    public static SimaticMlResult Export(ObjectExport export, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var result = new SimaticMlResult { ExportPath = export.FilePath, OutPath = Path.GetFullPath(outDir) };
        var byInst = export.Objects.GroupBy(o => o.InstanceId).ToDictionary(g => g.Key, g => g.First());

        foreach (var block in export.Objects.Where(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)))
        {
            var sm = new SimaticMlBlock
            {
                Name = block.Name ?? $"Block_{block.InstanceId}",
                Subtype = block.Subtype,
                Number = block.Number,
                Language = LanguageName(block.ProgrammingLanguage)
            };
            sm.Element = BlockElement(block.Subtype);

            var interfaceXml = LoadInterface(block, byInst);
            var sections = BuildSections(interfaceXml, sm);
            var compileUnit = LoadCompileUnit(block, byInst);
            if (compileUnit?.ProgrammingLanguage is int pl && pl != 0)
                sm.Language = LanguageName(pl);
            var body = compileUnit is null ? null : BuildBody(compileUnit, sm);

            var blockEl = new XElement(Blocks + sm.Element,
                new XAttribute("xmlns", Blocks.NamespaceName),
                new XAttribute("ID", "0"),
                new XElement(Blocks + "AttributeList",
                    new XElement(Blocks + "Interface",
                        new XElement(Interface + "Sections",
                            new XAttribute("xmlns", Interface.NamespaceName),
                            sections.Cast<object>().ToArray())),
                    new XElement(Blocks + "Name", sm.Name),
                    new XElement(Blocks + "Number", sm.Number ?? 0),
                    new XElement(Blocks + "ProgrammingLanguage", sm.Language ?? "LAD"),
                    new XElement(Blocks + "HeaderVersion", "0.1"),
                    new XElement(Blocks + "MemoryLayout", "Optimized")),
                new XElement(Blocks + "ObjectList",
                    new XElement(Blocks + "CompileUnit",
                        new XAttribute("ID", "1"),
                        new XElement(Blocks + "AttributeList",
                            new XElement(Blocks + "NetworkSource", body ?? new XElement("Text", "")),
                            new XElement(Blocks + "ProgrammingLanguage", sm.Language ?? "LAD")),
                        new XElement(Blocks + "ObjectList"))));

            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(DocumentNs + "Document",
                    new XAttribute("xmlns", DocumentNs.NamespaceName),
                    new XElement("Engineering", new XAttribute("version", "V21")),
                    blockEl));

            var file = Path.Combine(outDir, SafeFileName($"{sm.Subtype ?? "BLOCK"}_{sm.Number?.ToString() ?? "0"}_{sm.Name}") + ".simaticml.xml");
            doc.Save(file);
            sm.File = file;
            try
            {
                XDocument.Load(file);
                sm.WellFormed = true;
            }
            catch (Exception ex)
            {
                sm.Warnings.Add("XML check failed: " + ex.Message);
            }
            result.Blocks.Add(sm);
        }
        return result;
    }

    private static XElement? LoadInterface(ExportedObjectInfo block, Dictionary<long, ExportedObjectInfo> byInst)
    {
        var link = block.Links.FirstOrDefault(l => l.TypeId == 2233868);
        if (link.InstanceId == 0 || !byInst.TryGetValue(link.InstanceId, out var root)) return null;
        if (!root.CoreBlobs.TryGetValue("Payload", out var hex)) return null;
        var text = TextUtil.DecodeCoreBlob(hex);
        if (text is null) return null;
        try { return XDocument.Parse(text).Root; }
        catch { return null; }
    }

    private static ExportedObjectInfo? LoadCompileUnit(ExportedObjectInfo block, Dictionary<long, ExportedObjectInfo> byInst)
    {
        var link = block.Links.FirstOrDefault(l => l.TypeId == 2232342);
        if (link.InstanceId == 0 || !byInst.TryGetValue(link.InstanceId, out var cu)) return null;
        return cu;
    }

    private static XElement? DecodeXml(string hex)
    {
        var text = TextUtil.DecodeCoreBlob(hex);
        if (text is null) return null;
        try { return XDocument.Parse(text).Root; }
        catch { return null; }
    }

    private static List<XElement> BuildSections(XElement? interfaceRoot, SimaticMlBlock sm)
    {
        var sections = new List<XElement>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (interfaceRoot is not null)
        {
            foreach (var member in interfaceRoot.Elements().Where(e => e.Name.LocalName == "Member"))
            {
                var name = member.Attribute("Name")?.Value;
                if (string.IsNullOrEmpty(name)) continue;
                if (!SectionOrder.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                names.Add(name);
                var section = new XElement(Interface + "Section", new XAttribute("Name", name));
                foreach (var child in member.Elements().Where(e => e.Name.LocalName == "Member"))
                {
                    var cname = child.Attribute("Name")?.Value;
                    if (string.IsNullOrEmpty(cname)) continue;
                    if (sm.Subtype?.StartsWith("OB", StringComparison.OrdinalIgnoreCase) == true
                        && (cname == "Initial_Call" || cname == "Remanence")) continue;
                    var datatype = DatatypeOf(child.Attribute("RID")?.Value);
                    section.Add(new XElement(Interface + "Member",
                        new XAttribute("Name", cname),
                        new XAttribute("Datatype", datatype)));
                }
                sections.Add(section);
                sm.Sections.Add(name);
            }
        }
        // SimaticML requires at least the standard sections to exist for code blocks.
        foreach (var name in SectionOrder)
        {
            if (names.Contains(name)) continue;
            if (name is "Static" or "Constant") continue;
            sections.Add(new XElement(Interface + "Section", new XAttribute("Name", name)));
            sm.Sections.Add(name);
        }
        return sections;
    }

    private static string DatatypeOf(string? rid) => rid switch
    {
        "0x02000000" => "Void",
        "0x02000001" => "Bool",
        "0x02000002" => "Byte",
        "0x02000003" => "Word",
        "0x02000004" => "DWord",
        "0x02000005" => "Int",
        "0x02000006" => "DInt",
        "0x02000007" => "Real",
        "0x02000008" => "Char",
        "0x02000009" => "String",
        _ => "Bool"
    };

    private static XElement? BuildBody(ExportedObjectInfo cu, SimaticMlBlock sm)
    {
        if (!cu.CoreBlobs.TryGetValue("Data", out var hex)) return null;
        var text = TextUtil.DecodeCoreBlob(hex);
        if (text is null) return null;
        XElement? root;
        try { root = XDocument.Parse(text).Root; }
        catch (Exception ex) { sm.Warnings.Add("body parse failed: " + ex.Message); return null; }
        if (root is null) return null;
        sm.BodyRoot = root.Name.LocalName;
        if (root.Name.LocalName == "FlgNet")
            return new XElement(root);
        if (root.Name.LocalName == "SCLSource")
            return BuildStructuredText(root);
        return new XElement("Text", root.ToString(SaveOptions.DisableFormatting));
    }

    private static XElement BuildStructuredText(XElement scl)
    {
        var st = new XElement("StructuredText");
        foreach (var text in scl.Descendants().Where(e => e.Name.LocalName == "LC"))
        {
            var value = text.Attribute("TE")?.Value;
            if (!string.IsNullOrEmpty(value)) st.Add(new XElement("Text", value));
        }
        foreach (var sym in scl.Descendants().Where(e => e.Name.LocalName == "SymVa"))
        {
            var name = sym.Attribute("ODN")?.Value;
            if (string.IsNullOrEmpty(name)) continue;
            st.Add(new XElement("Access",
                new XElement("Symbol",
                    new XElement("Component", new XAttribute("Name", name)))));
        }
        // Preserve the internal AST for tools that want exact semantics.
        st.Add(new XElement("Text", " " + Regex.Replace(scl.ToString(SaveOptions.DisableFormatting), @"\s+", " ")));
        return st;
    }

    private static string BlockElement(string? subtype)
    {
        if (subtype is null) return "SW.Blocks.FC";
        if (subtype.StartsWith("OB", StringComparison.OrdinalIgnoreCase)) return "SW.Blocks.OB";
        if (subtype.StartsWith("FB", StringComparison.OrdinalIgnoreCase)) return "SW.Blocks.FB";
        if (subtype.StartsWith("FC", StringComparison.OrdinalIgnoreCase)) return "SW.Blocks.FC";
        if (subtype.StartsWith("DB", StringComparison.OrdinalIgnoreCase)) return "SW.Blocks.DB";
        return "SW.Blocks.FC";
    }

    private static string? LanguageName(int? language) => language switch
    {
        1 => "STL",
        2 => "LAD",
        3 => "FBD",
        4 => "SCL",
        5 => "GRAPH",
        _ => language?.ToString()
    };

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 120 ? name[..120] : name;
    }
}
