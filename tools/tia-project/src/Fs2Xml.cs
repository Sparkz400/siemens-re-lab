using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TiaProject;

public sealed class ExportedObjectInfo
{
    public string ClassName { get; set; } = "";
    public long InstanceId { get; set; }
    public int ClusterId { get; set; }
    public Guid Guid { get; set; }
    public string? Name { get; set; }
    public string? Subtype { get; set; }
    public int? Number { get; set; }
    public int? BlockType { get; set; }
    public int? ProgrammingLanguage { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = [];
    public Dictionary<string, string> CoreBlobs { get; set; } = [];
    public List<(int TypeId, long InstanceId)> Links { get; set; } = [];
}

public sealed class ExportedBlock
{
    public string Name { get; set; } = "";
    public string? Subtype { get; set; }
    public int? Number { get; set; }
    public int? BlockType { get; set; }
    public int? ProgrammingLanguage { get; set; }
    public string? LanguageName { get; set; }
    public Guid BlockGuid { get; set; }
    public long BlockInstanceId { get; set; }
    public long? CompileUnitInstanceId { get; set; }
    public Guid? CompileUnitGuid { get; set; }
    public string? SourceRoot { get; set; }
    public string? SourceFile { get; set; }
    public string? SourceText { get; set; }
    public int? SourceLength { get; set; }
}

public sealed class ObjectExport
{
    public string FilePath { get; set; } = "";
    public string? FileVersion { get; set; }
    public int ObjectCount { get; set; }
    public int LastInstanceId { get; set; }
    public List<ExportedObjectInfo> Objects { get; set; } = [];
    public Dictionary<Guid, ExportedObjectInfo> ByGuid { get; set; } = [];
    public List<ExportedBlock> Blocks { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    public static ObjectExport Read(string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root ?? throw new CliException($"empty XML: {path}");
        var result = new ObjectExport
        {
            FilePath = Path.GetFullPath(path),
            FileVersion = root.Attribute("fileversion")?.Value
        };
        var objectsEl = root.Elements().FirstOrDefault(e => e.Name.LocalName == "objects")
                        ?? throw new CliException("no <objects> element in export");
        result.ObjectCount = int.TryParse(objectsEl.Attribute("objectCount")?.Value, out var oc) ? oc : 0;
        result.LastInstanceId = int.TryParse(objectsEl.Attribute("lastInstanceId")?.Value, out var li) ? li : 0;

        foreach (var so in objectsEl.Elements().Where(e => e.Name.LocalName == "StorageObject"))
        {
            var cls = so.Attribute("name")?.Value ?? "";
            var instText = so.Attribute("instId")?.Value;
            if (!long.TryParse(instText, out var inst)) continue;
            var guidText = so.Attribute("guid")?.Value;
            if (!Guid.TryParse(guidText, out var guid)) guid = Guid.Empty;
            var info = new ExportedObjectInfo
            {
                ClassName = cls,
                InstanceId = inst,
                ClusterId = int.TryParse(so.Attribute("clusterId")?.Value, out var cl) ? cl : 0,
                Guid = guid
            };

            foreach (var set in so.Elements().Where(e => e.Name.LocalName == "attribSet"))
            {
                var setName = set.Attribute("name")?.Value ?? "";
                foreach (var attrib in set.Elements().Where(e => e.Name.LocalName == "attrib"))
                {
                    var name = attrib.Attribute("name")?.Value;
                    if (string.IsNullOrEmpty(name)) continue;
                    var valueEl = attrib.Elements().FirstOrDefault();
                    var value = valueEl is null ? "" : ValueOf(valueEl);
                    var key = setName.Length > 0 ? setName + "." + name : name;
                    info.Attributes[key] = value;
                    if (name == "Name") info.Name = value;
                    else if (name == "Subtype") info.Subtype = value;
                    else if (name == "Number" && int.TryParse(value, out var num)) info.Number = num;
                    else if (name == "BlockType" && int.TryParse(value, out var bt)) info.BlockType = bt;
                    else if (name == "ProgrammingLanguage" && int.TryParse(value, out var pl)) info.ProgrammingLanguage = pl;
                    if (valueEl is not null && valueEl.Name.LocalName is "coreblob" or "corexmlblob")
                        info.CoreBlobs[name] = value;
                }

                foreach (var expando in set.Elements().Where(e => e.Name.LocalName == "expando"))
                {
                    foreach (var entry in expando.Descendants().Where(e => e.Name.LocalName == "entry"))
                    {
                        var name = entry.Attribute("name")?.Value;
                        if (string.IsNullOrEmpty(name)) continue;
                        var valueEl = entry.Elements().FirstOrDefault();
                        var value = valueEl is null ? "" : ValueOf(valueEl);
                        info.Attributes[$"{setName}.{name}"] = value;
                        if (valueEl?.Name.LocalName is "coreblob" or "corexmlblob")
                            info.CoreBlobs[name] = value;
                    }
                }
            }

            foreach (var link in so.Descendants().Where(e => e.Name.LocalName == "link"))
            {
                var text = link.Value.Trim();
                var dash = text.IndexOf('-');
                if (dash <= 0) continue;
                if (int.TryParse(text[..dash], out var lt) && long.TryParse(text[(dash + 1)..], out var li2))
                    info.Links.Add((lt, li2));
            }
            foreach (var parent in so.Descendants().Where(e => e.Name.LocalName == "parentlink"))
            {
                var text = parent.Attribute("link")?.Value?.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                var dash = text.IndexOf('-');
                if (dash <= 0) continue;
                if (int.TryParse(text[..dash], out var lt) && long.TryParse(text[(dash + 1)..], out var li2))
                    info.Links.Add((lt, li2));
            }

            result.Objects.Add(info);
            if (guid != Guid.Empty) result.ByGuid[guid] = info;
        }

        result.BuildBlocks();
        return result;
    }

    private static string ValueOf(XElement el) => el.Name.LocalName switch
    {
        "coretextattribute" => "",
        "structure" => string.Join("|", el.Elements().Select(x => x.Value)),
        "expando" => string.Join("|", el.Descendants().Where(x => x.Name.LocalName == "entry")
            .Select(x => x.Attribute("name")?.Value + "=" + x.Elements().FirstOrDefault()?.Value)),
        _ => el.Value
    };

    private void BuildBlocks()
    {
        var compileUnits = Objects.Where(o => o.ClassName.EndsWith(".CompileUnitData", StringComparison.Ordinal))
            .GroupBy(o => o.InstanceId).ToDictionary(g => g.Key, g => g.First());
        var compileUnitList = compileUnits.Values.ToList();

        foreach (var block in Objects.Where(o => o.ClassName.EndsWith(".CodeBlockData", StringComparison.Ordinal)))
        {
            var eb = new ExportedBlock
            {
                Name = block.Name ?? $"Block_{block.InstanceId}",
                Subtype = block.Subtype,
                Number = block.Number,
                BlockType = block.BlockType,
                ProgrammingLanguage = block.ProgrammingLanguage,
                LanguageName = LanguageName(block.ProgrammingLanguage),
                BlockGuid = block.Guid,
                BlockInstanceId = block.InstanceId
            };

            var cuLink = block.Links.FirstOrDefault(l => l.TypeId == 2232342);
            ExportedObjectInfo? cu = null;
            if (cuLink.InstanceId != 0 && compileUnits.TryGetValue(cuLink.InstanceId, out var found))
                cu = found;
            else
                cu = compileUnitList.FirstOrDefault(c => c.Links.Any(l => l.TypeId == 2232321 && l.InstanceId == block.InstanceId));

            if (cu is not null)
            {
                eb.CompileUnitInstanceId = cu.InstanceId;
                eb.CompileUnitGuid = cu.Guid;
                if (cu.CoreBlobs.TryGetValue("Data", out var hex))
                {
                    var text = TextUtil.DecodeCoreBlob(hex);
                    if (text is not null)
                    {
                        eb.SourceText = text;
                        eb.SourceLength = text.Length;
                        eb.SourceRoot = TryRoot(text);
                    }
                }
            }
            else
            {
                Warnings.Add($"no CompileUnitData for block {eb.Name} ({block.InstanceId})");
            }
            Blocks.Add(eb);
        }
    }

    public void WriteHmiModel(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var hmi = Objects
            .Where(o => o.ClassName.Contains("Hmi", StringComparison.OrdinalIgnoreCase)
                        || o.ClassName.Contains("HwConfiguration.Hmi", StringComparison.OrdinalIgnoreCase))
            .Select(ToHmiObject)
            .ToList();
        var screens = hmi.Where(o => o.ClassName.EndsWith("HmiScreenData", StringComparison.Ordinal)
                                     || o.ClassName.EndsWith("HmiScreenItemData", StringComparison.Ordinal)).ToList();
        var tags = hmi.Where(o => o.ClassName.EndsWith("HmiSystemTagData", StringComparison.Ordinal)
                                  || o.ClassName.EndsWith("HmiTagTableData", StringComparison.Ordinal)
                                  || o.ClassName.EndsWith("HmiTagSettingsData", StringComparison.Ordinal)).ToList();
        File.WriteAllText(Path.Combine(outDir, "objects.json"), JsonUtil.Serialize(hmi), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "screens.json"), JsonUtil.Serialize(screens), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "tags.json"), JsonUtil.Serialize(tags), new UTF8Encoding(false));
    }

