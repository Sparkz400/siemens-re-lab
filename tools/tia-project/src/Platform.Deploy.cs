using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TiaProject;

public sealed class RollbackManifest
{
    public string Project { get; set; } = "";
    public string Created { get; set; } = DateTime.UtcNow.ToString("O");
    public Dictionary<string, string> Hashes { get; set; } = [];
    public List<string> Payload { get; set; } = [];
    public string Seal { get; set; } = "";
    public SignatureRecord? Signature { get; set; }
}

public static class RollbackDesk
{
    public static RollbackManifest Create(ProjectPaths paths, string exportPath, string outDir, string? privateKey = null)
    {
        Directory.CreateDirectory(outDir);
        var payloadDir = Path.Combine(outDir, "payload");
        Directory.CreateDirectory(payloadDir);
        var manifest = new RollbackManifest { Project = paths.Root };
        foreach (var (name, path) in new[]
                 {
                     ("PEData.plf", paths.Plf), ("PEData.idx", paths.Idx),
                     ("XRef.db", paths.XrefDb), ("project.objectframe.xml", exportPath)
                 })
        {
            if (!File.Exists(path)) continue;
            var dst = Path.Combine(payloadDir, name);
            File.Copy(path, dst, overwrite: true);
            manifest.Hashes[name] = PlatformIo.HashFile(dst);
            manifest.Payload.Add(name);
        }
        var sealBytes = JsonSerializer.SerializeToUtf8Bytes(manifest.Hashes.OrderBy(kv => kv.Key), JsonUtil.Options);
        manifest.Seal = EquivalenceCertifier.Hash(Encoding.UTF8.GetString(sealBytes));
        if (privateKey is not null)
            manifest.Signature = LogicRegistry.SignBytes(privateKey, "rollback-manifest", sealBytes);
        File.WriteAllText(Path.Combine(outDir, "rollback-manifest.json"), JsonUtil.Serialize(manifest), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "RESTORE.md"), $"""
            # Rollback package

            Seal: `{manifest.Seal}`

            1. Stop TIA Portal and the file-storage server.
            2. Copy `payload/PEData.plf` and `payload/PEData.idx` into `System/`.
            3. Copy `payload/XRef.db` into `XRef/`.
            4. Reopen the project and verify the object count against `rollback-manifest.json`.
            5. If a signature is present, verify it with the registry public key.
            """, new UTF8Encoding(false));
        return manifest;
    }
}

public sealed class PodRecord
{
    public string Artifact { get; set; } = "";
    public string ArtifactHash { get; set; } = "";
    public string CpuSerial { get; set; } = "";
    public string RackSlot { get; set; } = "";
    public string Timestamp { get; set; } = DateTime.UtcNow.ToString("O");
    public SignatureRecord? Signature { get; set; }
}

public static class ProofOfDelivery
{
    public static PodRecord Create(string artifactPath, string cpuSerial, string rackSlot, string privateKey)
    {
        var rec = new PodRecord
        {
            Artifact = Path.GetFullPath(artifactPath),
            ArtifactHash = PlatformIo.HashFile(artifactPath),
            CpuSerial = cpuSerial,
            RackSlot = rackSlot
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { rec.Artifact, rec.ArtifactHash, rec.CpuSerial, rec.RackSlot, rec.Timestamp }, JsonUtil.Options);
        rec.Signature = LogicRegistry.SignBytes(privateKey, "pod", bytes);
        return rec;
    }

    public static bool Verify(PodRecord rec, string publicKey)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { rec.Artifact, rec.ArtifactHash, rec.CpuSerial, rec.RackSlot, rec.Timestamp }, JsonUtil.Options);
        return rec.Signature is not null && LogicRegistry.VerifyBytes(publicKey, bytes, rec.Signature);
    }
}

public sealed class UmacObjectSummary
{
    public string ClassName { get; set; } = "";
    public long InstanceId { get; set; }
    public Guid Guid { get; set; }
    public string? Name { get; set; }
    public string? Subtype { get; set; }
}

