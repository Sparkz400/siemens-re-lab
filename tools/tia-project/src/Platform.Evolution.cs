using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace TiaProject;

public sealed class IoTraceSample
{
    public long T { get; set; }
    public Dictionary<string, double> Inputs { get; set; } = [];
    public Dictionary<string, double> Expected { get; set; } = [];
}

public sealed class IoTrace
{
    public string Name { get; set; } = "io";
    public string Block { get; set; } = "";
    public string? Source { get; set; }
    public List<IoTraceSample> Samples { get; set; } = [];
}

public sealed class SclEvaluation
{
    public bool Ok { get; set; } = true;
    public int Unmodeled { get; set; }
    public List<string> Errors { get; set; } = [];
    public Dictionary<string, double> Outputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Assigned { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class SclInterpreter
{
    public static SclEvaluation Evaluate(XElement body, Dictionary<string, double> inputs)
    {
        var state = new SclEvaluation();
        var vars = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in inputs) vars[kv.Key] = kv.Value;
        try
        {
            var root = body.Name.LocalName == "RootStatements"
                ? body
                : body.Descendants().FirstOrDefault(e => e.Name.LocalName == "RootStatements") ?? body;
            ExecuteStatements(root, vars, state);
            state.Outputs = new Dictionary<string, double>(vars, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            state.Ok = false;
            state.Errors.Add(ex.Message);
        }
        return state;
    }

    public static bool TryParseNumber(XElement e, out double value)
    {
        value = 0;
        foreach (var name in new[] { "TE", "Value", "V", "Text" })
        {
            var text = e.Attribute(name)?.Value;
            if (TryParseNumber(text, out value)) return true;
        }
        return TryParseNumber(e.Value, out value);
    }

    public static bool TryParseNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().Trim('"', '\'');
        if (s.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) { value = 1; return true; }
        if (s.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) { value = 0; return true; }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static void ExecuteStatements(XElement container, Dictionary<string, double> vars, SclEvaluation state)
    {
        foreach (var stmt in container.Elements().Where(e => e.Name.LocalName == "Statement"))
            ExecuteStatement(stmt, vars, state);
    }

    private static void ExecuteStatement(XElement stmt, Dictionary<string, double> vars, SclEvaluation state)
    {
        var si = stmt.Attribute("SI")?.Value ?? "";
        var hasAssignToken = stmt.Elements().Any(e => e.Name.LocalName == "Token" && e.Attribute("TE")?.Value == ":=");
        if (si.Contains("Ass", StringComparison.OrdinalIgnoreCase) || hasAssignToken)
        {
            var target = stmt.Elements().FirstOrDefault(e => e.Name.LocalName == "SymVa")?.Attribute("ODN")?.Value?.TrimStart('#');
            var expressions = stmt.Elements().Where(e => e.Name.LocalName == "Expression").ToList();
            var rhs = expressions.LastOrDefault();
            if (target is null && expressions.Count > 0)
                target = expressions[0].Descendants().FirstOrDefault(e => e.Name.LocalName == "SymVa")
                    ?.Attribute("ODN")?.Value?.TrimStart('#');
            double? value = rhs is null ? null : Eval(rhs, vars, state);
            if (target is null)
            {
                state.Unmodeled++;
                return;
            }
            if (value is null) value = vars.GetValueOrDefault(target, 0);
            vars[target] = value.Value;
            state.Assigned.Add(target);
            return;
        }

        if (si.Contains("If", StringComparison.OrdinalIgnoreCase))
        {
            var condition = stmt.Elements().FirstOrDefault(e => e.Name.LocalName == "Expression");
            var branches = stmt.Elements().Where(e => e.Name.LocalName == "Statement").ToList();
            var cond = condition is null ? null : Eval(condition, vars, state);
            if (cond is null || branches.Count == 0)
            {
                state.Unmodeled++;
                return;
            }
            var take = cond.Value != 0 ? 0 : 1;
            if (take < branches.Count) ExecuteStatement(branches[take], vars, state);
            return;
        }

        if (si.Contains("Call", StringComparison.OrdinalIgnoreCase))
        {
            state.Unmodeled++;
            return;
        }

        foreach (var nested in stmt.Elements().Where(e => e.Name.LocalName == "Statement"))
            ExecuteStatement(nested, vars, state);
    }

    private static double? Eval(XElement e, Dictionary<string, double> vars, SclEvaluation state)
    {
        var name = e.Name.LocalName;
        if (name is "SymVa" or "Symbol")
        {
            var n = (e.Attribute("ODN")?.Value ?? e.Attribute("Name")?.Value)?.TrimStart('#');
            if (string.IsNullOrEmpty(n)) { state.Unmodeled++; return null; }
            return vars.GetValueOrDefault(n, 0);
        }
        if (name is "Const" or "Literal" or "Immediate" or "Value")
            return TryParseNumber(e, out var literal) ? literal : null;
        if (name is "Token" or "EToken" or "Operator") return null;

        var children = e.Elements().Where(c => c.Name.LocalName is not "Token" and not "EToken" and not "Operator"
            and not "BL" and not "FiSt" and not "NumBLs"
            && !c.Name.LocalName.StartsWith("Op", StringComparison.Ordinal)).ToList();
        var token = e.Elements().FirstOrDefault(c => c.Name.LocalName is "Token" or "EToken" or "Operator");
        var opElement = e.Elements().FirstOrDefault(c => c.Name.LocalName.StartsWith("Op", StringComparison.Ordinal));
        var op = token?.Attribute("TE")?.Value ?? token?.Value.Trim() ?? OperatorName(opElement?.Name.LocalName);
        if (!string.IsNullOrEmpty(op) && children.Count >= 2)
        {
            var a = Eval(children[0], vars, state);
            var b = Eval(children[1], vars, state);
            if (a is null || b is null) return null;
            return ApplyBinary(op, a.Value, b.Value, state);
        }
        if (!string.IsNullOrEmpty(op) && children.Count == 1)
        {
            var a = Eval(children[0], vars, state);
            if (a is null) return null;
            return ApplyUnary(op, a.Value, state);
        }
        if (children.Count == 1) return Eval(children[0], vars, state);
        if (children.Count >= 2)
        {
            var si = e.Attribute("SI")?.Value ?? "";
            var inferred = si.Contains("And", StringComparison.OrdinalIgnoreCase) ? "AND"
                : si.Contains("Or", StringComparison.OrdinalIgnoreCase) ? "OR"
                : si.Contains("Xor", StringComparison.OrdinalIgnoreCase) ? "XOR"
                : si.Contains("Add", StringComparison.OrdinalIgnoreCase) ? "+"
                : si.Contains("Sub", StringComparison.OrdinalIgnoreCase) ? "-"
                : si.Contains("Mul", StringComparison.OrdinalIgnoreCase) ? "*"
                : si.Contains("Div", StringComparison.OrdinalIgnoreCase) ? "/"
                : null;
            if (inferred is not null)
            {
                var a = Eval(children[0], vars, state);
                var b = Eval(children[1], vars, state);
                if (a is null || b is null) return null;
                return ApplyBinary(inferred, a.Value, b.Value, state);
            }
        }
        return TryParseNumber(e, out var value) ? value : null;
    }

    private static string? OperatorName(string? element) => element switch
    {
        "OpAND" => "AND",
        "OpOR" => "OR",
        "OpXOR" => "XOR",
        "OpNOT" => "NOT",
        "OpADD" => "+",
        "OpSUB" => "-",
        "OpMUL" => "*",
        "OpDIV" => "/",
        "OpMOD" => "MOD",
        "OpEQ" => "=",
        "OpNE" => "<>",
        "OpLT" => "<",
        "OpGT" => ">",
        "OpLE" => "<=",
        "OpGE" => ">=",
        _ => null
    };

    private static double? ApplyBinary(string op, double a, double b, SclEvaluation state)
    {
        var o = op.Trim().ToUpperInvariant();
        return o switch
        {
            "AND" or "&" => (a != 0 && b != 0) ? 1 : 0,
            "OR" or "|" => (a != 0 || b != 0) ? 1 : 0,
            "XOR" => ((a != 0) ^ (b != 0)) ? 1 : 0,
            "+" => a + b,
            "-" => a - b,
            "*" => a * b,
            "/" => Math.Abs(b) < 1e-12 ? null : a / b,
            "MOD" => Math.Abs(b) < 1e-12 ? null : a % b,
            "=" or "==" => Math.Abs(a - b) < 1e-9 ? 1 : 0,
            "<>" or "!=" => Math.Abs(a - b) >= 1e-9 ? 1 : 0,
            "<" => a < b ? 1 : 0,
            ">" => a > b ? 1 : 0,
            "<=" => a <= b ? 1 : 0,
            ">=" => a >= b ? 1 : 0,
            _ => Unknown(state)
        };
    }

    private static double? ApplyUnary(string op, double a, SclEvaluation state)
    {
        var o = op.Trim().ToUpperInvariant();
        return o switch
        {
            "NOT" or "!" => a == 0 ? 1 : 0,
            "-" => -a,
            "+" => a,
            _ => Unknown(state)
        };
    }

    private static double? Unknown(SclEvaluation state)
    {
        state.Unmodeled++;
        return null;
    }
}

public sealed class EvolveCandidate
{
    public int Index { get; set; }
    public string Mutation { get; set; } = "";
    public int Length { get; set; }
    public double Fitness { get; set; }
    public int Matched { get; set; }
    public int Total { get; set; }
    public int Unmodeled { get; set; }
    public string? Body { get; set; }
}

public sealed class EvolveResult
{
    public string Block { get; set; } = "";
    public int Population { get; set; }
    public int Generations { get; set; }
    public double BaselineFitness { get; set; }
    public int BaselineMatched { get; set; }
    public int BaselineTotal { get; set; }
    public double BestFitness { get; set; }
    public bool Improved { get; set; }
    public string? Trace { get; set; }
    public EvolveCandidate? Best { get; set; }
    public List<EvolveCandidate> Candidates { get; set; } = [];
}

public static class EvolveEngine
{
    public static EvolveResult Run(ObjectExport export, string blockName, IoTrace trace, int count = 12)
    {
        var block = export.Objects.FirstOrDefault(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)
            && string.Equals(o.Name, blockName, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException("block not found: " + blockName);
        var body = PlatformIo.Body(block, export) ?? throw new CliException("block has no body: " + blockName);
        var result = Run(body, blockName, trace, count);
        result.Block = blockName;
        return result;
    }

    public static EvolveResult Run(XElement body, string blockName, IoTrace trace, int count = 12)
    {
        var baseline = Score(body, trace);
        var result = new EvolveResult
        {
            Block = blockName,
            Population = count,
            Generations = 1,
            BaselineFitness = baseline.Fitness,
            BaselineMatched = baseline.Matched,
            BaselineTotal = baseline.Total,
            Trace = trace.Name
        };
        for (int i = 0; i < count; i++)
        {
            var mutated = new XElement(body);
            var mutation = Mutate(mutated, i);
            var score = Score(mutated, trace);
            result.Candidates.Add(new EvolveCandidate
            {
                Index = i,
                Mutation = mutation,
                Length = PlatformIo.CanonicalElement(mutated).Length,
                Fitness = score.Fitness,
                Matched = score.Matched,
                Total = score.Total,
                Unmodeled = score.Unmodeled,
                Body = PlatformIo.CanonicalElement(mutated)
            });
        }
        result.Best = result.Candidates.OrderByDescending(c => c.Fitness).ThenBy(c => c.Index).FirstOrDefault();
        result.BestFitness = result.Best?.Fitness ?? 0;
        result.Improved = result.BestFitness > result.BaselineFitness + 1e-9;
        return result;
    }

    public static IoTrace GenerateTrace(ObjectExport export, string blockName, int samples = 8)
    {
        var block = export.Objects.FirstOrDefault(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)
            && string.Equals(o.Name, blockName, StringComparison.OrdinalIgnoreCase))
            ?? throw new CliException("block not found: " + blockName);
        var body = PlatformIo.Body(block, export) ?? throw new CliException("block has no body: " + blockName);
        var symbols = body.Descendants().Where(e => e.Name.LocalName == "SymVa")
            .Select(e => e.Attribute("ODN")?.Value?.TrimStart('#'))
            .Where(n => !string.IsNullOrEmpty(n)).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var trace = new IoTrace { Name = blockName + "-golden", Block = blockName };
        for (int i = 0; i < samples; i++)
        {
            var inputs = symbols.Select((s, idx) => (s, idx))
                .ToDictionary(x => x.s, x => (double)((i >> (x.idx % 8)) & 1), StringComparer.OrdinalIgnoreCase);
            var eval = SclInterpreter.Evaluate(body, inputs);
            var expected = symbols.ToDictionary(s => s, s => eval.Outputs.GetValueOrDefault(s, 0), StringComparer.OrdinalIgnoreCase);
            trace.Samples.Add(new IoTraceSample { T = i, Inputs = inputs, Expected = expected });
        }
        return trace;
    }

    public static (double Fitness, int Matched, int Total, int Unmodeled) Score(XElement body, IoTrace trace)
    {
        int matched = 0, total = 0, unmodeled = 0;
        foreach (var sample in trace.Samples)
        {
            var eval = SclInterpreter.Evaluate(body, sample.Inputs);
            unmodeled += eval.Unmodeled;
            foreach (var kv in sample.Expected)
            {
                total++;
                if (eval.Assigned.Contains(kv.Key)
                    && eval.Outputs.TryGetValue(kv.Key, out var actual)
                    && Math.Abs(actual - kv.Value) < 1e-9)
                    matched++;
            }
        }
        var fitness = total == 0 ? 0 : (double)matched / total;
        if (unmodeled > 0) fitness *= 0.5;
        return (fitness, matched, total, unmodeled);
    }

    public static string Mutate(XElement root, int seed)
    {
        var mode = Math.Abs(seed) % 4;
        var assignmentStatements = root.Descendants().Where(e => e.Name.LocalName == "Statement"
            && ((e.Attribute("SI")?.Value.Contains("Ass", StringComparison.OrdinalIgnoreCase) ?? false)
                || e.Elements().Any(c => c.Name.LocalName == "Token" && c.Attribute("TE")?.Value == ":="))).ToList();
        if (mode is 1 or 2 && assignmentStatements.Count > 0)
        {
            var stmt = assignmentStatements[seed % assignmentStatements.Count];
            if (mode == 1)
            {
                stmt.AddAfterSelf(new XElement(stmt));
                return "statement-duplicate";
            }
            stmt.Remove();
            return "statement-delete";
        }
        if (mode == 3)
        {
            var syms = root.Descendants().Where(e => e.Name.LocalName == "SymVa").ToList();
            if (syms.Count > 0)
            {
                var s = syms[seed % syms.Count];
                s.SetAttributeValue("ODN", (s.Attribute("ODN")?.Value ?? "sym") + "_v" + seed);
                return "symbol-rename";
            }
        }
        var flip = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AND"] = "OR", ["OR"] = "AND", ["XOR"] = "OR",
            ["+"] = "-", ["-"] = "+", ["*"] = "/", ["/"] = "*",
            ["="] = "<>", ["<>"] = "=", ["<"] = ">=", [">="] = "<",
            [">"] = "<=", ["<="] = ">"
        };
        var token = root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName is "Token" or "EToken"
                                 && e.Attribute("TE")?.Value is { } op && flip.ContainsKey(op));
        if (token is not null)
        {
            var op = token.Attribute("TE")!.Value;
            token.SetAttributeValue("TE", flip[op]);
            return $"operator-flip:{op}->{flip[op]}";
        }

        var literal = root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName is "Const" or "Literal" or "Immediate"
                                 && SclInterpreter.TryParseNumber(e, out _));
        if (literal is not null && SclInterpreter.TryParseNumber(literal, out var value))
        {
            var next = value + 1;
            literal.SetAttributeValue("TE", next.ToString(CultureInfo.InvariantCulture));
            if (literal.Attribute("Value") is not null) literal.SetAttributeValue("Value", next.ToString(CultureInfo.InvariantCulture));
            return "constant-substitution";
        }

