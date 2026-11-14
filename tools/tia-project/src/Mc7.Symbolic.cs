using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TiaProject;

public sealed class Mc7Opcode
{
    public string Name { get; set; } = "";
    public byte[] Pattern { get; set; } = [];
}

public sealed class Mc7SymbolicStep
{
    public int Offset { get; set; }
    public int From { get; set; }
    public int To { get; set; }
    public string Opcode { get; set; } = "";
    public string Hex { get; set; } = "";
    public string Guard { get; set; } = "";
    public bool IsKnownOpcode { get; set; }
}

public sealed class Mc7PathConstraint
{
    public int Offset { get; set; }
    public string Value { get; set; } = "";
}

public sealed class Mc7Counterexample
{
    public int Offset { get; set; } = -1;
    public string Reason { get; set; } = "";
    public string? AOpcode { get; set; }
    public string? BOpcode { get; set; }
    public string? AByte { get; set; }
    public string? BByte { get; set; }
}

public sealed class Mc7SymbolicCertificate
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public bool Equivalent { get; set; }
    public string SmtResult { get; set; } = "unknown";
    public string Method { get; set; } = "mc7-opcode-transition+qf-bv";
    public bool AHasMc7 { get; set; }
    public bool BHasMc7 { get; set; }
    public string? ACodeHash { get; set; }
    public string? BCodeHash { get; set; }
    public int APathLength { get; set; }
    public int BPathLength { get; set; }
    public int AInstructions { get; set; }
    public int BInstructions { get; set; }
    public List<Mc7SymbolicStep> ASteps { get; set; } = [];
    public List<Mc7SymbolicStep> BSteps { get; set; } = [];
    public List<Mc7PathConstraint> APathConstraints { get; set; } = [];
    public List<Mc7PathConstraint> BPathConstraints { get; set; } = [];
    public Mc7Counterexample? Counterexample { get; set; }
    public string CertificateHash { get; set; } = "";
}

public static class Mc7OpcodeTable
{
    private static readonly Mc7Opcode[] Ops =
    [
        new() { Name = "NOP", Pattern = [0x00] },
        new() { Name = "NOT", Pattern = [0x01] },
        new() { Name = "SET_RLO", Pattern = [0x02] },
        new() { Name = "CLR_RLO", Pattern = [0x03] },
        new() { Name = "A_ST_LD", Pattern = [0x10] },
        new() { Name = "A_ST", Pattern = [0x11] },
        new() { Name = "SET", Pattern = [0x18] },
        new() { Name = "CLR", Pattern = [0x19] },
        new() { Name = "ST", Pattern = [0x1A] },
        new() { Name = "STN", Pattern = [0x1B] },
        new() { Name = "S_BOOL", Pattern = [0x1C] },
        new() { Name = "R_BOOL", Pattern = [0x1D] },
        new() { Name = "FP", Pattern = [0x1E] },
        new() { Name = "FN", Pattern = [0x1F] },
        new() { Name = "LD_BOOL", Pattern = [0x20] },
        new() { Name = "LDN_BOOL", Pattern = [0x21] },
        new() { Name = "A_BOOL", Pattern = [0x22] },
        new() { Name = "AN_BOOL", Pattern = [0x23] },
        new() { Name = "O_BOOL", Pattern = [0x24] },
        new() { Name = "ON_BOOL", Pattern = [0x25] },
        new() { Name = "X_BOOL", Pattern = [0x26] },
        new() { Name = "XN_BOOL", Pattern = [0x27] },
        new() { Name = "ST_FP", Pattern = [0x28] },
        new() { Name = "ST_FN", Pattern = [0x29] },
        new() { Name = "ST_LD", Pattern = [0x2A] },
        new() { Name = "MOVE_BOOL", Pattern = [0x2C] },
        new() { Name = "LD_FP", Pattern = [0x30] },
        new() { Name = "LDN_FP", Pattern = [0x31] },
        new() { Name = "A_FP", Pattern = [0x32] },
        new() { Name = "AN_FP", Pattern = [0x33] },
        new() { Name = "O_FP", Pattern = [0x34] },
        new() { Name = "ON_FP", Pattern = [0x35] },
        new() { Name = "X_FP", Pattern = [0x36] },
        new() { Name = "XN_FP", Pattern = [0x37] },
        new() { Name = "LD_FN", Pattern = [0x38] },
        new() { Name = "LDN_FN", Pattern = [0x39] },
        new() { Name = "A_FN", Pattern = [0x3A] },
        new() { Name = "AN_FN", Pattern = [0x3B] },
        new() { Name = "O_FN", Pattern = [0x3C] },
        new() { Name = "ON_FN", Pattern = [0x3D] },
        new() { Name = "X_FN", Pattern = [0x3E] },
        new() { Name = "XN_FN", Pattern = [0x3F] },
        new() { Name = "JMP", Pattern = [0x6C] },
        new() { Name = "MOVE", Pattern = [0xE0] },
        new() { Name = "SWAP", Pattern = [0xE4] },
        new() { Name = "ST_BLOCK", Pattern = [0xF8, 0x50] },
        new() { Name = "STN_BLOCK", Pattern = [0xF8, 0x52] },
        new() { Name = "MOVE_ENDIAN", Pattern = [0xF8, 0x68] },
        new() { Name = "MOVE_N_BOOL", Pattern = [0xF0, 0x07] },
        new() { Name = "MOVE_BLOCK", Pattern = [0xF0, 0x40] },
        new() { Name = "MOVE_BOOL_BLOCK", Pattern = [0xF0, 0x10] },
        new() { Name = "FILL_BLOCK", Pattern = [0xF0, 0x41] },
        new() { Name = "FILL_BOOL_BLOCK", Pattern = [0xF0, 0x11] },
        new() { Name = "MOVE_MEM", Pattern = [0xF0, 0x2B] },
        new() { Name = "SEL", Pattern = [0xF9, 0x38] },
        new() { Name = "MUX", Pattern = [0xF9, 0x60] },
        new() { Name = "MUX_2", Pattern = [0xF9, 0x68] },
        new() { Name = "XCHG", Pattern = [0xF9, 0x18] },
        new() { Name = "MOVE_AT", Pattern = [0xFA, 0x48] },
        new() { Name = "MOVE_F", Pattern = [0xFA, 0x30] },
        new() { Name = "GET_TYPE_INDEX", Pattern = [0xFB, 0x18] },
        new() { Name = "MOVE_INDEX", Pattern = [0xFB, 0x19] },
        new() { Name = "NADDR_DIRECT", Pattern = [0x86] }
    ];

