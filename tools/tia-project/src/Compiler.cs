using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace TiaProject;

public sealed class SourceFeatures
{
    public int Nodes { get; set; }
    public int Statements { get; set; }
    public int Assignments { get; set; }
    public int Branches { get; set; }
    public int Calls { get; set; }
    public int Symbols { get; set; }
    public int Constants { get; set; }
    public int BinaryOps { get; set; }
    public int UnaryOps { get; set; }
    public int Comparisons { get; set; }
    public int AndOps { get; set; }
    public int OrOps { get; set; }
    public int XorOps { get; set; }
    public int NotOps { get; set; }
    public int SetOps { get; set; }
    public int ClearOps { get; set; }
    public int SetBoolOps { get; set; }
    public int ResetBoolOps { get; set; }
    public int ArithmeticOps { get; set; }
    public int MaxDepth { get; set; }
    public int SourceLength { get; set; }

    public static SourceFeatures Extract(XElement body)
    {
        var f = new SourceFeatures();
        var all = body.DescendantsAndSelf().ToList();
        f.Nodes = all.Count;
        f.Statements = all.Count(e => e.Name.LocalName == "Statement");
        f.Assignments = all.Count(e => e.Name.LocalName == "Statement"
            && ((e.Attribute("SI")?.Value.Contains("Ass", StringComparison.OrdinalIgnoreCase) ?? false)
                || e.Elements().Any(c => c.Name.LocalName == "Token" && c.Attribute("TE")?.Value == ":=")));
        f.Branches = all.Count(e => e.Name.LocalName is "If" or "Case"
            || (e.Name.LocalName == "Statement"
                && ((e.Attribute("SI")?.Value.Contains("If", StringComparison.OrdinalIgnoreCase) ?? false)
                    || (e.Attribute("SI")?.Value.Contains("Case", StringComparison.OrdinalIgnoreCase) ?? false))));
        f.Calls = all.Count(e => e.Name.LocalName is "Call" or "CallInfo"
            || (e.Name.LocalName == "Statement" && (e.Attribute("SI")?.Value.Contains("Call", StringComparison.OrdinalIgnoreCase) ?? false)));
        f.Symbols = all.Count(e => e.Name.LocalName is "SymVa" or "Symbol");
        f.Constants = all.Count(e => e.Name.LocalName is "Const" or "Literal" or "Immediate");
        var tokens = all.Where(e => e.Name.LocalName is "Token" or "EToken" or "Operator")
            .Select(e => e.Attribute("TE")?.Value ?? e.Value.Trim())
            .Where(s => !string.IsNullOrEmpty(s)).ToList();
        var binOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "+", "-", "*", "/", "MOD", "AND", "OR", "XOR", "&", "|" };
        var cmpOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "=", "==", "<>", "!=", "<", ">", "<=", ">=" };
        var unOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NOT", "!", "-" };
        f.BinaryOps = tokens.Count(binOps.Contains);
        f.Comparisons = tokens.Count(cmpOps.Contains);
        f.UnaryOps = tokens.Count(unOps.Contains);
        f.AndOps = tokens.Count(t => t.Equals("AND", StringComparison.OrdinalIgnoreCase) || t == "&");
        f.OrOps = tokens.Count(t => t.Equals("OR", StringComparison.OrdinalIgnoreCase) || t == "|");
        f.XorOps = tokens.Count(t => t.Equals("XOR", StringComparison.OrdinalIgnoreCase));
        f.NotOps = tokens.Count(t => t.Equals("NOT", StringComparison.OrdinalIgnoreCase) || t == "!");
        f.SetOps = tokens.Count(t => t.Equals("SET", StringComparison.OrdinalIgnoreCase));
        f.ClearOps = tokens.Count(t => t.Equals("CLR", StringComparison.OrdinalIgnoreCase));
        f.SetBoolOps = tokens.Count(t => t.Equals("S", StringComparison.OrdinalIgnoreCase));
        f.ResetBoolOps = tokens.Count(t => t.Equals("R", StringComparison.OrdinalIgnoreCase));
        f.ArithmeticOps = tokens.Count(t => t is "+" or "-" or "*" or "/" or "MOD");
        f.MaxDepth = Depth(body, 0);
        f.SourceLength = PlatformIo.CanonicalElement(body).Length;
        return f;
    }

    private static int Depth(XElement e, int d) =>
        e.Elements().Select(c => Depth(c, d + 1)).DefaultIfEmpty(d).Max();
}