        var statements = root.Descendants().Where(e => e.Name.LocalName == "Statement"
            && ((e.Attribute("SI")?.Value.Contains("Ass", StringComparison.OrdinalIgnoreCase) ?? false)
                || e.Elements().Any(c => c.Name.LocalName == "Token" && c.Attribute("TE")?.Value == ":="))).ToList();
        if (statements.Count > 0 && seed % 2 == 0)
        {
            statements[seed % statements.Count].Remove();
            return "statement-delete";
        }
        if (statements.Count > 0)
        {
            var stmt = statements[seed % statements.Count];
            stmt.AddAfterSelf(new XElement(stmt));
            return "statement-duplicate";
        }

        var symbols = root.Descendants().Where(e => e.Name.LocalName == "SymVa").ToList();
        if (symbols.Count > 0)
        {
            var s = symbols[seed % symbols.Count];
            s.SetAttributeValue("ODN", (s.Attribute("ODN")?.Value ?? "sym") + "_v" + seed);
            return "symbol-rename";
        }
        var texts = root.Descendants().Where(e => e.Name.LocalName == "LC").ToList();
        if (texts.Count > 0)
        {
            var t = texts[seed % texts.Count];
            t.SetAttributeValue("TE", (t.Attribute("TE")?.Value ?? "") + " #v" + seed);
            return "comment-mutate";
        }
        var parts = root.Descendants().Where(e => e.Name.LocalName == "Part").ToList();
        if (parts.Count > 0)
        {
            var p = parts[seed % parts.Count];
            p.SetAttributeValue("Gate", seed % 2 == 0 ? "Coil" : "Contact");
            return "gate-flip";
        }
        return "no-op";
    }
}

