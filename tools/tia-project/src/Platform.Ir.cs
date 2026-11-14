using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace TiaProject;

public sealed class IrInterfaceMember
{
    public string Name { get; set; } = "";
    public string Datatype { get; set; } = "Bool";
}

public sealed class IrInterfaceSection
{
    public string Name { get; set; } = "";
    public List<IrInterfaceMember> Members { get; set; } = [];
}

public sealed class IrSclToken
{
    public string Kind { get; set; } = "";
    public string? Name { get; set; }
    public string? Text { get; set; }
}

public sealed class IrLadPart
{
    public int UId { get; set; }
    public string Gate { get; set; } = "";
    public int? RefId { get; set; }
    public string? Name { get; set; }
}

public sealed class IrLadEndpoint
{
    public string Kind { get; set; } = "";
    public int? UId { get; set; }
    public string? PinName { get; set; }
}

public sealed class IrLadWire
{
    public int UId { get; set; }
    public List<IrLadEndpoint> Endpoints { get; set; } = [];
}

public sealed class IrUnit
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "block";
    public string Name { get; set; } = "";
    public string Class { get; set; } = "";
    public string? Language { get; set; }
    public string? Subtype { get; set; }
    public int? Number { get; set; }
    public List<string> InterfaceSections { get; set; } = [];
    public List<IrInterfaceSection> Interface { get; set; } = [];
    public List<IrSclToken> SclTokens { get; set; } = [];
    public List<IrLadPart> LadParts { get; set; } = [];
    public List<IrLadWire> LadWires { get; set; } = [];
    public Dictionary<int, string> OperandNames { get; set; } = [];
    public List<string> Calls { get; set; } = [];
    public string? BodyHash { get; set; }
    public string? Body { get; set; }
    public string? Mc7Hex { get; set; }
    public string? Mc7Hash { get; set; }
    public int Mc7Length { get; set; }
    public int Mc7Instructions { get; set; }
    public string? CompilerVersion { get; set; }
}

public sealed class IrTag
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? DataType { get; set; }
}