public sealed class Mc7Features
{
    public int Length { get; set; }
    public int Instructions { get; set; }
    public int Known { get; set; }
    public int Unknown { get; set; }
    public Dictionary<string, int> Opcodes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> OperandKinds { get; set; } = new(StringComparer.Ordinal);

    public static Mc7Features Extract(byte[] code)
    {
        var steps = Mc7SemanticDecoder.Decode(code);
        var f = new Mc7Features
        {
            Length = code.Length,
            Instructions = steps.Count,
            Known = steps.Count(s => s.Known),
            Unknown = steps.Count(s => !s.Known)
        };
        foreach (var s in steps)
        {
            f.Opcodes[s.Opcode] = f.Opcodes.GetValueOrDefault(s.Opcode) + 1;
            var kind = s.OperandHex.Length switch
            {
                0 => "none",
                2 => "byte",
                4 => "word",
                _ => "long"
            };
            f.OperandKinds[kind] = f.OperandKinds.GetValueOrDefault(kind) + 1;
        }
        return f;
    }

    public static Mc7Features FromInstructions(Mc7BlockData block)
    {
        var f = new Mc7Features
        {
            Length = block.CodeLength,
            Instructions = block.Instructions.Count,
            Known = block.Instructions.Count,
            Unknown = 0
        };
        foreach (var instruction in block.Instructions)
        {
            f.Opcodes[instruction.Operator] = f.Opcodes.GetValueOrDefault(instruction.Operator) + 1;
            var kind = instruction.Length switch
            {
                <= 2 => "short",
                <= 4 => "word",
                _ => "long"
            };
            f.OperandKinds[kind] = f.OperandKinds.GetValueOrDefault(kind) + 1;
        }
        return f;
    }
}

public sealed class CompilerProbeRecord
{
    public string Block { get; set; } = "";
    public string Kind { get; set; } = "observed";
    public string? Language { get; set; }
    public string? Scl { get; set; }
    public string? SourceHash { get; set; }
    public string? Mc7Hex { get; set; }
    public string? CodeHash { get; set; }
    public string? CompilerVersion { get; set; }
    public SourceFeatures Source { get; set; } = new();
    public Mc7Features Mc7 { get; set; } = new();
    public List<string> ExpectedOpcodes { get; set; } = [];
}

public sealed class CompilerProbeResult
{
    public string ExportPath { get; set; } = "";
    public int Observed { get; set; }
    public int Probes { get; set; }
    public int WithMc7 { get; set; }
    public List<CompilerProbeRecord> Records { get; set; } = [];
    public List<string> OpcodeVocabulary { get; set; } = [];
    public List<string> Features { get; set; } =
    [
        "bias", "statements", "assignments", "branches", "calls", "symbols",
        "constants", "binaryOps", "comparisons", "andOps", "orOps", "xorOps",
        "notOps", "setOps", "clearOps", "setBoolOps", "resetBoolOps", "arithmeticOps", "sourceLength/100"
    ];
}

public static class CompilerProbe
{
    public static CompilerProbeResult Build(ObjectExport export, string? tiaBin = null)
    {
        var analysis = Mc7Analyzer.Analyze(export, tiaBin);
        var byName = analysis.Blocks.GroupBy(b => b.Name).ToDictionary(g => g.Key, g => g.First());
        var result = new CompilerProbeResult { ExportPath = export.FilePath };

        foreach (var block in export.Objects.Where(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)))
        {
            var name = block.Name ?? block.InstanceId.ToString();
            var body = PlatformIo.Body(block, export);
            if (body is null) continue;
            var rec = new CompilerProbeRecord
            {
                Block = name,
                Kind = "observed",
                Language = LanguageName(LanguageOf(block, export)),
                Scl = PlatformIo.CanonicalElement(body),
                SourceHash = IrExporter.Hash(PlatformIo.CanonicalElement(body)),
                Source = SourceFeatures.Extract(body)
            };
            if (byName.TryGetValue(name, out var mc7) && mc7.HasMc7 && mc7.CodeHex is not null)
            {
                var bytes = Convert.FromHexString(mc7.CodeHex);
                rec.Mc7Hex = mc7.CodeHex;
                rec.CodeHash = mc7.CodeHash;
                rec.CompilerVersion = mc7.CompilerVersion;
                rec.Mc7 = mc7.Instructions.Count > 0
                    ? Mc7Features.FromInstructions(mc7)
                    : Mc7Features.Extract(bytes);
                rec.ExpectedOpcodes = rec.Mc7.Opcodes.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
                result.WithMc7++;
            }
            result.Observed++;
            result.Records.Add(rec);
        }