    private static HmiObject ToHmiObject(ExportedObjectInfo o) => new()
    {
        ClassName = o.ClassName,
        InstanceId = o.InstanceId,
        Guid = o.Guid,
        Name = o.Name,
        Subtype = o.Subtype,
        Attributes = o.Attributes,
        Links = o.Links.Select(l => $"{l.TypeId}-{l.InstanceId}").ToList()
    };

    public void WriteBlockSources(string outDir)
    {
        Directory.CreateDirectory(outDir);
        foreach (var b in Blocks)
        {
            if (b.SourceText is null) continue;
            var ext = b.SourceRoot switch
            {
                "SCLSource" => ".scl.xml",
                "FlgNet" => ".lad.xml",
                "STLSource" => ".stl.xml",
                _ => ".xml"
            };
            var stem = SafeFileName($"{b.Subtype ?? "BLOCK"}_{b.Number?.ToString() ?? "0"}_{b.Name}");
            var file = Path.Combine(outDir, stem + ext);
            File.WriteAllText(file, b.SourceText, new UTF8Encoding(false));
            b.SourceFile = file;
        }
    }

    private static string? TryRoot(string text)
    {
        try { return XDocument.Parse(text).Root?.Name.LocalName; }
        catch { return null; }
    }

    private static string? LanguageName(int? language) => language switch
    {
        1 => "STL",
        2 => "LAD",
        3 => "FBD",
        4 => "SCL",
        5 => "GRAPH",
        6 => "F_LAD",
        7 => "F_FBD",
        8 => "F_SCL",
        9 => "F_STL",
        _ => language?.ToString()
    };

