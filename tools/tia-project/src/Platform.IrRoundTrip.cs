using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace TiaProject;

public sealed class IrMc7Sidecar
{
    public string Schema { get; set; } = "tia-project/ir-mc7/v1";
    public string Unit { get; set; } = "";
    public string? Hex { get; set; }
    public int Length { get; set; }
    public string? Hash { get; set; }
    public int Instructions { get; set; }
    public string? CompilerVersion { get; set; }
}

public sealed class IrTagSidecar
{
    public string Schema { get; set; } = "tia-project/ir-tags/v1";
    public List<IrTag> Tags { get; set; } = [];
}

public sealed class IrRoundTripUnit
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Language { get; set; }
    public string BodyKind { get; set; } = "";
    public string? LoweredFile { get; set; }
    public bool SchemaValid { get; set; }
    public List<string> SchemaErrors { get; set; } = [];
    public int InterfaceSections { get; set; }
    public int InterfaceMembers { get; set; }
    public bool InterfaceMatch { get; set; }
    public string? BodyHashBefore { get; set; }
    public string? BodyHashAfter { get; set; }
    public bool BodyMatch { get; set; }
    public int Tokens { get; set; }
    public bool TokenMatch { get; set; }
    public string? TokenBefore { get; set; }
    public string? TokenAfter { get; set; }
    public int LadParts { get; set; }
    public int LadWires { get; set; }
    public bool LadMatch { get; set; }
    public int OperandNames { get; set; }
    public int Mc7Length { get; set; }
    public string? Mc7HashBefore { get; set; }
    public string? Mc7HashAfter { get; set; }
    public bool Mc7Match { get; set; }
}

public sealed class IrRoundTripResult
{
    public bool Pass { get; set; }
    public string Export { get; set; } = "";
    public string OutPath { get; set; } = "";
    public string SchemasPath { get; set; } = "";
    public int Units { get; set; }
    public int Lowered { get; set; }
    public int SchemaValid { get; set; }
    public int BodyMatches { get; set; }
    public int InterfaceMatches { get; set; }
    public int TokenMatches { get; set; }
    public int LadMatches { get; set; }
    public int Mc7Units { get; set; }
    public int Mc7Matches { get; set; }
    public int Tags { get; set; }
    public int TagsMatch { get; set; }
    public int Edges { get; set; }
    public string? ProjectionBefore { get; set; }
    public string? ProjectionAfter { get; set; }
    public bool ProjectionMatch { get; set; }
    public List<string> Checks { get; set; } = [];
    public List<IrRoundTripUnit> Items { get; set; } = [];
}

public sealed class IrSchemaSet
{
    public required XmlSchemaSet Scl { get; init; }
    public required XmlSchemaSet Flg { get; init; }
    public required XmlSchemaSet Iface { get; init; }
    public required string Directory { get; init; }
}

public static class IrSchemaValidator
{
    public static IrSchemaSet Load(string schemasDir)
    {
        var dir = Path.GetFullPath(schemasDir);
        if (!Directory.Exists(dir)) throw new CliException("schema directory not found: " + dir);
        XmlSchemaSet Build(params string[] files)
        {
            var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
            foreach (var file in files)
            {
                var path = Path.Combine(dir, file);
                if (!File.Exists(path)) throw new CliException("Openness schema not found: " + path);
                set.Add(null, path);
            }
            set.Compile();
            return set;
        }
        return new IrSchemaSet
        {
            Scl = Build("SW.PlcBlocks.SCL_v4.xsd"),
            Flg = Build("SW.PlcBlocks.LADFBD_v5.xsd", "SW.PlcBlocks.Access_v5.xsd"),
            Iface = Build("SW.InterfaceSections_v5.xsd"),
            Directory = dir
        };
    }

    public static List<string> Validate(XElement element, XmlSchemaSet set)
    {
        var errors = new List<string>();
        var stripped = Strip(element);
        new XDocument(stripped).Validate(set, (_, e) => errors.Add($"{e.Severity}: {e.Message}"));
        return errors;
    }

    private static XElement Strip(XElement element) =>
        new(element.Name.LocalName,
            element.Attributes()
                .Where(a => !a.IsNamespaceDeclaration)
                .Select(a => new XAttribute(a.Name.LocalName, a.Value)),
            element.Elements().Select(Strip));
}