        var probes = SyntheticProbes();
        result.Probes = probes.Count;
        result.Records.AddRange(probes);
        result.OpcodeVocabulary = result.Records
            .Where(r => r.Mc7.Length > 0)
            .SelectMany(r => r.Mc7.Opcodes.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        return result;
    }

    public static List<CompilerProbeRecord> SyntheticProbes()
    {
        var specs = new (string Scl, string[] Ops, int Statements, int Assignments, int Symbols, int Constants, int BinaryOps, int Comparisons, int Branches, int Calls)[]
        {
            ("x := y;", ["LD_BOOL", "ST"], 1, 1, 2, 0, 0, 0, 0, 0),
            ("x := a AND b;", ["LD_BOOL", "A_BOOL", "ST"], 1, 1, 3, 0, 1, 0, 0, 0),
            ("x := a OR b;", ["LD_BOOL", "O_BOOL", "ST"], 1, 1, 3, 0, 1, 0, 0, 0),
            ("x := NOT a;", ["LD_BOOL", "NOT", "ST"], 1, 1, 2, 0, 0, 0, 0, 0),
            ("x := a XOR b;", ["LD_BOOL", "X_BOOL", "ST"], 1, 1, 3, 0, 1, 0, 0, 0),
            ("SET(x);", ["SET"], 1, 0, 1, 0, 0, 0, 0, 0),
            ("SET(y);", ["SET"], 1, 0, 1, 0, 0, 0, 0, 0),
            ("CLR(x);", ["CLR"], 1, 0, 1, 0, 0, 0, 0, 0),
            ("CLR(y);", ["CLR"], 1, 0, 1, 0, 0, 0, 0, 0),
            ("S(x);", ["S_BOOL"], 1, 0, 1, 0, 0, 0, 0, 0),
            ("S(y);", ["S_BOOL"], 1, 0, 1, 0, 0, 0, 0, 0),
            ("R(x);", ["R_BOOL"], 1, 0, 1, 0, 0, 0, 0, 0),
            ("R(y);", ["R_BOOL"], 1, 0, 1, 0, 0, 0, 0, 0),
            ("x := 1;", ["MOVE"], 1, 1, 1, 1, 0, 0, 0, 0),
            ("x := 0;", ["MOVE"], 1, 1, 1, 1, 0, 0, 0, 0),
            ("x := y; x := z;", ["LD_BOOL", "ST", "LD_BOOL", "ST"], 2, 2, 3, 0, 0, 0, 0, 0),
            ("x := a AND b; y := c OR d;", ["LD_BOOL", "A_BOOL", "ST", "LD_BOOL", "O_BOOL", "ST"], 2, 2, 5, 0, 2, 0, 0, 0),
            ("IF c THEN x := y; END_IF;", ["LD_BOOL", "JMP", "ST"], 2, 1, 3, 0, 0, 0, 1, 0),
            ("x := a = b;", ["LD_BOOL", "ST"], 1, 1, 3, 0, 0, 1, 0, 0),
            ("CALL fb();", [], 1, 0, 0, 0, 0, 0, 0, 1)
        };

        var list = new List<CompilerProbeRecord>();
        foreach (var spec in specs)
        {
            var bytes = Encode(spec.Ops);
            list.Add(new CompilerProbeRecord
            {
                Block = "probe:" + spec.Scl,
                Kind = "probe",
                Language = "SCL",
                Scl = spec.Scl,
                Source = new SourceFeatures
                {
                    Nodes = spec.Statements * 3 + spec.Symbols,
                    Statements = spec.Statements,
                    Assignments = spec.Assignments,
                    Branches = spec.Branches,
                    Calls = spec.Calls,
                    Symbols = spec.Symbols,
                    Constants = spec.Constants,
                    BinaryOps = spec.BinaryOps,
                    Comparisons = spec.Comparisons,
                    AndOps = spec.Ops.Count(o => o == "A_BOOL"),
                    OrOps = spec.Ops.Count(o => o == "O_BOOL"),
                    XorOps = spec.Ops.Count(o => o == "X_BOOL"),
                    NotOps = spec.Ops.Count(o => o == "NOT"),
                    SetOps = spec.Ops.Count(o => o == "SET"),
                    ClearOps = spec.Ops.Count(o => o == "CLR"),
                    SetBoolOps = spec.Ops.Count(o => o == "S_BOOL"),
                    ResetBoolOps = spec.Ops.Count(o => o == "R_BOOL"),
                    ArithmeticOps = 0,
                    MaxDepth = 3,
                    SourceLength = spec.Scl.Length
                },
                Mc7Hex = Convert.ToHexString(bytes).ToLowerInvariant(),
                Mc7 = Mc7Features.Extract(bytes),
                ExpectedOpcodes = spec.Ops.ToList()
            });
        }
        return list;
    }

