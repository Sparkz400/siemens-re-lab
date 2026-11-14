using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaProject;

public sealed class Mc7Section
{
    public int Index { get; set; }
    public int Offset { get; set; }
    public int Length { get; set; }
    public string Prefix { get; set; } = "";
}

public sealed class Mc7BlockData
{
    public string Name { get; set; } = "";
    public long InstanceId { get; set; }
    public string? ClassName { get; set; }
    public string? Subtype { get; set; }
    public bool HasMc7 { get; set; }
    public int CodeLength { get; set; }
    public string? CodeHash { get; set; }
    public string? CodeHex { get; set; }
    public int? LanguageLevel { get; set; }
    public string? CompilerVersion { get; set; }
    public int? DependentLength { get; set; }
    public string? DependentHash { get; set; }
    public List<Mc7Section> Sections { get; set; } = [];
    public List<Mc7Instruction> Instructions { get; set; } = [];
    public string? Error { get; set; }
}

public sealed class Mc7AnalysisResult
{
    public string ExportPath { get; set; } = "";
    public string? HostPath { get; set; }
    public string? HostWarning { get; set; }
    public List<Mc7BlockData> Blocks { get; set; } = [];
}

public static class Mc7Analyzer
{
    public static Mc7AnalysisResult Analyze(ObjectExport export, string? tiaBin)
    {
        var result = new Mc7AnalysisResult { ExportPath = export.FilePath };
        var byInst = export.Objects.GroupBy(o => o.InstanceId).ToDictionary(g => g.Key, g => g.First());
        string? host = null;
        string? classicHost = null;
        if (tiaBin is not null)
        {
            host = Mc7HostRunner.EnsureHost(tiaBin, out var warning);
            result.HostPath = host;
            result.HostWarning = warning;
            classicHost = Mc7ClassicHostRunner.EnsureHost(tiaBin, out var classicWarning);
            if (!string.IsNullOrWhiteSpace(classicWarning))
                result.HostWarning = string.Join("; ", new[] { result.HostWarning, classicWarning }
                    .Where(w => !string.IsNullOrWhiteSpace(w)));
        }

        foreach (var block in export.Objects.Where(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)))
        {
            var data = new Mc7BlockData
            {
                Name = block.Name ?? block.InstanceId.ToString(),
                InstanceId = block.InstanceId,
                ClassName = block.ClassName,
                Subtype = block.Subtype
            };
            var loadableLink = block.Links.FirstOrDefault(l => l.TypeId == 2232336 || l.TypeId == 2232330);
            if (loadableLink.InstanceId == 0 || !byInst.TryGetValue(loadableLink.InstanceId, out var loadable))
            {
                data.Error = "no loadable code block data";
                result.Blocks.Add(data);
                continue;
            }

            if (loadableLink.TypeId == 2232330)
            {
                AnalyzeClassic(loadable, data, classicHost, tiaBin);
                result.Blocks.Add(data);
                continue;
            }

            string? codeHex = null;
            if (loadable.CoreBlobs.TryGetValue("OpenImmediateData", out var open) && open.Length > 0) codeHex = open;
            else if (loadable.CoreBlobs.TryGetValue("OwnSignatureData", out var sig) && sig.Length > 0) codeHex = sig;
            if (codeHex is not null)
            {
                data.HasMc7 = true;
                data.CodeHex = codeHex;
                data.CodeLength = codeHex.Length / 2;
                var code = Convert.FromHexString(codeHex);
                data.CodeHash = Hash(code);
            }

            var depLink = loadable.Links.FirstOrDefault(l => l.TypeId == 2232327);
            if (depLink.InstanceId != 0 && byInst.TryGetValue(depLink.InstanceId, out var dep)
                && dep.CoreBlobs.TryGetValue("MC7PlusData", out var depHex))
            {
                var depBytes = Convert.FromHexString(depHex);
                data.DependentLength = depBytes.Length;
                data.DependentHash = Hash(depBytes);
                data.Sections = ParseSections(depBytes);
            }

            if (host is not null && data.HasMc7)
            {
                var temp = Path.Combine(Path.GetTempPath(), "tia-project", "mc7-analysis");
                Directory.CreateDirectory(temp);
                var binPath = Path.Combine(temp, Safe(data.Name) + ".bin");
                File.WriteAllBytes(binPath, Convert.FromHexString(data.CodeHex!));
                var run = Mc7HostRunner.Run(host, tiaBin!, binPath);
                if (run.Ok)
                {
                    data.LanguageLevel = run.LanguageLevel;
                    data.CompilerVersion = run.CompilerVersion;
                    data.Instructions = run.Instructions;
                    if (run.Error is not null) data.Error = run.Error;
                }
                else
                {
                    data.Error = run.Error;
                }
            }
            result.Blocks.Add(data);
        }
        return result;
    }

    private static void AnalyzeClassic(ExportedObjectInfo loadable, Mc7BlockData data, string? classicHost, string? tiaBin)
    {
        if (!loadable.CoreBlobs.TryGetValue("BinaryData", out var binaryHex) || binaryHex.Length == 0)
        {
            data.Error = "classic block has no BinaryData";
            return;
        }
        var binary = Convert.FromHexString(binaryHex);
        var endSac = ParseEndSac(loadable);
        var codeLength = endSac >= 0 ? endSac + 1 : Math.Max(0, binary.Length - 78);
        if (codeLength <= 0 || 78 + codeLength > binary.Length)
        {
            data.Error = "classic BinaryData has an invalid code range";
            return;
        }
        data.HasMc7 = true;
        data.CodeLength = codeLength;
        data.CodeHex = Convert.ToHexString(binary, 78, codeLength).ToLowerInvariant();
        data.CodeHash = Hash(Convert.FromHexString(data.CodeHex));
        data.CompilerVersion = "classic-mc7";
        if (classicHost is null || tiaBin is null) return;
        var temp = Path.Combine(Path.GetTempPath(), "tia-project", "mc7-classic-analysis");
        Directory.CreateDirectory(temp);
        var binPath = Path.Combine(temp, Safe(data.Name) + ".binary");
        File.WriteAllBytes(binPath, binary);
        var run = Mc7ClassicHostRunner.Run(classicHost, tiaBin, binPath, codeLength);
        if (run.Ok)
        {
            if (!string.IsNullOrEmpty(run.CodeHex))
            {
                data.CodeHex = run.CodeHex;
                data.CodeLength = run.CodeLength > 0 ? run.CodeLength : data.CodeLength;
                data.CodeHash = Hash(Convert.FromHexString(data.CodeHex));
            }
            data.Instructions = run.Instructions;
        }
        else
        {
            data.Error = run.Error;
        }
    }

    private static int ParseEndSac(ExportedObjectInfo loadable)
    {
        if (!loadable.CoreBlobs.TryGetValue("BlockDebugInformation", out var hex) || hex.Length == 0) return -1;
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromHexString(hex));
            var match = Regex.Match(text, "EndSAC=\"(\\d+)\"");
            return match.Success ? int.Parse(match.Groups[1].Value) : -1;
        }
        catch
        {
            return -1;
        }
    }

    public static List<Mc7Section> ParseSections(byte[] data)
    {
        var markers = new List<int>();
        for (int i = 0; i + 4 <= data.Length; i++)
            if (data[i] == 0xEF && data[i + 1] == 0xBE && data[i + 2] == 0xAD && data[i + 3] == 0xDE)
                markers.Add(i);
        var sections = new List<Mc7Section>();
        for (int i = 0; i < markers.Count; i++)
        {
            var start = markers[i];
            var end = i + 1 < markers.Count ? markers[i + 1] - 1 : data.Length - 1;
            var len = end - start + 1;
            var prefix = data.Skip(start).Take(Math.Min(8, len)).ToArray();
            sections.Add(new Mc7Section { Index = i, Offset = start, Length = len, Prefix = Convert.ToHexString(prefix).ToLowerInvariant() });
        }
        return sections;
    }

    public static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Safe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}