public sealed class UmacDigestResult
{
    public string Artifact { get; set; } = "";
    public string ArtifactHash { get; set; } = "";
    public string Digest { get; set; } = "";
    public int ObjectCount { get; set; }
    public Dictionary<string, int> Classes { get; set; } = [];
    public List<UmacObjectSummary> Objects { get; set; } = [];
}

public static class UmacDigest
{
    public static UmacDigestResult Compute(string artifactPath)
    {
        var export = ObjectExport.Read(artifactPath);
        var objects = DomainExporter.Select(export, "umac");
        var lines = objects
            .OrderBy(o => o.Guid.ToString(), StringComparer.Ordinal)
            .Select(o => string.Join("|", new[]
            {
                o.ClassName,
                o.InstanceId.ToString(CultureInfo.InvariantCulture),
                o.Guid.ToString(),
                o.Name ?? "",
                o.Subtype ?? "",
                string.Join(";", o.Attributes.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value)),
                string.Join(";", o.Links.OrderBy(x => x, StringComparer.Ordinal))
            }))
            .ToList();
        return new UmacDigestResult
        {
            Artifact = Path.GetFullPath(artifactPath),
            ArtifactHash = PlatformIo.HashFile(artifactPath),
            Digest = EquivalenceCertifier.Hash(string.Join("\n", lines)),
            ObjectCount = objects.Count,
            Classes = objects.GroupBy(o => o.ClassName).ToDictionary(g => g.Key, g => g.Count()),
            Objects = objects.Select(o => new UmacObjectSummary
            {
                ClassName = o.ClassName,
                InstanceId = o.InstanceId,
                Guid = o.Guid,
                Name = o.Name,
                Subtype = o.Subtype
            }).ToList()
        };
    }
}

public sealed class UmacReviewRecord
{
    public string Schema { get; set; } = UmacReview.SchemaId;
    public string Artifact { get; set; } = "";
    public string ArtifactHash { get; set; } = "";
    public string UmacDigest { get; set; } = "";
    public int ObjectCount { get; set; }
    public string ReviewedBy { get; set; } = "";
    public string ReviewedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string Decision { get; set; } = "approved";
    public List<string> Findings { get; set; } = [];
    public string ReviewHash { get; set; } = "";
    public SignatureRecord? Signature { get; set; }
}

public sealed class UmacReviewVerification
{
    public bool Valid { get; set; }
    public List<string> Checks { get; set; } = [];
    public List<string> Findings { get; set; } = [];
}

public static class UmacReview
{
    public const string SchemaId = "tia-project/umac-review/v1";

    public static UmacReviewRecord Create(string artifactPath, string reviewedBy, string decision,
        string? privateKeyPath = null, UmacDigestResult? digestOverride = null)
    {
        var digest = digestOverride ?? UmacDigest.Compute(artifactPath);
        var record = new UmacReviewRecord
        {
            Artifact = Path.GetFullPath(artifactPath),
            ArtifactHash = digest.ArtifactHash,
            UmacDigest = digest.Digest,
            ObjectCount = digest.ObjectCount,
            ReviewedBy = reviewedBy,
            Decision = decision,
            Findings = []
        };
        Rehash(record);
        if (privateKeyPath is not null)
            record.Signature = LogicRegistry.SignBytes(privateKeyPath, "umac-review", Payload(record));
        return record;
    }

    public static void Rehash(UmacReviewRecord record) =>
        record.ReviewHash = EquivalenceCertifier.Hash(Encoding.UTF8.GetString(Payload(record)));