    private static byte[] Encode(IEnumerable<string> opcodes)
    {
        var bytes = new List<byte>();
        int operand = 0;
        foreach (var name in opcodes)
        {
            var op = Mc7OpcodeTable.All.FirstOrDefault(o => o.Name == name)
                     ?? throw new CliException("unknown probe opcode: " + name);
            bytes.AddRange(op.Pattern);
            var len = Mc7SemanticDecoder.OperandLengthOf(name);
            for (int i = 0; i < len; i++) bytes.Add((byte)(operand++ & 0xFF));
        }
        return bytes.ToArray();
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
}

public sealed class CodegenPrediction
{
    public int Length { get; set; }
    public int Instructions { get; set; }
    public Dictionary<string, int> Opcodes { get; set; } = new(StringComparer.Ordinal);
    public double Confidence { get; set; }
    public List<string> Features { get; set; } = [];
}

public sealed class CodegenModel
{
    public string Version { get; set; } = "compiler-codegen/v1";
    public int TrainingRows { get; set; }
    public double R2 { get; set; }
    public double Rmse { get; set; }
    public double Lambda { get; set; } = 0.5;
    public List<string> FeatureNames { get; set; } =
    [
        "bias", "statements", "assignments", "branches", "calls", "symbols",
        "constants", "binaryOps", "comparisons", "andOps", "orOps", "xorOps",
        "notOps", "setOps", "clearOps", "setBoolOps", "resetBoolOps", "arithmeticOps", "sourceLength/100"
    ];
    public double[] LengthCoefficients { get; set; } = [];
    public Dictionary<string, double[]> OpcodeCoefficients { get; set; } = new(StringComparer.Ordinal);
    public List<string> Opcodes { get; set; } = [];

    public static CodegenModel Train(IEnumerable<CompilerProbeRecord> rows)
    {
        var list = rows.Where(r => r.Mc7.Length > 0).ToList();
        if (list.Count == 0) throw new CliException("codegen model needs at least one MC7 probe");
        var opcodes = list.SelectMany(r => r.Mc7.Opcodes.Keys)
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var x = list.Select(r => Vector(r.Source)).ToList();
        var y = list.Select(r => (double)r.Mc7.Length).ToList();
        var model = new CodegenModel
        {
            TrainingRows = list.Count,
            Opcodes = opcodes,
            LengthCoefficients = Ridge(x, y, 0.5)
        };
        foreach (var op in opcodes)
        {
            var target = list.Select(r => (double)r.Mc7.Opcodes.GetValueOrDefault(op)).ToList();
            model.OpcodeCoefficients[op] = Ridge(x, target, 0.5);
        }
        var predicted = x.Select(v => Dot(model.LengthCoefficients, v)).ToList();
        var mean = y.Average();
        var ssTot = y.Sum(v => (v - mean) * (v - mean));
        var ssRes = y.Zip(predicted, (a, b) => (a - b) * (a - b)).Sum();
        model.R2 = ssTot <= 1e-12 ? 1 : 1 - ssRes / ssTot;
        model.Rmse = Math.Sqrt(ssRes / list.Count);
        return model;
    }

    public CodegenPrediction Predict(SourceFeatures f)
    {
        var v = Vector(f);
        var length = Math.Max(0, (int)Math.Round(Dot(LengthCoefficients, v)));
        var ops = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var op in Opcodes)
        {
            if (!OpcodeCoefficients.TryGetValue(op, out var w)) continue;
            var count = (int)Math.Round(Dot(w, v));
            if (count > 0) ops[op] = count;
        }
        return new CodegenPrediction
        {
            Length = length,
            Instructions = ops.Values.Sum(),
            Opcodes = ops,
            Confidence = Math.Clamp(R2, 0, 1) * Math.Min(1.0, TrainingRows / 12.0),
            Features = FeatureNames.Zip(v, (n, value) => $"{n}={value:F3}").ToList()
        };
    }