public sealed class Mc7GraphNode
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public List<(string Label, string Target)> Outgoing { get; set; } = [];
}

public sealed class Mc7Graph
{
    public string Root { get; set; } = "entry";
    public List<Mc7GraphNode> Nodes { get; set; } = [];
    public string GraphHash { get; set; } = "";
    public string? RootColor { get; set; }
}

public static class Mc7GraphBuilder
{
    public static Mc7Graph Build(Mc7BlockData block)
    {
        var g = new Mc7Graph();
        if (!block.HasMc7)
        {
            var missing = new Mc7GraphNode { Id = "missing", Label = "missing|no-mc7" };
            g.Nodes.Add(missing);
            g.Root = "missing";
            g.GraphHash = Hash("missing|no-mc7");
            return g;
        }

        var entry = new Mc7GraphNode
        {
            Id = "entry",
            Label = $"entry|lang={block.LanguageLevel?.ToString() ?? "?"}|len={block.CodeLength}|hash={block.CodeHash}"
        };
        var code = new Mc7GraphNode
        {
            Id = "code",
            Label = $"code|ops={block.Instructions.Count}|dep={block.DependentLength?.ToString() ?? "0"}"
        };
        entry.Outgoing.Add(("start", "code"));
        foreach (var ins in block.Instructions)
            code.Outgoing.Add(("call:" + ins.Operator, "code"));
        if (block.Instructions.Count == 0)
            code.Outgoing.Add(("nocall", "code"));

        g.Nodes.Add(entry);
        g.Nodes.Add(code);
        var previous = "code";
        for (int i = 0; i < block.Sections.Count; i++)
        {
            var s = block.Sections[i];
            var id = "s" + i;
            g.Nodes.Add(new Mc7GraphNode
            {
                Id = id,
                Label = $"section|idx={i}|len={s.Length}|prefix={s.Prefix[..Math.Min(8, s.Prefix.Length)]}"
            });
            g.Nodes.First(n => n.Id == previous).Outgoing.Add(("section", id));
            previous = id;
        }
        var exit = new Mc7GraphNode { Id = "exit", Label = "exit" };
        g.Nodes.Add(exit);
        g.Nodes.First(n => n.Id == previous).Outgoing.Add(("exit", "exit"));
        g.Root = "entry";
        g.GraphHash = Hash(string.Join("\n", g.Nodes.OrderBy(n => n.Id).Select(n =>
            n.Id + ":" + n.Label + "->" + string.Join(",", n.Outgoing.OrderBy(e => e.Label).Select(e => e.Label + ":" + e.Target)))));
        return g;
    }

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}

