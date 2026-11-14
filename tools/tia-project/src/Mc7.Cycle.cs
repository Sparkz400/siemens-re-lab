using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Z3;

namespace TiaProject;

public sealed class Mc7CycleInstruction
{
    public int Index { get; set; }
    public int Offset { get; set; }
    public string Opcode { get; set; } = "";
    public string OperandHex { get; set; } = "";
    public byte[] Operands { get; set; } = [];
    public int Length { get; set; }
    public bool Known { get; set; }
    public int? JumpTarget { get; set; }
    public bool JumpUnresolved { get; set; }
    public uint? BitAddress { get; set; }
    public uint? ByteAddress { get; set; }
}

public sealed class Mc7CycleMachine
{
    public string Name { get; set; } = "";
    public string? CodeHash { get; set; }
    public string CodeHex { get; set; } = "";
    public bool HasMc7 { get; set; }
    public int Unknown { get; set; }
    public int UnresolvedJumps { get; set; }
    public List<Mc7CycleInstruction> Instructions { get; set; } = [];
    public List<uint> BitAddresses { get; set; } = [];
    public List<uint> ByteAddresses { get; set; } = [];

    public static Mc7CycleMachine Build(Mc7BlockData block)
    {
        var machine = new Mc7CycleMachine
        {
            Name = block.Name,
            CodeHash = block.CodeHash,
            CodeHex = block.CodeHex ?? "",
            HasMc7 = block.HasMc7
        };
        if (!block.HasMc7 || block.CodeHex is null) return machine;

        var code = Convert.FromHexString(block.CodeHex);
        var decoded = Mc7SemanticDecoder.Decode(code);
        machine.Instructions = decoded.Select((d, i) => new Mc7CycleInstruction
        {
            Index = i,
            Offset = d.Offset,
            Opcode = d.Opcode,
            OperandHex = d.OperandHex,
            Operands = d.OperandHex.Length > 0 ? Convert.FromHexString(d.OperandHex) : [],
            Length = d.Length,
            Known = d.Known
        }).ToList();
        machine.Unknown = machine.Instructions.Count(i => !i.Known);

        var byOffset = machine.Instructions.ToDictionary(i => i.Offset);
        var bitAddrs = new SortedSet<uint>();
        var byteAddrs = new SortedSet<uint>();
        foreach (var ins in machine.Instructions)
        {
            if (ins.Opcode == "JMP" && ins.Operands.Length >= 4)
            {
                var target = (uint)((ins.Operands[0] << 24) | (ins.Operands[1] << 16)
                                    | (ins.Operands[2] << 8) | ins.Operands[3]);
                if (byOffset.TryGetValue((int)target, out var targetIns))
                    ins.JumpTarget = targetIns.Index;
                else
                    ins.JumpUnresolved = true;
            }

            if (ins.Operands.Length == 0) continue;
            if (IsBitOpcode(ins.Opcode))
            {
                ins.BitAddress = ins.Operands[0];
                bitAddrs.Add(ins.Operands[0]);
            }
            else if (IsByteOpcode(ins.Opcode))
            {
                ins.ByteAddress = ins.Operands[0];
                byteAddrs.Add(ins.Operands[0]);
            }
        }
        machine.UnresolvedJumps = machine.Instructions.Count(i => i.JumpUnresolved);
        machine.BitAddresses = bitAddrs.ToList();
        machine.ByteAddresses = byteAddrs.ToList();
        return machine;
    }

    public static bool IsBitOpcode(string opcode) => opcode is
        "ST" or "STN" or "SET" or "CLR" or "S_BOOL" or "R_BOOL" or
        "LD_BOOL" or "LDN_BOOL" or "A_BOOL" or "AN_BOOL" or "O_BOOL" or "ON_BOOL" or
        "X_BOOL" or "XN_BOOL" or "ST_LD" or "MOVE_BOOL" or
        "LD_FP" or "LDN_FP" or "A_FP" or "AN_FP" or "O_FP" or "ON_FP" or
        "X_FP" or "XN_FP" or "LD_FN" or "LDN_FN" or "A_FN" or "AN_FN" or
        "O_FN" or "ON_FN" or "X_FN" or "XN_FN" or "ST_FP" or "ST_FN";

    public static bool IsByteOpcode(string opcode) =>
        opcode is "MOVE" or "MOVE_F" or "SWAP" or "XCHG";
}

public sealed class Mc7CycleObservationDiff
{
    public string Kind { get; set; } = "";
    public string? Address { get; set; }
    public string? A { get; set; }
    public string? B { get; set; }
}

public sealed class Mc7CycleCounterexample
{
    public int Cycle { get; set; } = -1;
    public string Reason { get; set; } = "";
    public string? PathA { get; set; }
    public string? PathB { get; set; }
    public List<Mc7CycleObservationDiff> Differences { get; set; } = [];
    public string? Model { get; set; }
}

public sealed class Mc7CycleCertificate
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public string Method { get; set; } = "mc7-cycle-observational-qf-aufbv";
    public bool Equivalent { get; set; }
    public bool Definitive { get; set; }
    public bool Complete { get; set; }
    public bool Bounded { get; set; }
    public string SmtResult { get; set; } = "unknown";
    public int Cycles { get; set; }
    public int CyclesChecked { get; set; }
    public int PathsA { get; set; }
    public int PathsB { get; set; }
    public int PairsChecked { get; set; }
    public int AInstructions { get; set; }
    public int BInstructions { get; set; }
    public int AKnown { get; set; }
    public int AUnknown { get; set; }
    public int BKnown { get; set; }
    public int BUnknown { get; set; }
    public string? ACodeHash { get; set; }
    public string? BCodeHash { get; set; }
    public List<string> ObservationBits { get; set; } = [];
    public List<string> ObservationBytes { get; set; } = [];
    public Dictionary<string, string> InitialBits { get; set; } = [];
    public Dictionary<string, string> InitialBytes { get; set; } = [];
    public Mc7CycleCounterexample? Counterexample { get; set; }
    public string CertificateHash { get; set; } = "";
}

