$ErrorActionPreference = 'Stop'
$failed = $false
foreach ($framework in @('net10.0','net462')) {
    $config = Join-Path $PSScriptRoot "runner-$framework.json"
    $settings = Get-Content $config -Raw | ConvertFrom-Json
    & "$PSScriptRoot/viewer/FreePDF.Lab.exe" --run $config 2>&1 | Set-Content (Join-Path $settings.dataDirectory "last-$framework.log")
    if ($LASTEXITCODE -ne 0) { $failed = $true }
}
if ($failed) { exit 1 }