    private static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 120 ? name[..120] : name;
    }
}

public sealed class HmiObject
{
    public string ClassName { get; set; } = "";
    public long InstanceId { get; set; }
    public Guid Guid { get; set; }
    public string? Name { get; set; }
    public string? Subtype { get; set; }
    public Dictionary<string, string> Attributes { get; set; } = [];
    public List<string> Links { get; set; } = [];
}

public sealed class Fs2XmlResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string EncryptedPath { get; set; } = "";
    public string PlainPath { get; set; } = "";
    public long EncryptedLength { get; set; }
    public long PlainLength { get; set; }
    public string? PlainHeader { get; set; }
    public string? ScriptOutput { get; set; }
}

public static class Fs2XmlBridge
{
    // KeyGenerator.m_FixedKeyArray / m_FixedIvArray (CoreKeyType.PeEncrypt, AES-128-CBC/PKCS7).
    private static readonly byte[] FixedKey =
    [
        40, 111, 118, 92, 110, 59, 30, 76, 208, 142, 66, 49, 67, 123, 142, 191
    ];

    private static readonly byte[] FixedIv = [1, 2, 3, 1, 2, 3, 1, 2, 3, 1, 2, 3, 1, 2, 3, 1];

    public static Fs2XmlResult ExportPlain(string plfPath, string outputXmlPath, string tiaBin, int timeoutSeconds = 600, bool keepEncrypted = false)
    {
        var outFull = Path.GetFullPath(outputXmlPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outFull)!);
        var work = Path.Combine(Path.GetTempPath(), "tia-project", "fs2xml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var encrypted = Path.Combine(work, "export.enc");
        var scriptPath = Path.Combine(work, "export.ps1");
        File.WriteAllText(scriptPath, BuildScript(tiaBin, Path.GetFullPath(plfPath), encrypted, timeoutSeconds), new UTF8Encoding(false));

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = tiaBin
        };

        using var proc = Process.Start(psi) ?? throw new CliException("could not start powershell.exe");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(timeoutSeconds * 1000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new CliException($"FS2XML export timed out after {timeoutSeconds}s");
        }