public sealed class EvolveSelfTestResult
{
    public bool Pass { get; set; }
    public double BaselineFitness { get; set; }
    public double BestFitness { get; set; }
    public string? BestMutation { get; set; }
    public bool Improved { get; set; }
    public int Samples { get; set; }
    public List<string> Checks { get; set; } = [];
}

public static class EvolveSelfTest
{
    public static EvolveSelfTestResult Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var body = SyntheticBody("OR");
        var trace = new IoTrace
        {
            Name = "and-gate-trace",
            Block = "Synthetic",
            Samples =
            [
                new() { T = 0, Inputs = new() { ["In1"] = 0, ["In2"] = 0 }, Expected = new() { ["Out"] = 0 } },
                new() { T = 1, Inputs = new() { ["In1"] = 0, ["In2"] = 1 }, Expected = new() { ["Out"] = 0 } },
                new() { T = 2, Inputs = new() { ["In1"] = 1, ["In2"] = 0 }, Expected = new() { ["Out"] = 0 } },
                new() { T = 3, Inputs = new() { ["In1"] = 1, ["In2"] = 1 }, Expected = new() { ["Out"] = 1 } }
            ]
        };
        var result = EvolveEngine.Run(body, "Synthetic", trace, 8);
        var checks = new List<string>
        {
            $"baseline={result.BaselineFitness:F2}",
            $"best={result.BestFitness:F2}",
            $"mutation={result.Best?.Mutation}",
            $"improved={result.Improved}",
            $"samples={trace.Samples.Count}"
        };
        var pass = Math.Abs(result.BaselineFitness - 0.5) < 1e-9
                   && Math.Abs(result.BestFitness - 1.0) < 1e-9
                   && result.Improved
                   && result.Best?.Mutation?.Contains("operator-flip", StringComparison.Ordinal) == true;
        var test = new EvolveSelfTestResult
        {
            Pass = pass,
            BaselineFitness = result.BaselineFitness,
            BestFitness = result.BestFitness,
            BestMutation = result.Best?.Mutation,
            Improved = result.Improved,
            Samples = trace.Samples.Count,
            Checks = checks
        };
        JsonUtil.Write(test, Path.Combine(outDir, "evolve-test.json"));
        JsonUtil.Write(trace, Path.Combine(outDir, "synthetic-trace.json"));
        JsonUtil.Write(result, Path.Combine(outDir, "synthetic-evolve.json"));
        return test;
    }

    private static XElement SyntheticBody(string op) => XElement.Parse($"""
        <SCLSource Version="3.4.0.0">
          <Symbols>
            <Unknown SymID="1" RefId="1" />
            <Unknown SymID="2" RefId="2" />
            <Unknown SymID="3" RefId="3" />
          </Symbols>
          <RootStatements Version="3.4.0.0">
            <Statement UId="1" SI="STAss">
              <SymVa UId="2" SI="Var" SyId="1" ODN="Out" />
              <Token UId="3" TE=":=" />
              <Expression UId="4" SI="ExprBin">
                <SymVa UId="5" SI="Var" SyId="2" ODN="In1" />
                <Token UId="6" TE="{op}" />
                <SymVa UId="7" SI="Var" SyId="3" ODN="In2" />
              </Expression>
            </Statement>
          </RootStatements>
        </SCLSource>
        """);
}
public sealed class ThymusDetector
{
    public string NGram { get; set; } = "";
    public int Hits { get; set; }
}