public sealed class Mc7BisimulationResult
{
    public bool Bisimilar { get; set; }
    public string? AColor { get; set; }
    public string? BColor { get; set; }
    public int Iterations { get; set; }
    public string? AGraphHash { get; set; }
    public string? BGraphHash { get; set; }
}

public static class Mc7Bisimulation
{
    public static Mc7BisimulationResult Compare(Mc7Graph a, Mc7Graph b)
    {
        var nodes = new Dictionary<string, Mc7GraphNode>(StringComparer.Ordinal);
        foreach (var n in a.Nodes) nodes["a:" + n.Id] = new Mc7GraphNode
        {
            Id = "a:" + n.Id,
            Label = n.Label,
            Outgoing = n.Outgoing.Select(e => (e.Label, "a:" + e.Target)).ToList()
        };
        foreach (var n in b.Nodes) nodes["b:" + n.Id] = new Mc7GraphNode
        {
            Id = "b:" + n.Id,
            Label = n.Label,
            Outgoing = n.Outgoing.Select(e => (e.Label, "b:" + e.Target)).ToList()
        };

        var colors = nodes.ToDictionary(kv => kv.Key, kv => Hash("L:" + kv.Value.Label), StringComparer.Ordinal);
        int iterations = 0;
        for (int iter = 0; iter < 64; iter++)
        {
            iterations = iter + 1;
            var next = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in nodes)
            {
                var sig = colors[kv.Key] + "|" + string.Join(";", kv.Value.Outgoing
                    .OrderBy(e => e.Label, StringComparer.Ordinal)
                    .ThenBy(e => e.Target, StringComparer.Ordinal)
                    .Select(e => e.Label + "->" + colors.GetValueOrDefault(e.Target, "?")));
                next[kv.Key] = Hash(sig);
            }
            if (next.All(kv => colors.GetValueOrDefault(kv.Key) == kv.Value)) { colors = next; break; }
            colors = next;
        }
        var aRoot = "a:" + a.Root;
        var bRoot = "b:" + b.Root;
        return new Mc7BisimulationResult
        {
            Bisimilar = colors.GetValueOrDefault(aRoot) == colors.GetValueOrDefault(bRoot),
            AColor = colors.GetValueOrDefault(aRoot),
            BColor = colors.GetValueOrDefault(bRoot),
            Iterations = iterations,
            AGraphHash = a.GraphHash,
            BGraphHash = b.GraphHash
        };
    }

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}

public sealed class Mc7EquivalenceCertificate
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public bool Equivalent { get; set; }
    public string Method { get; set; } = "mc7-graph-bisimulation";
    public bool AHasMc7 { get; set; }
    public bool BHasMc7 { get; set; }
    public int ACodeLength { get; set; }
    public int BCodeLength { get; set; }
    public string? ACodeHash { get; set; }
    public string? BCodeHash { get; set; }
    public int? ALanguageLevel { get; set; }
    public int? BLanguageLevel { get; set; }
    public bool Bisimilar { get; set; }
    public string? AColor { get; set; }
    public string? BColor { get; set; }
    public string? AGraphHash { get; set; }
    public string? BGraphHash { get; set; }
    public List<string> Findings { get; set; } = [];
    public string CertificateHash { get; set; } = "";
}