internal sealed class CycleSymState
{
    public required BoolExpr Rlo;
    public required BitVecExpr Accu;
    public SortedDictionary<uint, BoolExpr> Bits { get; init; } = [];
    public SortedDictionary<uint, BitVecExpr> Bytes { get; init; } = [];
}

internal sealed class CycleObservation
{
    public required BoolExpr Rlo;
    public required BitVecExpr Accu;
    public SortedDictionary<uint, BoolExpr> Bits { get; init; } = [];
    public SortedDictionary<uint, BitVecExpr> Bytes { get; init; } = [];
}

internal sealed class CyclePath
{
    public required CycleSymState State;
    public required BoolExpr Guard;
    public List<int> Steps { get; init; } = [];
    public bool Terminated { get; set; }
    public bool Looped { get; set; }
    public bool Truncated { get; set; }
}

internal sealed class CycleInputs
{
    private readonly Context _ctx;
    private readonly Dictionary<uint, BoolExpr> _bits = [];
    private readonly Dictionary<uint, BitVecExpr> _bytes = [];

    public CycleInputs(Context ctx) => _ctx = ctx;

    public BoolExpr Bit(uint address)
    {
        if (_bits.TryGetValue(address, out var value)) return value;
        value = _ctx.MkBoolConst($"in_bit_{address:x8}");
        _bits[address] = value;
        return value;
    }

    public BitVecExpr Byte(uint address)
    {
        if (_bytes.TryGetValue(address, out var value)) return value;
        value = _ctx.MkBVConst($"in_byte_{address:x8}", 8);
        _bytes[address] = value;
        return value;
    }
}

internal sealed class CycleHavoc
{
    private readonly Context _ctx;
    private readonly Dictionary<string, BoolExpr> _bools = [];
    private readonly Dictionary<string, BitVecExpr> _bvs = [];

    public CycleHavoc(Context ctx) => _ctx = ctx;

    public BoolExpr Bool(string key)
    {
        if (_bools.TryGetValue(key, out var value)) return value;
        value = _ctx.MkBoolConst("havoc_" + key);
        _bools[key] = value;
        return value;
    }

    public BitVecExpr Bv(string key, uint width)
    {
        if (_bvs.TryGetValue(key, out var value)) return value;
        value = _ctx.MkBVConst("havoc_" + key, width);
        _bvs[key] = value;
        return value;
    }
}

internal sealed class CycleFrontierItem
{
    public required CycleSymState A;
    public required CycleSymState B;
    public required BoolExpr Guard;
    public List<int> PathA { get; init; } = [];
    public List<int> PathB { get; init; } = [];
}