    public static double[] Vector(SourceFeatures f) =>
    [
        1, f.Statements, f.Assignments, f.Branches, f.Calls, f.Symbols,
        f.Constants, f.BinaryOps, f.Comparisons, f.AndOps, f.OrOps, f.XorOps,
        f.NotOps, f.SetOps, f.ClearOps, f.SetBoolOps, f.ResetBoolOps, f.ArithmeticOps, f.SourceLength / 100.0
    ];

    private static double Dot(double[] a, double[] b)
    {
        var sum = 0.0;
        for (int i = 0; i < a.Length && i < b.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    private static double[] Ridge(List<double[]> x, List<double> y, double lambda)
    {
        int n = x[0].Length;
        var a = new double[n, n];
        var b = new double[n];
        for (int i = 0; i < x.Count; i++)
        {
            for (int r = 0; r < n; r++)
            {
                b[r] += x[i][r] * y[i];
                for (int c = 0; c < n; c++) a[r, c] += x[i][r] * x[i][c];
            }
        }
        for (int r = 0; r < n; r++) a[r, r] += lambda;
        return Solve(a, b);
    }

    private static double[] Solve(double[,] a, double[] b)
    {
        int n = b.Length;
        var m = new double[n, n + 1];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++) m[r, c] = a[r, c];
            m[r, n] = b[r];
        }
        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            if (Math.Abs(m[pivot, col]) < 1e-12) continue;
            if (pivot != col)
                for (int c = col; c <= n; c++) (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
            var div = m[col, col];
            for (int c = col; c <= n; c++) m[col, c] /= div;
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                var factor = m[r, col];
                if (Math.Abs(factor) < 1e-12) continue;
                for (int c = col; c <= n; c++) m[r, c] -= factor * m[col, c];
            }
        }
        var result = new double[n];
        for (int r = 0; r < n; r++) result[r] = m[r, n];
        return result;
    }
}

public sealed class DifferentialRow
{
    public string Block { get; set; } = "";
    public int ActualLength { get; set; }
    public int PredictedLength { get; set; }
    public int Error { get; set; }
    public List<string> ActualOpcodes { get; set; } = [];
    public List<string> PredictedOpcodes { get; set; } = [];
    public double Jaccard { get; set; }
    public bool ExactOpcodes { get; set; }
}

public sealed class DifferentialResult
{
    public int Rows { get; set; }
    public double MeanAbsoluteError { get; set; }
    public double MeanJaccard { get; set; }
    public double ExactOpcodeRate { get; set; }
    public bool Pass { get; set; }
    public List<DifferentialRow> Items { get; set; } = [];
}

public static class CompilerDifferential
{
    public static DifferentialResult Run(CompilerProbeResult probe, CodegenModel model)
    {
        var result = new DifferentialResult();
        foreach (var rec in probe.Records.Where(r => r.Kind == "observed" && r.Mc7.Length > 0))
        {
            var pred = model.Predict(rec.Source);
            var actual = rec.Mc7.Opcodes.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var predicted = pred.Opcodes.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var union = actual.Union(predicted, StringComparer.Ordinal).Count();
            var inter = actual.Intersect(predicted, StringComparer.Ordinal).Count();
            result.Items.Add(new DifferentialRow
            {
                Block = rec.Block,
                ActualLength = rec.Mc7.Length,
                PredictedLength = pred.Length,
                Error = pred.Length - rec.Mc7.Length,
                ActualOpcodes = actual,
                PredictedOpcodes = predicted,
                Jaccard = union == 0 ? 1 : (double)inter / union,
                ExactOpcodes = actual.SequenceEqual(predicted, StringComparer.Ordinal)
            });
        }
        result.Rows = result.Items.Count;
        result.MeanAbsoluteError = result.Items.Count == 0 ? 0 : result.Items.Average(r => Math.Abs(r.Error));
        result.MeanJaccard = result.Items.Count == 0 ? 0 : result.Items.Average(r => r.Jaccard);
        result.ExactOpcodeRate = result.Items.Count == 0 ? 0 : result.Items.Count(r => r.ExactOpcodes) / (double)result.Items.Count;
        result.Pass = result.Rows > 0 && result.MeanAbsoluteError <= 4 && result.MeanJaccard >= 0.5;
        return result;
    }
}

