using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Z3;

namespace TiaProject;

public sealed class Mc7SemanticInstruction
{
    public int Offset { get; set; }
    public string Opcode { get; set; } = "";
    public string OpcodeHex { get; set; } = "";
    public string OperandHex { get; set; } = "";
    public int Length { get; set; }
    public bool Known { get; set; }
    public string Guard { get; set; } = "";
}

public sealed class Mc7SemanticCertificate
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public bool Equivalent { get; set; }
    public string SmtResult { get; set; } = "unknown";
    public string Method { get; set; } = "mc7-register-memory-qf-bv-z3";
    public bool AHasMc7 { get; set; }
    public bool BHasMc7 { get; set; }
    public string? ACodeHash { get; set; }
    public string? BCodeHash { get; set; }
    public int AKnown { get; set; }
    public int AUnknown { get; set; }
    public int BKnown { get; set; }
    public int BUnknown { get; set; }
    public List<Mc7SemanticInstruction> ASteps { get; set; } = [];
    public List<Mc7SemanticInstruction> BSteps { get; set; } = [];
    public string? CounterexampleReason { get; set; }
    public int? CounterexampleOffset { get; set; }
    public string? CounterexampleModel { get; set; }
    public string CertificateHash { get; set; } = "";
}

public sealed class Mc7SemanticSelfTestResult
{
    public bool Pass { get; set; }
    public List<string> Checks { get; set; } = [];
    public Mc7SemanticCertificate MainMain { get; set; } = new();
    public Mc7SemanticCertificate MainBlock1 { get; set; } = new();
    public Mc7SemanticCertificate MainMutated { get; set; } = new();
}

public static class Mc7SemanticDecoder
{
    public static List<Mc7SemanticInstruction> Decode(byte[] code)
    {
        var list = new List<Mc7SemanticInstruction>();
        int offset = 0;
        while (offset < code.Length)
        {
            var step = Mc7OpcodeTable.DecodeAt(code, offset);
            int opLen = step.Hex.Length / 2;
            int operandLen = OperandLength(step.Opcode);
            operandLen = Math.Min(operandLen, code.Length - offset - opLen);
            var operands = operandLen > 0 ? code.AsSpan(offset + opLen, operandLen).ToArray() : [];
            list.Add(new Mc7SemanticInstruction
            {
                Offset = offset,
                Opcode = step.Opcode,
                OpcodeHex = step.Hex,
                OperandHex = Convert.ToHexString(operands).ToLowerInvariant(),
                Length = opLen + operandLen,
                Known = step.IsKnownOpcode,
                Guard = GuardFor(step.Opcode)
            });
            offset += opLen + operandLen;
        }
        return list;
    }

    public static int OperandLengthOf(string opcode) => OperandLength(opcode);

    private static int OperandLength(string opcode) => opcode switch
    {
        "NOP" or "CLR_RLO" or "SET_RLO" or "NOT" => 0,
        "ST" or "SET" or "CLR" or "STN" or "S_BOOL" or "R_BOOL" or
        "LD_BOOL" or "LDN_BOOL" or "A_BOOL" or "AN_BOOL" or "O_BOOL" or "ON_BOOL" or
        "X_BOOL" or "XN_BOOL" or "ST_LD" or
        "LD_FP" or "LDN_FP" or "A_FP" or "AN_FP" or "O_FP" or "ON_FP" or
        "X_FP" or "XN_FP" or "LD_FN" or "LDN_FN" or "A_FN" or "AN_FN" or
        "O_FN" or "ON_FN" or "X_FN" or "XN_FN" or "ST_FP" or "ST_FN" or
        "MOVE_BOOL" => 1,
        "JMP" => 4,
        "MOVE" or "MOVE_F" or "SWAP" or "XCHG" => 2,
        _ => 0
    };

    private static string GuardFor(string opcode) => opcode switch
    {
        "ST" or "STN" or "ST_LD" or "S_BOOL" or "R_BOOL" => "rlo",
        "SET" or "CLR" => "unconditional",
        "LD_BOOL" or "LDN_BOOL" or "A_BOOL" or "AN_BOOL" or "O_BOOL" or "ON_BOOL"
            or "X_BOOL" or "XN_BOOL" or "LD_FP" or "LDN_FP" => "edge",
        _ => "straight-line"
    };
}