public sealed class ThymusTrainResult
{
    public int LegitimateBlocks { get; set; }
    public int Detectors { get; set; }
    public List<ThymusDetector> Repertoire { get; set; } = [];
}

public sealed class ThymusScanResult
{
    public int Blocks { get; set; }
    public int Flagged { get; set; }
    public List<Dictionary<string, object?>> Findings { get; set; } = [];
}

public static class Thymus
{
    public static ThymusTrainResult Train(ObjectExport export, int mutants = 20)
    {
        var blocks = export.Objects.Where(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)).ToList();
        var legitimate = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in blocks)
        {
            var body = PlatformIo.Body(b, export);
            if (body is null) continue;
            foreach (var g in NGrams(PlatformIo.CanonicalElement(body))) legitimate.Add(g);
        }
        var repertoire = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var b in blocks)
        {
            var body = PlatformIo.Body(b, export);
            if (body is null) continue;
            for (int i = 0; i < mutants; i++)
            {
                var m = new XElement(body);
                EvolveEngine.Mutate(m, i);
                foreach (var g in NGrams(PlatformIo.CanonicalElement(m)))
                    if (!legitimate.Contains(g)) repertoire[g] = repertoire.GetValueOrDefault(g) + 1;
            }
        }
        return new ThymusTrainResult
        {
            LegitimateBlocks = blocks.Count,
            Detectors = repertoire.Count,
            Repertoire = repertoire.OrderByDescending(kv => kv.Value).Take(200)
                .Select(kv => new ThymusDetector { NGram = kv.Key, Hits = kv.Value }).ToList()
        };
    }

    public static ThymusScanResult Scan(ObjectExport export, ThymusTrainResult trained)
    {
        var result = new ThymusScanResult();
        var detectors = trained.Repertoire.Select(d => d.NGram).ToHashSet(StringComparer.Ordinal);
        foreach (var b in export.Objects.Where(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)))
        {
            result.Blocks++;
            var body = PlatformIo.Body(b, export);
            if (body is null) continue;
            var grams = NGrams(PlatformIo.CanonicalElement(body));
            var hits = grams.Where(detectors.Contains).ToList();
            if (hits.Count == 0) continue;
            result.Flagged++;
            result.Findings.Add(new Dictionary<string, object?>
            {
                ["block"] = b.Name,
                ["hits"] = hits.Count,
                ["detectors"] = hits.Take(10).ToList()
            });
        }
        return result;
    }

    private static HashSet<string> NGrams(string text)
    {
        var tokens = text.Split([' ', '\t', '\r', '\n', '<', '>', '/', '=', '"', '\''], StringSplitOptions.RemoveEmptyEntries);
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i + 2 < tokens.Length; i++)
            set.Add(tokens[i] + "|" + tokens[i + 1] + "|" + tokens[i + 2]);
        return set;
    }
}