public sealed class IrEdge
{
    public string Src { get; set; } = "";
    public string Dst { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Provenance { get; set; } = "";
    public double Confidence { get; set; }
}

public sealed class IrDocument
{
    public int Version { get; set; } = 1;
    public string Project { get; set; } = "";
    public List<IrUnit> Units { get; set; } = [];
    public List<IrTag> Tags { get; set; } = [];
    public List<IrEdge> Edges { get; set; } = [];
    public Dictionary<string, int> Savepoints { get; set; } = [];
}

public static class IrExporter
{
    public static IrDocument Build(ObjectExport export, XrefData? xref = null, HistoryResult? history = null,
        Mc7AnalysisResult? mc7 = null)
    {
        var doc = new IrDocument { Project = export.FilePath };
        var mc7ByName = mc7?.Blocks.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var o in export.Objects)
        {
            if (o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal))
            {
                var body = PlatformIo.Body(o, export);
                var unit = new IrUnit
                {
                    Id = "block:" + (o.Name ?? o.InstanceId.ToString()),
                    Name = o.Name ?? o.InstanceId.ToString(),
                    Class = o.ClassName,
                    Subtype = o.Subtype,
                    Number = o.Number,
                    Language = LanguageName(LanguageOf(o, export)),
                    Body = body?.ToString(SaveOptions.DisableFormatting),
                    BodyHash = body is null ? null : Hash(PlatformIo.CanonicalElement(body)),
                    Interface = ParseInterface(o, export)
                };
                unit.InterfaceSections = unit.Interface.Select(s => s.Name).ToList();
                if (body is not null && body.Name.LocalName == "SCLSource")
                    unit.SclTokens = ExtractSclTokens(body);
                if (body is not null && body.Name.LocalName == "FlgNet")
                {
                    unit.LadParts = ParseLadParts(body);
                    unit.LadWires = ParseLadWires(body);
                    unit.OperandNames = ResolveOperandNames(o, export, unit.LadParts);
                    foreach (var part in unit.LadParts)
                        if (part.RefId is int refId && unit.OperandNames.TryGetValue(refId, out var operandName))
                            part.Name = operandName;
                }
                if (mc7ByName is not null && mc7ByName.TryGetValue(unit.Name, out var mc7Block) && mc7Block.HasMc7)
                {
                    unit.Mc7Hex = mc7Block.CodeHex;
                    unit.Mc7Hash = mc7Block.CodeHash;
                    unit.Mc7Length = mc7Block.CodeLength;
                    unit.Mc7Instructions = mc7Block.Instructions.Count;
                    unit.CompilerVersion = mc7Block.CompilerVersion;
                }
                if (body is not null)
                {
                    foreach (var call in body.Descendants().Where(e => e.Name.LocalName is "Call" or "CallInfo"))
                    {
                        var name = call.Attribute("Name")?.Value ?? call.Attribute("BlockName")?.Value
                                   ?? call.Descendants().FirstOrDefault(e => e.Name.LocalName == "Component")?.Attribute("Name")?.Value;
                        if (!string.IsNullOrEmpty(name)) unit.Calls.Add(name);
                    }
                }
                doc.Units.Add(unit);
            }
            else if (o.ClassName.EndsWith("EAMTZTagData", StringComparison.Ordinal))
            {
                doc.Tags.Add(new IrTag
                {
                    Id = "tag:" + (o.Name ?? o.InstanceId.ToString()),
                    Name = o.Name ?? o.InstanceId.ToString(),
                    Address = PlatformIo.Attr(o, "LogicalAddress"),
                    DataType = PlatformIo.Attr(o, "DisplayTypeName")
                });
            }
        }
        if (xref is not null)
        {
            foreach (var rel in xref.Relations)
            {
                var l = xref.Objects.FirstOrDefault(o => o.Id == rel.Left);
                var r = xref.Objects.FirstOrDefault(o => o.Id == rel.Right);
                if (l is null || r is null) continue;
                var lp = xref.Part(l.PrimaryPartId);
                var rp = xref.Part(r.PrimaryPartId);
                if (lp?.Name is null || rp?.Name is null) continue;
                doc.Edges.Add(new IrEdge
                {
                    Src = "part:" + lp.Name,
                    Dst = "part:" + rp.Name,
                    Kind = rel.RelationKind ?? "relation",
                    Provenance = "xref",
                    Confidence = 0.9
                });
            }
        }
        if (history is not null)
            doc.Savepoints = history.Savepoints.ToDictionary(s => s.Index.ToString(), s => s.Live);
        return doc;
    }

    public static List<IrLadPart> ParseLadParts(XElement flgNet)
    {
        var parts = new List<IrLadPart>();
        var container = flgNet.Elements().FirstOrDefault(e => e.Name.LocalName == "Parts");
        if (container is null) return parts;
        foreach (var element in container.Elements())
        {
            var uid = int.TryParse(element.Attribute("UId")?.Value, out var id) ? id : 0;
            if (element.Name.LocalName == "Part")
            {
                parts.Add(new IrLadPart { UId = uid, Gate = element.Attribute("Gate")?.Value ?? "" });
            }
            else if (element.Name.LocalName == "ORef")
            {
                var refId = int.TryParse(element.Attribute("RefId")?.Value, out var rid) ? rid : (int?)null;
                parts.Add(new IrLadPart { UId = uid, RefId = refId });
            }
        }
        return parts;
    }

    public static List<IrLadWire> ParseLadWires(XElement flgNet)
    {
        var wires = new List<IrLadWire>();
        var container = flgNet.Elements().FirstOrDefault(e => e.Name.LocalName == "Wires");
        if (container is null) return wires;
        foreach (var element in container.Elements().Where(e => e.Name.LocalName == "Wire"))
        {
            var wire = new IrLadWire
            {
                UId = int.TryParse(element.Attribute("UId")?.Value, out var id) ? id : 0
            };
            foreach (var child in element.Elements())
            {
                var kind = child.Name.LocalName switch
                {
                    "Powerrail" => "powerrail",
                    "PCon" => "pcon",
                    "OCon" => "ocon",
                    "Openbranch" => "openbranch",
                    "OpenCon" => "opencon",
                    "IdentCon" => "identcon",
                    "NameCon" => "namecon",
                    _ => null
                };
                if (kind is null) continue;
                wire.Endpoints.Add(new IrLadEndpoint
                {
                    Kind = kind,
                    UId = int.TryParse(child.Attribute("UId")?.Value, out var uid) ? uid : null,
                    PinName = child.Attribute("PinName")?.Value ?? child.Attribute("Name")?.Value
                });
            }
            wires.Add(wire);
        }
        return wires;
    }

    private static Dictionary<int, string> ResolveOperandNames(ExportedObjectInfo block, ObjectExport export,
        List<IrLadPart> parts)
    {
        var refIds = parts.Where(p => p.RefId is not null).Select(p => p.RefId!.Value).ToHashSet();
        if (refIds.Count == 0) return [];
        var candidates = new List<(ExportedObjectInfo Object, Dictionary<int, string> Names, bool Linked)>();
        foreach (var candidate in export.Objects.Where(o => o.ClassName.EndsWith("IdentPartData", StringComparison.Ordinal)))
        {
            var names = ParseIdentPart(candidate);
            if (!refIds.All(names.ContainsKey)) continue;
            var linked = candidate.Links.Any(l => block.Links.Any(b => b.TypeId == l.TypeId && b.InstanceId == l.InstanceId))
                         || candidate.Links.Any(l => l.TypeId == 2232834);
            candidates.Add((candidate, names, linked));
        }
        var chosen = candidates.FirstOrDefault(c => c.Linked);
        if (chosen.Object is null && candidates.Count > 0) chosen = candidates[0];
        return chosen.Names ?? [];
    }

    private static Dictionary<int, string> ParseIdentPart(ExportedObjectInfo candidate)
    {
        var names = new Dictionary<int, string>();
        if (!candidate.CoreBlobs.TryGetValue("PayLoad", out var hex)) return names;
        var text = TextUtil.DecodeCoreBlob(hex);
        if (text is null) return names;
        try
        {
            var doc = XDocument.Parse(text);
            foreach (var ident in doc.Descendants().Where(e => e.Name.LocalName == "ID"))
            {
                var name = ident.Attribute("N")?.Value;
                var ridText = ident.Attribute("RID")?.Value;
                if (!string.IsNullOrEmpty(name) && int.TryParse(ridText, out var rid))
                    names[rid] = name;
            }
        }
        catch { }
        return names;
    }

    public static List<IrInterfaceSection> ParseInterface(ExportedObjectInfo block, ObjectExport export)
    {
        var link = block.Links.FirstOrDefault(l => l.TypeId == 2233868);
        if (link.InstanceId == 0) return [];
        var root = export.Objects.FirstOrDefault(o => o.InstanceId == link.InstanceId);
        if (root is null || !root.CoreBlobs.TryGetValue("Payload", out var hex)) return [];
        var text = TextUtil.DecodeCoreBlob(hex);
        if (text is null) return [];
        try
        {
            var doc = XDocument.Parse(text);
            var sections = new List<IrInterfaceSection>();
            foreach (var member in doc.Root?.Elements().Where(e => e.Name.LocalName == "Member") ?? [])
            {
                var name = member.Attribute("Name")?.Value;
                if (string.IsNullOrEmpty(name)) continue;
                var section = new IrInterfaceSection { Name = name };
                foreach (var child in member.Elements().Where(e => e.Name.LocalName == "Member"))
                {
                    var childName = child.Attribute("Name")?.Value;
                    if (string.IsNullOrEmpty(childName)) continue;
                    section.Members.Add(new IrInterfaceMember
                    {
                        Name = childName,
                        Datatype = DatatypeOf(child.Attribute("RID")?.Value)
                    });
                }
                sections.Add(section);
            }
            return sections;
        }
        catch { return []; }
    }

    public static string DatatypeOf(string? rid) => rid switch
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

    public static List<IrSclToken> ExtractSclTokens(XElement body)
    {
        var tokens = new List<IrSclToken>();
        void Walk(XElement element)
        {
            switch (element.Name.LocalName)
            {
                case "SymVa":
                {
                    var name = element.Attribute("ODN")?.Value;
                    if (!string.IsNullOrEmpty(name)) tokens.Add(new IrSclToken { Kind = "access", Name = name });
                    return;
                }
                case "Token":
                {
                    var text = element.Attribute("TE")?.Value;
                    if (!string.IsNullOrEmpty(text)) tokens.Add(new IrSclToken { Kind = "token", Text = text });
                    return;
                }
                case "LC":
                {
                    var text = element.Attribute("TE")?.Value;
                    if (text is not null) tokens.Add(new IrSclToken { Kind = "text", Text = "//" + text });
                    return;
                }
                case "NL":
                    tokens.Add(new IrSclToken { Kind = "text", Text = "\n" });
                    return;
                case "EToken":
                    tokens.Add(new IrSclToken { Kind = "token", Text = ";" });
                    return;
                case "Symbols":
                    return;
                default:
                    foreach (var child in element.Elements()) Walk(child);
                    return;
            }
        }
        Walk(body);
        return tokens;
    }

    private static List<string> Sections(ExportedObjectInfo block, ObjectExport export)
    {
        var link = block.Links.FirstOrDefault(l => l.TypeId == 2233868);
        if (link.InstanceId == 0) return [];
        var root = export.Objects.FirstOrDefault(o => o.InstanceId == link.InstanceId);
        if (root is null || !root.CoreBlobs.TryGetValue("Payload", out var hex)) return [];
        var text = TextUtil.DecodeCoreBlob(hex);
        if (text is null) return [];
        try
        {
            return XDocument.Parse(text).Root?.Elements().Where(e => e.Name.LocalName == "Member")
                .Select(e => e.Attribute("Name")?.Value).Where(n => !string.IsNullOrEmpty(n)).Cast<string>().ToList() ?? [];
        }
        catch { return []; }
    }

    private static int? LanguageOf(ExportedObjectInfo o, ObjectExport export)
    {
        if (o.ProgrammingLanguage is int l && l != 0) return l;
        var link = o.Links.FirstOrDefault(x => x.TypeId == 2232342);
        if (link.InstanceId != 0)
        {
            var cu = export.Objects.FirstOrDefault(x => x.InstanceId == link.InstanceId);
            if (cu?.ProgrammingLanguage is int cl && cl != 0) return cl;
        }
        return o.ProgrammingLanguage;
    }

    private static string? LanguageName(int? l) => l switch
    {
        1 => "STL", 2 => "LAD", 3 => "FBD", 4 => "SCL", 5 => "GRAPH", _ => l?.ToString()
    };

    public static string Hash(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

public sealed class IrVerifyResult
{
    public int Units { get; set; }
    public int Bodies { get; set; }
    public int Verified { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = [];
}

public static class IrVerifier
{
    public static IrVerifyResult Verify(IrDocument doc)
    {
        var r = new IrVerifyResult { Units = doc.Units.Count };
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var u in doc.Units)
        {
            if (!ids.Add(u.Id)) r.Errors.Add("duplicate unit id " + u.Id);
            if (u.Body is null) continue;
            r.Bodies++;
            try
            {
                var root = XDocument.Parse(u.Body).Root;
                var hash = root is null ? "" : IrExporter.Hash(PlatformIo.CanonicalElement(root));
                if (hash == u.BodyHash) r.Verified++;
                else { r.Failed++; r.Errors.Add("body hash mismatch " + u.Id); }
            }
            catch (Exception ex)
            {
                r.Failed++;
                r.Errors.Add(u.Id + ": " + ex.Message);
            }
        }
        return r;
    }
}

public static class IrPasses
{
    public static List<string> List() =>
    [
        "normalize-attributes", "canonical-body-hash", "interface-section-extract",
        "call-graph-extract", "xref-join", "savepoint-annotate", "dead-tag-scan",
        "lower-json", "lower-dot", "lower-simaticml"
    ];
}
