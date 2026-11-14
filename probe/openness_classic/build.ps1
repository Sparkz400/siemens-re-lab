param(
    [string]$PublicApi = 'C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48',
    [string]$BinApi = 'C:\Program Files\Siemens\Automation\Portal V21\Bin\PublicAPI',
    [string]$OutDir = (Join-Path $PSScriptRoot 'bin')
)

$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }

$refs = @(
    'System.dll',
    'System.Core.dll',
    (Join-Path $PublicApi 'Siemens.Engineering.Base.dll'),
    (Join-Path $PublicApi 'Siemens.Engineering.Step7.dll'),
    (Join-Path $BinApi 'Siemens.Engineering.Contract.dll')
)
foreach ($ref in $refs) {
    if ($ref -notmatch '^System' -and -not (Test-Path $ref)) { throw "Missing reference: $ref" }
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$outExe = Join-Path $OutDir 'OpennessClassic.exe'
$cscArgs = @('/nologo', '/target:exe', '/platform:x64', "/out:$outExe")
foreach ($ref in $refs) { $cscArgs += "/r:$ref" }
$cscArgs += (Join-Path $PSScriptRoot 'Program.cs')

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
Write-Host "Built $outExe"
