using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TiaProject;

public sealed class Mc7Instruction
{
    public int Uid { get; set; }
    public int Offset { get; set; }
    public int Length { get; set; }
    public string Operator { get; set; } = "";
    public string Condition { get; set; } = "";
    public bool CondFlag { get; set; }
    public bool EnoFlag { get; set; }
    public bool StwFlag { get; set; }
    public bool WriteFlag { get; set; }
    public bool NoIntFlag { get; set; }
    public bool NegatedFlag { get; set; }
    public List<string> Types { get; set; } = [];
    public List<string> Operands { get; set; } = [];

    public string ToAsm()
    {
        var flags = new List<string>();
        if (CondFlag) flags.Add("cond");
        if (EnoFlag) flags.Add("eno");
        if (StwFlag) flags.Add("stw");
        if (WriteFlag) flags.Add("write");
        if (NoIntFlag) flags.Add("noint");
        if (NegatedFlag) flags.Add("neg");
        var f = flags.Count > 0 ? " [" + string.Join(",", flags) + "]" : "";
        var ops = Operands.Count > 0 ? " " + string.Join(", ", Operands) : "";
        return $"{Uid:X4}  {Operator,-24} {Condition}{f}{ops}";
    }
}

public sealed class Mc7Block
{
    public string Name { get; set; } = "";
    public long BlockInstanceId { get; set; }
    public string? Subtype { get; set; }
    public int? Number { get; set; }
    public string? Language { get; set; }
    public int CodeLength { get; set; }
    public string CodeHex { get; set; } = "";
    public string? CodeFile { get; set; }
    public string? DependentDataHex { get; set; }
    public int? DependentDataLength { get; set; }
    public int? LanguageLevel { get; set; }
    public string? CompilerVersion { get; set; }
    public string? Error { get; set; }
    public List<Mc7Instruction> Instructions { get; set; } = [];
    public string? AsmFile { get; set; }
    public string? JsonFile { get; set; }
}

public sealed class Mc7Result
{
    public string ExportPath { get; set; } = "";
    public string OutPath { get; set; } = "";
    public string? HostPath { get; set; }
    public List<Mc7Block> Blocks { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public static class Mc7Extractor
{
    public static Mc7Result Extract(ObjectExport export, string outDir, string tiaBin)
    {
        Directory.CreateDirectory(outDir);
        var result = new Mc7Result { ExportPath = export.FilePath, OutPath = Path.GetFullPath(outDir) };
        var host = Mc7HostRunner.EnsureHost(tiaBin, out var hostWarning);
        result.HostPath = host;
        if (hostWarning is not null) result.Warnings.Add(hostWarning);

        var byInst = export.Objects.GroupBy(o => o.InstanceId).ToDictionary(g => g.Key, g => g.First());
        var blocks = export.Objects.Where(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)).ToList();
        foreach (var block in blocks)
        {
            var loadableLink = block.Links.FirstOrDefault(l => l.TypeId == 2232336);
            if (loadableLink.InstanceId == 0 || !byInst.TryGetValue(loadableLink.InstanceId, out var loadable))
                continue;

            var mc7 = new Mc7Block
            {
                Name = block.Name ?? $"Block_{block.InstanceId}",
                BlockInstanceId = block.InstanceId,
                Subtype = block.Subtype,
                Number = block.Number,
                Language = LanguageName(block.ProgrammingLanguage)
            };

            if (loadable.CoreBlobs.TryGetValue("OpenImmediateData", out var openHex) && openHex.Length > 0)
            {
                mc7.CodeHex = openHex;
                mc7.CodeLength = openHex.Length / 2;
            }
            else if (loadable.CoreBlobs.TryGetValue("OwnSignatureData", out var sigHex))
            {
                mc7.CodeHex = sigHex;
                mc7.CodeLength = sigHex.Length / 2;
            }

            var dep = loadable.Links.Where(l => l.TypeId == 2232327)
                .Select(l => byInst.TryGetValue(l.InstanceId, out var d) ? d : null)
                .FirstOrDefault(d => d is not null && d.CoreBlobs.ContainsKey("MC7PlusData"));
            if (dep is not null && dep.CoreBlobs.TryGetValue("MC7PlusData", out var depHex))
            {
                mc7.DependentDataHex = depHex;
                mc7.DependentDataLength = depHex.Length / 2;
            }

            if (mc7.CodeLength == 0)
            {
                mc7.Error = "no OpenImmediateData/OwnSignatureData in LoadablePlusCodeBlockData";
                result.Blocks.Add(mc7);
                continue;
            }

            var bytes = Convert.FromHexString(mc7.CodeHex);
            var stem = SafeFileName($"{mc7.Subtype ?? "BLOCK"}_{mc7.Number?.ToString() ?? "0"}_{mc7.Name}");
            var binPath = Path.Combine(outDir, stem + ".bin");
            File.WriteAllBytes(binPath, bytes);
            mc7.CodeFile = binPath;

            if (host is not null)
            {
                var run = Mc7HostRunner.Run(host, tiaBin, binPath);
                if (run.Ok)
                {
                    mc7.LanguageLevel = run.LanguageLevel;
                    mc7.CompilerVersion = run.CompilerVersion;
                    mc7.Instructions = run.Instructions;
                    if (run.Error is not null) mc7.Error = run.Error;
                }
                else
                {
                    mc7.Error = run.Error;
                }
            }
            else
            {
                mc7.Error = hostWarning ?? "MC7 host not available";
            }

            var asm = new StringBuilder();
            asm.AppendLine($"; {mc7.Name}  {mc7.Subtype} {mc7.Number}  code={mc7.CodeLength} bytes  langLevel={mc7.LanguageLevel?.ToString() ?? "?"}");
            if (!string.IsNullOrEmpty(mc7.Error)) asm.AppendLine($"; error: {mc7.Error}");
            foreach (var ins in mc7.Instructions) asm.AppendLine(ins.ToAsm());
            var asmPath = Path.Combine(outDir, stem + ".asm.txt");
            File.WriteAllText(asmPath, asm.ToString(), new UTF8Encoding(false));
            mc7.AsmFile = asmPath;

            var jsonPath = Path.Combine(outDir, stem + ".mc7.json");
            File.WriteAllText(jsonPath, JsonUtil.Serialize(mc7), new UTF8Encoding(false));
            mc7.JsonFile = jsonPath;
            result.Blocks.Add(mc7);
        }
        return result;
    }

    private static string? LanguageName(int? language) => language switch
    {
        1 => "STL",
        2 => "LAD",
        3 => "FBD",
        4 => "SCL",
        5 => "GRAPH",
        _ => language?.ToString()
    };

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 120 ? name[..120] : name;
    }
}

