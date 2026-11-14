param(
    [string]$Bin = 'D:\Arbeit\Siemens\Portal V21\Bin',
    [string]$Dumpbin = 'C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64\dumpbin.exe',
    [string]$OutJson = 'analysis\native-exports.json',
    [string]$OutMd = 'docs\19-native-exports.md'
)

$targets = @(
    'Siemens.DF.CsiWrapper.dll',
    's7irsrvmc7x.dll',
    'JSEngineAdapterV8.dll',
    'rtpu.dll',
    'S7GSDxDLL.dll',
    'Siemens.MP.tkImplementation.dll',
    'Siemens.MP.LMC.tkImplementation.dll',
    'Siemens.Simatic.Hwcn.BusinessLogic.Ncm.scw_mips_V6_1.dll',
    'Siemens.Simatic.Hwcn.BusinessLogic.Ncm.scw_ppc460_V6_1.dll',
    'Siemens.Simatic.Srm.Rdp.Data.dll',
    'Siemens.Simatic.Hmi.BL.Scripting.RTScriptComponentWrapper.dll',
    'CCHMIScriptingSite.dll',
    'CCScriptComp.dll',
    'Siemens.DF.DG.SecurityPlus.CsiWrapper.Pure.BL.dll'
)

$results = New-Object System.Collections.Generic.List[object]
foreach ($name in $targets) {
    $path = Join-Path $Bin $name
    if (-not (Test-Path $path)) { continue }
    $lines = & $Dumpbin /exports $path 2>&1
    $exports = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ($line -match '^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(\S+)') {
            $exports.Add($Matches[1])
        }
    }
    $results.Add([pscustomobject]@{
        Name = $name
        SizeMB = [math]::Round((Get-Item $path).Length / 1MB, 3)
        ExportCount = $exports.Count
        Exports = @($exports | Select-Object -First 200)
    })
    Write-Host ("{0,-70} {1,5} exports" -f $name, $exports.Count)
}

$results | ConvertTo-Json -Depth 4 | Set-Content $OutJson -Encoding UTF8

$md = New-Object System.Collections.Generic.List[string]
$md.Add('# Native exports and entry points')
$md.Add('')
$md.Add('Curated `dumpbin /exports` inventory for the native TIA V21 modules')
$md.Add('that matter to the A/B/C tracks. The JSON keeps the first 200 names per')
$md.Add('module; the full export table is in the DLL itself.')
$md.Add('')
$md.Add('| Module | Size MB | Exports |')
$md.Add('|---|---:|---:|')
foreach ($r in $results) {
    $md.Add("| ``$($r.Name)`` | $($r.SizeMB) | $($r.ExportCount) |")
}
$md.Add('')
$md.Add('## Key findings')
$md.Add('')
$md.Add('- `Siemens.DF.CsiWrapper.dll` exposes only `InitCommonSecurity`,')
$md.Add('  `EncryptDataInit/Update/Final`, `CalculateMACHybrid`, `BlockLength`,')
$md.Add('  `CipherBufferLength` and `GetContextLength`. There is no `Decrypt*`.')
$md.Add('- `s7irsrvmc7x.dll` exposes the native MC7 compiler/runtime surface:')
$md.Add('  `IR_CodeGenerator_GenerateBinaryCode`, `IR_CallInstruction_*`,')
$md.Add('  `IR_CompilerEnvironment_*`, `IR_*` message and stack-size helpers.')
$md.Add('- `JSEngineAdapterV8.dll` and `rtpu.dll` expose the module entry points')
$md.Add('  `CfCreateInstanceModule`, `CfInitModule`, `CfExitModule`.')
$md.Add('- `Siemens.MP.tkImplementation.dll` exports a large C++ API')
$md.Add('  (`tk::`, `CTKServicePropertyBase`, `ITK*`); it is the motion/technology')
$md.Add('  implementation, not a small helper DLL.')
$md.Add('- `CCHMIScriptingSite.dll` / `CCScriptComp.dll` are the classic WinCC')
$md.Add('  VB scripting path; `RTScriptComponentWrapper.dll` is the Unified V8 path.')
$md.Add('')
$md.Add('Reproduce:')
$md.Add('')
$md.Add('```powershell')
$md.Add('.\probe\native_exports.ps1 -Bin ''<TIA_BIN>''')
$md.Add('```')
$md | Set-Content $OutMd -Encoding UTF8

Write-Host "wrote $OutJson and $OutMd"
