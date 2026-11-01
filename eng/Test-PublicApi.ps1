# Metadata-only comparison with reviewed MIT packages. Never resolve a floating baseline.
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ApiCompat,
    [string]$DependencyDirectory = "$PSScriptRoot/../tests/PackageSmoke/bin/Release/net10.0"
)
$ErrorActionPreference = 'Stop'
if (Test-Path $OutputDirectory) { throw 'Choose a new API audit directory.' }
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null
$output = (Resolve-Path $OutputDirectory).Path
$dependencySnapshot = Join-Path $output 'dependencies'
New-Item -ItemType Directory $dependencySnapshot -Force | Out-Null
Copy-Item (Join-Path $DependencyDirectory '*.dll') $dependencySnapshot
$DependencyDirectory = $dependencySnapshot
if (!$ApiCompat) {
    & dotnet tool install Microsoft.DotNet.ApiCompat.Tool --version 10.0.100 --tool-path "$output/tools"
    if ($LASTEXITCODE -ne 0) { throw 'API tool installation failed.' }
    $ApiCompat = Join-Path $output "tools/apicompat$(if ($IsWindows) {'.exe'})"
}
$toolVersion = (& $ApiCompat --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $toolVersion -notmatch '^10\.0\.100') { throw 'Expected ApiCompat 10.0.100.' }
function Extract-Libraries([string]$ArchivePath, [string]$Destination) {
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $nuspecEntry = @($archive.Entries | Where-Object { $_.Name -like '*.nuspec' })[0]
        $reader = [IO.StreamReader]::new($nuspecEntry.Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($nuspec.package.metadata.license.InnerText -ne 'MIT') { throw 'API input does not declare the reviewed MIT licence.' }
        foreach ($entry in $archive.Entries | Where-Object { $_.FullName -match '^lib/(net8\.0|net9\.0|net10\.0|netstandard2\.0)/[^/]+\.dll$' }) {
            $path = Join-Path $Destination ($entry.FullName.Substring(4))
            New-Item -ItemType Directory (Split-Path $path) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path, $true)
        }
    } finally { $archive.Dispose() }
}
$pins = Get-Content "$PSScriptRoot/api-baselines.json" -Raw | ConvertFrom-Json
foreach ($pin in $pins) {
    $name = "$($pin.id).$($pin.version).nupkg"
    $archive = Join-Path $output $name
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$($pin.id)/$($pin.version)/$name" -OutFile $archive
    if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $pin.sha256) { throw "Baseline hash mismatch: $name" }
    Extract-Libraries $archive (Join-Path $output $pin.version)
}
$packages = @(Get-ChildItem $PackageDirectory -Filter '*.nupkg')
if ($packages.Count -ne 2) { throw 'Expected exactly two candidate packages.' }
foreach ($package in $packages) { Extract-Libraries $package.FullName (Join-Path $output 'candidate') }
$dotnet = (Get-Command dotnet).Source
$dotnetRoot = Split-Path ((Get-Item $dotnet).ResolveLinkTarget($true) ?? (Get-Item $dotnet)).FullName
$referencePack = Get-ChildItem "$dotnetRoot/packs/Microsoft.NETCore.App.Ref" -Directory | Where-Object { $_.Name -like '10.0.*' } | Sort-Object { [version]$_.Name } | Select-Object -Last 1
if (!$referencePack) { throw '.NET 10 reference pack required for API dependency resolution.' }
$frameworkReferences = Join-Path $referencePack.FullName 'ref/net10.0'
$results = @(); $failed = $false
# Since 7.0.1 FreePDF signs with io7's key; the MIT baselines carry empira's. Each comparison must report
# exactly this identity change (pinned to both tokens) and, against Preview 1, nothing else.
$keyChange = "^\s*(error )?CP0003\s*:.* assembly public key token 'f94615aa0424f9eb' does not match with .* '77af05c50284aa70'\.\s*$"
foreach ($version in @('7.0.0-preview-1','6.2.4')) {
    foreach ($framework in @('net8.0','net9.0','net10.0','netstandard2.0')) {
        $left = Join-Path $output "$version/$framework"; $right = Join-Path $output "candidate/$framework"
        foreach ($assembly in Get-ChildItem $right -Filter '*.dll') {
            if (!(Test-Path (Join-Path $left $assembly.Name))) {
                if ($version -ne '6.2.4') { throw "Missing baseline assembly: $($assembly.Name)" }
                continue # Imaging and OpenType were separated into new assemblies in v7.
            }
            $log = "$version-$framework-$($assembly.BaseName).log"
            $messages = @(& $ApiCompat -l (Join-Path $left $assembly.Name) -r $assembly.FullName --lref "$left,$DependencyDirectory,$frameworkReferences" --rref "$right,$DependencyDirectory,$frameworkReferences" --enable-rule-cannot-change-parameter-name 2>&1)
            $exitCode = $LASTEXITCODE
            $messages | Set-Content (Join-Path $output $log) -Encoding utf8
            $diagnosticLines = @($messages | Where-Object { $_ -match '^\s*(error )?CP\d+\s*:' })
            $keyChanges = @($diagnosticLines | Where-Object { $_ -match $keyChange }).Count
            $diagnostics = $diagnosticLines.Count - $keyChanges
            if ($exitCode -ne 1 -or $keyChanges -ne 1) { $failed = $true }
            elseif ($version -ne '6.2.4' -and $diagnostics) { $failed = $true }
            $results += @{ baseline=$version; framework=$framework; assembly=$assembly.Name; exitCode=$exitCode; keyChanges=$keyChanges; diagnostics=$diagnostics; log=$log }
        }
    }
}
@{ tool=$toolVersion; baselineInputs=$pins; comparisons=$results; passed=(!$failed) } | ConvertTo-Json -Depth 6 | Set-Content "$output/api-validation.json" -Encoding utf8
if ($failed) { throw 'API validation failed; inspect per-assembly logs.' }
Write-Host 'PASS: no API compatibility removals from the MIT v7 baseline; only the expected io7 signing-key change; v6 migration diagnostics recorded separately.'
# The last v6 comparison may intentionally return 1. GitHub's pwsh wrapper
# forwards LASTEXITCODE, so explicitly report the gate's overall success.
exit 0