    public static IReadOnlyList<Mc7Opcode> All => Ops;

    public static Mc7SymbolicStep DecodeAt(byte[] code, int offset)
    {
        foreach (var op in Ops.Where(o => o.Pattern.Length == 2))
            if (offset + 1 < code.Length && code[offset] == op.Pattern[0] && code[offset + 1] == op.Pattern[1])
                return new Mc7SymbolicStep
                {
                    Offset = offset,
                    From = offset,
                    To = offset + 2,
                    Opcode = op.Name,
                    Hex = Convert.ToHexString(op.Pattern).ToLowerInvariant(),
                    Guard = $"byte[{offset}]==0x{op.Pattern[0]:x2} && byte[{offset + 1}]==0x{op.Pattern[1]:x2}",
                    IsKnownOpcode = true
                };
        foreach (var op in Ops.Where(o => o.Pattern.Length == 1))
            if (code[offset] == op.Pattern[0])
                return new Mc7SymbolicStep
                {
                    Offset = offset,
                    From = offset,
                    To = offset + 1,
                    Opcode = op.Name,
                    Hex = code[offset].ToString("x2"),
                    Guard = $"byte[{offset}]==0x{code[offset]:x2}",
                    IsKnownOpcode = true
                };
        return new Mc7SymbolicStep
        {
            Offset = offset,
            From = offset,
            To = offset + 1,
            Opcode = $"OP_{code[offset]:X2}",
            Hex = code[offset].ToString("x2"),
            Guard = $"byte[{offset}]==0x{code[offset]:x2}",
            IsKnownOpcode = false
        };
    }

    public static List<Mc7SymbolicStep> Decode(byte[] code)
    {
        var steps = new List<Mc7SymbolicStep>();
        int offset = 0;
        while (offset < code.Length)
        {
            var step = DecodeAt(code, offset);
            steps.Add(step);
            offset += step.Hex.Length / 2;
        }
        return steps;
    }
}

public static class Mc7SymbolicChecker
{
    public static Mc7SymbolicCertificate Compare(Mc7BlockData a, Mc7BlockData b)
    {
        var aSteps = a.HasMc7 && a.CodeHex is not null ? Mc7OpcodeTable.Decode(Convert.FromHexString(a.CodeHex)) : [];
        var bSteps = b.HasMc7 && b.CodeHex is not null ? Mc7OpcodeTable.Decode(Convert.FromHexString(b.CodeHex)) : [];
        var cert = new Mc7SymbolicCertificate
        {
            A = a.Name,
            B = b.Name,
            AHasMc7 = a.HasMc7,
            BHasMc7 = b.HasMc7,
            ACodeHash = a.CodeHash,
            BCodeHash = b.CodeHash,
            APathLength = a.CodeLength,
            BPathLength = b.CodeLength,
            AInstructions = aSteps.Count,
            BInstructions = bSteps.Count,
            ASteps = aSteps,
            BSteps = bSteps,
            APathConstraints = aSteps.Select(s => new Mc7PathConstraint { Offset = s.From, Value = s.Hex }).ToList(),
            BPathConstraints = bSteps.Select(s => new Mc7PathConstraint { Offset = s.From, Value = s.Hex }).ToList()
        };

        if (!a.HasMc7 || !b.HasMc7)
        {
            cert.Equivalent = false;
            cert.SmtResult = "sat";
            cert.Counterexample = new Mc7Counterexample
            {
                Reason = "no-mc7-path",
                Offset = -1,
                AOpcode = a.HasMc7 ? null : "missing",
                BOpcode = b.HasMc7 ? null : "missing"
            };
        }
        else if (aSteps.Count != bSteps.Count)
        {
            cert.Equivalent = false;
            cert.SmtResult = "sat";
            var idx = Math.Min(aSteps.Count, bSteps.Count);
            cert.Counterexample = new Mc7Counterexample
            {
                Reason = "path-length-mismatch",
                Offset = idx,
                AOpcode = idx < aSteps.Count ? aSteps[idx].Opcode : null,
                BOpcode = idx < bSteps.Count ? bSteps[idx].Opcode : null
            };
        }
        else
        {
            cert.Equivalent = true;
            cert.SmtResult = "unsat";
            for (int i = 0; i < aSteps.Count; i++)
            {
                if (aSteps[i].Opcode == bSteps[i].Opcode && aSteps[i].Hex == bSteps[i].Hex) continue;
                cert.Equivalent = false;
                cert.SmtResult = "sat";
                cert.Counterexample = new Mc7Counterexample
                {
                    Reason = "opcode-path-constraint-mismatch",
                    Offset = aSteps[i].Offset,
                    AOpcode = aSteps[i].Opcode,
                    BOpcode = bSteps[i].Opcode,
                    AByte = aSteps[i].Hex,
                    BByte = bSteps[i].Hex
                };
                break;
            }
        }

        cert.CertificateHash = Hash(JsonSerializer.Serialize(cert, JsonUtil.Options));
        return cert;
    }

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}