public static class Mc7CycleChecker
{
    public static Mc7CycleCertificate Compare(Mc7BlockData a, Mc7BlockData b, int cycles = 3,
        IEnumerable<uint>? observeBits = null, IEnumerable<uint>? observeBytes = null,
        IReadOnlyDictionary<uint, bool>? initialBits = null,
        IReadOnlyDictionary<uint, byte>? initialBytes = null,
        int maxPaths = 32, int maxSteps = 256, int maxPairs = 64)
    {
        var machineA = Mc7CycleMachine.Build(a);
        var machineB = Mc7CycleMachine.Build(b);
        var cert = new Mc7CycleCertificate
        {
            A = a.Name,
            B = b.Name,
            Cycles = cycles,
            AInstructions = machineA.Instructions.Count,
            BInstructions = machineB.Instructions.Count,
            AKnown = machineA.Instructions.Count(i => i.Known),
            AUnknown = machineA.Unknown,
            BKnown = machineB.Instructions.Count(i => i.Known),
            BUnknown = machineB.Unknown,
            ACodeHash = machineA.CodeHash,
            BCodeHash = machineB.CodeHash
        };
        var bitSet = new SortedSet<uint>(observeBits ?? machineA.BitAddresses.Concat(machineB.BitAddresses));
        var byteSet = new SortedSet<uint>(observeBytes ?? machineA.ByteAddresses.Concat(machineB.ByteAddresses));
        cert.ObservationBits = bitSet.Select(x => $"0x{x:x}").ToList();
        cert.ObservationBytes = byteSet.Select(x => $"0x{x:x}").ToList();
        cert.InitialBits = (initialBits ?? new Dictionary<uint, bool>())
            .OrderBy(kv => kv.Key).ToDictionary(kv => $"0x{kv.Key:x}", kv => kv.Value ? "true" : "false");
        cert.InitialBytes = (initialBytes ?? new Dictionary<uint, byte>())
            .OrderBy(kv => kv.Key).ToDictionary(kv => $"0x{kv.Key:x}", kv => kv.Value.ToString(CultureInfo.InvariantCulture));

        if (!machineA.HasMc7 || !machineB.HasMc7)
        {
            cert.Equivalent = false;
            cert.SmtResult = "sat";
            cert.Definitive = true;
            cert.Complete = true;
            cert.Bounded = false;
            cert.Counterexample = new Mc7CycleCounterexample
            {
                Reason = "no-mc7-path",
                Differences =
                [
                    new Mc7CycleObservationDiff { Kind = "machine", A = machineA.HasMc7 ? "mc7" : "no-mc7", B = machineB.HasMc7 ? "mc7" : "no-mc7" }
                ]
            };
            cert.CertificateHash = Hash(JsonSerializer.Serialize(cert, JsonUtil.Options));
            return cert;
        }

        using var ctx = new Context();
        var inputs = new CycleInputs(ctx);
        var havoc = new CycleHavoc(ctx);
        var solver = ctx.MkSolver();
        foreach (var kv in initialBits ?? new Dictionary<uint, bool>())
            solver.Assert(ctx.MkEq(inputs.Bit(kv.Key), ctx.MkBool(kv.Value)));
        foreach (var kv in initialBytes ?? new Dictionary<uint, byte>())
            solver.Assert(ctx.MkEq(inputs.Byte(kv.Key), ctx.MkBV(kv.Value, 8)));

        var frontier = new List<CycleFrontierItem>
        {
            new()
            {
                A = new CycleSymState { Rlo = ctx.MkFalse(), Accu = ctx.MkBV(0, 32) },
                B = new CycleSymState { Rlo = ctx.MkFalse(), Accu = ctx.MkBV(0, 32) },
                Guard = ctx.MkTrue()
            }
        };
        var truncated = false;
        var looped = false;
        var unknown = false;
        var pathsA = 0;
        var pathsB = 0;
        var pairs = 0;
        var cyclesChecked = 0;

        for (int cycle = 0; cycle < cycles && frontier.Count > 0; cycle++)
        {
            cyclesChecked = cycle + 1;
            var next = new List<CycleFrontierItem>();
            foreach (var item in frontier)
            {
                var exploreA = Explore(machineA, item.A, ctx, inputs, havoc, maxPaths, maxSteps);
                var exploreB = Explore(machineB, item.B, ctx, inputs, havoc, maxPaths, maxSteps);
                pathsA += exploreA.Paths.Count;
                pathsB += exploreB.Paths.Count;
                if (exploreA.Truncated || exploreB.Truncated) truncated = true;

                foreach (var pa in exploreA.Paths)
                {
                    foreach (var pb in exploreB.Paths)
                    {
                        pairs++;
                        if (pa.Looped || pb.Looped) looped = true;
                        var guard = ctx.MkAnd(item.Guard, pa.Guard, pb.Guard);
                        solver.Push();
                        solver.Assert(guard);
                        var feasible = solver.Check();
                        if (feasible != Status.SATISFIABLE)
                        {
                            if (feasible == Status.UNKNOWN) unknown = true;
                            solver.Pop();
                            continue;
                        }

                        var obsA = Observe(pa.State, bitSet, byteSet, inputs);
                        var obsB = Observe(pb.State, bitSet, byteSet, inputs);
                        var diff = BuildDiff(ctx, obsA, obsB, bitSet, byteSet);
                        solver.Assert(diff);
                        var status = solver.Check();
                        if (status == Status.SATISFIABLE)
                        {
                            var model = solver.Model;
                            cert.Equivalent = false;
                            cert.SmtResult = "sat";
                            cert.Definitive = true;
                            cert.Bounded = truncated;
                            cert.Complete = !truncated;
                            cert.Counterexample = new Mc7CycleCounterexample
                            {
                                Cycle = cycle,
                                Reason = "cycle-observation-difference",
                                PathA = string.Join(".", item.PathA.Concat(pa.Steps)),
                                PathB = string.Join(".", item.PathB.Concat(pb.Steps)),
                                Differences = DiffDetails(model, obsA, obsB, bitSet, byteSet),
                                Model = model.ToString()
                            };
                            cert.CyclesChecked = cyclesChecked;
                            cert.PathsA = pathsA;
                            cert.PathsB = pathsB;
                            cert.PairsChecked = pairs;
                            solver.Pop();
                            cert.CertificateHash = Hash(JsonSerializer.Serialize(cert, JsonUtil.Options));
                            return cert;
                        }
                        if (status == Status.UNKNOWN) unknown = true;
                        solver.Pop();

                        next.Add(new CycleFrontierItem
                        {
                            A = pa.State,
                            B = pb.State,
                            Guard = guard,
                            PathA = [.. item.PathA, .. pa.Steps],
                            PathB = [.. item.PathB, .. pb.Steps]
                        });
                    }
                }
            }

            if (next.Count > maxPairs)
            {
                next = next.Take(maxPairs).ToList();
                truncated = true;
            }
            frontier = Deduplicate(ctx, next);
        }

        cert.Equivalent = !unknown;
        cert.SmtResult = unknown ? "unknown" : "unsat";
        cert.CyclesChecked = cyclesChecked;
        cert.PathsA = pathsA;
        cert.PathsB = pathsB;
        cert.PairsChecked = pairs;
        cert.Bounded = truncated || looped || cyclesChecked < cycles;
        cert.Complete = !cert.Bounded && !unknown;
        cert.Definitive = !unknown && !cert.Bounded;
        cert.CertificateHash = Hash(JsonSerializer.Serialize(cert, JsonUtil.Options));
        return cert;
    }

    private static (List<CyclePath> Paths, bool Truncated) Explore(Mc7CycleMachine machine, CycleSymState state,
        Context ctx, CycleInputs inputs, CycleHavoc havoc, int maxPaths, int maxSteps)
    {
        var paths = new List<CyclePath>();
        var truncated = false;
        Walk(state, 0, ctx.MkTrue(), [], [], 0);
        return (paths, truncated);

        void Walk(CycleSymState current, int index, BoolExpr guard, List<int> steps, HashSet<int> visited, int depth)
        {
            if (paths.Count >= maxPaths)
            {
                truncated = true;
                return;
            }
            if (depth >= maxSteps)
            {
                paths.Add(new CyclePath { State = current, Guard = guard, Steps = [.. steps], Truncated = true });
                truncated = true;
                return;
            }
            if (index >= machine.Instructions.Count)
            {
                paths.Add(new CyclePath { State = current, Guard = guard, Steps = [.. steps], Terminated = true });
                return;
            }
            if (visited.Contains(index))
            {
                paths.Add(new CyclePath { State = current, Guard = guard, Steps = [.. steps], Looped = true });
                truncated = true;
                return;
            }

            var ins = machine.Instructions[index];
            var nextState = Step(current, ins, ctx, inputs, havoc);
            var nextSteps = new List<int>(steps) { index };
            var nextVisited = new HashSet<int>(visited) { index };

            if (ins.Opcode == "JMP" && ins.JumpTarget is int target)
            {
                Walk(nextState, target, ctx.MkAnd(guard, nextState.Rlo), nextSteps, nextVisited, depth + 1);
                Walk(nextState, index + 1, ctx.MkAnd(guard, ctx.MkNot(nextState.Rlo)), nextSteps, nextVisited, depth + 1);
                return;
            }
            if (ins.Opcode == "JMP" && ins.JumpUnresolved)
            {
                paths.Add(new CyclePath
                {
                    State = nextState,
                    Guard = ctx.MkAnd(guard, nextState.Rlo),
                    Steps = [.. nextSteps],
                    Terminated = true
                });
                Walk(nextState, index + 1, ctx.MkAnd(guard, ctx.MkNot(nextState.Rlo)), nextSteps, nextVisited, depth + 1);
                return;
            }
            Walk(nextState, index + 1, guard, nextSteps, nextVisited, depth + 1);
        }
    }

