using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TiaProject;

public sealed class EquivalenceFinding
{
    public string Aspect { get; set; } = "";
    public string? A { get; set; }
    public string? B { get; set; }
}

public sealed class EquivalenceCertificate
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public bool Equivalent { get; set; }
    public string Method { get; set; } = "structural+call-graph";
    public string? AHash { get; set; }
    public string? BHash { get; set; }
    public string CertificateHash { get; set; } = "";
    public List<EquivalenceFinding> Findings { get; set; } = [];
}

public static class EquivalenceCertifier
{
    public static EquivalenceCertificate Compare(IrUnit a, IrUnit b)
    {
        var cert = new EquivalenceCertificate
        {
            A = a.Id,
            B = b.Id,
            AHash = a.BodyHash,
            BHash = b.BodyHash,
            Equivalent = true
        };
        if (a.BodyHash != b.BodyHash)
        {
            cert.Equivalent = false;
            cert.Findings.Add(new EquivalenceFinding { Aspect = "body", A = a.BodyHash, B = b.BodyHash });
        }
        var asec = new HashSet<string>(a.InterfaceSections, StringComparer.OrdinalIgnoreCase);
        var bsec = new HashSet<string>(b.InterfaceSections, StringComparer.OrdinalIgnoreCase);
        foreach (var s in asec.Except(bsec)) { cert.Equivalent = false; cert.Findings.Add(new EquivalenceFinding { Aspect = "interface", A = s }); }
        foreach (var s in bsec.Except(asec)) { cert.Equivalent = false; cert.Findings.Add(new EquivalenceFinding { Aspect = "interface", B = s }); }
        var acalls = new HashSet<string>(a.Calls, StringComparer.OrdinalIgnoreCase);
        var bcalls = new HashSet<string>(b.Calls, StringComparer.OrdinalIgnoreCase);
        foreach (var c in acalls.Except(bcalls)) { cert.Equivalent = false; cert.Findings.Add(new EquivalenceFinding { Aspect = "call", A = c }); }
        foreach (var c in bcalls.Except(acalls)) { cert.Equivalent = false; cert.Findings.Add(new EquivalenceFinding { Aspect = "call", B = c }); }
        cert.CertificateHash = Hash(JsonSerializer.Serialize(cert, JsonUtil.Options));
        return cert;
    }

    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

public sealed class BisimulationCertificate
{
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    public bool Bisimilar { get; set; }
    public string? AColor { get; set; }
    public string? BColor { get; set; }
    public int Iterations { get; set; }
    public string CertificateHash { get; set; } = "";
}

public static class BisimulationOracle
{
    public static BisimulationCertificate Check(IrDocument doc, string aId, string bId)
    {
        var graph = BuildGraph(doc);
        var initial = doc.Units.ToDictionary(u => u.Id,
            u => EquivalenceCertifier.Hash($"{u.Kind}|{u.Language}|{u.BodyHash}")[..16]);
        var colors = Refine(graph, initial, out var iterations);
        var a = doc.Units.FirstOrDefault(u => u.Id == aId) ?? throw new CliException("unit not found: " + aId);
        var b = doc.Units.FirstOrDefault(u => u.Id == bId) ?? throw new CliException("unit not found: " + bId);
        var cert = new BisimulationCertificate
        {
            A = aId,
            B = bId,
            Bisimilar = colors.GetValueOrDefault(aId) == colors.GetValueOrDefault(bId),
            AColor = colors.GetValueOrDefault(aId),
            BColor = colors.GetValueOrDefault(bId),
            Iterations = iterations
        };
        cert.CertificateHash = EquivalenceCertifier.Hash(JsonSerializer.Serialize(cert, JsonUtil.Options));
        return cert;
    }

    private static Dictionary<string, List<(string Label, string Target)>> BuildGraph(IrDocument doc)
    {
        var byName = doc.Units.GroupBy(u => u.Name).ToDictionary(g => g.Key, g => g.First().Id);
        var graph = doc.Units.ToDictionary(u => u.Id, u => new List<(string, string)>());
        foreach (var u in doc.Units)
            foreach (var call in u.Calls)
                if (byName.TryGetValue(call, out var target))
                    graph[u.Id].Add(("call:" + call, target));
        return graph;
    }