public static class IrRoundTrip
{
    private static readonly XNamespace BlocksNs = "http://www.siemens.com/automation/Openness/SW/Blocks/v5";
    private static readonly XNamespace IfaceNs = "http://www.siemens.com/automation/Openness/SW/Interface/v5";
    private static readonly XNamespace DocNs = "http://www.siemens.com/automation/Openness/SW/Document/v5";

    public static IrRoundTripResult Run(ObjectExport export, string outDir, string schemasDir,
        Mc7AnalysisResult? mc7 = null)
    {
        Directory.CreateDirectory(outDir);
        var schemas = IrSchemaValidator.Load(schemasDir);
        var ir = IrExporter.Build(export, null, null, mc7);
        var result = new IrRoundTripResult
        {
            Export = Path.GetFullPath(export.FilePath),
            OutPath = Path.GetFullPath(outDir),
            SchemasPath = schemas.Directory,
            Units = ir.Units.Count,
            Tags = ir.Tags.Count,
            Edges = ir.Edges.Count
        };

        var loweredDir = Path.Combine(outDir, "lowered");
        var mc7Dir = Path.Combine(outDir, "mc7");
        Directory.CreateDirectory(loweredDir);
        Directory.CreateDirectory(mc7Dir);
        var byName = ir.Units.ToDictionary(u => u.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var unit in ir.Units)
        {
            var item = new IrRoundTripUnit
            {
                Id = unit.Id,
                Name = unit.Name,
                Language = unit.Language,
                InterfaceSections = unit.Interface.Count,
                InterfaceMembers = unit.Interface.Sum(s => s.Members.Count),
                Tokens = unit.SclTokens.Count,
                LadParts = unit.LadParts.Count,
                LadWires = unit.LadWires.Count,
                OperandNames = unit.OperandNames.Count,
                Mc7Length = unit.Mc7Length
            };

            var doc = LowerUnit(unit);
            var file = Path.Combine(loweredDir, Safe(unit.Name) + ".simaticml.xml");
            doc.Save(file);
            item.LoweredFile = file;
            result.Lowered++;

            var block = doc.Descendants().FirstOrDefault(e => e.Name.LocalName is
                "SW.Blocks.OB" or "SW.Blocks.FC" or "SW.Blocks.FB" or "SW.Blocks.DB");
            var body = block?.Descendants().FirstOrDefault(e => e.Name.LocalName == "NetworkSource")
                ?.Elements().FirstOrDefault();
            var sections = block?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Sections");

            var schemaErrors = new List<string>();
            if (body is not null)
                schemaErrors.AddRange(IrSchemaValidator.Validate(body,
                    body.Name.LocalName == "FlgNet" ? schemas.Flg : schemas.Scl));
            if (sections is not null)
                schemaErrors.AddRange(IrSchemaValidator.Validate(sections, schemas.Iface));
            item.SchemaErrors = schemaErrors;
            item.SchemaValid = body is not null && schemaErrors.Count == 0;
            if (item.SchemaValid) result.SchemaValid++;

            if (!string.IsNullOrEmpty(unit.Mc7Hex))
            {
                var sidecar = new IrMc7Sidecar
                {
                    Unit = unit.Name,
                    Hex = unit.Mc7Hex,
                    Length = unit.Mc7Length,
                    Hash = unit.Mc7Hash,
                    Instructions = unit.Mc7Instructions,
                    CompilerVersion = unit.CompilerVersion
                };
                File.WriteAllText(Path.Combine(mc7Dir, Safe(unit.Name) + ".hex"), unit.Mc7Hex + "\n");
                JsonUtil.Write(sidecar, Path.Combine(mc7Dir, Safe(unit.Name) + ".json"));
                result.Mc7Units++;
            }

            result.Items.Add(item);
        }

        JsonUtil.Write(new IrTagSidecar { Tags = ir.Tags }, Path.Combine(outDir, "tags.json"));

        var relifted = Relift(loweredDir, outDir, ir);
        foreach (var item in result.Items)
        {
            if (!byName.TryGetValue(item.Name, out var original)) continue;
            var lifted = relifted.Units.FirstOrDefault(u =>
                string.Equals(u.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            if (lifted is null)
            {
                item.SchemaErrors.Add("re-lift did not produce unit " + item.Name);
                continue;
            }

            item.InterfaceMatch = Canonical(original.Interface) == Canonical(lifted.Interface);
            if (item.InterfaceMatch) result.InterfaceMatches++;

            if (original.Language == "SCL")
            {
                item.BodyKind = "StructuredText";
                item.TokenBefore = Canonical(original.SclTokens);
                item.TokenAfter = Canonical(lifted.SclTokens);
                item.TokenMatch = item.TokenBefore == item.TokenAfter;
                item.LadMatch = true;
                if (item.TokenMatch) result.TokenMatches++;
                item.BodyHashBefore = IrExporter.Hash(PlatformIo.CanonicalElement(LowerBody(original)));
                item.BodyHashAfter = IrExporter.Hash(PlatformIo.CanonicalElement(LowerBody(lifted)));
                item.BodyMatch = item.TokenMatch && item.BodyHashBefore == item.BodyHashAfter;
            }
            else
            {
                item.BodyKind = "FlgNetV5";
                item.TokenMatch = true;
                item.LadMatch = Canonical(original.LadParts) == Canonical(lifted.LadParts)
                                && Canonical(original.LadWires) == Canonical(lifted.LadWires)
                                && Canonical(original.OperandNames) == Canonical(lifted.OperandNames);
                if (item.LadMatch) result.LadMatches++;
                item.BodyHashBefore = IrExporter.Hash(PlatformIo.CanonicalElement(LowerBody(original)));
                item.BodyHashAfter = IrExporter.Hash(PlatformIo.CanonicalElement(LowerBody(lifted)));
                item.BodyMatch = item.LadMatch && item.BodyHashBefore == item.BodyHashAfter;
            }
            if (item.BodyMatch) result.BodyMatches++;

            if (!string.IsNullOrEmpty(original.Mc7Hex))
            {
                item.Mc7HashBefore = original.Mc7Hash;
                item.Mc7HashAfter = lifted.Mc7Hash;
                item.Mc7Match = original.Mc7Hex == lifted.Mc7Hex
                               && original.Mc7Hash == lifted.Mc7Hash
                               && original.Mc7Length == lifted.Mc7Length
                               && original.Mc7Instructions == lifted.Mc7Instructions;
                if (item.Mc7Match) result.Mc7Matches++;
            }
            else item.Mc7Match = true;
        }

        var tagsFile = Path.Combine(outDir, "tags.json");
        var tags = JsonSerializer.Deserialize<IrTagSidecar>(File.ReadAllText(tagsFile), JsonUtil.Options)?.Tags ?? [];
        result.TagsMatch = Canonical(tags) == Canonical(ir.Tags) ? tags.Count : 0;

        result.ProjectionBefore = Projection(ir);
        result.ProjectionAfter = Projection(relifted);
        result.ProjectionMatch = result.ProjectionBefore == result.ProjectionAfter;

        result.Checks.Add($"units={result.Units}");
        result.Checks.Add($"lowered={result.Lowered}");
        result.Checks.Add($"schema.valid={result.SchemaValid}/{result.Units}");
        result.Checks.Add($"body.matches={result.BodyMatches}/{result.Units}");
        result.Checks.Add($"interface.matches={result.InterfaceMatches}/{result.Units}");
        result.Checks.Add($"token.matches={result.TokenMatches}");
        result.Checks.Add($"lad.matches={result.LadMatches}");
        result.Checks.Add($"mc7.units={result.Mc7Units}");
        result.Checks.Add($"mc7.matches={result.Mc7Matches}/{result.Mc7Units}");
        result.Checks.Add($"tags={result.Tags} tags.match={result.TagsMatch}");
        result.Checks.Add($"edges={result.Edges}");
        result.Checks.Add($"projection.before={result.ProjectionBefore}");
        result.Checks.Add($"projection.after={result.ProjectionAfter}");
        result.Checks.Add($"projection.match={result.ProjectionMatch}");
        foreach (var item in result.Items)
        {
            result.Checks.Add(
                $"unit.{item.Name}.language={item.Language} body={item.BodyKind} schema={item.SchemaValid} " +
                $"bodyMatch={item.BodyMatch} interfaceMatch={item.InterfaceMatch} tokens={item.Tokens} " +
                $"tokenMatch={item.TokenMatch} ladParts={item.LadParts} ladWires={item.LadWires} " +
                $"ladMatch={item.LadMatch} operands={item.OperandNames} mc7Length={item.Mc7Length} mc7Match={item.Mc7Match}");
            foreach (var error in item.SchemaErrors) result.Checks.Add($"unit.{item.Name}.schema.error={error}");
        }

        result.Pass = result.Units > 0
                      && result.Lowered == result.Units
                      && result.SchemaValid == result.Units
                      && result.BodyMatches == result.Units
                      && result.InterfaceMatches == result.Units
                      && result.Mc7Matches == result.Mc7Units
                      && result.TagsMatch == result.Tags
                      && result.ProjectionMatch;
        JsonUtil.Write(result, Path.Combine(outDir, "roundtrip.json"));
        return result;
    }

    private static XDocument LowerUnit(IrUnit unit)
    {
        var interfaceElement = new XElement(IfaceNs + "Sections",
            new XAttribute("xmlns", IfaceNs.NamespaceName),
            unit.Interface.Select(section =>
                new XElement(IfaceNs + "Section",
                    new XAttribute("Name", section.Name),
                    section.Members.Select(member =>
                        new XElement(IfaceNs + "Member",
                            new XAttribute("Name", member.Name),
                            new XAttribute("Datatype", member.Datatype))))));

        var body = LowerBody(unit);
        var blockName = BlockElement(unit.Subtype);
        var block = new XElement(BlocksNs + blockName,
            new XAttribute("ID", "0"),
            new XElement(BlocksNs + "AttributeList",
                new XElement(BlocksNs + "Interface", interfaceElement),
                new XElement(BlocksNs + "Name", unit.Name),
                new XElement(BlocksNs + "Number", unit.Number ?? 0),
                new XElement(BlocksNs + "ProgrammingLanguage", unit.Language ?? "LAD"),
                new XElement(BlocksNs + "HeaderVersion", "0.1"),
                new XElement(BlocksNs + "MemoryLayout", "Optimized")),
            new XElement(BlocksNs + "ObjectList",
                new XElement(BlocksNs + "CompileUnit",
                    new XAttribute("ID", "1"),
                    new XElement(BlocksNs + "AttributeList",
                        new XElement(BlocksNs + "NetworkSource", body),
                        new XElement(BlocksNs + "ProgrammingLanguage", unit.Language ?? "LAD")),
                    new XElement(BlocksNs + "ObjectList"))));

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(DocNs + "Document",
                new XElement(DocNs + "Engineering", new XAttribute("version", "V21")),
                block));
    }

    private static XElement LowerBody(IrUnit unit)
    {
        if (unit.Language == "SCL")
        {
            var text = new XElement("StructuredText", new XAttribute("xmlns", ""));
            foreach (var token in unit.SclTokens)
            {
                switch (token.Kind)
                {
                    case "access":
                        text.Add(new XElement("Access",
                            new XAttribute("Scope", "GlobalVariable"),
                            new XElement("Symbol",
                                new XElement("Component", new XAttribute("Name", token.Name ?? "")))));
                        break;
                    case "token":
                        text.Add(new XElement("Token", new XAttribute("Text", token.Text ?? "")));
                        break;
                    case "text":
                        text.Add(new XElement("Text", token.Text ?? ""));
                        break;
                }
            }
            return text;
        }

        var parts = new XElement("Parts");
        foreach (var part in unit.LadParts)
        {
            if (part.RefId is int refId)
            {
                var name = unit.OperandNames.GetValueOrDefault(refId) ?? part.Name ?? $"Ref_{refId}";
                parts.Add(new XElement("Access",
                    new XAttribute("UId", part.UId),
                    new XAttribute("Scope", "GlobalVariable"),
                    new XElement("Symbol",
                        new XElement("Component", new XAttribute("Name", name)))));
            }
            else
            {
                parts.Add(new XElement("Part",
                    new XAttribute("UId", part.UId),
                    new XAttribute("Name", part.Gate)));
            }
        }

        XElement? wires = unit.LadWires.Count > 0 ? new XElement("Wires") : null;
        foreach (var wire in unit.LadWires)
        {
            var element = new XElement("Wire", new XAttribute("UId", wire.UId));
            foreach (var endpoint in wire.Endpoints)
            {
                switch (endpoint.Kind)
                {
                    case "powerrail":
                        element.Add(new XElement("Powerrail"));
                        break;
                    case "pcon":
                        element.Add(new XElement("NameCon",
                            new XAttribute("UId", endpoint.UId ?? 0),
                            new XAttribute("Name", endpoint.PinName ?? "")));
                        break;
                    case "ocon":
                        element.Add(new XElement("IdentCon", new XAttribute("UId", endpoint.UId ?? 0)));
                        break;
                    case "openbranch":
                        element.Add(new XElement("Openbranch"));
                        break;
                    case "opencon":
                        element.Add(new XElement("OpenCon", new XAttribute("UId", endpoint.UId ?? 0)));
                        break;
                }
            }
            wires!.Add(element);
        }
        var flgNet = new XElement("FlgNet", new XAttribute("xmlns", ""), parts);
        if (wires is not null) flgNet.Add(wires);
        return flgNet;
    }

    private static IrDocument Relift(string loweredDir, string sidecarDir, IrDocument source)
    {
        var doc = new IrDocument();
        foreach (var file in Directory.GetFiles(loweredDir, "*.simaticml.xml").OrderBy(f => f, StringComparer.Ordinal))
        {
            var xdoc = XDocument.Load(file, LoadOptions.PreserveWhitespace);
            var block = xdoc.Descendants().FirstOrDefault(e => e.Name.LocalName is
                "SW.Blocks.OB" or "SW.Blocks.FC" or "SW.Blocks.FB" or "SW.Blocks.DB");
            if (block is null) continue;
            var attributes = block.Elements().FirstOrDefault(e => e.Name.LocalName == "AttributeList");
            string? Value(string name) => attributes?.Elements()
                .FirstOrDefault(e => e.Name.LocalName == name)?.Value;

            var sections = attributes?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Sections");
            var interfaceSections = ParseSections(sections);
            var name = Value("Name") ?? Path.GetFileNameWithoutExtension(file);
            var language = Value("ProgrammingLanguage");
            var numberText = Value("Number");
            int? number = int.TryParse(numberText, out var parsedNumber) ? parsedNumber : null;
            var sourceUnit = source.Units.FirstOrDefault(u =>
                string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));

            var unit = new IrUnit
            {
                Id = "block:" + name,
                Name = name,
                Language = language,
                Number = number,
                Interface = interfaceSections
            };
            unit.InterfaceSections = interfaceSections.Select(s => s.Name).ToList();

            var body = block.Descendants().FirstOrDefault(e => e.Name.LocalName == "NetworkSource")
                ?.Elements().FirstOrDefault();
            if (body is not null)
            {
                if (body.Name.LocalName == "StructuredText")
                {
                    unit.SclTokens = ParseSclTokens(body);
                }
                else if (body.Name.LocalName == "FlgNet")
                {
                    unit.LadParts = ParseLadPartsV5(body);
                    unit.LadWires = ParseLadWiresV5(body);
                    if (sourceUnit is not null)
                    {
                        unit.OperandNames = new Dictionary<int, string>(sourceUnit.OperandNames);
                        var nameToRef = sourceUnit.OperandNames
                            .GroupBy(kv => kv.Value, StringComparer.Ordinal)
                            .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);
                        foreach (var part in unit.LadParts)
                        {
                            if (part.Name is not null && nameToRef.TryGetValue(part.Name, out var refId))
                                part.RefId = refId;
                        }
                    }
                }
            }

            var sidecarPath = Path.Combine(sidecarDir, "mc7", Safe(name) + ".json");
            if (File.Exists(sidecarPath))
            {
                var sidecar = JsonSerializer.Deserialize<IrMc7Sidecar>(
                    File.ReadAllText(sidecarPath), JsonUtil.Options);
                if (sidecar is not null)
                {
                    unit.Mc7Hex = sidecar.Hex;
                    unit.Mc7Hash = sidecar.Hash;
                    unit.Mc7Length = sidecar.Length;
                    unit.Mc7Instructions = sidecar.Instructions;
                    unit.CompilerVersion = sidecar.CompilerVersion;
                }
            }
            doc.Units.Add(unit);
        }