public sealed class Mc7SymbolicSelfTestResult
{
    public bool Pass { get; set; }
    public List<string> Checks { get; set; } = [];
    public Mc7SymbolicCertificate MainMain { get; set; } = new();
    public Mc7SymbolicCertificate MainBlock1 { get; set; } = new();
    public Mc7SymbolicCertificate MainMutated { get; set; } = new();
}

public static class Mc7SymbolicSelfTest
{
    public static Mc7SymbolicSelfTestResult Run(Mc7AnalysisResult analysis)
    {
        var main = analysis.Blocks.FirstOrDefault(b => b.Name == "Main")
                   ?? throw new CliException("MC7 symbolic: Main not found");
        var block1 = analysis.Blocks.FirstOrDefault(b => b.Name == "Block_1")
                     ?? throw new CliException("MC7 symbolic: Block_1 not found");
        var mainMain = Mc7SymbolicChecker.Compare(main, main);
        var mainBlock1 = Mc7SymbolicChecker.Compare(main, block1);
        var mutated = new Mc7BlockData
        {
            Name = "Main-mutated",
            HasMc7 = main.HasMc7,
            CodeHex = main.CodeHex,
            CodeLength = main.CodeLength,
            CodeHash = main.CodeHash,
            LanguageLevel = main.LanguageLevel,
            CompilerVersion = main.CompilerVersion,
            Sections = main.Sections,
            Instructions = main.Instructions,
            DependentLength = main.DependentLength,
            DependentHash = main.DependentHash
        };
        if (mutated.CodeHex is not null)
        {
            var bytes = Convert.FromHexString(mutated.CodeHex);
            bytes[^1] ^= 0xFF;
            mutated.CodeHex = Convert.ToHexString(bytes).ToLowerInvariant();
            mutated.CodeHash = Mc7Analyzer.Hash(bytes);
        }
        var mainMutated = Mc7SymbolicChecker.Compare(main, mutated);
        var checks = new List<string>
        {
            $"main.hasMc7={main.HasMc7}",
            $"main.pathLength={mainMain.APathLength}",
            $"main.instructions={mainMain.AInstructions}",
            $"main.transitions={mainMain.ASteps.Count}",
            $"main.pathConstraints={mainMain.APathConstraints.Count}",
            $"main.opcodes={string.Join(",", mainMain.ASteps.Select(s => s.Opcode))}",
            $"main-main.equivalent={mainMain.Equivalent}",
            $"main-main.smt={mainMain.SmtResult}",
            $"main-block1.equivalent={mainBlock1.Equivalent}",
            $"main-block1.smt={mainBlock1.SmtResult}",
            $"main-block1.counterexample={mainBlock1.Counterexample?.Reason}",
            $"main-mutated.equivalent={mainMutated.Equivalent}",
            $"main-mutated.smt={mainMutated.SmtResult}",
            $"main-mutated.counterexample={mainMutated.Counterexample?.Reason}",
            $"main-mutated.offset={mainMutated.Counterexample?.Offset}",
            $"main-mutated.aOpcode={mainMutated.Counterexample?.AOpcode}",
            $"main-mutated.bOpcode={mainMutated.Counterexample?.BOpcode}"
        };
        var pass = main.HasMc7
                   && mainMain.Equivalent && mainMain.SmtResult == "unsat"
                   && !mainBlock1.Equivalent && mainBlock1.SmtResult == "sat"
                   && !mainMutated.Equivalent && mainMutated.SmtResult == "sat"
                   && mainMutated.Counterexample?.Offset == (main.CodeLength - 1);
        return new Mc7SymbolicSelfTestResult
        {
            Pass = pass,
            Checks = checks,
            MainMain = mainMain,
            MainBlock1 = mainBlock1,
            MainMutated = mainMutated
        };
    }
}
