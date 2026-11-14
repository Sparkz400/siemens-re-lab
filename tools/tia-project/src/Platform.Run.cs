using System.Text;
using System.Text.Json;

namespace TiaProject;

public sealed class PlatformStep
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "pass";
    public string Detail { get; set; } = "";
    public string? Artifact { get; set; }
}

public sealed class PlatformRunResult
{
    public string Project { get; set; } = "";
    public string Export { get; set; } = "";
    public string OutPath { get; set; } = "";
    public string Status { get; set; } = "pass";
    public List<PlatformStep> Steps { get; set; } = [];
}

public static class PlatformRun
{
    public static PlatformRunResult Run(ProjectPaths paths, string exportPath, string outDir, string? keyBase = null, string? tiaBin = null,
        string? releaseProjectRoot = null, string? releaseExportPath = null, string? compilerExportPath = null)
    {
        Directory.CreateDirectory(outDir);
        var result = new PlatformRunResult { Project = paths.Root, Export = Path.GetFullPath(exportPath), OutPath = Path.GetFullPath(outDir) };
        var export = ObjectExport.Read(exportPath);
        var history = HistoryBuilder.Build(paths.Root, paths.Plf, paths.Idx, Path.Combine(outDir, "history"), export, false);

        void Step(string name, Action action, string? artifact = null)
        {
            try
            {
                action();
                result.Steps.Add(new PlatformStep { Name = name, Status = "pass", Detail = "ok", Artifact = artifact });
            }
            catch (Exception ex)
            {
                result.Steps.Add(new PlatformStep { Name = name, Status = "fail", Detail = ex.Message, Artifact = artifact });
            }
        }

        var key = keyBase ?? Path.Combine(outDir, "registry", "project");
        if (!File.Exists(key + ".private.pem")) LogicRegistry.InitKey(key);

        ProjectPaths fixturePaths;
        string fixtureExport;
        if (!string.IsNullOrWhiteSpace(releaseProjectRoot) && !string.IsNullOrWhiteSpace(releaseExportPath))
        {
            fixturePaths = ProjectPaths.Resolve(releaseProjectRoot);
            fixtureExport = Path.GetFullPath(releaseExportPath);
        }
        else
        {
            var fixtureName = new DirectoryInfo(paths.Root).Name + "-R2";
            var fixture = ReleaseFixture.Create(paths.Root, exportPath,
                Path.Combine(outDir, "fixtures", fixtureName), fixtureName, "0.1400.2.0", true);
            JsonUtil.Write(fixture, Path.Combine(outDir, "fixtures", fixtureName, "fixture.json"));
            fixturePaths = ProjectPaths.Resolve(fixture.ProjectRoot);
            fixtureExport = fixture.ExportPath;
        }

        Step("semantic-gate", () =>
        {
            var gate = GateEngine.Compare(export, export);
            JsonUtil.Write(gate, Path.Combine(outDir, "gate", "gate.json"));
        }, "gate/gate.json");

        Step("behavioral-bisect", () =>
        {
            var b = GateBisect.Bisect(export, history, "Main");
            JsonUtil.Write(b, Path.Combine(outDir, "gate", "bisect.json"));
        }, "gate/bisect.json");

        Step("semantic-merge", () =>
        {
            var merged = SemanticMerge.Merge(exportPath, exportPath, exportPath, Path.Combine(outDir, "merge", "merged.xml"));
            JsonUtil.Write(merged, Path.Combine(outDir, "merge", "merge.json"));
        }, "merge/merged.xml");

        Step("forensics", () =>
        {
            var f = Forensics.Build(paths.Root, exportPath, history, Path.Combine(outDir, "forensics"));
            JsonUtil.Write(f, Path.Combine(outDir, "forensics", "forensics.json"));
        }, "forensics/forensics.json");

        Step("flight-recorder", () =>
        {
            var test = RecordedTraceSelfTest.Run(history, export, Path.Combine(outDir, "flightrec"));
            if (!test.Pass) throw new CliException("recorded flight-recorder replay test failed");
        }, "flightrec/recorded-test.json");

        Step("programmed-forgetting", () =>
        {
            var pruned = Retention.Prune(history, 2, []);
            JsonUtil.Write(pruned, Path.Combine(outDir, "retention", "retention.json"));
            var forgotten = Forgetting.Execute(paths, exportPath, Path.Combine(outDir, "forgetting", "project"), 2, [], false);
            JsonUtil.Write(forgotten, Path.Combine(outDir, "forgetting", "result.json"));
            if (!forgotten.Pass) throw new CliException("programmed forgetting verification failed");
        }, "forgetting/result.json");

        Step("ontology", () =>
        {
            var db = Path.Combine(outDir, "ontology", "ontology.db");
            var ont = OntologyBuilder.Build(paths, exportPath, db);
            JsonUtil.Write(ont, Path.Combine(outDir, "ontology", "build.json"));
            var bench = ApiBenchmark.Run(db);
            JsonUtil.Write(bench, Path.Combine(outDir, "ontology", "benchmark.json"));
        }, "ontology/ontology.db");

        Step("control-ir", () =>
        {
            var ir = IrExporter.Build(export);
            JsonUtil.Write(ir, Path.Combine(outDir, "ir", "project.ir.json"));
            var verify = IrVerifier.Verify(ir);
            JsonUtil.Write(verify, Path.Combine(outDir, "ir", "verify.json"));
            File.WriteAllText(Path.Combine(outDir, "ir", "project-ir.dot"), IrToDot(ir), new UTF8Encoding(false));
        }, "ir/project.ir.json");

        Step("ir-roundtrip", () =>
        {
            var schemas = FileUtil.FindOpennessSchemas(tiaBin)
                ?? throw new CliException("Openness schema directory not found");
            var rt = IrRoundTrip.Run(export, Path.Combine(outDir, "ir", "roundtrip"), schemas,
                Mc7Analyzer.Analyze(export, tiaBin));
            if (!rt.Pass) throw new CliException("IR round-trip failed");
        }, "ir/roundtrip/roundtrip.json");

        Step("headless-ci", () =>
        {
            var ci = CiRunner.Run(paths, exportPath, Path.Combine(outDir, "ci"));
            JsonUtil.Write(ci, Path.Combine(outDir, "ci", "ci-report.json"));
        }, "ci/ci-report.json");

        Step("headless-linux", () =>
        {
            var linuxPaths = !string.IsNullOrWhiteSpace(releaseProjectRoot)
                ? ProjectPaths.Resolve(releaseProjectRoot)
                : paths;
            var linuxExport = !string.IsNullOrWhiteSpace(releaseExportPath) ? releaseExportPath : exportPath;
            var result = CiLinux.Run(linuxPaths, linuxExport, Path.Combine(outDir, "ci", "linux"));
            if (!result.Pass) throw new CliException("headless Linux CI failed: " + result.Reason);
        }, "ci/linux/ci-linux-report.json");

        Step("equivalence", () =>
        {
            var analysis = Mc7Analyzer.Analyze(export, tiaBin);
            var main = analysis.Blocks.First(b => b.Name == "Main");
            var block1 = analysis.Blocks.First(b => b.Name == "Block_1");
            var test = Mc7SelfTest.Run(analysis);
            JsonUtil.Write(test, Path.Combine(outDir, "proof", "mc7-test.json"));
            JsonUtil.Write(Mc7Equivalence.Compare(main, main), Path.Combine(outDir, "proof", "mc7-main-main.json"));
            JsonUtil.Write(Mc7Equivalence.Compare(main, block1), Path.Combine(outDir, "proof", "mc7-main-block1.json"));
            var symbolic = Mc7SymbolicSelfTest.Run(analysis);
            JsonUtil.Write(symbolic, Path.Combine(outDir, "proof", "mc7-symbolic-test.json"));
            JsonUtil.Write(symbolic.MainMain, Path.Combine(outDir, "proof", "mc7-symbolic-main-main.json"));
            JsonUtil.Write(symbolic.MainBlock1, Path.Combine(outDir, "proof", "mc7-symbolic-main-block1.json"));
            var semantic = Mc7SemanticSelfTest.Run(analysis);
            JsonUtil.Write(semantic, Path.Combine(outDir, "proof", "mc7-semantic-test.json"));
            JsonUtil.Write(semantic.MainMain, Path.Combine(outDir, "proof", "mc7-semantic-main-main.json"));
            JsonUtil.Write(semantic.MainBlock1, Path.Combine(outDir, "proof", "mc7-semantic-main-block1.json"));
            var cycle = Mc7CycleSelfTest.Run(analysis);
            JsonUtil.Write(cycle, Path.Combine(outDir, "proof", "mc7-cycle-test.json"));
            if (cycle.MainMain is not null)
                JsonUtil.Write(cycle.MainMain, Path.Combine(outDir, "proof", "mc7-cycle-main-main.json"));
            if (cycle.MainBlock1 is not null)
                JsonUtil.Write(cycle.MainBlock1, Path.Combine(outDir, "proof", "mc7-cycle-main-block1.json"));
            if (!test.Pass || !symbolic.Pass || !semantic.Pass || !cycle.Pass)
                throw new CliException("MC7 equivalence negative test failed");
        }, "proof/mc7-cycle-test.json");

        Step("signed-registry", () =>
        {
            var manifest = new RegistryManifest { Project = exportPath, Created = DateTime.UtcNow.ToString("O") };
            foreach (var o in export.Objects.Where(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)
                                                       || o.ClassName.EndsWith("EAMTZTagData", StringComparison.Ordinal)
                                                       || o.ClassName.EndsWith("HmiScreenData", StringComparison.Ordinal)))
                manifest.Entries.Add(new ManifestEntry { Kind = o.ClassName.Split('.').Last(), Name = o.Name ?? o.InstanceId.ToString(), Hash = PlatformIo.ObjectFingerprint(o) });
            var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonUtil.Options);
            File.WriteAllBytes(Path.Combine(outDir, "registry", "manifest.json"), bytes);
            JsonUtil.Write(LogicRegistry.SignBytes(key + ".private.pem", "manifest.json", bytes), Path.Combine(outDir, "registry", "manifest.sig.json"));
        }, "registry/manifest.sig.json");

        Step("verified-market", () =>
        {
            var market = MarketSelfTest.Run(paths, exportPath, key, Path.Combine(outDir, "market"));
            if (!market.Pass) throw new CliException("marketplace contract/provenance test failed");
        }, "market/market-test.json");

        Step("format-drift", () =>
        {
            var a = FormatDrift.Snapshot(paths, exportPath);
            var b = FormatDrift.Snapshot(fixturePaths, fixtureExport);
            JsonUtil.Write(a, Path.Combine(outDir, "drift", "snapshot-scratch.json"));
            JsonUtil.Write(b, Path.Combine(outDir, "drift", "snapshot-r2.json"));
            var report = FormatDrift.Compare(a, b);
            JsonUtil.Write(report, Path.Combine(outDir, "drift", "compare.json"));
            if (report.Severity != "breaking" || report.Breaking.Count == 0)
                throw new CliException("release-to-release drift did not produce a breaking report");
        }, "drift/compare.json");

        Step("golden-corpus", () =>
        {
            var spec = new CorpusSpec
            {
                Projects =
                [
                    new() { Name = "scratch", Project = paths.Root, Export = exportPath },
                    new() { Name = "release", Project = fixturePaths.Root, Export = fixtureExport }
                ]
            };
            var index = GoldenCorpusIndex.Build(spec, Path.Combine(outDir, "corpus"));
            if (!index.Pass) throw new CliException("multi-project corpus verification failed");
            GoldenCorpus.Mutate(exportPath, Path.Combine(outDir, "corpus", "mutants"), 3);
        }, "corpus/index.json");

        Step("fleet-corpus", () =>
        {
            var fleetDir = Path.Combine(outDir, "fleet");
            JsonUtil.Write(FleetCorpus.Record(paths, exportPath), Path.Combine(fleetDir, "scratch.fleet.json"));
            JsonUtil.Write(FleetCorpus.Record(fixturePaths, fixtureExport), Path.Combine(fleetDir, "scratch-r2.fleet.json"));
            var report = FleetCorpus.Report(fleetDir);
            JsonUtil.Write(report, Path.Combine(fleetDir, "report.json"));
            if (report.Records < 2) throw new CliException("fleet report did not aggregate both projects");
        }, "fleet/report.json");

        Step("compiler-recovery", () =>
        {
            var compilerExport = !string.IsNullOrWhiteSpace(compilerExportPath)
                ? ObjectExport.Read(compilerExportPath)
                : !string.IsNullOrWhiteSpace(releaseExportPath)
                    ? ObjectExport.Read(releaseExportPath)
                    : export;
            var test = CompilerSelfTest.Run(compilerExport, tiaBin, Path.Combine(outDir, "compiler"));
            if (!test.Pass) throw new CliException("compiler recovery probe/model test failed");
        }, "compiler/compiler-test.json");

        Step("affinity-maturation", () =>
        {
            var trace = EvolveEngine.GenerateTrace(export, "Block_1", 8);
            JsonUtil.Write(trace, Path.Combine(outDir, "evolve", "trace.json"));
            var result = EvolveEngine.Run(export, "Block_1", trace, 8);
            JsonUtil.Write(result, Path.Combine(outDir, "evolve", "evolve.json"));
            var test = EvolveSelfTest.Run(Path.Combine(outDir, "evolve"));
            if (!test.Pass) throw new CliException("trace-fitness evolution self-test failed");
            if (result.BestFitness + 1e-9 < result.BaselineFitness)
                throw new CliException("evolution regressed below baseline");

            var recordedPath = Path.Combine(outDir, "flightrec", "recorded-trace.json");
            var recorded = JsonSerializer.Deserialize<FlightTrace>(File.ReadAllText(recordedPath), JsonUtil.Options)
                           ?? throw new CliException("recorded flight trace missing");
            var recordedResult = RecordedEvolution.RunSynthetic(recorded, recorded.Tags[0], recorded.Tags[1],
                Path.Combine(outDir, "evolve", "recorded"));
            if (!recordedResult.Pass) throw new CliException("recorded-I/O evolution test failed");
        }, "evolve/recorded-evolve.json");
        Step("thymic-selection", () =>
        {
            var trained = Thymus.Train(export, 8);
            JsonUtil.Write(trained, Path.Combine(outDir, "thymus", "repertoire.json"));
            JsonUtil.Write(Thymus.Scan(export, trained), Path.Combine(outDir, "thymus", "scan.json"));
        }, "thymus/repertoire.json");

        Step("hippocampal-replay", () =>
        {
            JsonUtil.Write(ReplayConsolidator.Consolidate(history), Path.Combine(outDir, "replay", "consolidated.json"));
        }, "replay/consolidated.json");

        Step("quorum-contracts", () =>
        {
            JsonUtil.Write(ContractDiscovery.Discover(export, 1), Path.Combine(outDir, "contracts", "contracts.json"));
        }, "contracts/contracts.json");

        Step("ant-colony-midden", () =>
        {
            JsonUtil.Write(Midden.Analyze(export), Path.Combine(outDir, "midden", "midden.json"));
        }, "midden/midden.json");

        Step("returns-desk", () =>
        {
            JsonUtil.Write(RollbackDesk.Create(paths, exportPath, Path.Combine(outDir, "rollback"), key + ".private.pem"), Path.Combine(outDir, "rollback", "rollback-manifest.json"));
        }, "rollback/rollback-manifest.json");

        Step("proof-of-delivery", () =>
        {
            var rec = ProofOfDelivery.Create(exportPath, "CPU-S7-1500-001", "rack0/slot1", key + ".private.pem");
            JsonUtil.Write(rec, Path.Combine(outDir, "pod", "pod.json"));
            if (!ProofOfDelivery.Verify(rec, key + ".public.pem")) throw new CliException("POD signature verification failed");
        }, "pod/pod.json");

        Step("bonded-warehouse", () =>
        {
            var test = WarehouseSelfTest.Run(exportPath, Path.Combine(outDir, "warehouse"), key);
            if (!test.Pass) throw new CliException("warehouse UMAC review test failed");
            JsonUtil.Write(BondedWarehouse.List(Path.Combine(outDir, "warehouse")), Path.Combine(outDir, "warehouse", "list.json"));
        }, "warehouse/warehouse-test.json");

        Step("customs-brokerage", () =>
        {
            JsonUtil.Write(CustomsBroker.Classify(export, "Main"), Path.Combine(outDir, "customs", "tariff.json"));
        }, "customs/tariff.json");

        Step("ltl-consolidation", () =>
        {
            var changes = new List<ChangeItem>
            {
                new() { Id = "c1", Artifact = "a", Target = "PLC_1", Plant = "PlantA", Window = "2026-11-14T02:00", Priority = 2, Size = 10 },
                new() { Id = "c2", Artifact = "b", Target = "PLC_1", Plant = "PlantA", Window = "2026-11-14T02:00", Priority = 1, Size = 5 },
                new() { Id = "c3", Artifact = "c", Target = "HMI_1", Plant = "PlantB", Window = "2026-11-15T02:00", Priority = 3, Size = 8 }
            };
            JsonUtil.Write(LtlConsolidator.Consolidate(changes), Path.Combine(outDir, "deploy", "consolidated.json"));
        }, "deploy/consolidated.json");

        Step("queue-aging", () =>
        {
            var now = DateTime.UtcNow;
            var items = new List<QueueItem>
            {
                new() { Id = "q1", Owner = "alice", Entered = now.AddHours(-8).ToString("O") },
                new() { Id = "q2", Owner = "bob", Entered = now.AddHours(-24).ToString("O"), Released = now.AddHours(-8).ToString("O") }
            };
            JsonUtil.Write(QueueMeter.Age(items, 50), Path.Combine(outDir, "queue", "invoice.json"));
        }, "queue/invoice.json");

        result.Status = result.Steps.Any(s => s.Status == "fail") ? "fail" : "pass";
        JsonUtil.Write(result, Path.Combine(outDir, "platform-summary.json"));
        var md = new StringBuilder();
        md.AppendLine("# Platform run");
        md.AppendLine();
        md.AppendLine($"Status: **{result.Status}**");
        md.AppendLine();
        md.AppendLine("| Step | Status | Detail | Artifact |");
        md.AppendLine("|---|---|---|---|");
        foreach (var s in result.Steps) md.AppendLine($"| {s.Name} | {s.Status} | {s.Detail.Replace("|", "\\|")} | {s.Artifact} |");
        File.WriteAllText(Path.Combine(outDir, "platform-summary.md"), md.ToString(), new UTF8Encoding(false));
        return result;
    }

    private static string IrToDot(IrDocument ir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("digraph IR { rankdir=LR;");
        foreach (var u in ir.Units) sb.AppendLine($"  \"{u.Id}\" [shape=box,label=\"{u.Name}\\n{u.Language}\"];");
        foreach (var e in ir.Edges) sb.AppendLine($"  \"{e.Src}\" -> \"{e.Dst}\" [label=\"{e.Kind}\"];");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