public sealed class ConsolidatedObject
{
    public string Key { get; set; } = "";
    public string? Name { get; set; }
    public string? ClassName { get; set; }
    public int Savepoints { get; set; }
    public bool Stable { get; set; }
}

public sealed class ReplayConsolidationResult
{
    public int Savepoints { get; set; }
    public int Objects { get; set; }
    public int Stable { get; set; }
    public int OneOff { get; set; }
    public List<ConsolidatedObject> Consolidated { get; set; } = [];
}

public static class ReplayConsolidator
{
    public static ReplayConsolidationResult Consolidate(HistoryResult history)
    {
        var byKey = history.History.GroupBy(v => v.Key).ToList();
        var result = new ReplayConsolidationResult { Savepoints = history.Savepoints.Count, Objects = byKey.Count };
        foreach (var g in byKey)
        {
            var sps = g.Select(v => v.Savepoint).Distinct().Count();
            var stable = sps >= Math.Max(2, history.Savepoints.Count / 2);
            if (stable) result.Stable++; else result.OneOff++;
            result.Consolidated.Add(new ConsolidatedObject
            {
                Key = g.Key,
                Name = g.LastOrDefault(v => v.Name is not null)?.Name,
                ClassName = g.LastOrDefault(v => v.ClassName is not null)?.ClassName,
                Savepoints = sps,
                Stable = stable
            });
        }
        return result;
    }
}