        var tagsPath = Path.Combine(sidecarDir, "tags.json");
        if (File.Exists(tagsPath))
            doc.Tags = JsonSerializer.Deserialize<IrTagSidecar>(File.ReadAllText(tagsPath), JsonUtil.Options)?.Tags ?? [];
        return doc;
    }

    private static List<IrLadPart> ParseLadPartsV5(XElement flgNet)
    {
        var parts = new List<IrLadPart>();
        var container = flgNet.Elements().FirstOrDefault(e => e.Name.LocalName == "Parts");
        if (container is null) return parts;
        foreach (var element in container.Elements())
        {
            var uid = int.TryParse(element.Attribute("UId")?.Value, out var id) ? id : 0;
            if (element.Name.LocalName == "Part")
            {
                parts.Add(new IrLadPart { UId = uid, Gate = element.Attribute("Name")?.Value ?? "" });
            }
            else if (element.Name.LocalName == "Access")
            {
                var name = element.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "Component")?.Attribute("Name")?.Value;
                parts.Add(new IrLadPart { UId = uid, Name = name });
            }
        }
        return parts;
    }

    private static List<IrLadWire> ParseLadWiresV5(XElement flgNet)
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
                    "NameCon" => "pcon",
                    "IdentCon" => "ocon",
                    "Openbranch" => "openbranch",
                    "OpenCon" => "opencon",
                    _ => null
                };
                if (kind is null) continue;
                wire.Endpoints.Add(new IrLadEndpoint
                {
                    Kind = kind,
                    UId = int.TryParse(child.Attribute("UId")?.Value, out var uid) ? uid : null,
                    PinName = child.Attribute("Name")?.Value
                });
            }
            wires.Add(wire);
        }
        return wires;
    }

    public static List<IrInterfaceSection> ParseSections(XElement? sections)
    {
        if (sections is null) return [];
        return sections.Elements().Where(e => e.Name.LocalName == "Section")
            .Select(section => new IrInterfaceSection
            {
                Name = section.Attribute("Name")?.Value ?? "",
                Members = section.Elements().Where(e => e.Name.LocalName == "Member")
                    .Select(member => new IrInterfaceMember
                    {
                        Name = member.Attribute("Name")?.Value ?? "",
                        Datatype = member.Attribute("Datatype")?.Value ?? "Bool"
                    }).ToList()
            }).ToList();
    }

    public static List<IrSclToken> ParseSclTokens(XElement structuredText)
    {
        var tokens = new List<IrSclToken>();
        foreach (var child in structuredText.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Access":
                {
                    var name = child.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "Component")?.Attribute("Name")?.Value;
                    if (!string.IsNullOrEmpty(name))
                        tokens.Add(new IrSclToken { Kind = "access", Name = name });
                    break;
                }
                case "Token":
                    tokens.Add(new IrSclToken { Kind = "token", Text = child.Attribute("Text")?.Value ?? "" });
                    break;
                case "Text":
                    tokens.Add(new IrSclToken { Kind = "text", Text = child.Value });
                    break;
            }
        }
        return tokens;
    }

    private static string Projection(IrDocument doc)
    {
        var normalized = new
        {
            units = doc.Units.OrderBy(u => u.Name, StringComparer.Ordinal).Select(u => new
            {
                u.Name,
                u.Language,
                u.Number,
                iface = u.Interface.Select(s => new
                {
                    s.Name,
                    members = s.Members.Select(m => new { m.Name, m.Datatype })
                }),
                tokens = u.SclTokens.Select(t => new { t.Kind, t.Name, t.Text }),
                ladParts = u.LadParts.Select(p => new { p.UId, p.Gate, p.RefId, p.Name }),
                ladWires = u.LadWires.Select(w => new
                {
                    w.UId,
                    endpoints = w.Endpoints.Select(e => new { e.Kind, e.UId, e.PinName })
                }),
                operands = u.OperandNames.OrderBy(kv => kv.Key).Select(kv => new { kv.Key, kv.Value }),
                u.Mc7Hash,
                u.Mc7Length,
                u.Mc7Instructions
            }),
            tags = doc.Tags.OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => new { t.Name, t.Address, t.DataType })
        };
        return IrExporter.Hash(JsonSerializer.Serialize(normalized, JsonUtil.Options));
    }

    private static string Canonical<T>(T value) => JsonSerializer.Serialize(value, JsonUtil.Options);

    private static string BlockElement(string? subtype)
    {
        if (subtype is null) return "SW.Blocks.FC";
        if (subtype.StartsWith("OB", StringComparison.OrdinalIgnoreCase)) return "SW.Blocks.OB";
        if (subtype.StartsWith("FB", StringComparison.OrdinalIgnoreCase)) return "SW.Blocks.FB";
        if (subtype.StartsWith("FC", StringComparison.OrdinalIgnoreCase)) return "SW.Blocks.FC";
        if (subtype.StartsWith("DB", StringComparison.OrdinalIgnoreCase)) return "SW.Blocks.DB";
        return "SW.Blocks.FC";
    }

    private static string Safe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "unit" : name;
    }
}