        var result = new Fs2XmlResult
        {
            EncryptedPath = encrypted,
            PlainPath = outFull,
            ScriptOutput = (stdout + "\n" + stderr).Trim()
        };

        var marker = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(l => l.StartsWith("TIA_PROJECT_RESULT:", StringComparison.Ordinal));
        if (marker is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(marker["TIA_PROJECT_RESULT:".Length..]);
                result.Ok = doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean();
                result.Error = doc.RootElement.TryGetProperty("error", out var err) ? err.GetString() : null;
            }
            catch (JsonException)
            {
                result.Error = "could not parse export result: " + marker;
            }
        }
        else
        {
            result.Error = "export host produced no result marker. " + result.ScriptOutput;
        }

        if (!result.Ok)
        {
            result.Error ??= "FS2XML export failed";
            FileUtil.TryDeleteDirectory(work);
            return result;
        }

        if (!File.Exists(encrypted))
        {
            result.Ok = false;
            result.Error = "FS2XML reported success but wrote no file";
            FileUtil.TryDeleteDirectory(work);
            return result;
        }

        var cipher = File.ReadAllBytes(encrypted);
        result.EncryptedLength = cipher.Length;
        var plain = Decrypt(cipher);
        File.WriteAllBytes(outFull, plain);
        result.PlainLength = plain.Length;
        result.PlainHeader = Encoding.UTF8.GetString(plain, 0, Math.Min(120, plain.Length));
        result.Ok = plain.Length > 0 && plain[0] == (byte)'<';
        if (!result.Ok) result.Error = "decrypted output does not look like XML";

        if (keepEncrypted)
        {
            var encOut = outFull + ".enc";
            File.Copy(encrypted, encOut, overwrite: true);
            result.EncryptedPath = encOut;
        }
        else
        {
            result.EncryptedPath = "";
        }
        FileUtil.TryDeleteDirectory(work);
        return result;
    }

    public static byte[] Decrypt(byte[] cipher)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = FixedKey;
        aes.IV = FixedIv;
        var compressed = aes.CreateDecryptor().TransformFinalBlock(cipher, 0, cipher.Length);
        using var input = new MemoryStream(compressed);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    private static string BuildScript(string bin, string plf, string xml, int timeoutSeconds)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        return $$"""
$ErrorActionPreference = 'Stop'
$bin = {{Q(bin)}}
Set-Location $bin
try {
  $kasm = [Reflection.Assembly]::LoadFrom((Join-Path $bin 'Siemens.Automation.ObjectFrame.Kernel.dll'))
  $mm = $kasm.GetType('Siemens.Automation.ObjectFrame.Kernel.MemoryManager', $true)
  $init = $mm.GetMethod('Initialize', [Reflection.BindingFlags]'Static,Public,NonPublic', $null, [Type[]]@([int]), $null)
  $init.Invoke($null, @(104857600)) | Out-Null

  $asm = [Reflection.Assembly]::LoadFrom((Join-Path $bin 'Siemens.Automation.ObjectFrame.FileStorage.FS2XML.exe'))
  $t = $asm.GetType('Siemens.Automation.ObjectFrame.FileStorage.FS2XML.ConsoleExportHelper', $true)
  $ctor = $t.GetConstructor([Reflection.BindingFlags]'Instance,Public,NonPublic', $null, [Type[]]@([string],[string],[string[]],[string[]],[bool]), $null)
  if (-not $ctor) { throw 'ConsoleExportHelper constructor not found' }
  Remove-Item {{Q(xml)}} -ErrorAction SilentlyContinue
  $helper = $ctor.Invoke(@({{Q(plf)}}, {{Q(xml)}}, $null, $null, $false))
  $helper.StartAsync()
  $deadline = (Get-Date).AddSeconds({{timeoutSeconds}})
  while ($helper.IsBusy -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
  $err = $helper.ErrorText
  $ok = (-not $helper.ErrorExists) -and (Test-Path {{Q(xml)}})
  try { $helper.Dispose() } catch { }
  $payload = @{ ok = [bool]$ok; error = [string]$err; xml = {{Q(xml)}} } | ConvertTo-Json -Compress
  Write-Output ('TIA_PROJECT_RESULT:' + $payload)
} catch {
  $payload = @{ ok = $false; error = $_.Exception.ToString(); xml = {{Q(xml)}} } | ConvertTo-Json -Compress
  Write-Output ('TIA_PROJECT_RESULT:' + $payload)
}
""";
    }
}