public sealed class DbContract
{
    public string Symbol { get; set; } = "";
    public List<string> Blocks { get; set; } = [];
    public string Kind { get; set; } = "shared-symbol";
    public double Confidence { get; set; }
}

public sealed class ContractDiscoveryResult
{
    public int MinBlocks { get; set; }
    public int Contracts { get; set; }
    public List<DbContract> Items { get; set; } = [];
}

public static class ContractDiscovery
{
    public static ContractDiscoveryResult Discover(ObjectExport export, int minBlocks = 2)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in export.Objects.Where(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)))
        {
            var body = PlatformIo.Body(b, export);
            if (body is null) continue;
            var name = b.Name ?? b.InstanceId.ToString();
            foreach (var s in body.Descendants().Where(e => e.Name.LocalName == "SymVa"))
            {
                var od = s.Attribute("ODN")?.Value;
                if (string.IsNullOrEmpty(od)) continue;
                if (!map.TryGetValue(od, out var set)) map[od] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(name);
            }
        }
        var items = map.Where(kv => kv.Value.Count >= minBlocks)
            .Select(kv => new DbContract
            {
                Symbol = kv.Key,
                Blocks = kv.Value.OrderBy(x => x).ToList(),
                Confidence = Math.Min(1.0, kv.Value.Count / 5.0)
            }).OrderByDescending(c => c.Blocks.Count).ToList();
        return new ContractDiscoveryResult { MinBlocks = minBlocks, Contracts = items.Count, Items = items };
    }
}