    private static CycleSymState Step(CycleSymState s, Mc7CycleInstruction ins, Context ctx,
        CycleInputs inputs, CycleHavoc havoc)
    {
        BoolExpr Bit(uint address) => s.Bits.TryGetValue(address, out var v) ? v : inputs.Bit(address);
        BitVecExpr Byte(uint address) => s.Bytes.TryGetValue(address, out var v) ? v : inputs.Byte(address);
        CycleSymState Next(BoolExpr? rlo = null, BitVecExpr? accu = null,
            SortedDictionary<uint, BoolExpr>? bits = null, SortedDictionary<uint, BitVecExpr>? bytes = null) => new()
        {
            Rlo = rlo ?? s.Rlo,
            Accu = accu ?? s.Accu,
            Bits = bits ?? s.Bits,
            Bytes = bytes ?? s.Bytes
        };
        CycleSymState WriteBit(uint address, BoolExpr value)
        {
            var bits = new SortedDictionary<uint, BoolExpr>(s.Bits) { [address] = value };
            return Next(bits: bits);
        }
        CycleSymState WriteByte(uint address, BitVecExpr value)
        {
            var bytes = new SortedDictionary<uint, BitVecExpr>(s.Bytes) { [address] = value };
            return Next(bytes: bytes);
        }

        var bitAddress = ins.BitAddress ?? (ins.Operands.Length > 0 ? ins.Operands[0] : SyntheticAddress(ins));
        var byteAddress = ins.ByteAddress ?? (ins.Operands.Length > 0 ? ins.Operands[0] : SyntheticAddress(ins));
        var bit = Bit(bitAddress);
        var mem = Byte(byteAddress);

        switch (ins.Opcode)
        {
            case "NOP":
            case "JMP":
                return s;
            case "CLR_RLO":
                return Next(rlo: ctx.MkFalse());
            case "SET_RLO":
                return Next(rlo: ctx.MkTrue());
            case "NOT":
                return Next(rlo: ctx.MkNot(s.Rlo));
            case "LD_BOOL":
            case "LD_FP":
                return Next(rlo: bit);
            case "LDN_BOOL":
            case "LDN_FP":
                return Next(rlo: ctx.MkNot(bit));
            case "A_BOOL":
            case "A_FP":
                return Next(rlo: ctx.MkAnd(s.Rlo, bit));
            case "AN_BOOL":
            case "AN_FP":
                return Next(rlo: ctx.MkAnd(s.Rlo, ctx.MkNot(bit)));
            case "O_BOOL":
            case "O_FP":
                return Next(rlo: ctx.MkOr(s.Rlo, bit));
            case "ON_BOOL":
            case "ON_FP":
                return Next(rlo: ctx.MkOr(s.Rlo, ctx.MkNot(bit)));
            case "X_BOOL":
            case "X_FP":
                return Next(rlo: ctx.MkXor(s.Rlo, bit));
            case "XN_BOOL":
            case "XN_FP":
                return Next(rlo: ctx.MkNot(ctx.MkXor(s.Rlo, bit)));
            case "ST":
            case "ST_LD":
                return WriteBit(bitAddress, s.Rlo);
            case "STN":
                return WriteBit(bitAddress, ctx.MkNot(s.Rlo));
            case "SET":
                return WriteBit(bitAddress, ctx.MkTrue());
            case "CLR":
                return WriteBit(bitAddress, ctx.MkFalse());
            case "S_BOOL":
                return WriteBit(bitAddress, ctx.MkOr(bit, s.Rlo));
            case "R_BOOL":
                return WriteBit(bitAddress, ctx.MkAnd(bit, ctx.MkNot(s.Rlo)));
            case "MOVE_BOOL":
                return Next(rlo: bit);
            case "MOVE":
            case "MOVE_F":
                return Next(accu: ctx.MkZeroExt(24, mem));
            case "SWAP":
            case "XCHG":
                return Next(accu: ctx.MkConcat(
                    ctx.MkExtract(7, 0, s.Accu),
                    ctx.MkConcat(
                        ctx.MkExtract(15, 8, s.Accu),
                        ctx.MkConcat(
                            ctx.MkExtract(23, 16, s.Accu),
                            ctx.MkExtract(31, 24, s.Accu)))));
            default:
                return Havoc(s, ins, ctx, havoc);
        }
    }

    private static CycleSymState Havoc(CycleSymState s, Mc7CycleInstruction ins, Context ctx, CycleHavoc havoc)
    {
        var key = $"{ins.Offset:x}_{ins.Opcode}";
        var bits = new SortedDictionary<uint, BoolExpr>();
        foreach (var kv in s.Bits) bits[kv.Key] = havoc.Bool($"{key}_b_{kv.Key:x8}");
        var bytes = new SortedDictionary<uint, BitVecExpr>();
        foreach (var kv in s.Bytes) bytes[kv.Key] = havoc.Bv($"{key}_m_{kv.Key:x8}", 8);
        return new CycleSymState
        {
            Rlo = havoc.Bool(key + "_r"),
            Accu = havoc.Bv(key + "_a", 32),
            Bits = bits,
            Bytes = bytes
        };
    }

    private static uint SyntheticAddress(Mc7CycleInstruction ins) =>
        uint.MaxValue - (uint)ins.Offset;