internal sealed class Mc7SymState
{
    public required BoolExpr Rlo;
    public required BitVecExpr Accu;
    public required ArrayExpr Mem;
    public required ArrayExpr Bits;
}

public static class Mc7SemanticChecker
{
    public static Mc7SemanticCertificate Compare(Mc7BlockData a, Mc7BlockData b)
    {
        var cert = new Mc7SemanticCertificate
        {
            A = a.Name,
            B = b.Name,
            AHasMc7 = a.HasMc7,
            BHasMc7 = b.HasMc7,
            ACodeHash = a.CodeHash,
            BCodeHash = b.CodeHash
        };
        var aCode = a.HasMc7 && a.CodeHex is not null ? Convert.FromHexString(a.CodeHex) : [];
        var bCode = b.HasMc7 && b.CodeHex is not null ? Convert.FromHexString(b.CodeHex) : [];
        cert.ASteps = Mc7SemanticDecoder.Decode(aCode);
        cert.BSteps = Mc7SemanticDecoder.Decode(bCode);
        cert.AKnown = cert.ASteps.Count(s => s.Known);
        cert.AUnknown = cert.ASteps.Count(s => !s.Known);
        cert.BKnown = cert.BSteps.Count(s => s.Known);
        cert.BUnknown = cert.BSteps.Count(s => !s.Known);

        if (!a.HasMc7 || !b.HasMc7)
        {
            cert.Equivalent = false;
            cert.SmtResult = "sat";
            cert.CounterexampleReason = "no-mc7-path";
            cert.CounterexampleOffset = -1;
            cert.CertificateHash = Hash(JsonSerializer.Serialize(cert, JsonUtil.Options));
            return cert;
        }

        using var ctx = new Context();
        var sa = Execute(aCode, ctx);
        var sb = Execute(bCode, ctx);
        var solver = ctx.MkSolver();
        var diff = ctx.MkOr(
            ctx.MkNot(ctx.MkEq(sa.Rlo, sb.Rlo)),
            ctx.MkNot(ctx.MkEq(sa.Accu, sb.Accu)),
            ctx.MkNot(ctx.MkEq(sa.Mem, sb.Mem)),
            ctx.MkNot(ctx.MkEq(sa.Bits, sb.Bits)));
        solver.Assert(diff);
        var status = solver.Check();
        cert.Equivalent = status == Status.UNSATISFIABLE;
        cert.SmtResult = status == Status.UNSATISFIABLE ? "unsat" : status == Status.SATISFIABLE ? "sat" : "unknown";
        if (status == Status.SATISFIABLE)
        {
            cert.CounterexampleReason = "register-memory-state-difference";
            cert.CounterexampleOffset = FirstDifference(cert.ASteps, cert.BSteps);
            cert.CounterexampleModel = solver.Model.ToString();
        }
        else if (status == Status.UNKNOWN)
        {
            cert.CounterexampleReason = "solver-unknown";
        }
        cert.CertificateHash = Hash(JsonSerializer.Serialize(cert, JsonUtil.Options));
        return cert;
    }

    internal static Mc7SymState Execute(byte[] code, Context ctx)
    {
        var state = new Mc7SymState
        {
            Rlo = ctx.MkFalse(),
            Accu = ctx.MkBV(0, 32),
            Mem = ctx.MkArrayConst("mem0", ctx.MkBitVecSort(32), ctx.MkBitVecSort(8)),
            Bits = ctx.MkArrayConst("bits0", ctx.MkBitVecSort(32), ctx.MkBoolSort())
        };
        foreach (var ins in Mc7SemanticDecoder.Decode(code))
            state = Step(state, ins, ctx);
        return state;
    }

