param(
    [string]$TiaBin = $(if ($env:TIA_BIN) { $env:TIA_BIN } else { 'C:\Program Files\Siemens\Automation\Portal V21\Bin' }),
    [string]$OutDir = (Join-Path $PSScriptRoot 'bin')
)

$ErrorActionPreference = 'Stop'

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    throw "csc.exe not found at $csc (install the .NET Framework 4.x compiler)."
}
if (-not (Test-Path (Join-Path $TiaBin 'Siemens.Simatic.Lang.MC7ClassicCodeGen.dll'))) {
    throw "TIA Portal Bin directory not found or incomplete: $TiaBin"
}

$netstandardCandidates = @(
    'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll',
    'C:\Program Files\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll'
)
$netstandard = $netstandardCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

$refs = @(
    'System.dll',
    'System.Core.dll'
)
if ($netstandard) { $refs += $netstandard }

$tiaRefs = @(
    'Siemens.Simatic.Lang.MC7ClassicCodeGen.dll',
    'Siemens.Simatic.Lang.PLInternal.dll',
    'Siemens.Simatic.Lang.PLInterface.dll',
    'Siemens.Automation.Basics.dll',
    'Siemens.Simatic.Lang.IdentManager.Interfaces.dll',
    'Siemens.Simatic.Lang.Types.Interfaces.dll',
    'Siemens.Automation.ObjectFrame.dll'
)
foreach ($name in $tiaRefs) {
    $path = Join-Path $TiaBin $name
    if (-not (Test-Path $path)) {
        throw "Missing TIA reference: $path"
    }
    $refs += $path
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$outExe = Join-Path $OutDir 'Mc7ClassicHost.exe'
$cscArgs = @(
    '/nologo',
    '/target:exe',
    '/platform:x64',
    "/out:$outExe"
)
foreach ($ref in $refs) { $cscArgs += "/r:$ref" }
$cscArgs += (Join-Path $PSScriptRoot 'Mc7ClassicHost.cs')

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) {
    throw "csc failed with exit code $LASTEXITCODE"
}
Write-Host "Built $outExe"
Write-Host "Run with: $outExe --tia-bin '$TiaBin' --data <block.bin> --start 0 --length <codeLength>"