public sealed class Mc7HostRun
{
    public bool Ok { get; set; }
    public int? LanguageLevel { get; set; }
    public string? CompilerVersion { get; set; }
    public List<Mc7Instruction> Instructions { get; set; } = [];
    public string? Error { get; set; }
    public string? Raw { get; set; }
}

public static class Mc7HostRunner
{
    public static string? EnsureHost(string tiaBin, out string? warning)
    {
        warning = null;
        var existing = FindUp("tools\\Mc7Host\\bin\\Mc7Host.exe");
        if (existing is not null) return existing;

        var source = FindUp("tools\\Mc7Host\\Mc7Host.cs");
        if (source is null)
        {
            warning = "Mc7Host.cs not found; MC7 disassembly disabled";
            return null;
        }
        var csc = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe";
        if (!File.Exists(csc))
        {
            warning = "csc.exe not found; MC7 disassembly disabled";
            return null;
        }
        var outDir = Path.Combine(Path.GetTempPath(), "tia-project", "mc7host");
        Directory.CreateDirectory(outDir);
        var exe = Path.Combine(outDir, "Mc7Host.exe");
        var netstandard = @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll";
        var args = new List<string>
        {
            "/nologo", "/target:exe", "/platform:x64", "/out:" + exe,
            "/r:System.dll", "/r:System.Core.dll"
        };
        if (File.Exists(netstandard)) args.Add("/r:" + netstandard);
        foreach (var dll in new[]
                 {
                     "Siemens.Simatic.Lang.MC7Codegenerator.dll",
                     "Siemens.Simatic.Lang.MC7plus.Internal.dll",
                     "Siemens.Simatic.Lang.MC7plus.External.dll"
                 })
        {
            var p = Path.Combine(tiaBin, dll);
            if (File.Exists(p)) args.Add("/r:" + p);
        }
        args.Add(source);
        var psi = new ProcessStartInfo
        {
            FileName = csc,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p2 = Process.Start(psi);
        if (p2 is null) { warning = "could not start csc.exe"; return null; }
        var stdout = p2.StandardOutput.ReadToEnd();
        var stderr = p2.StandardError.ReadToEnd();
        p2.WaitForExit(120000);
        if (p2.ExitCode != 0 || !File.Exists(exe))
        {
            warning = "MC7 host build failed: " + (stderr + stdout).Trim();
            return null;
        }
        return exe;
    }

    public static Mc7HostRun Run(string host, string tiaBin, string codePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = host,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = tiaBin
        };
        psi.ArgumentList.Add("--bin"); psi.ArgumentList.Add(tiaBin);
        psi.ArgumentList.Add("--code"); psi.ArgumentList.Add(Path.GetFullPath(codePath));
        psi.Environment["PATH"] = tiaBin + ";" + (psi.Environment.TryGetValue("PATH", out var path) ? path : "");
        using var proc = Process.Start(psi);
        if (proc is null) return new Mc7HostRun { Error = "could not start MC7 host" };
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(120000);
        var result = new Mc7HostRun { Raw = (stdout + "\n" + stderr).Trim() };
        if (proc.ExitCode != 0)
        {
            result.Error = stderr.Trim();
            if (result.Error.Length == 0) result.Error = stdout.Trim();
            return result;
        }
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            result.Ok = true;
            if (root.TryGetProperty("languageLevel", out var level)) result.LanguageLevel = level.GetInt32();
            if (root.TryGetProperty("compilerVersion", out var cv)) result.CompilerVersion = cv.GetString();
            if (root.TryGetProperty("disasmError", out var de) && de.ValueKind == JsonValueKind.String)
                result.Error = de.GetString();
            if (root.TryGetProperty("instructions", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var i in arr.EnumerateArray())
                {
                    var ins = new Mc7Instruction
                    {
                        Uid = i.TryGetProperty("uid", out var uid) ? uid.GetInt32() : 0,
                        Operator = i.TryGetProperty("operator", out var op) ? op.GetString() ?? "" : "",
                        Condition = i.TryGetProperty("condition", out var cond) ? cond.GetString() ?? "" : "",
                        CondFlag = i.TryGetProperty("condFlag", out var cf) && cf.GetBoolean(),
                        EnoFlag = i.TryGetProperty("enoFlag", out var ef) && ef.GetBoolean(),
                        StwFlag = i.TryGetProperty("stwFlag", out var sf) && sf.GetBoolean(),
                        WriteFlag = i.TryGetProperty("writeFlag", out var wf) && wf.GetBoolean(),
                        NoIntFlag = i.TryGetProperty("noIntFlag", out var nf) && nf.GetBoolean(),
                        NegatedFlag = i.TryGetProperty("negatedFlag", out var gf) && gf.GetBoolean()
                    };
                    if (i.TryGetProperty("types", out var types))
                        foreach (var t in types.EnumerateArray()) ins.Types.Add(t.GetString() ?? "");
                    if (i.TryGetProperty("operands", out var ops))
                        foreach (var o in ops.EnumerateArray()) ins.Operands.Add(o.GetString() ?? "");
                    result.Instructions.Add(ins);
                }
            }
            return result;
        }
        catch (JsonException ex)
        {
            result.Error = "could not parse MC7 host output: " + ex.Message + "\n" + stdout;
            return result;
        }
    }

    private static string? FindUp(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, relative);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}