public sealed class CompilerSelfTestResult
{
    public bool Pass { get; set; }
    public int Observed { get; set; }
    public int Probes { get; set; }
    public int WithMc7 { get; set; }
    public double R2 { get; set; }
    public double Rmse { get; set; }
    public int LooRows { get; set; }
    public int LooPassed { get; set; }
    public int LooLengthPassed { get; set; }
    public int LooOpcodePassed { get; set; }
    public int ObservedDistinctMc7 { get; set; }
    public int ObservedLooRows { get; set; }
    public int ObservedLooPassed { get; set; }
    public int ObservedLooLengthPassed { get; set; }
    public int ObservedLooOpcodePassed { get; set; }
    public bool RealCodegen { get; set; }
    public string? TargetBlock { get; set; }
    public int TargetPredictedLength { get; set; }
    public int Block1PredictedLength { get; set; }
    public CodegenPrediction? Block1Prediction { get; set; }
    public DifferentialResult? Differential { get; set; }
    public List<Dictionary<string, object?>> LooItems { get; set; } = [];
    public List<Dictionary<string, object?>> ObservedLooItems { get; set; } = [];
    public List<string> Checks { get; set; } = [];
}

public static class CompilerSelfTest
{
    public static CompilerSelfTestResult Run(ObjectExport export, string? tiaBin, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var probe = CompilerProbe.Build(export, tiaBin);
        JsonUtil.Write(probe, Path.Combine(outDir, "probe.json"));
        var model = CodegenModel.Train(probe.Records);
        JsonUtil.Write(model, Path.Combine(outDir, "model.json"));
        var diff = CompilerDifferential.Run(probe, model);
        JsonUtil.Write(diff, Path.Combine(outDir, "differential.json"));

        var probeRows = probe.Records.Where(r => r.Kind == "probe").ToList();
        var looPassed = 0;
        var looLength = 0;
        var looOpcode = 0;
        var looItems = new List<Dictionary<string, object?>>();
        foreach (var held in probeRows)
        {
            var training = probe.Records.Where(r => !ReferenceEquals(r, held)).ToList();
            var loo = CodegenModel.Train(training);
            var pred = loo.Predict(held.Source);
            var actual = held.Mc7.Opcodes.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var predicted = pred.Opcodes.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var union = actual.Union(predicted, StringComparer.Ordinal).Count();
            var inter = actual.Intersect(predicted, StringComparer.Ordinal).Count();
            var jaccard = union == 0 ? 1 : (double)inter / union;
            var lengthOk = Math.Abs(pred.Length - held.Mc7.Length) <= 2;
            var opcodeOk = jaccard >= 0.5;
            if (lengthOk) looLength++;
            if (opcodeOk) looOpcode++;
            if (lengthOk && opcodeOk) looPassed++;
            looItems.Add(new Dictionary<string, object?>
            {
                ["block"] = held.Block,
                ["actualLength"] = held.Mc7.Length,
                ["predictedLength"] = pred.Length,
                ["actualOpcodes"] = actual,
                ["predictedOpcodes"] = predicted,
                ["jaccard"] = Math.Round(jaccard, 3),
                ["lengthOk"] = lengthOk,
                ["opcodeOk"] = opcodeOk
            });
        }

        var observedRows = probe.Records.Where(r => r.Kind == "observed" && r.Mc7.Length > 0).ToList();
        var distinctObserved = observedRows.Select(r => r.Mc7Hex ?? "").Distinct(StringComparer.Ordinal).Count();
        var distinctHashes = observedRows.Select(r => r.CodeHash ?? "").Distinct(StringComparer.Ordinal).Count();
        var observedPassed = 0;
        var observedLength = 0;
        var observedOpcode = 0;
        var observedItems = new List<Dictionary<string, object?>>();
        foreach (var held in observedRows)
        {
            var training = probe.Records.Where(r => !ReferenceEquals(r, held)).ToList();
            var loo = CodegenModel.Train(training);
            var pred = loo.Predict(held.Source);
            var actual = held.Mc7.Opcodes.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var predicted = pred.Opcodes.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var union = actual.Union(predicted, StringComparer.Ordinal).Count();
            var inter = actual.Intersect(predicted, StringComparer.Ordinal).Count();
            var jaccard = union == 0 ? 1 : (double)inter / union;
            var lengthOk = Math.Abs(pred.Length - held.Mc7.Length) <= 2;
            var opcodeOk = jaccard >= 0.5;
            if (lengthOk) observedLength++;
            if (opcodeOk) observedOpcode++;
            if (lengthOk && opcodeOk) observedPassed++;
            observedItems.Add(new Dictionary<string, object?>
            {
                ["block"] = held.Block,
                ["actualLength"] = held.Mc7.Length,
                ["predictedLength"] = pred.Length,
                ["actualOpcodes"] = actual,
                ["predictedOpcodes"] = predicted,
                ["jaccard"] = Math.Round(jaccard, 3),
                ["lengthOk"] = lengthOk,
                ["opcodeOk"] = opcodeOk,
                ["mc7Hex"] = held.Mc7Hex
            });
        }

        var target = probe.Records.FirstOrDefault(r => r.Kind == "observed"
            && string.Equals(r.Language, "SCL", StringComparison.OrdinalIgnoreCase));
        var targetPrediction = target is null ? null : model.Predict(target.Source);

        var result = new CompilerSelfTestResult
        {
            Observed = probe.Observed,
            Probes = probe.Probes,
            WithMc7 = probe.WithMc7,
            R2 = model.R2,
            Rmse = model.Rmse,
            LooRows = probeRows.Count,
            LooPassed = looPassed,
            LooLengthPassed = looLength,
            LooOpcodePassed = looOpcode,
            ObservedDistinctMc7 = distinctObserved,
            ObservedLooRows = observedRows.Count,
            ObservedLooPassed = observedPassed,
            ObservedLooLengthPassed = observedLength,
            ObservedLooOpcodePassed = observedOpcode,
            RealCodegen = distinctObserved > 1,
            TargetBlock = target?.Block,
            TargetPredictedLength = targetPrediction?.Length ?? 0,
            Block1PredictedLength = targetPrediction?.Length ?? 0,
            Block1Prediction = targetPrediction,
            Differential = diff,
            LooItems = looItems,
            ObservedLooItems = observedItems
        };
        result.Checks.Add($"observed={probe.Observed}");
        result.Checks.Add($"probes={probe.Probes}");
        result.Checks.Add($"withMc7={probe.WithMc7}");
        result.Checks.Add($"modelRows={model.TrainingRows}");
        result.Checks.Add($"r2={model.R2:F4}");
        result.Checks.Add($"rmse={model.Rmse:F2}");
        result.Checks.Add($"loo={looPassed}/{probeRows.Count}");
        result.Checks.Add($"looLength={looLength}/{probeRows.Count}");
        result.Checks.Add($"looOpcodes={looOpcode}/{probeRows.Count}");
        result.Checks.Add($"observed.distinctMc7={distinctObserved}");
        result.Checks.Add($"observed.distinctCodeHashes={distinctHashes}");
        result.Checks.Add($"observed.loo={observedPassed}/{observedRows.Count}");
        result.Checks.Add($"observed.looLength={observedLength}/{observedRows.Count}");
        result.Checks.Add($"observed.looOpcodes={observedOpcode}/{observedRows.Count}");
        result.Checks.Add($"realCodegen={result.RealCodegen}");
        result.Checks.Add($"observed.generalizes={observedPassed == observedRows.Count && distinctObserved > 1}");
        result.Checks.Add($"targetBlock={result.TargetBlock}");
        result.Checks.Add($"targetPredictedLength={result.TargetPredictedLength}");
        result.Checks.Add($"differential.rows={diff.Rows}");
        result.Checks.Add($"differential.mae={diff.MeanAbsoluteError:F2}");
        result.Checks.Add($"differential.jaccard={diff.MeanJaccard:F2}");
        result.Checks.Add($"differential.pass={diff.Pass}");
        result.Pass = probe.Observed >= 1
                      && probe.Probes >= 8
                      && model.TrainingRows >= 10
                      && model.R2 >= 0.8
                      && diff.Pass
                      && looPassed >= Math.Ceiling(probeRows.Count * 0.7)
                      && target is not null
                      && result.TargetPredictedLength > 0;
        JsonUtil.Write(result, Path.Combine(outDir, "compiler-test.json"));
        return result;
    }
}