public static class Mc7Equivalence
{
    public static Mc7EquivalenceCertificate Compare(Mc7BlockData a, Mc7BlockData b)
    {
        var ga = Mc7GraphBuilder.Build(a);
        var gb = Mc7GraphBuilder.Build(b);
        var bisim = Mc7Bisimulation.Compare(ga, gb);
        var cert = new Mc7EquivalenceCertificate
        {
            A = a.Name,
            B = b.Name,
            AHasMc7 = a.HasMc7,
            BHasMc7 = b.HasMc7,
            ACodeLength = a.CodeLength,
            BCodeLength = b.CodeLength,
            ACodeHash = a.CodeHash,
            BCodeHash = b.CodeHash,
            ALanguageLevel = a.LanguageLevel,
            BLanguageLevel = b.LanguageLevel,
            Bisimilar = bisim.Bisimilar,
            AColor = bisim.AColor,
            BColor = bisim.BColor,
            AGraphHash = bisim.AGraphHash,
            BGraphHash = bisim.BGraphHash
        };
        if (!a.HasMc7) cert.Findings.Add("A has no MC7 code");
        if (!b.HasMc7) cert.Findings.Add("B has no MC7 code");
        if (a.HasMc7 && b.HasMc7 && a.CodeHash != b.CodeHash) cert.Findings.Add("MC7 code hash differs");
        if (a.HasMc7 && b.HasMc7 && a.LanguageLevel != b.LanguageLevel) cert.Findings.Add("MC7 language level differs");
        if (!bisim.Bisimilar) cert.Findings.Add("MC7 labeled transition graphs are not bisimilar");
        cert.Equivalent = a.HasMc7 && b.HasMc7 && a.CodeHash == b.CodeHash
                          && a.LanguageLevel == b.LanguageLevel && bisim.Bisimilar;
        cert.CertificateHash = Hash(System.Text.Json.JsonSerializer.Serialize(cert, JsonUtil.Options));
        return cert;
    }

    private static string Hash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}

public sealed class Mc7SelfTestResult
{
    public bool Pass { get; set; }
    public List<string> Checks { get; set; } = [];
    public Mc7EquivalenceCertificate MainMain { get; set; } = new();
    public Mc7EquivalenceCertificate MainBlock1 { get; set; } = new();
    public Mc7EquivalenceCertificate MainMutated { get; set; } = new();
}

public static class Mc7SelfTest
{
    public static Mc7SelfTestResult Run(Mc7AnalysisResult analysis)
    {
        var main = analysis.Blocks.FirstOrDefault(b => b.Name == "Main")
                   ?? throw new CliException("MC7 analysis: Main not found");
        var block1 = analysis.Blocks.FirstOrDefault(b => b.Name == "Block_1")
                     ?? throw new CliException("MC7 analysis: Block_1 not found");
        var mm = Mc7Equivalence.Compare(main, main);
        var mb = Mc7Equivalence.Compare(main, block1);
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
        var mut = Mc7Equivalence.Compare(main, mutated);
        var checks = new List<string>
        {
            $"main.hasMc7={main.HasMc7}",
            $"main.codeLength={main.CodeLength}",
            $"main.languageLevel={main.LanguageLevel?.ToString() ?? "?"}",
            $"block1.hasMc7={block1.HasMc7}",
            $"main-main.equivalent={mm.Equivalent}",
            $"main-main.bisimilar={mm.Bisimilar}",
            $"main-block1.equivalent={mb.Equivalent}",
            $"main-block1.bisimilar={mb.Bisimilar}",
            $"main-mutated.equivalent={mut.Equivalent}",
            $"main-mutated.bisimilar={mut.Bisimilar}",
            $"main-main.color={mm.AColor}",
            $"main-block1.aColor={mb.AColor}",
            $"main-block1.bColor={mb.BColor}",
            $"main-mutated.aColor={mut.AColor}",
            $"main-mutated.bColor={mut.BColor}"
        };
        var pass = main.HasMc7 && mm.Equivalent && mm.Bisimilar
                   && !mb.Equivalent && !mb.Bisimilar
                   && !mut.Equivalent && !mut.Bisimilar;
        return new Mc7SelfTestResult { Pass = pass, Checks = checks, MainMain = mm, MainBlock1 = mb, MainMutated = mut };
    }
}
