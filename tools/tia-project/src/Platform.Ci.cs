using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TiaProject;

public sealed class CiCheck
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "pass";
    public string Detail { get; set; } = "";
}

public sealed class CiReport
{
    public string Project { get; set; } = "";
    public string Export { get; set; } = "";
    public string Status { get; set; } = "pass";
    public List<CiCheck> Checks { get; set; } = [];
    public string? MarkdownFile { get; set; }
}

public static class CiRunner
{
    public static CiReport Run(ProjectPaths paths, string exportPath, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var report = new CiReport { Project = paths.Root, Export = Path.GetFullPath(exportPath) };
        var export = ObjectExport.Read(exportPath);

        try
        {
            var plf = PlfReader.Read(paths.Plf, verifyHashes: true);
            PlfReader.AttachIndex(plf, PlfReader.ReadIndex(paths.Idx));
            report.Checks.Add(new CiCheck
            {
                Name = "plf",
                Status = plf.HashFailures == 0 ? "pass" : "fail",
                Detail = $"{plf.Objects.Count} records, {plf.LiveObjects.Count} live, {plf.HashFailures} hash failures"
            });
        }
        catch (Exception ex) { report.Checks.Add(new CiCheck { Name = "plf", Status = "fail", Detail = ex.Message }); }

        try
        {
            var xref = XrefReader.Read(paths.XrefDb);
            report.Checks.Add(new CiCheck { Name = "xref", Status = "pass", Detail = $"{xref.Summary.PartCount} parts, {xref.Summary.RelationCount} relations" });
        }
        catch (Exception ex) { report.Checks.Add(new CiCheck { Name = "xref", Status = "fail", Detail = ex.Message }); }

        try
        {
            var vci = VciVerifier.Verify(paths.Root, paths.VciDb);
            report.Checks.Add(new CiCheck
            {
                Name = "vci",
                Status = vci.Mismatch == 0 && vci.Missing == 0 && vci.Errors == 0 ? "pass" : "fail",
                Detail = $"total={vci.Total} ok={vci.Ok} mismatch={vci.Mismatch} missing={vci.Missing}"
            });
        }
        catch (Exception ex) { report.Checks.Add(new CiCheck { Name = "vci", Status = "fail", Detail = ex.Message }); }

        try
        {
            var gate = GateEngine.Compare(export, export);
            report.Checks.Add(new CiCheck { Name = "semantic-gate", Status = gate.Verdict == "allow" ? "pass" : "review", Detail = $"verdict={gate.Verdict}" });
        }
        catch (Exception ex) { report.Checks.Add(new CiCheck { Name = "semantic-gate", Status = "fail", Detail = ex.Message }); }

        try
        {
            var db = Path.Combine(outDir, "ontology.db");
            var ont = OntologyBuilder.Build(paths, exportPath, db);
            report.Checks.Add(new CiCheck { Name = "ontology", Status = "pass", Detail = $"{ont.Nodes} nodes, {ont.Edges} edges" });
        }
        catch (Exception ex) { report.Checks.Add(new CiCheck { Name = "ontology", Status = "fail", Detail = ex.Message }); }

        try
        {
            var ir = IrExporter.Build(export);
            var verify = IrVerifier.Verify(ir);
            report.Checks.Add(new CiCheck
            {
                Name = "control-ir",
                Status = verify.Failed == 0 ? "pass" : "fail",
                Detail = $"{verify.Units} units, {verify.Bodies} bodies, {verify.Verified} verified, {verify.Failed} failed"
            });
        }
        catch (Exception ex) { report.Checks.Add(new CiCheck { Name = "control-ir", Status = "fail", Detail = ex.Message }); }

        report.Status = report.Checks.Any(c => c.Status == "fail") ? "fail" : report.Checks.Any(c => c.Status == "review") ? "review" : "pass";
        report.MarkdownFile = Path.Combine(outDir, "ci-report.md");
        var md = new StringBuilder();
        md.AppendLine("# tia-project CI");
        md.AppendLine();
        md.AppendLine($"Status: **{report.Status}**");
        md.AppendLine();
        md.AppendLine("| Check | Status | Detail |");
        md.AppendLine("|---|---|---|");
        foreach (var c in report.Checks) md.AppendLine($"| {c.Name} | {c.Status} | {c.Detail} |");
        File.WriteAllText(report.MarkdownFile, md.ToString(), new UTF8Encoding(false));
        return report;
    }
}