    private static CycleObservation Observe(CycleSymState state, SortedSet<uint> bitSet,
        SortedSet<uint> byteSet, CycleInputs inputs)
    {
        var observation = new CycleObservation { Rlo = state.Rlo, Accu = state.Accu };
        foreach (var address in bitSet)
            observation.Bits[address] = state.Bits.TryGetValue(address, out var bit) ? bit : inputs.Bit(address);
        foreach (var address in byteSet)
            observation.Bytes[address] = state.Bytes.TryGetValue(address, out var value) ? value : inputs.Byte(address);
        return observation;
    }

    private static BoolExpr BuildDiff(Context ctx, CycleObservation a, CycleObservation b,
        SortedSet<uint> bitSet, SortedSet<uint> byteSet)
    {
        var parts = new List<BoolExpr>
        {
            ctx.MkNot(ctx.MkEq(a.Rlo, b.Rlo)),
            ctx.MkNot(ctx.MkEq(a.Accu, b.Accu))
        };
        foreach (var address in bitSet)
            parts.Add(ctx.MkNot(ctx.MkEq(a.Bits[address], b.Bits[address])));
        foreach (var address in byteSet)
            parts.Add(ctx.MkNot(ctx.MkEq(a.Bytes[address], b.Bytes[address])));
        return ctx.MkOr(parts.ToArray());
    }

    private static List<Mc7CycleObservationDiff> DiffDetails(Model model, CycleObservation a,
        CycleObservation b, SortedSet<uint> bitSet, SortedSet<uint> byteSet)
    {
        var diffs = new List<Mc7CycleObservationDiff>();
        var rloA = Eval(model, a.Rlo);
        var rloB = Eval(model, b.Rlo);
        if (rloA != rloB)
            diffs.Add(new Mc7CycleObservationDiff { Kind = "rlo", A = rloA, B = rloB });
        var accuA = Eval(model, a.Accu);
        var accuB = Eval(model, b.Accu);
        if (accuA != accuB)
            diffs.Add(new Mc7CycleObservationDiff { Kind = "accu", A = accuA, B = accuB });
        foreach (var address in bitSet)
        {
            var va = Eval(model, a.Bits[address]);
            var vb = Eval(model, b.Bits[address]);
            if (va != vb)
                diffs.Add(new Mc7CycleObservationDiff { Kind = "bit", Address = $"0x{address:x}", A = va, B = vb });
        }
        foreach (var address in byteSet)
        {
            var va = Eval(model, a.Bytes[address]);
            var vb = Eval(model, b.Bytes[address]);
            if (va != vb)
                diffs.Add(new Mc7CycleObservationDiff { Kind = "byte", Address = $"0x{address:x}", A = va, B = vb });
        }
        return diffs;
    }

    private static string Eval(Model model, Expr expr)
    {
        var value = model.Eval(expr, true).ToString();
        return value switch
        {
            "true" => "1",
            "false" => "0",
            _ => value
        };
    }

