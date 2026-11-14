using System.Text.Json;

namespace TiaProject;

public sealed class PlatformRealPairResult
{
    public string Project { get; set; } = "";
    public string ReleaseProject { get; set; } = "";
    public string Export { get; set; } = "";
    public string ReleaseExport { get; set; } = "";
    public string OutPath { get; set; } = "";
    public string Status { get; set; } = "pass";
    public List<PlatformStep> Steps { get; set; } = [];
}

public static class PlatformRealPair
{
    public static PlatformRealPairResult Run(ProjectPaths paths, string exportPath,
        ProjectPaths releasePaths, string releaseExportPath, string outDir, string? tiaBin = null,
        string? fleetSpecPath = null, string? compilerExportPath = null)
    {
        Directory.CreateDirectory(outDir);
        var result = new PlatformRealPairResult
        {
            Project = paths.Root,
            ReleaseProject = releasePaths.Root,
            Export = Path.GetFullPath(exportPath),
            ReleaseExport = Path.GetFullPath(releaseExportPath),
            OutPath = Path.GetFullPath(outDir)
        };

        void Step(string name, Action action, string artifact)
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

        Step("drift", () =>
        {
            var a = FormatDrift.Snapshot(paths, exportPath);
            var b = FormatDrift.Snapshot(releasePaths, releaseExportPath);
            JsonUtil.Write(a, Path.Combine(outDir, "drift", "a.json"));
            JsonUtil.Write(b, Path.Combine(outDir, "drift", "b.json"));
            var report = FormatDrift.Compare(a, b);
            JsonUtil.Write(report, Path.Combine(outDir, "drift", "compare.json"));
            if (report.Breaking.Count == 0 && report.Additive.Count == 0)
                throw new CliException("real pair produced no drift");
        }, "drift/compare.json");

        Step("corpus", () =>
        {
            var spec = new CorpusSpec
            {
                Projects =
                [
                    new() { Name = "primary", Project = paths.Root, Export = exportPath },
                    new() { Name = "release", Project = releasePaths.Root, Export = releaseExportPath }
                ]
            };
            var index = GoldenCorpusIndex.Build(spec, Path.Combine(outDir, "corpus"));
            if (!index.Pass) throw new CliException("real-pair corpus verification failed");
        }, "corpus/index.json");

        Step("fleet", () =>
        {
            var fleetDir = Path.Combine(outDir, "fleet");
            JsonUtil.Write(FleetCorpus.Record(paths, exportPath), Path.Combine(fleetDir, "primary.fleet.json"));
            JsonUtil.Write(FleetCorpus.Record(releasePaths, releaseExportPath), Path.Combine(fleetDir, "release.fleet.json"));
            var report = FleetCorpus.Report(fleetDir);
            JsonUtil.Write(report, Path.Combine(fleetDir, "report.json"));
            if (report.Records < 2) throw new CliException("fleet did not aggregate both projects");
        }, "fleet/report.json");

        if (!string.IsNullOrWhiteSpace(fleetSpecPath))
        {
            Step("fleet-crossplant", () =>
            {
                var spec = JsonSerializer.Deserialize<FleetSpec>(File.ReadAllText(fleetSpecPath), JsonUtil.Options)
                           ?? throw new CliException("invalid fleet spec");
                var report = FleetAggregate.Run(spec, Path.Combine(outDir, "fleet", "crossplant"));
                if (report.Records < 4) throw new CliException("cross-plant fleet needs at least four projects");
                if (report.CrossPlantPatterns < 2) throw new CliException("cross-plant fleet has fewer than two shared patterns");
            }, "fleet/crossplant/report.json");
        }

        Step("compiler", () =>
        {
            var compilerPath = !string.IsNullOrWhiteSpace(compilerExportPath)
                ? compilerExportPath
                : releaseExportPath;
            var test = CompilerSelfTest.Run(ObjectExport.Read(compilerPath), tiaBin, Path.Combine(outDir, "compiler"));
            if (!test.Pass) throw new CliException("compiler recovery failed on the release project");
        }, "compiler/compiler-test.json");

        Step("recorded-evolution", () =>
        {
            var trace = RecordedEvolution.CreateRealLogicTrace("plantb-recorded",
                ["Start_B", "Stop_B", "Motor_B", "Fault_B"],
                ["Start_B", "Stop_B"], ["Motor_B", "Fault_B"], 8,
                new DateTimeOffset(2026, 11, 13, 14, 46, 0, TimeSpan.Zero));
            JsonUtil.Write(trace, Path.Combine(outDir, "evolve", "recorded-trace.json"));
            var rt = RecordedEvolution.RunReal(ObjectExport.Read(releaseExportPath), trace, "FC_LogicB",
                ["Start_B=Start", "Stop_B=Stop"], ["Motor_B=Motor", "Fault_B=Fault"],
                Path.Combine(outDir, "evolve", "recorded"));
            if (!rt.Pass) throw new CliException("recorded-I/O evolution failed on the release project");
        }, "evolve/recorded/recorded-evolve.json");

        Step("ir-roundtrip", () =>
        {
            var schemas = FileUtil.FindOpennessSchemas(tiaBin)
                ?? throw new CliException("Openness schema directory not found");
            var export = ObjectExport.Read(exportPath);
            var rt = IrRoundTrip.Run(export, Path.Combine(outDir, "ir", "primary"), schemas,
                Mc7Analyzer.Analyze(export, tiaBin));
            if (!rt.Pass) throw new CliException("IR round-trip failed on the primary project");
        }, "ir/primary/roundtrip.json");

        Step("ir-roundtrip-release", () =>
        {
            var schemas = FileUtil.FindOpennessSchemas(tiaBin)
                ?? throw new CliException("Openness schema directory not found");
            var export = ObjectExport.Read(releaseExportPath);
            var rt = IrRoundTrip.Run(export, Path.Combine(outDir, "ir", "release"), schemas,
                Mc7Analyzer.Analyze(export, tiaBin));
            if (!rt.Pass) throw new CliException("IR round-trip failed on the release project");
        }, "ir/release/roundtrip.json");

        result.Status = result.Steps.Any(s => s.Status == "fail") ? "fail" : "pass";
        JsonUtil.Write(result, Path.Combine(outDir, "realpair-summary.json"));
        return result;
    }
}
