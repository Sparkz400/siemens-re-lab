param(
    [string]$TiaBin = 'C:\Program Files\Siemens\Automation\Portal V21\Bin',
    [string]$OutDir = (Join-Path $PSScriptRoot '..\analysis')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $TiaBin)) { throw "TIA Bin directory not found: $TiaBin" }
New-Item -ItemType Directory -Force $OutDir | Out-Null

$rows = New-Object System.Collections.Generic.List[object]
$files = Get-ChildItem $TiaBin -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.dll', '.exe' }
$total = $files.Count
$i = 0

foreach ($file in $files) {
    $i++
    if ($i % 100 -eq 0) { Write-Progress -Activity 'TIA inventory' -Status "$i / $total" -PercentComplete (($i / $total) * 100) }
    $managed = $false
    $assemblyName = ''
    $assemblyVersion = ''
    try {
        $name = [System.Reflection.AssemblyName]::GetAssemblyName($file.FullName)
        $managed = $true
        $assemblyName = $name.Name
        $assemblyVersion = $name.Version.ToString()
    } catch {
        $managed = $false
    }
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($file.FullName)
    $rows.Add([pscustomobject]@{
        Name = $file.Name
        Extension = $file.Extension
        SizeMB = [math]::Round($file.Length / 1MB, 3)
        Managed = $managed
        AssemblyName = $assemblyName
        AssemblyVersion = $assemblyVersion
        FileVersion = $versionInfo.FileVersion
    })
}
Write-Progress -Activity 'TIA inventory' -Completed

$csv = Join-Path $OutDir 'tia-assembly-inventory.csv'
$rows | Sort-Object Managed, SizeMB -Descending |
    Export-Csv -NoTypeInformation -Encoding UTF8 $csv

$managedRows = $rows | Where-Object Managed
$nativeRows = $rows | Where-Object { -not $_.Managed }
$summary = [ordered]@{
    tiaBin = (Resolve-Path $TiaBin).Path
    generated = (Get-Date).ToString('o')
    files = $rows.Count
    managed = $managedRows.Count
    native = $nativeRows.Count
    totalSizeMB = [math]::Round((($rows | Measure-Object SizeMB -Sum).Sum), 1)
    managedSizeMB = [math]::Round((($managedRows | Measure-Object SizeMB -Sum).Sum), 1)
    nativeSizeMB = [math]::Round((($nativeRows | Measure-Object SizeMB -Sum).Sum), 1)
    topManaged = $managedRows | Sort-Object SizeMB -Descending | Select-Object -First 20 Name, SizeMB, AssemblyVersion
    topNative = $nativeRows | Sort-Object SizeMB -Descending | Select-Object -First 20 Name, SizeMB
}
$json = Join-Path $OutDir 'tia-assembly-summary.json'
$summary | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 $json

Write-Host "files=$($rows.Count) managed=$($managedRows.Count) native=$($nativeRows.Count)"
Write-Host "csv=$csv"
Write-Host "summary=$json"