public sealed class MiddenCluster
{
    public string Kind { get; set; } = "";
    public int Count { get; set; }
    public List<string> Samples { get; set; } = [];
}

public sealed class MiddenResult
{
    public int Objects { get; set; }
    public int Orphans { get; set; }
    public List<MiddenCluster> Clusters { get; set; } = [];
}

public static class Midden
{
    public static MiddenResult Analyze(ObjectExport export)
    {
        var incoming = new HashSet<long>();
        foreach (var o in export.Objects)
            foreach (var l in o.Links) incoming.Add(l.InstanceId);
        var orphans = export.Objects.Where(o => !incoming.Contains(o.InstanceId))
            .Where(o => !o.ClassName.Contains("Container", StringComparison.Ordinal)
                        && !o.ClassName.Contains("ProjectData", StringComparison.Ordinal)
                        && !o.ClassName.Contains("Package", StringComparison.Ordinal))
            .ToList();
        var result = new MiddenResult { Objects = export.Objects.Count, Orphans = orphans.Count };
        result.Clusters = orphans.GroupBy(o => o.ClassName.Split('.').Last())
            .OrderByDescending(g => g.Count())
            .Select(g => new MiddenCluster
            {
                Kind = g.Key,
                Count = g.Count(),
                Samples = g.Select(o => o.Name ?? o.InstanceId.ToString()).Take(5).ToList()
            }).ToList();
        return result;
    }

}
