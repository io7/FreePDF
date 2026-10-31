param([Parameter(Mandatory)][string]$Project, [Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$result = @(& dotnet list $Project package --vulnerable --include-transitive --format json --output-version 1 --no-restore)
if ($LASTEXITCODE -ne 0) { throw "Dependency advisory query failed: $Project" }
$result | Set-Content $OutputPath -Encoding utf8
$report = $result -join "`n" | ConvertFrom-Json
if (@($report.logs | Where-Object { $_.level -eq 'error' }).Count) { throw 'NuGet advisory source returned an error.' }
if (!$report.projects) { throw 'NuGet returned no audited projects.' }
foreach ($projectResult in $report.projects) {
    foreach ($framework in $projectResult.frameworks) {
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            if ($package.vulnerabilities) { throw "Dependency advisory: $($package.id) $($package.resolvedVersion)" }
        }
    }
}
Write-Host 'PASS: no known dependency advisories in the resolved graph at query time.'