    public static UmacReviewVerification Verify(UmacReviewRecord? review, WarehouseItem item, string? publicKeyPath = null)
    {
        var result = new UmacReviewVerification();
        if (review is null)
        {
            result.Findings.Add("no UMAC review artifact");
            return result;
        }

        void Check(string name, bool ok, string detail)
        {
            result.Checks.Add((ok ? "PASS " : "FAIL ") + name + ": " + detail);
            if (!ok) result.Findings.Add(name + ": " + detail);
        }

        Check("schema", string.Equals(review.Schema, SchemaId, StringComparison.Ordinal), review.Schema);
        Check("decision", string.Equals(review.Decision, "approved", StringComparison.OrdinalIgnoreCase), review.Decision);
        Check("artifact-hash", string.Equals(review.ArtifactHash, item.Hash, StringComparison.OrdinalIgnoreCase), $"{review.ArtifactHash} vs {item.Hash}");
        Check("umac-digest", string.Equals(review.UmacDigest, item.UmacDigest, StringComparison.OrdinalIgnoreCase), $"{review.UmacDigest} vs {item.UmacDigest}");
        Check("object-count", review.ObjectCount == item.UmacObjectCount, $"{review.ObjectCount} vs {item.UmacObjectCount}");
        var expected = EquivalenceCertifier.Hash(Encoding.UTF8.GetString(Payload(review)));
        Check("review-hash", string.Equals(review.ReviewHash, expected, StringComparison.OrdinalIgnoreCase), review.ReviewHash);
        if (publicKeyPath is not null && review.Signature is not null)
            Check("signature", LogicRegistry.VerifyBytes(publicKeyPath, Payload(review), review.Signature), "ECDSA-P256");
        result.Valid = result.Checks.All(c => c.StartsWith("PASS ", StringComparison.Ordinal));
        return result;
    }

    public static byte[] Payload(UmacReviewRecord record) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            record.Schema,
            record.Artifact,
            record.ArtifactHash,
            record.UmacDigest,
            record.ObjectCount,
            record.ReviewedBy,
            record.ReviewedAt,
            record.Decision,
            record.Findings
        }, JsonUtil.Options);
}

public sealed class WarehouseItem
{
    public string Id { get; set; } = "";
    public string Artifact { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Status { get; set; } = "held";
    public List<string> Tests { get; set; } = [];
    public string Added { get; set; } = DateTime.UtcNow.ToString("O");
    public string UmacDigest { get; set; } = "";
    public int UmacObjectCount { get; set; }
    public string? UmacReviewHash { get; set; }
    public string? UmacReviewDecision { get; set; }
    public string? UmacReviewArtifact { get; set; }
}

public static class BondedWarehouse
{
    public static WarehouseItem Add(string file, string id, string dir)
    {
        var itemDir = Path.Combine(dir, id);
        Directory.CreateDirectory(itemDir);
        var dst = Path.Combine(itemDir, "artifact" + Path.GetExtension(file));
        File.Copy(file, dst, overwrite: true);
        var item = new WarehouseItem { Id = id, Artifact = dst, Hash = PlatformIo.HashFile(dst) };
        try
        {
            var digest = UmacDigest.Compute(dst);
            item.UmacDigest = digest.Digest;
            item.UmacObjectCount = digest.ObjectCount;
        }
        catch { }
        JsonUtil.Write(item, Path.Combine(itemDir, "status.json"));
        return item;
    }

    public static WarehouseItem Release(string dir, string id, IEnumerable<string> tests,
        UmacReviewRecord? review = null, string? publicKeyPath = null)
    {
        var itemDir = Path.Combine(dir, id);
        var item = JsonSerializer.Deserialize<WarehouseItem>(File.ReadAllText(Path.Combine(itemDir, "status.json")), JsonUtil.Options)!;
        var verification = UmacReview.Verify(review, item, publicKeyPath);
        if (!verification.Valid)
            throw new CliException("UMAC review rejected: " + string.Join("; ", verification.Findings));
        item.Tests.AddRange(tests);
        item.Status = "released";
        item.UmacReviewHash = review!.ReviewHash;
        item.UmacReviewDecision = review.Decision;
        item.UmacReviewArtifact = review.Artifact;
        JsonUtil.Write(item, Path.Combine(itemDir, "status.json"));
        return item;
    }