    private static Mc7SymState Step(Mc7SymState s, Mc7SemanticInstruction ins, Context ctx)
    {
        var operand = ins.OperandHex.Length >= 2
            ? ctx.MkBV(Convert.ToByte(ins.OperandHex[..2], 16), 32)
            : ctx.MkBVConst($"operand_{ins.Offset}", 32);
        var bit = (BoolExpr)ctx.MkSelect(s.Bits, operand);
        var mem = (BitVecExpr)ctx.MkSelect(s.Mem, operand);
        switch (ins.Opcode)
        {
            case "NOP":
                return s;
            case "CLR_RLO":
                return new Mc7SymState { Rlo = ctx.MkFalse(), Accu = s.Accu, Mem = s.Mem, Bits = s.Bits };
            case "SET_RLO":
                return new Mc7SymState { Rlo = ctx.MkTrue(), Accu = s.Accu, Mem = s.Mem, Bits = s.Bits };
            case "NOT":
                return new Mc7SymState { Rlo = ctx.MkNot(s.Rlo), Accu = s.Accu, Mem = s.Mem, Bits = s.Bits };
            case "LD_BOOL":
            case "LD_FP":
                return new Mc7SymState { Rlo = bit, Accu = s.Accu, Mem = s.Mem, Bits = s.Bits };
            case "LDN_BOOL":
            case "LDN_FP":
                return new Mc7SymState { Rlo = ctx.MkNot(bit), Accu = s.Accu, Mem = s.Mem, Bits = s.Bits };
            case "A_BOOL":
            case "A_FP":
                return new Mc7SymState { Rlo = ctx.MkAnd(s.Rlo, bit), Accu = s.Accu, Mem = s.Mem, Bits = s.Bits };
            case "AN_BOOL":
            case "AN_FP":
                return new Mc7SymState { Rlo = ctx.MkAnd(s.Rlo, ctx.MkNot(bit)), Accu = s.Accu, Mem = s.Mem, Bits = s.Bits };
            case "O_BOOL":
            case "O_FP":
                return new Mc7SymState { Rlo = ctx.MkOr(s.Rlo, bit), Accu = s.Accu, Mem = s.Mem, Bits = s.Bits };
            case "ON_BOOL":
            case "ON_FP":
                return new Mc7SymState { Rlo = ctx.MkOr(s.Rlo, ctx.MkNot(bit)), Accu = s.Accu, Mem = s.Mem, Bits = s.Bits };
            case "X_BOOL":
            case "X_FP":
                return new Mc7SymState { Rlo = ctx.MkXor(s.Rlo, bit), Accu = s.Accu, Mem = s.Mem, Bits = s.Bits };
            case "XN_BOOL":
            case "XN_FP":
                return new Mc7SymState { Rlo = ctx.MkNot(ctx.MkXor(s.Rlo, bit)), Accu = s.Accu, Mem = s.Mem, Bits = s.Bits };
            case "ST":
            case "ST_LD":
                return new Mc7SymState { Rlo = s.Rlo, Accu = s.Accu, Mem = s.Mem, Bits = ctx.MkStore(s.Bits, operand, s.Rlo) };
            case "STN":
                return new Mc7SymState { Rlo = s.Rlo, Accu = s.Accu, Mem = s.Mem, Bits = ctx.MkStore(s.Bits, operand, ctx.MkNot(s.Rlo)) };
            case "SET":
                return new Mc7SymState { Rlo = s.Rlo, Accu = s.Accu, Mem = s.Mem, Bits = ctx.MkStore(s.Bits, operand, ctx.MkTrue()) };
            case "CLR":
                return new Mc7SymState { Rlo = s.Rlo, Accu = s.Accu, Mem = s.Mem, Bits = ctx.MkStore(s.Bits, operand, ctx.MkFalse()) };
            case "S_BOOL":
                return new Mc7SymState { Rlo = s.Rlo, Accu = s.Accu, Mem = s.Mem, Bits = ctx.MkStore(s.Bits, operand, ctx.MkOr(bit, s.Rlo)) };
            case "R_BOOL":
                return new Mc7SymState { Rlo = s.Rlo, Accu = s.Accu, Mem = s.Mem, Bits = ctx.MkStore(s.Bits, operand, ctx.MkAnd(bit, ctx.MkNot(s.Rlo))) };
            case "MOVE":
            case "MOVE_F":
                return new Mc7SymState { Rlo = s.Rlo, Accu = ctx.MkZeroExt(24, mem), Mem = s.Mem, Bits = s.Bits };
            case "MOVE_BOOL":
                return new Mc7SymState { Rlo = bit, Accu = s.Accu, Mem = s.Mem, Bits = s.Bits };
            default:
                return new Mc7SymState
                {
                    Rlo = ctx.MkBoolConst($"havoc_rlo_{ins.Offset}"),
                    Accu = ctx.MkBVConst($"havoc_accu_{ins.Offset}", 32),
                    Mem = ctx.MkArrayConst($"havoc_mem_{ins.Offset}", ctx.MkBitVecSort(32), ctx.MkBitVecSort(8)),
                    Bits = ctx.MkArrayConst($"havoc_bits_{ins.Offset}", ctx.MkBitVecSort(32), ctx.MkBoolSort())
                };
        }
    }