    private static Dictionary<string, string> Refine(Dictionary<string, List<(string Label, string Target)>> graph, Dictionary<string, string> initial, out int iterations)
    {
        var colors = new Dictionary<string, string>(initial);
        iterations = 0;
        for (int iter = 0; iter < 32; iter++)
        {
            iterations = iter + 1;
            var next = new Dictionary<string, string>();
            foreach (var kv in graph)
            {
                var sig = string.Join("|", kv.Value.OrderBy(e => e.Label, StringComparer.Ordinal)
                    .Select(e => e.Label + "->" + colors.GetValueOrDefault(e.Target, "?")));
                next[kv.Key] = EquivalenceCertifier.Hash(colors[kv.Key] + "|" + sig)[..16];
            }
            if (next.All(kv => colors.GetValueOrDefault(kv.Key) == kv.Value)) { colors = next; break; }
            colors = next;
        }
        return colors;
    }
}

public sealed class SignatureRecord
{
    public string Artifact { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Algorithm { get; set; } = "ECDSA-P256-SHA256";
    public string Signature { get; set; } = "";
}

public static class LogicRegistry
{
    public static (string PrivateKey, string PublicKey) InitKey(string basePath)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var priv = basePath + ".private.pem";
        var pub = basePath + ".public.pem";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(priv))!);
        File.WriteAllText(priv, ecdsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(pub, ecdsa.ExportSubjectPublicKeyInfoPem());
        return (priv, pub);
    }

    public static SignatureRecord Sign(string privateKeyPath, string artifactPath)
    {
        var bytes = File.ReadAllBytes(artifactPath);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(File.ReadAllText(privateKeyPath));
        var sig = ecdsa.SignData(bytes, HashAlgorithmName.SHA256);
        return new SignatureRecord
        {
            Artifact = Path.GetFullPath(artifactPath),
            Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Signature = Convert.ToBase64String(sig)
        };
    }

    public static bool Verify(string publicKeyPath, string artifactPath, SignatureRecord record)
    {
        var bytes = File.ReadAllBytes(artifactPath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(hash, record.Hash, StringComparison.OrdinalIgnoreCase)) return false;
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(File.ReadAllText(publicKeyPath));
        return ecdsa.VerifyData(bytes, Convert.FromBase64String(record.Signature), HashAlgorithmName.SHA256);
    }

    public static SignatureRecord SignBytes(string privateKeyPath, string artifactName, byte[] bytes)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(File.ReadAllText(privateKeyPath));
        var sig = ecdsa.SignData(bytes, HashAlgorithmName.SHA256);
        return new SignatureRecord
        {
            Artifact = artifactName,
            Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Signature = Convert.ToBase64String(sig)
        };
    }

    public static bool VerifyBytes(string publicKeyPath, byte[] bytes, SignatureRecord record)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(File.ReadAllText(publicKeyPath));
        return ecdsa.VerifyData(bytes, Convert.FromBase64String(record.Signature), HashAlgorithmName.SHA256);
    }
}

public sealed class RegistryManifest
{
    public string Project { get; set; } = "";
    public string Created { get; set; } = "";
    public List<ManifestEntry> Entries { get; set; } = [];
}

public sealed class ManifestEntry
{
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string Hash { get; set; } = "";
}

public sealed class MarketContract
{
    public string Schema { get; set; } = ContractVerifier.SchemaId;
    public string Block { get; set; } = "";
    public List<ContractSection> Interface { get; set; } = [];
    public List<string> Preconditions { get; set; } = [];
    public List<string> Postconditions { get; set; } = [];
    public List<string> Effects { get; set; } = [];
    public string? Description { get; set; }
}