    public static List<WarehouseItem> List(string dir) =>
        Directory.GetFiles(dir, "status.json", SearchOption.AllDirectories)
            .Select(f => JsonSerializer.Deserialize<WarehouseItem>(File.ReadAllText(f), JsonUtil.Options)!)
            .ToList();
}

public sealed class WarehouseSelfTestResult
{
    public bool Pass { get; set; }
    public int UmacObjects { get; set; }
    public string Digest { get; set; } = "";
    public List<string> Checks { get; set; } = [];
}

public static class WarehouseSelfTest
{
    public static WarehouseSelfTestResult Run(string exportPath, string outDir, string keyBase)
    {
        Directory.CreateDirectory(outDir);
        if (!File.Exists(keyBase + ".private.pem")) LogicRegistry.InitKey(keyBase);
        var digest = UmacDigest.Compute(exportPath);
        var good = UmacReview.Create(exportPath, "warehouse-self-test", "approved", keyBase + ".private.pem", digest);
        JsonUtil.Write(good, Path.Combine(outDir, "umac-review.json"));
        var warehouse = Path.Combine(outDir, "warehouse");
        Directory.CreateDirectory(warehouse);

        bool TryRelease(string id, UmacReviewRecord? review, out string error)
        {
            error = "";
            try
            {
                BondedWarehouse.Add(exportPath, id, warehouse);
                BondedWarehouse.Release(warehouse, id, ["hash", "interface", "umac"], review, keyBase + ".public.pem");
                return true;
            }
            catch (CliException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        var positive = TryRelease("positive", good, out var positiveError);
        var missing = TryRelease("missing-review", null, out var missingError);
        var rejectedReview = UmacReview.Create(exportPath, "warehouse-self-test", "rejected", null, digest);
        var rejected = TryRelease("rejected", rejectedReview, out var rejectedError);
        var wrongDigestReview = UmacReview.Create(exportPath, "warehouse-self-test", "approved", null,
            new UmacDigestResult
            {
                ArtifactHash = digest.ArtifactHash,
                Digest = new string('0', 64),
                ObjectCount = digest.ObjectCount
            });
        var wrongDigest = TryRelease("wrong-digest", wrongDigestReview, out var wrongDigestError);
        var tampered = UmacReview.Create(exportPath, "warehouse-self-test", "approved", null, digest);
        tampered.ReviewedBy = "tampered";
        var tamperedResult = TryRelease("tampered", tampered, out var tamperedError);
        var wrongArtifact = UmacReview.Create(exportPath, "warehouse-self-test", "approved", null, digest);
        wrongArtifact.ArtifactHash = new string('1', 64);
        UmacReview.Rehash(wrongArtifact);
        var wrongArtifactResult = TryRelease("wrong-artifact", wrongArtifact, out var wrongArtifactError);

        var checks = new List<string>
        {
            $"umacObjects={digest.ObjectCount}",
            $"digest={digest.Digest}",
            $"positive.released={positive}",
            $"positive.error={positiveError}",
            $"missing-review.rejected={!missing}",
            $"rejected-decision.rejected={!rejected}",
            $"wrong-digest.rejected={!wrongDigest}",
            $"tampered-review.rejected={!tamperedResult}",
            $"wrong-artifact.rejected={!wrongArtifactResult}",
            $"missing-review.error={missingError}",
            $"rejected.error={rejectedError}",
            $"wrong-digest.error={wrongDigestError}",
            $"tampered.error={tamperedError}",
            $"wrong-artifact.error={wrongArtifactError}"
        };
        var pass = digest.ObjectCount > 0
                   && positive
                   && !missing && !rejected && !wrongDigest && !tamperedResult && !wrongArtifactResult;
        var result = new WarehouseSelfTestResult
        {
            Pass = pass,
            UmacObjects = digest.ObjectCount,
            Digest = digest.Digest,
            Checks = checks
        };
        JsonUtil.Write(result, Path.Combine(outDir, "warehouse-test.json"));
        return result;
    }
}
public sealed class Tariff
{
    public string Block { get; set; } = "";
    public string TariffCode { get; set; } = "";
    public string Language { get; set; } = "";
    public int InterfaceSections { get; set; }
    public int Calls { get; set; }
    public List<string> Duties { get; set; } = [];
    public string Origin { get; set; } = "local";
}

public static class CustomsBroker
{
    public static Tariff Classify(ObjectExport export, string blockName)
    {
        var ir = IrExporter.Build(export);
        var unit = ir.Units.FirstOrDefault(u => string.Equals(u.Name, blockName, StringComparison.OrdinalIgnoreCase))
                   ?? throw new CliException("block not found: " + blockName);
        var code = $"TC-{(unit.Language ?? "??").ToUpperInvariant()}-{unit.InterfaceSections.Count:D2}-{unit.Calls.Count:D2}-{(unit.BodyHash ?? "0")[..8]}";
        var duties = new List<string> { "interface-review", "hash-verify" };
        if (unit.Calls.Count > 0) duties.Add("call-graph-review");
        if (unit.InterfaceSections.Count > 4) duties.Add("interface-freeze");
        if (unit.Language == "SCL") duties.Add("scl-lint");
        return new Tariff
        {
            Block = unit.Name,
            TariffCode = code,
            Language = unit.Language ?? "",
            InterfaceSections = unit.InterfaceSections.Count,
            Calls = unit.Calls.Count,
            Duties = duties
        };
    }
}

public sealed class ChangeItem
{
    public string Id { get; set; } = "";
    public string Artifact { get; set; } = "";
    public string Target { get; set; } = "";
    public string Plant { get; set; } = "";
    public string Window { get; set; } = "";
    public int Priority { get; set; }
    public int Size { get; set; }
}

public sealed class ConsolidationBatch
{
    public string Plant { get; set; } = "";
    public string Window { get; set; } = "";
    public int TotalSize { get; set; }
    public List<ChangeItem> Items { get; set; } = [];
}

public sealed class ConsolidationResult
{
    public int Changes { get; set; }
    public int Batches { get; set; }
    public List<ConsolidationBatch> Items { get; set; } = [];
}

public static class LtlConsolidator
{
    public static ConsolidationResult Consolidate(IEnumerable<ChangeItem> changes)
    {
        var list = changes.ToList();
        var batches = list.GroupBy(c => new { c.Plant, c.Window })
            .Select(g => new ConsolidationBatch
            {
                Plant = g.Key.Plant,
                Window = g.Key.Window,
                Items = g.OrderByDescending(c => c.Priority).ThenBy(c => c.Size).ToList()
            }).ToList();
        foreach (var b in batches) b.TotalSize = b.Items.Sum(i => i.Size);
        return new ConsolidationResult { Changes = list.Count, Batches = batches.Count, Items = batches };
    }
}

public sealed class QueueItem
{
    public string Id { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Entered { get; set; } = "";
    public string? Released { get; set; }
}

public sealed class QueueLine
{
    public string Id { get; set; } = "";
    public string Owner { get; set; } = "";
    public double Hours { get; set; }
    public double Cost { get; set; }
}

public sealed class QueueInvoice
{
    public double Rate { get; set; }
    public int Items { get; set; }
    public double TotalHours { get; set; }
    public double TotalCost { get; set; }
    public List<QueueLine> Lines { get; set; } = [];
}

public static class QueueMeter
{
    public static QueueInvoice Age(IEnumerable<QueueItem> items, double rate)
    {
        var now = DateTime.UtcNow;
        var lines = new List<QueueLine>();
        foreach (var item in items)
        {
            if (!DateTime.TryParse(item.Entered, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var entered)) continue;
            var end = item.Released is not null && DateTime.TryParse(item.Released, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var rel) ? rel : now;
            var hours = Math.Max(0, (end - entered).TotalHours);
            lines.Add(new QueueLine { Id = item.Id, Owner = item.Owner, Hours = hours, Cost = hours * rate });
        }
        return new QueueInvoice
        {
            Rate = rate,
            Items = lines.Count,
            TotalHours = lines.Sum(l => l.Hours),
            TotalCost = lines.Sum(l => l.Cost),
            Lines = lines
        };
    }
}
