using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Z3;

namespace TiaProject;

public sealed class ContractSection
{
    public string Section { get; set; } = "";
    public List<string> Members { get; set; } = [];
}

public sealed class ContractVerification
{
    public bool Valid { get; set; }
    public bool SchemaValid { get; set; }
    public bool InterfaceValid { get; set; }
    public bool EffectsValid { get; set; }
    public bool PrePostValid { get; set; }
    public string SmtResult { get; set; } = "unknown";
    public string Coverage { get; set; } = "modeled";
    public List<string> Findings { get; set; } = [];
    public string? CounterexampleModel { get; set; }
}

public sealed class VciProvenanceRecord
{
    public string RelativePath { get; set; } = "";
    public string? ExpectedHash { get; set; }
    public string? Description { get; set; }
    public string? ActualHash { get; set; }
    public bool Match { get; set; }
    public string Status { get; set; } = "missing";
    public string? Error { get; set; }
}

public static class ContractSchema
{
    public const string Id = "tia-project/block-contract/v1";

    public const string Json = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "$id": "https://tia-project.local/schemas/block-contract/v1",
          "title": "TIA Project block contract",
          "type": "object",
          "required": ["schema", "block", "interface", "preconditions", "postconditions", "effects"],
          "additionalProperties": false,
          "properties": {
            "schema": { "const": "tia-project/block-contract/v1" },
            "block": { "type": "string", "minLength": 1 },
            "interface": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["section", "members"],
                "additionalProperties": false,
                "properties": {
                  "section": { "type": "string", "minLength": 1 },
                  "members": { "type": "array", "items": { "type": "string", "minLength": 1 } }
                }
              }
            },
            "preconditions": { "type": "array", "items": { "type": "string", "minLength": 1 } },
            "postconditions": { "type": "array", "items": { "type": "string", "minLength": 1 } },
            "effects": { "type": "array", "items": { "type": "string", "minLength": 1 } },
            "description": { "type": "string" }
          }
        }
        """;

    public static string Write(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, "contract.schema.json");
        File.WriteAllText(path, Json, new UTF8Encoding(false));
        return path;
    }
}

public static class ContractVerifier
{
    public const string SchemaId = ContractSchema.Id;

    public static ContractVerification Verify(ObjectExport export, Mc7AnalysisResult analysis, string blockName, MarketContract contract)
    {
        var result = new ContractVerification();
        result.SchemaValid = string.Equals(contract.Schema, SchemaId, StringComparison.Ordinal);
        if (!result.SchemaValid) result.Findings.Add($"schema must be {SchemaId}");

        var ir = IrExporter.Build(export);
        var unit = ir.Units.FirstOrDefault(u => string.Equals(u.Name, blockName, StringComparison.OrdinalIgnoreCase));
        if (unit is null)
        {
            result.Findings.Add("block not found in IR: " + blockName);
            result.Valid = false;
            return result;
        }
        if (!string.Equals(contract.Block, blockName, StringComparison.OrdinalIgnoreCase))
            result.Findings.Add($"contract block '{contract.Block}' does not match '{blockName}'");

        var sections = InterfaceSections(export, blockName);
        result.InterfaceValid = true;
        foreach (var cs in contract.Interface)
        {
            if (!sections.TryGetValue(cs.Section, out var members))
            {
                result.InterfaceValid = false;
                result.Findings.Add("contract interface section missing: " + cs.Section);
                continue;
            }
            foreach (var member in cs.Members)
            {
                if (members.Contains(member)) continue;
                result.InterfaceValid = false;
                result.Findings.Add($"contract member missing: {cs.Section}.{member}");
            }
        }

        var symbols = Symbols(export, blockName);
        result.EffectsValid = true;
        foreach (var effect in contract.Effects)
        {
            var name = effect.Contains(':') ? effect[(effect.IndexOf(':') + 1)..] : effect;
            if (symbols.Contains(name) || sections.Values.Any(s => s.Contains(name))) continue;
            result.EffectsValid = false;
            result.Findings.Add("contract effect not found in block symbols/interface: " + effect);
        }

        var blockData = analysis.Blocks.FirstOrDefault(b => string.Equals(b.Name, blockName, StringComparison.OrdinalIgnoreCase));
        if (blockData is null || !blockData.HasMc7 || blockData.CodeHex is null)
        {
            result.PrePostValid = false;
            result.Findings.Add("block has no MC7 code for pre/post verification");
            result.Valid = false;
            return result;
        }

        try
        {
            using var ctx = new Context();
            var state = Mc7SemanticChecker.Execute(Convert.FromHexString(blockData.CodeHex), ctx);
            var pre = ContractExprParser.ParseAll(ctx, state, contract.Preconditions);
            var post = ContractExprParser.ParseAll(ctx, state, contract.Postconditions);
            var solver = ctx.MkSolver();
            solver.Assert(pre);
            solver.Assert(ctx.MkNot(post));
            var status = solver.Check();
            result.SmtResult = status == Status.UNSATISFIABLE ? "unsat" : status == Status.SATISFIABLE ? "sat" : "unknown";
            result.PrePostValid = status == Status.UNSATISFIABLE;
            if (status == Status.SATISFIABLE)
            {
                result.CounterexampleModel = solver.Model.ToString();
                result.Findings.Add("postcondition is not implied by precondition + MC7 state");
            }
            else if (status == Status.UNKNOWN)
            {
                result.Findings.Add("solver returned unknown");
            }
            var known = blockData.Instructions.Count(i => true);
            result.Coverage = blockData.Instructions.Count == 0 ? "no-instructions" : "mc7-semantic";
        }
        catch (Exception ex)
        {
            result.PrePostValid = false;
            result.Findings.Add("contract expression/error: " + ex.Message);
        }

        result.Valid = result.SchemaValid && result.InterfaceValid && result.EffectsValid && result.PrePostValid;
        return result;
    }

    public static Dictionary<string, HashSet<string>> InterfaceSections(ObjectExport export, string blockName)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var block = export.Objects.FirstOrDefault(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)
            && string.Equals(o.Name, blockName, StringComparison.OrdinalIgnoreCase));
        if (block is null) return result;
        var link = block.Links.FirstOrDefault(l => l.TypeId == 2233868);
        if (link.InstanceId == 0) return result;
        var root = export.Objects.FirstOrDefault(o => o.InstanceId == link.InstanceId);
        if (root is null || !root.CoreBlobs.TryGetValue("Payload", out var hex)) return result;
        var text = TextUtil.DecodeCoreBlob(hex);
        if (text is null) return result;
        try
        {
            var doc = XDocument.Parse(text);
            foreach (var member in doc.Root?.Elements().Where(e => e.Name.LocalName == "Member") ?? [])
            {
                var name = member.Attribute("Name")?.Value;
                if (string.IsNullOrEmpty(name)) continue;
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var child in member.Elements().Where(e => e.Name.LocalName == "Member"))
                {
                    var cn = child.Attribute("Name")?.Value;
                    if (!string.IsNullOrEmpty(cn)) set.Add(cn);
                }
                result[name] = set;
            }
        }
        catch { }
        return result;
    }

    private static HashSet<string> Symbols(ObjectExport export, string blockName)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var block = export.Objects.FirstOrDefault(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)
            && string.Equals(o.Name, blockName, StringComparison.OrdinalIgnoreCase));
        if (block is null) return set;
        var body = PlatformIo.Body(block, export);
        if (body is not null)
        {
            foreach (var e in body.Descendants().Where(e => e.Name.LocalName is "SymVa" or "Component"))
            {
                var n = e.Attribute("ODN")?.Value ?? e.Attribute("Name")?.Value;
                if (!string.IsNullOrEmpty(n)) set.Add(n);
            }
        }
        foreach (var call in IrExporter.Build(export).Units.Where(u => u.Name == blockName).SelectMany(u => u.Calls))
            set.Add(call);
        return set;
    }
}

internal sealed class ContractExprParser
{
    private readonly Context _ctx;
    private readonly Mc7SymState _state;
    private readonly List<string> _tokens;
    private int _pos;

    public ContractExprParser(Context ctx, Mc7SymState state, string expression)
    {
        _ctx = ctx;
        _state = state;
        _tokens = Tokenize(expression);
    }

    public static BoolExpr ParseAll(Context ctx, Mc7SymState state, IEnumerable<string> expressions)
    {
        var parts = expressions.Select(e => ParseOne(ctx, state, e)).ToArray();
        return parts.Length == 0 ? ctx.MkTrue() : ctx.MkAnd(parts);
    }

    private static BoolExpr ParseOne(Context ctx, Mc7SymState state, string expression)
    {
        var parser = new ContractExprParser(ctx, state, expression);
        var expr = parser.ParseOr();
        if (parser._pos != parser._tokens.Count)
            throw new FormatException("unexpected token: " + parser._tokens[parser._pos]);
        return expr;
    }

    private BoolExpr ParseOr()
    {
        var left = ParseAnd();
        while (Peek("||")) { _pos++; left = _ctx.MkOr(left, ParseAnd()); }
        return left;
    }

    private BoolExpr ParseAnd()
    {
        var left = ParseNot();
        while (Peek("&&")) { _pos++; left = _ctx.MkAnd(left, ParseNot()); }
        return left;
    }

    private BoolExpr ParseNot()
    {
        if (Peek("!")) { _pos++; return _ctx.MkNot(ParseNot()); }
        return ParseAtom();
    }

    private BoolExpr ParseAtom()
    {
        if (_pos >= _tokens.Count) throw new FormatException("unexpected end of expression");
        var t = _tokens[_pos++];
        if (t == "true") return _ctx.MkTrue();
        if (t == "false") return _ctx.MkFalse();
        if (t == "RLO")
        {
            if (Peek("==") || Peek("!="))
            {
                var op = _tokens[_pos++];
                var rhs = ParseAtom();
                var eq = _ctx.MkEq(_state.Rlo, rhs);
                return op == "==" ? eq : _ctx.MkNot(eq);
            }
            return _state.Rlo;
        }
        if (t == "(")
        {
            var e = ParseOr();
            if (!Peek(")")) throw new FormatException("missing )");
            _pos++;
            return e;
        }
        throw new FormatException("unsupported token: " + t);
    }

    private bool Peek(string token) => _pos < _tokens.Count && _tokens[_pos] == token;

    private static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        for (int i = 0; i < s.Length;)
        {
            if (char.IsWhiteSpace(s[i])) { i++; continue; }
            if (i + 1 < s.Length && (s.Substring(i, 2) is "==" or "!=" or "&&" or "||"))
            {
                tokens.Add(s.Substring(i, 2));
                i += 2;
                continue;
            }
            if (s[i] is '(' or ')' or '!')
            {
                tokens.Add(s[i].ToString());
                i++;
                continue;
            }
            if (char.IsLetter(s[i]))
            {
                int j = i;
                while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                tokens.Add(s[i..j]);
                i = j;
                continue;
            }
            throw new FormatException("unsupported character: " + s[i]);
        }
        return tokens;
    }
}

public static class VciProvenance
{
    public static VciProvenanceRecord Bind(string projectRoot, string vciPath, string relativePath = "System/PEData.plf")
    {
        var rec = new VciProvenanceRecord { RelativePath = relativePath };
        var temp = FileUtil.CopyToTemp(vciPath, Path.Combine(Path.GetTempPath(), "tia-project", "market-vci"));
        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = temp, Mode = SqliteOpenMode.ReadOnly }.ToString();
            using var conn = new SqliteConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT RelativeFilePath, FileHash, FileHashDescription FROM FileRecord WHERE replace(RelativeFilePath,'\\','/') = $p LIMIT 1";
            cmd.Parameters.AddWithValue("$p", relativePath.Replace('\\', '/'));
            using var r = cmd.ExecuteReader();
            if (!r.Read())
            {
                rec.Status = "missing";
                rec.Match = false;
                return rec;
            }
            rec.ExpectedHash = r.IsDBNull(1) ? null : r.GetString(1);
            rec.Description = r.IsDBNull(2) ? null : r.GetString(2);
            var full = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            rec.ActualHash = File.Exists(full) ? HashFile(full, rec.Description) : null;
            rec.Match = rec.ExpectedHash is not null && rec.ActualHash is not null
                        && string.Equals(rec.ExpectedHash, rec.ActualHash, StringComparison.OrdinalIgnoreCase);
            rec.Status = rec.Match ? "match" : "mismatch";
            return rec;
        }
        finally
        {
            FileUtil.TryDelete(temp);
            FileUtil.TryDeleteDirectory(Path.GetDirectoryName(temp)!);
        }
    }

    public static string CreateFixture(string sourceVci, string targetVci, string relativePath, string hash, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetVci))!);
        File.Copy(sourceVci, targetVci, overwrite: true);
        var cs = new SqliteConnectionStringBuilder { DataSource = targetVci }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=OFF; DELETE FROM FileRecord; INSERT INTO FileRecord(BookkeepingId, RelativeFilePath, FileHash, FileHashDescription) VALUES(0, $p, $h, $d);";
        cmd.Parameters.AddWithValue("$p", relativePath.Replace('\\', '/'));
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.Parameters.AddWithValue("$d", description);
        cmd.ExecuteNonQuery();
        return targetVci;
    }

    public static string HashFile(string path, string? description)
    {
        var d = (description ?? "").ToUpperInvariant();
        using var stream = File.OpenRead(path);
        byte[] hash = d.Contains("SHA512") ? SHA512.HashData(stream)
            : d.Contains("SHA1") ? SHA1.HashData(stream)
            : d.Contains("MD5") ? MD5.HashData(stream)
            : SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed class MarketSelfTestResult
{
    public bool Pass { get; set; }
    public List<string> Checks { get; set; } = [];
    public MarketPackage PositivePackage { get; set; } = new();
    public MarketPackage NegativeContract { get; set; } = new();
    public MarketPackage NegativeProvenance { get; set; } = new();
    public MarketPackage NegativeInterface { get; set; } = new();
}

public static class MarketSelfTest
{
    public static MarketSelfTestResult Run(ProjectPaths paths, string exportPath, string keyBase, string outDir)
    {
        Directory.CreateDirectory(outDir);
        ContractSchema.Write(outDir);
        if (!File.Exists(keyBase + ".private.pem")) LogicRegistry.InitKey(keyBase);
        var export = ObjectExport.Read(exportPath);
        var analysis = Mc7Analyzer.Analyze(export, FileUtil.FindTiaBin());
        var main = IrExporter.Build(export).Units.First(u => u.Name == "Main");
        var sections = main.InterfaceSections.Select(s => new ContractSection { Section = s }).ToList();
        var actualHash = PlatformIo.HashFile(paths.Plf);
        var goodVci = VciProvenance.CreateFixture(paths.VciDb, Path.Combine(outDir, "vci-good.db"), "System/PEData.plf", actualHash, "SHA256");
        var badVci = VciProvenance.CreateFixture(paths.VciDb, Path.Combine(outDir, "vci-bad.db"), "System/PEData.plf", new string('0', 64), "SHA256");

        var good = new MarketContract
        {
            Block = "Main",
            Interface = sections,
            Preconditions = ["true"],
            Postconditions = ["RLO == true || RLO == false"],
            Effects = []
        };
        var positive = BlockMarket.Publish(export, analysis, "Main", good, keyBase + ".private.pem", paths.Root, goodVci);
        var positiveOk = positive.ContractVerified && positive.ProvenanceVerified
                         && BlockMarket.Verify(positive, keyBase + ".public.pem", paths.Root, goodVci, export, analysis);

        var badContract = new MarketContract
        {
            Block = "Main",
            Interface = sections,
            Preconditions = ["true"],
            Postconditions = ["RLO == true && RLO == false"],
            Effects = []
        };
        var negativeContract = BlockMarket.Publish(export, analysis, "Main", badContract, keyBase + ".private.pem", paths.Root, goodVci);

        var negativeProvenance = BlockMarket.Publish(export, analysis, "Main", good, keyBase + ".private.pem", paths.Root, badVci);

        var badInterface = new MarketContract
        {
            Block = "Main",
            Interface = [new ContractSection { Section = "Bogus" }],
            Preconditions = ["true"],
            Postconditions = ["true"],
            Effects = []
        };
        var negativeInterface = BlockMarket.Publish(export, analysis, "Main", badInterface, keyBase + ".private.pem", paths.Root, goodVci);
        var negativeContractReverify = BlockMarket.Verify(negativeContract, keyBase + ".public.pem", paths.Root, goodVci, export, analysis);
        var negativeProvenanceReverify = BlockMarket.Verify(positive, keyBase + ".public.pem", paths.Root, badVci, export, analysis);

        var checks = new List<string>
        {
            $"positive.contract={positive.ContractVerified}",
            $"positive.provenance={positive.ProvenanceVerified}",
            $"positive.signature={positiveOk}",
            $"positive.smt={positive.ContractVerification?.SmtResult}",
            $"negative-contract.contract={negativeContract.ContractVerified}",
            $"negative-contract.expected=false",
            $"negative-contract.reverify={negativeContractReverify}",
            $"negative-contract.reverify.expected=false",
            $"negative-provenance.provenance={negativeProvenance.ProvenanceVerified}",
            $"negative-provenance.expected=false",
            $"negative-provenance.reverify={negativeProvenanceReverify}",
            $"negative-provenance.reverify.expected=false",
            $"negative-interface.contract={negativeInterface.ContractVerified}",
            $"negative-interface.expected=false"
        };
        var pass = positiveOk
                   && !negativeContract.ContractVerified && !negativeContractReverify
                   && !negativeProvenance.ProvenanceVerified && !negativeProvenanceReverify
                   && !negativeInterface.ContractVerified;
        var result = new MarketSelfTestResult
        {
            Pass = pass,
            Checks = checks,
            PositivePackage = positive,
            NegativeContract = negativeContract,
            NegativeProvenance = negativeProvenance,
            NegativeInterface = negativeInterface
        };
        JsonUtil.Write(positive, Path.Combine(outDir, "package.json"));
        JsonUtil.Write(result, Path.Combine(outDir, "market-test.json"));
        return result;
    }
}