public sealed class Mc7ClassicRun
{
    public bool Ok { get; set; }
    public string? CodeHex { get; set; }
    public int CodeLength { get; set; }
    public List<Mc7Instruction> Instructions { get; set; } = [];
    public string? Error { get; set; }
    public string? Raw { get; set; }
}

public static class Mc7ClassicHostRunner
{
    public static string? EnsureHost(string tiaBin, out string? warning)
    {
        warning = null;
        var existing = FindUp("tools\\Mc7ClassicHost\\bin\\Mc7ClassicHost.exe");
        if (existing is not null) return existing;

        var source = FindUp("tools\\Mc7ClassicHost\\Mc7ClassicHost.cs");
        if (source is null)
        {
            warning = "Mc7ClassicHost.cs not found; classic MC7 decoding disabled";
            return null;
        }
        var csc = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe";
        if (!File.Exists(csc))
        {
            warning = "csc.exe not found; classic MC7 decoding disabled";
            return null;
        }
        var outDir = Path.Combine(Path.GetTempPath(), "tia-project", "mc7classic");
        Directory.CreateDirectory(outDir);
        var exe = Path.Combine(outDir, "Mc7ClassicHost.exe");
        var netstandard = @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll";
        var args = new List<string>
        {
            "/nologo", "/target:exe", "/platform:x64", "/out:" + exe,
            "/r:System.dll", "/r:System.Core.dll"
        };
        if (File.Exists(netstandard)) args.Add("/r:" + netstandard);
        foreach (var dll in new[]
                 {
                     "Siemens.Simatic.Lang.MC7ClassicCodeGen.dll",
                     "Siemens.Simatic.Lang.PLInternal.dll",
                     "Siemens.Simatic.Lang.PLInterface.dll",
                     "Siemens.Automation.Basics.dll",
                     "Siemens.Simatic.Lang.IdentManager.Interfaces.dll",
                     "Siemens.Simatic.Lang.Types.Interfaces.dll",
                     "Siemens.Automation.ObjectFrame.dll"
                 })
        {
            var p = Path.Combine(tiaBin, dll);
            if (File.Exists(p)) args.Add("/r:" + p);
        }
        args.Add(source);
        var psi = new ProcessStartInfo
        {
            FileName = csc,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p2 = Process.Start(psi);
        if (p2 is null) { warning = "could not start csc.exe"; return null; }
        var stdout = p2.StandardOutput.ReadToEnd();
        var stderr = p2.StandardError.ReadToEnd();
        p2.WaitForExit(120000);
        if (p2.ExitCode != 0 || !File.Exists(exe))
        {
            warning = "classic MC7 host build failed: " + (stderr + stdout).Trim();
            return null;
        }
        return exe;
    }

    public static Mc7ClassicRun Run(string host, string tiaBin, string dataPath, int codeLength)
    {
        var psi = new ProcessStartInfo
        {
            FileName = host,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = tiaBin
        };
        psi.ArgumentList.Add("--tia-bin"); psi.ArgumentList.Add(tiaBin);
        psi.ArgumentList.Add("--data"); psi.ArgumentList.Add(Path.GetFullPath(dataPath));
        psi.ArgumentList.Add("--length"); psi.ArgumentList.Add(codeLength.ToString());
        psi.Environment["PATH"] = tiaBin + ";" + (psi.Environment.TryGetValue("PATH", out var path) ? path : "");
        using var proc = Process.Start(psi);
        if (proc is null) return new Mc7ClassicRun { Error = "could not start classic MC7 host" };
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(120000);
        var result = new Mc7ClassicRun { Raw = (stdout + "\n" + stderr).Trim() };
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            result.Ok = root.TryGetProperty("ok", out var ok) && ok.GetBoolean();
            if (root.TryGetProperty("codeHex", out var codeHex)) result.CodeHex = codeHex.GetString();
            if (root.TryGetProperty("codeLength", out var len)) result.CodeLength = len.GetInt32();
            if (root.TryGetProperty("error", out var err)) result.Error = err.GetString();
            if (root.TryGetProperty("statements", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in arr.EnumerateArray())
                {
                    var ins = new Mc7Instruction
                    {
                        Uid = result.Instructions.Count,
                        Offset = s.TryGetProperty("address", out var addr) ? addr.GetInt32() : 0,
                        Length = s.TryGetProperty("length", out var blen) ? blen.GetInt32() : 0,
                        Operator = s.TryGetProperty("operator", out var op) ? op.GetString() ?? "" : "",
                        Condition = s.TryGetProperty("range", out var range) ? range.GetString() ?? "" : ""
                    };
                    if (s.TryGetProperty("access", out var access)) ins.Operands.Add(access.GetString() ?? "");
                    if (s.TryGetProperty("byteNr", out var byteNr)) ins.Operands.Add("byte=" + byteNr.GetInt32());
                    if (s.TryGetProperty("bitNr", out var bitNr)) ins.Operands.Add("bit=" + bitNr.GetInt32());
                    result.Instructions.Add(ins);
                }
            }
            if (!result.Ok && result.Error is null) result.Error = "classic MC7 host returned ok=false";
            return result;
        }
        catch (JsonException ex)
        {
            result.Error = "could not parse classic MC7 host output: " + ex.Message + "\n" + stdout;
            return result;
        }
    }

    private static string? FindUp(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, relative);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