public sealed class MarketPackage
{
    public string Name { get; set; } = "";
    public string BlockHash { get; set; } = "";
    public string InterfaceHash { get; set; } = "";
    public MarketContract Contract { get; set; } = new();
    public List<string> Calls { get; set; } = [];
    public ContractVerification? ContractVerification { get; set; }
    public VciProvenanceRecord? Provenance { get; set; }
    public bool ContractVerified { get; set; }
    public bool ProvenanceVerified { get; set; }
    public bool ContractSatisfied { get; set; }
    public SignatureRecord? Signature { get; set; }
    public List<string> Errors { get; set; } = [];
}

public static class BlockMarket
{
    public static MarketPackage Publish(ObjectExport export, Mc7AnalysisResult analysis, string blockName,
        MarketContract? contract, string privateKeyPath, string? projectRoot = null, string? vciPath = null)
    {
        var ir = IrExporter.Build(export);
        var unit = ir.Units.FirstOrDefault(u => string.Equals(u.Name, blockName, StringComparison.OrdinalIgnoreCase))
                   ?? throw new CliException("block not found: " + blockName);
        contract ??= new MarketContract
        {
            Block = unit.Name,
            Interface = unit.InterfaceSections.Select(s => new ContractSection { Section = s }).ToList(),
            Preconditions = ["true"],
            Postconditions = ["true"],
            Effects = []
        };
        var verification = ContractVerifier.Verify(export, analysis, blockName, contract);
        var provenance = projectRoot is not null && vciPath is not null
            ? VciProvenance.Bind(projectRoot, vciPath)
            : new VciProvenanceRecord { Status = "not-provided" };
        var pkg = new MarketPackage
        {
            Name = unit.Name,
            BlockHash = unit.BodyHash ?? "",
            InterfaceHash = EquivalenceCertifier.Hash(string.Join("|", unit.InterfaceSections)),
            Calls = unit.Calls,
            Contract = contract,
            ContractVerification = verification,
            Provenance = provenance,
            ContractVerified = verification.Valid,
            ProvenanceVerified = provenance.Match,
            ContractSatisfied = verification.Valid && provenance.Match
        };
        if (!pkg.ContractVerified) pkg.Errors.Add("contract verification failed");
        if (!pkg.ProvenanceVerified) pkg.Errors.Add("VCI provenance verification failed");
        pkg.Signature = LogicRegistry.SignBytes(privateKeyPath, pkg.Name, Payload(pkg));
        return pkg;
    }

    public static bool Verify(MarketPackage pkg, string publicKeyPath, string? projectRoot = null, string? vciPath = null,
        ObjectExport? export = null, Mc7AnalysisResult? analysis = null)
    {
        if (!pkg.ContractVerified || !pkg.ProvenanceVerified) return false;
        if (pkg.Signature is null || !LogicRegistry.VerifyBytes(publicKeyPath, Payload(pkg), pkg.Signature))
            return false;
        if (export is not null)
        {
            var unit = IrExporter.Build(export).Units
                .FirstOrDefault(u => string.Equals(u.Name, pkg.Name, StringComparison.OrdinalIgnoreCase));
            if (unit is null || !string.Equals(unit.BodyHash ?? "", pkg.BlockHash, StringComparison.OrdinalIgnoreCase))
                return false;
            if (analysis is not null)
            {
                var verification = ContractVerifier.Verify(export, analysis, pkg.Name, pkg.Contract);
                if (!verification.Valid) return false;
            }
        }
        if (projectRoot is not null && vciPath is not null)
        {
            var p = VciProvenance.Bind(projectRoot, vciPath);
            if (!p.Match || pkg.Provenance?.ActualHash != p.ActualHash) return false;
        }
        return true;
    }

    private static byte[] Payload(MarketPackage pkg) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            pkg.Name,
            pkg.BlockHash,
            pkg.InterfaceHash,
            pkg.Contract,
            pkg.Calls,
            pkg.ContractVerified,
            pkg.ProvenanceVerified,
            pkg.ContractVerification,
            pkg.Provenance
        }, JsonUtil.Options);
}
