param(
    [string]$TiaBin = $(if ($env:TIA_BIN) { $env:TIA_BIN } else { 'C:\Program Files\Siemens\Automation\Portal V21\Bin' }),
    [string]$OutDir = (Join-Path $PSScriptRoot 'bin')
)

$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }
if (-not (Test-Path (Join-Path $TiaBin 'Siemens.Simatic.Lang.MC7plusCommon.dll'))) {
    throw "TIA Portal Bin directory not found or incomplete: $TiaBin"
}

$netstandardCandidates = @(
    'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll',
    'C:\Program Files\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll'
)
$netstandard = $netstandardCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

$refs = @('System.dll', 'System.Core.dll')
if ($netstandard) { $refs += $netstandard }
foreach ($name in @(
    'Siemens.Simatic.Lang.MC7plusCommon.dll',
    'Siemens.Simatic.Lang.MC7plus.Internal.dll',
    'Siemens.Simatic.Lang.MC7plus.External.dll',
    'Siemens.Simatic.Lang.MC7Codegenerator.dll'
)) {
    $path = Join-Path $TiaBin $name
    if (-not (Test-Path $path)) { throw "Missing TIA reference: $path" }
    $refs += $path
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$outExe = Join-Path $OutDir 'Mc7PlusReader.exe'
$cscArgs = @('/nologo', '/target:exe', '/platform:x64', "/out:$outExe")
foreach ($ref in $refs) { $cscArgs += "/r:$ref" }
$cscArgs += (Join-Path $PSScriptRoot 'Program.cs')

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
Write-Host "Built $outExe"