    private static List<CycleFrontierItem> Deduplicate(Context ctx, List<CycleFrontierItem> items)
    {
        var byKey = new Dictionary<string, CycleFrontierItem>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var key = Key(item.A) + "|" + Key(item.B);
            if (byKey.TryGetValue(key, out var existing))
                existing.Guard = ctx.MkOr(existing.Guard, item.Guard);
            else
                byKey[key] = item;
        }
        return byKey.Values.ToList();
    }

    private static string Key(CycleSymState s)
    {
        var sb = new StringBuilder();
        sb.Append(s.Rlo).Append(';').Append(s.Accu).Append(';');
        foreach (var kv in s.Bits) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
        sb.Append('|');
        foreach (var kv in s.Bytes) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
        return sb.ToString();
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

public sealed class Mc7ConcreteCycle
{
    public int Cycle { get; set; }
    public bool Rlo { get; set; }
    public uint Accu { get; set; }
    public SortedDictionary<uint, bool> Bits { get; set; } = [];
    public SortedDictionary<uint, byte> Bytes { get; set; } = [];
}

public sealed class Mc7ConcreteTrace
{
    public string Machine { get; set; } = "";
    public bool Havoc { get; set; }
    public string? HavocReason { get; set; }
    public List<Mc7ConcreteCycle> Cycles { get; set; } = [];
}

public static class Mc7ConcreteMachine
{
    public static Mc7ConcreteTrace Run(Mc7CycleMachine machine, int cycles,
        IReadOnlyDictionary<uint, bool>? initialBits = null,
        IReadOnlyDictionary<uint, byte>? initialBytes = null)
    {
        var trace = new Mc7ConcreteTrace { Machine = machine.Name };
        var bits = new SortedDictionary<uint, bool>();
        foreach (var kv in initialBits ?? new Dictionary<uint, bool>()) bits[kv.Key] = kv.Value;
        var bytes = new SortedDictionary<uint, byte>();
        foreach (var kv in initialBytes ?? new Dictionary<uint, byte>()) bytes[kv.Key] = kv.Value;
        bool rlo = false;
        uint accu = 0;

        for (int cycle = 0; cycle < cycles; cycle++)
        {
            int index = 0;
            int steps = 0;
            var visited = new HashSet<int>();
            while (index < machine.Instructions.Count)
            {
                if (steps++ > 4096 || !visited.Add(index))
                {
                    trace.Havoc = true;
                    trace.HavocReason = "loop-or-step-limit";
                    return trace;
                }
                var ins = machine.Instructions[index];
                if (!ins.Known)
                {
                    trace.Havoc = true;
                    trace.HavocReason = $"unknown-opcode@{ins.Offset}:{ins.Opcode}";
                    return trace;
                }
                bool Bit(uint address) => bits.TryGetValue(address, out var v) && v;
                byte Byte(uint address) => bytes.TryGetValue(address, out var v) ? v : (byte)0;
                var bitAddress = ins.BitAddress ?? 0;
                var byteAddress = ins.ByteAddress ?? 0;
                switch (ins.Opcode)
                {
                    case "NOP":
                        break;
                    case "CLR_RLO": rlo = false; break;
                    case "SET_RLO": rlo = true; break;
                    case "NOT": rlo = !rlo; break;
                    case "LD_BOOL":
                    case "LD_FP": rlo = Bit(bitAddress); break;
                    case "LDN_BOOL":
                    case "LDN_FP": rlo = !Bit(bitAddress); break;
                    case "A_BOOL":
                    case "A_FP": rlo = rlo && Bit(bitAddress); break;
                    case "AN_BOOL":
                    case "AN_FP": rlo = rlo && !Bit(bitAddress); break;
                    case "O_BOOL":
                    case "O_FP": rlo = rlo || Bit(bitAddress); break;
                    case "ON_BOOL":
                    case "ON_FP": rlo = rlo || !Bit(bitAddress); break;
                    case "X_BOOL":
                    case "X_FP": rlo ^= Bit(bitAddress); break;
                    case "XN_BOOL":
                    case "XN_FP": rlo = !(rlo ^ Bit(bitAddress)); break;
                    case "ST":
                    case "ST_LD": bits[bitAddress] = rlo; break;
                    case "STN": bits[bitAddress] = !rlo; break;
                    case "SET": bits[bitAddress] = true; break;
                    case "CLR": bits[bitAddress] = false; break;
                    case "S_BOOL": bits[bitAddress] = Bit(bitAddress) || rlo; break;
                    case "R_BOOL": bits[bitAddress] = Bit(bitAddress) && !rlo; break;
                    case "MOVE_BOOL": rlo = Bit(bitAddress); break;
                    case "MOVE":
                    case "MOVE_F": accu = Byte(byteAddress); break;
                    case "SWAP":
                    case "XCHG":
                        accu = (accu & 0x000000FF) << 24 | (accu & 0x0000FF00) << 8
                               | (accu & 0x00FF0000) >> 8 | (accu & 0xFF000000) >> 24;
                        break;
                    default:
                        trace.Havoc = true;
                        trace.HavocReason = $"unmodeled-opcode@{ins.Offset}:{ins.Opcode}";
                        return trace;
                }

                if (ins.Opcode == "JMP")
                {
                    if (rlo && ins.JumpTarget is int target) index = target;
                    else index++;
                }
                else index++;
            }

            trace.Cycles.Add(new Mc7ConcreteCycle
            {
                Cycle = cycle,
                Rlo = rlo,
                Accu = accu,
                Bits = new SortedDictionary<uint, bool>(bits),
                Bytes = new SortedDictionary<uint, byte>(bytes)
            });
        }
        return trace;
    }
}

public sealed class Mc7AsmProgram
{
    public byte[] Code { get; set; } = [];
    public Dictionary<string, int> Labels { get; set; } = [];
}

public sealed class Mc7AsmBuilder
{
    private sealed class Item
    {
        public string Opcode = "";
        public byte[] Operands = [];
        public string? Label;
        public string? JumpLabel;
    }

    private readonly List<Item> _items = [];

    public Mc7AsmBuilder Label(string name)
    {
        _items.Add(new Item { Label = name });
        return this;
    }

    public Mc7AsmBuilder Emit(string opcode, params byte[] operands)
    {
        _items.Add(new Item { Opcode = opcode, Operands = operands });
        return this;
    }

    public Mc7AsmBuilder Bit(string opcode, uint address) =>
        Emit(opcode, (byte)(address & 0xFF));

    public Mc7AsmBuilder Jmp(string label)
    {
        _items.Add(new Item { Opcode = "JMP", Operands = new byte[4], JumpLabel = label });
        return this;
    }

    public Mc7AsmProgram Build()
    {
        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        var offsets = new List<int>();
        int offset = 0;
        foreach (var item in _items)
        {
            if (item.Label is not null)
            {
                labels[item.Label] = offset;
                continue;
            }
            offsets.Add(offset);
            offset += OpcodeLength(item.Opcode) + item.Operands.Length;
        }

        var bytes = new List<byte>();
        var labelIndex = 0;
        foreach (var item in _items)
        {
            if (item.Label is not null) continue;
            var pattern = Pattern(item.Opcode);
            bytes.AddRange(pattern);
            if (item.JumpLabel is not null)
            {
                if (!labels.TryGetValue(item.JumpLabel, out var target))
                    throw new CliException("unknown jump label: " + item.JumpLabel);
                bytes.Add((byte)((target >> 24) & 0xFF));
                bytes.Add((byte)((target >> 16) & 0xFF));
                bytes.Add((byte)((target >> 8) & 0xFF));
                bytes.Add((byte)(target & 0xFF));
            }
            else
            {
                bytes.AddRange(item.Operands);
            }
            labelIndex++;
        }
        return new Mc7AsmProgram { Code = bytes.ToArray(), Labels = labels };
    }

    private static int OpcodeLength(string opcode) => Pattern(opcode).Length;

    private static byte[] Pattern(string opcode) =>
        Mc7OpcodeTable.All.FirstOrDefault(o => o.Name == opcode)?.Pattern
        ?? throw new CliException("unknown MC7 opcode: " + opcode);
}

public sealed class Mc7CycleCase
{
    public string Name { get; set; } = "";
    public bool ExpectedEquivalent { get; set; }
    public bool ActualEquivalent { get; set; }
    public bool Pass { get; set; }
    public string? Detail { get; set; }
    public Mc7CycleCertificate Certificate { get; set; } = new();
}

public sealed class Mc7CycleSelfTestResult
{
    public bool Pass { get; set; }
    public List<string> Checks { get; set; } = [];
    public List<Mc7CycleCase> Cases { get; set; } = [];
    public Mc7CycleCertificate? MainMain { get; set; }
    public Mc7CycleCertificate? MainBlock1 { get; set; }
}

public static class Mc7CycleSelfTest
{
    public static Mc7CycleSelfTestResult Run(Mc7AnalysisResult analysis)
    {
        var result = new Mc7CycleSelfTestResult();

        var commutedA = Machine("and-commuted-a", new Mc7AsmBuilder()
            .Bit("LD_BOOL", 0x01).Bit("A_BOOL", 0x02).Bit("ST", 0x10).Build());
        var commutedB = Machine("and-commuted-b", new Mc7AsmBuilder()
            .Bit("LD_BOOL", 0x02).Bit("A_BOOL", 0x01).Bit("ST", 0x10).Build());
        var hiddenTemp = Machine("hidden-temp", new Mc7AsmBuilder()
            .Bit("LD_BOOL", 0x01).Bit("ST", 0x20).Bit("LD_BOOL", 0x20).Bit("ST", 0x10).Build());
        var direct = Machine("direct", new Mc7AsmBuilder()
            .Bit("LD_BOOL", 0x01).Bit("ST", 0x10).Build());
        var andOrA = Machine("and", new Mc7AsmBuilder()
            .Bit("LD_BOOL", 0x01).Bit("A_BOOL", 0x02).Bit("ST", 0x10).Build());
        var andOrB = Machine("or", new Mc7AsmBuilder()
            .Bit("LD_BOOL", 0x01).Bit("O_BOOL", 0x02).Bit("ST", 0x10).Build());
        var toggle = Machine("toggle", new Mc7AsmBuilder()
            .Bit("LD_BOOL", 0x01).Bit("X_BOOL", 0x10).Bit("ST", 0x10).Build());
        var branchA = Machine("branch", new Mc7AsmBuilder()
            .Bit("LD_BOOL", 0x01)
            .Jmp("l1")
            .Bit("ST", 0x10)
            .Label("l1")
            .Bit("LD_BOOL", 0x02)
            .Bit("ST", 0x11)
            .Build());
        var branchB = Machine("branchless", new Mc7AsmBuilder()
            .Bit("LD_BOOL", 0x02)
            .Bit("ST", 0x11)
            .Build());
        var loop = Machine("loop", new Mc7AsmBuilder()
            .Label("top")
            .Bit("LD_BOOL", 0x01)
            .Jmp("top")
            .Build());

        void Case(string name, bool expected, Mc7CycleCertificate cert, string? detail = null)
        {
            result.Cases.Add(new Mc7CycleCase
            {
                Name = name,
                ExpectedEquivalent = expected,
                ActualEquivalent = cert.Equivalent,
                Pass = cert.Equivalent == expected,
                Detail = detail,
                Certificate = cert
            });
        }

        var c1 = Mc7CycleChecker.Compare(commutedA, commutedB, cycles: 3);
        Case("commuted-and", true, c1);

        var c2 = Mc7CycleChecker.Compare(hiddenTemp, direct, cycles: 3,
            observeBits: [0x10]);
        Case("hidden-temp-out-only", true, c2);

        var c3 = Mc7CycleChecker.Compare(hiddenTemp, direct, cycles: 3,
            observeBits: [0x10, 0x20]);
        Case("hidden-temp-observed", false, c3);

        var c4 = Mc7CycleChecker.Compare(andOrA, andOrB, cycles: 3,
            observeBits: [0x10]);
        Case("and-vs-or", false, c4);

        var c5 = Mc7CycleChecker.Compare(direct, toggle, cycles: 2,
            observeBits: [0x10], initialBits: new Dictionary<uint, bool> { [0x10] = false });
        Case("delayed-toggle-bound2", false, c5);

        var c6 = Mc7CycleChecker.Compare(direct, toggle, cycles: 1,
            observeBits: [0x10], initialBits: new Dictionary<uint, bool> { [0x10] = false });
        Case("delayed-toggle-bound1", true, c6);

        var c7 = Mc7CycleChecker.Compare(branchA, branchB, cycles: 2,
            observeBits: [0x11]);
        Case("branch-path-enumeration", true, c7);

        var c8 = Mc7CycleChecker.Compare(loop, loop, cycles: 2,
            observeBits: [0x01]);
        Case("loop-bounded", true, c8, "loop cannot terminate a scan cycle; result is bounded");

        var fc = analysis.Blocks.FirstOrDefault(b => b.HasMc7
            && string.Equals(b.Subtype, "scl", StringComparison.OrdinalIgnoreCase))
            ?? analysis.Blocks.FirstOrDefault(b => b.HasMc7);
        if (fc is not null)
        {
            var self = Mc7CycleChecker.Compare(fc, fc, cycles: 3);
            Case("real-self", true, self);
            var mutated = MutateLastByte(fc);
            var mutatedCert = Mc7CycleChecker.Compare(fc, mutated, cycles: 3);
            Case("real-mutated", false, mutatedCert);
            result.MainMain = self;
        }

        var main = analysis.Blocks.FirstOrDefault(b => b.Name == "Main");
        var block1 = analysis.Blocks.FirstOrDefault(b => b.Name == "Block_1");
        if (main is not null)
        {
            result.MainMain = Mc7CycleChecker.Compare(main, main, cycles: 3);
            Case("main-self", true, result.MainMain);
        }
        if (main is not null && block1 is not null)
        {
            result.MainBlock1 = Mc7CycleChecker.Compare(main, block1, cycles: 3);
            Case("main-vs-block1", false, result.MainBlock1);
        }

        var concreteA = Mc7ConcreteMachine.Run(Mc7CycleMachine.Build(commutedA), 3,
            new Dictionary<uint, bool> { [0x01] = true, [0x02] = true });
        var concreteB = Mc7ConcreteMachine.Run(Mc7CycleMachine.Build(commutedB), 3,
            new Dictionary<uint, bool> { [0x01] = true, [0x02] = true });
        var concreteAnd = Mc7ConcreteMachine.Run(Mc7CycleMachine.Build(andOrA), 1,
            new Dictionary<uint, bool> { [0x01] = true, [0x02] = false });
        var concreteOr = Mc7ConcreteMachine.Run(Mc7CycleMachine.Build(andOrB), 1,
            new Dictionary<uint, bool> { [0x01] = true, [0x02] = false });
        var concreteDirect = Mc7ConcreteMachine.Run(Mc7CycleMachine.Build(direct), 2,
            new Dictionary<uint, bool> { [0x01] = true, [0x10] = false });
        var concreteToggle = Mc7ConcreteMachine.Run(Mc7CycleMachine.Build(toggle), 2,
            new Dictionary<uint, bool> { [0x01] = true, [0x10] = false });

        string Bits(Mc7ConcreteTrace trace, uint address) =>
            string.Join(",", trace.Cycles.Select(c => c.Bits.TryGetValue(address, out var v) && v ? "1" : "0"));

        var concreteCommutedEqual = Bits(concreteA, 0x10) == Bits(concreteB, 0x10);
        var concreteAndOrDiffer = Bits(concreteAnd, 0x10) != Bits(concreteOr, 0x10);
        var concreteDelayed = Bits(concreteDirect, 0x10) == "1,1" && Bits(concreteToggle, 0x10) == "1,0";

        var delayedDiff = FirstConcreteDifference(concreteDirect, concreteToggle, 0x10);

        result.Checks.Add($"commuted-and.equivalent={c1.Equivalent}");
        result.Checks.Add($"commuted-and.complete={c1.Complete}");
        result.Checks.Add($"commuted-and.cyclesChecked={c1.CyclesChecked}");
        result.Checks.Add($"hidden-temp-out-only.equivalent={c2.Equivalent}");
        result.Checks.Add($"hidden-temp-observed.equivalent={c3.Equivalent}");
        result.Checks.Add($"and-vs-or.equivalent={c4.Equivalent}");
        result.Checks.Add($"and-vs-or.counterexampleCycle={c4.Counterexample?.Cycle}");
        result.Checks.Add($"and-vs-or.differences={c4.Counterexample?.Differences.Count ?? 0}");
        result.Checks.Add($"and-vs-or.modelLength={c4.Counterexample?.Model?.Length ?? 0}");
        result.Checks.Add($"delayed-toggle-bound2.equivalent={c5.Equivalent}");
        result.Checks.Add($"delayed-toggle-bound2.counterexampleCycle={c5.Counterexample?.Cycle}");
        result.Checks.Add($"delayed-toggle-bound1.equivalent={c6.Equivalent}");
        result.Checks.Add($"branch-path-enumeration.equivalent={c7.Equivalent}");
        result.Checks.Add($"branch-path-enumeration.pathsA={c7.PathsA}");
        result.Checks.Add($"branch-path-enumeration.complete={c7.Complete}");
        result.Checks.Add($"loop-bounded.equivalent={c8.Equivalent}");
        result.Checks.Add($"loop-bounded.complete={c8.Complete}");
        result.Checks.Add($"loop-bounded.definitive={c8.Definitive}");
        result.Checks.Add($"concrete.commuted.out={Bits(concreteA, 0x10)}");
        result.Checks.Add($"concrete.and-vs-or.and={Bits(concreteAnd, 0x10)} or={Bits(concreteOr, 0x10)}");
        result.Checks.Add($"concrete.delayed.direct={Bits(concreteDirect, 0x10)} toggle={Bits(concreteToggle, 0x10)}");
        result.Checks.Add($"concrete.delayed.firstDifference={delayedDiff}");
        foreach (var c in result.Cases)
            result.Checks.Add($"case.{c.Name}.expected={c.ExpectedEquivalent} actual={c.ActualEquivalent} pass={c.Pass}");

        result.Pass = result.Cases.All(c => c.Pass)
                      && c1.Equivalent && c1.Complete && c1.CyclesChecked == 3
                      && c2.Equivalent && !c3.Equivalent
                      && !c4.Equivalent && c4.Counterexample?.Cycle == 0
                      && c4.Counterexample?.Differences.Count > 0
                      && (c4.Counterexample?.Model?.Length ?? 0) > 0
                      && !c5.Equivalent && c5.Counterexample?.Cycle == 1
                      && c6.Equivalent && c6.Complete
                      && c7.Equivalent && c7.PathsA >= 2 && c7.Complete
                      && c8.Equivalent && !c8.Complete && !c8.Definitive
                      && concreteCommutedEqual && concreteAndOrDiffer && concreteDelayed
                      && delayedDiff == c5.Counterexample?.Cycle;
        return result;
    }

    private static int FirstConcreteDifference(Mc7ConcreteTrace a, Mc7ConcreteTrace b, uint address)
    {
        var n = Math.Min(a.Cycles.Count, b.Cycles.Count);
        for (int i = 0; i < n; i++)
        {
            var va = a.Cycles[i].Bits.TryGetValue(address, out var x) && x;
            var vb = b.Cycles[i].Bits.TryGetValue(address, out var y) && y;
            if (va != vb) return i;
        }
        return -1;
    }

    private static Mc7BlockData Machine(string name, Mc7AsmProgram program) => new()
    {
        Name = name,
        HasMc7 = true,
        CodeLength = program.Code.Length,
        CodeHex = Convert.ToHexString(program.Code).ToLowerInvariant(),
        CodeHash = Convert.ToHexString(SHA256.HashData(program.Code)).ToLowerInvariant()
    };

    private static Mc7BlockData MutateLastByte(Mc7BlockData source)
    {
        var clone = new Mc7BlockData
        {
            Name = source.Name + "-mutated",
            InstanceId = source.InstanceId,
            ClassName = source.ClassName,
            Subtype = source.Subtype,
            HasMc7 = source.HasMc7,
            CodeLength = source.CodeLength,
            CodeHash = source.CodeHash,
            LanguageLevel = source.LanguageLevel,
            CompilerVersion = source.CompilerVersion,
            DependentLength = source.DependentLength,
            DependentHash = source.DependentHash,
            Sections = source.Sections,
            Instructions = source.Instructions
        };
        if (source.CodeHex is null) return clone;
        var bytes = Convert.FromHexString(source.CodeHex);
        bytes[^1] ^= 0xFF;
        clone.CodeHex = Convert.ToHexString(bytes).ToLowerInvariant();
        clone.CodeHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return clone;
    }
}
