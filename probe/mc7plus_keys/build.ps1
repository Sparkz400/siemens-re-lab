param(
    [string]$Out = (Join-Path $PSScriptRoot 'bin\mc7plus-keys.exe')
)

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    throw "csc.exe not found: $csc"
}
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
& $csc /nologo /target:exe /platform:x64 /out:$Out (Join-Path $PSScriptRoot 'Program.cs')
if ($LASTEXITCODE -ne 0) {
    throw "compile failed"
}
Write-Host "built $Out"