    private static int FirstDifference(List<Mc7SemanticInstruction> a, List<Mc7SemanticInstruction> b)
    {
        int n = Math.Min(a.Count, b.Count);
        for (int i = 0; i < n; i++)
            if (a[i].Opcode != b[i].Opcode || a[i].OperandHex != b[i].OperandHex)
                return a[i].Offset;
        return a.Count == b.Count ? -1 : n;
    }

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}

public static class Mc7SemanticSelfTest
{
    public static Mc7SemanticSelfTestResult Run(Mc7AnalysisResult analysis)
    {
        var main = analysis.Blocks.FirstOrDefault(b => b.Name == "Main")
                   ?? throw new CliException("MC7 semantic: Main not found");
        var block1 = analysis.Blocks.FirstOrDefault(b => b.Name == "Block_1")
                     ?? throw new CliException("MC7 semantic: Block_1 not found");
        var mainMain = Mc7SemanticChecker.Compare(main, main);
        var mainBlock1 = Mc7SemanticChecker.Compare(main, block1);
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
            bytes[^1] = 0x2A; // ST_LD: guarded write of RLO to bit memory
            mutated.CodeHex = Convert.ToHexString(bytes).ToLowerInvariant();
            mutated.CodeHash = Mc7Analyzer.Hash(bytes);
        }
        var mainMutated = Mc7SemanticChecker.Compare(main, mutated);
        var checks = new List<string>
        {
            $"main.hasMc7={main.HasMc7}",
            $"main.steps={mainMain.ASteps.Count}",
            $"main.known={mainMain.AKnown}",
            $"main.unknown={mainMain.AUnknown}",
            $"main.steps.detail={string.Join(";", mainMain.ASteps.Select(s => $"{s.Offset}:{s.Opcode}[{s.Guard}]"))}",
            $"main-main.equivalent={mainMain.Equivalent}",
            $"main-main.smt={mainMain.SmtResult}",
            $"main-block1.equivalent={mainBlock1.Equivalent}",
            $"main-block1.smt={mainBlock1.SmtResult}",
            $"main-block1.reason={mainBlock1.CounterexampleReason}",
            $"main-mutated.equivalent={mainMutated.Equivalent}",
            $"main-mutated.smt={mainMutated.SmtResult}",
            $"main-mutated.reason={mainMutated.CounterexampleReason}",
            $"main-mutated.offset={mainMutated.CounterexampleOffset}",
            $"main-mutated.modelLength={mainMutated.CounterexampleModel?.Length ?? 0}"
        };
        var pass = main.HasMc7
                   && mainMain.Equivalent && mainMain.SmtResult == "unsat"
                   && !mainBlock1.Equivalent && mainBlock1.SmtResult == "sat"
                   && !mainMutated.Equivalent && mainMutated.SmtResult == "sat"
                   && mainMutated.CounterexampleOffset == 11;
        return new Mc7SemanticSelfTestResult
        {
            Pass = pass,
            Checks = checks,
            MainMain = mainMain,
            MainBlock1 = mainBlock1,
            MainMutated = mainMutated
        };
    }
}