public static class CiInit
{
    public static List<string> Write(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var files = new List<string>();
        void Write(string name, string content)
        {
            var p = Path.Combine(outDir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, content.Replace("\r\n", "\n"), new UTF8Encoding(false));
            files.Add(p);
        }

        Write("Dockerfile", """
            FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
            WORKDIR /src
            COPY . .
            RUN dotnet publish TiaProject.csproj -c Release -o /out

            FROM mcr.microsoft.com/dotnet/runtime:10.0
            WORKDIR /app
            COPY --from=build /out .
            ENTRYPOINT ["dotnet", "tia-project.dll"]
            """);

        Write(".github/workflows/tia-project.yml", """
            name: tia-project-ci
            on: [push, pull_request]
            jobs:
              verify:
                runs-on: ubuntu-latest
                steps:
                  - uses: actions/checkout@v4
                  - uses: actions/setup-dotnet@v4
                    with:
                      dotnet-version: '10.0.x'
                  - run: dotnet build TiaProject.csproj -c Release
                  - run: dotnet run --project TiaProject.csproj -c Release -- ci run --project "${{ github.workspace }}/project" --fs2xml "${{ github.workspace }}/out/project.objectframe.xml" --out "${{ github.workspace }}/out/ci"
            """);

        Write("azure-pipelines.yml", """
            trigger: [main]
            pool:
              vmImage: ubuntu-latest
            steps:
              - task: UseDotNet@2
                inputs:
                  version: '10.0.x'
              - script: dotnet build TiaProject.csproj -c Release
              - script: dotnet run --project TiaProject.csproj -c Release -- ci run --project "$(Build.SourcesDirectory)/project" --fs2xml "$(Build.SourcesDirectory)/out/project.objectframe.xml" --out "$(Build.SourcesDirectory)/out/ci"
            """);

        Write("tia-project.ci.json", """
            {
              "version": 1,
              "checks": ["plf", "xref", "vci", "semantic-gate", "ontology", "control-ir"],
              "artifacts": ["ci-report.json", "ci-report.md", "ontology.db"],
              "notes": "HMI SQL export requires a Windows runner with MSSQL$WINCC; all other checks are file-level."
            }
            """);

        Write("README-ci.md", """
            # Headless CI

            This folder contains a GUI-free CI scaffold for `tia-project`.

            - `Dockerfile` builds the file-level toolkit; HMI SQL export is excluded because it needs Windows + MSSQL$WINCC.
            - `.github/workflows/tia-project.yml` and `azure-pipelines.yml` run `ci run`.
            - `tia-project.ci.json` lists the checks and artifacts.

            The CI runner parses PLF/IDX/XRef/VCI, builds the ontology, verifies the control IR, and runs the semantic gate.
            """);
        return files;
    }
}

public sealed class CiLinuxResult
{
    public bool Pass { get; set; }
    public bool Skipped { get; set; }
    public string? Reason { get; set; }
    public string PublishDir { get; set; } = "";
    public string Command { get; set; } = "";
    public string StdOut { get; set; } = "";
    public string StdErr { get; set; } = "";
    public CiReport? Report { get; set; }
}

public static class CiLinux
{
    public static CiLinuxResult Run(ProjectPaths paths, string exportPath, string outDir, string? projectFile = null)
    {
        Directory.CreateDirectory(outDir);
        var result = new CiLinuxResult();
        try
        {
            projectFile ??= FindProjectFile();
            var publishDir = Path.Combine(outDir, "publish");
            Directory.CreateDirectory(publishDir);
            var publish = RunProcess("dotnet",
                ["publish", projectFile, "-c", "Release", "-r", "linux-x64", "--self-contained", "true", "-o", publishDir]);
            result.StdOut = publish.StdOut;
            result.StdErr = publish.StdErr;
            if (publish.ExitCode != 0)
            {
                result.Reason = "dotnet publish failed: " + publish.StdErr;
                return result;
            }

            var binary = Path.Combine(publishDir, "tia-project");
            if (!File.Exists(binary))
            {
                result.Reason = "published Linux binary not found: " + binary;
                return result;
            }
            result.PublishDir = publishDir;

            var runDir = Path.Combine(outDir, "run");
            Directory.CreateDirectory(runDir);
            var command = $"chmod +x '{WslPath(binary)}' && '{WslPath(binary)}' ci run --project '{WslPath(paths.Root)}' --fs2xml '{WslPath(exportPath)}' --out '{WslPath(runDir)}'";
            result.Command = command;
            var run = RunProcess("wsl", ["-e", "bash", "-lc", command]);
            result.StdOut = run.StdOut;
            result.StdErr = run.StdErr;
            if (run.ExitCode != 0)
            {
                result.Reason = "Linux ci run failed: " + run.StdErr;
                return result;
            }

            var reportPath = Path.Combine(runDir, "ci-report.json");
            if (!File.Exists(reportPath))
            {
                result.Reason = "Linux ci report not found";
                return result;
            }
            result.Report = JsonSerializer.Deserialize<CiReport>(File.ReadAllText(reportPath), JsonUtil.Options);
            result.Pass = result.Report?.Status == "pass";
            File.Copy(reportPath, Path.Combine(outDir, "ci-linux-report.json"), overwrite: true);
            if (!result.Pass) result.Reason = "Linux ci status=" + result.Report?.Status;
            return result;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            result.Skipped = true;
            result.Reason = "WSL not available: " + ex.Message;
            return result;
        }
        catch (Exception ex)
        {
            result.Reason = ex.Message;
            return result;
        }
        finally
        {
            JsonUtil.Write(result, Path.Combine(outDir, "ci-linux-report.json"));
        }
    }

    public static string FindProjectFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "TiaProject.csproj");
            if (File.Exists(candidate)) return candidate;
        }
        throw new CliException("TiaProject.csproj not found; pass --project-file");
    }

    private static string WslPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length >= 2 && full[1] == ':')
            return "/mnt/" + char.ToLowerInvariant(full[0]) + full[2..].Replace('\\', '/');
        return full.Replace('\\', '/');
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string file, string[] arguments)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
