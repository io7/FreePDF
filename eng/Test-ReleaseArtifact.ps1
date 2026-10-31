# Inspect the exact CI packages without loading or executing their assemblies.
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$ExpectedRevision,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string[]]$RequiredFrameworks = @('net8.0','net9.0','net10.0')
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path $PackageDirectory).Path
$receipt = Get-Content (Join-Path $root 'validation.json') -Raw | ConvertFrom-Json
if ($receipt.sourceRevision -cne $ExpectedRevision) { throw 'Release source revision mismatch.' }
if ($receipt.version -cne $Version) { throw 'Release version mismatch.' }
foreach ($framework in $RequiredFrameworks) {
    $consumers = @($receipt.consumers | Where-Object { $_.framework -ceq $framework })
    if ($consumers.Count -ne 1 -or $consumers[0].status -cne 'passed') { throw "Missing successful consumer: $framework" }
}
$expected = @("io7.FreePDF.$Version.nupkg", "io7.FreePDF.MigraDoc.$Version.nupkg")
if (@($receipt.packages).Count -ne 2 -or (Compare-Object ($expected | Sort-Object) (@($receipt.packages.file) | Sort-Object))) { throw 'Unexpected release package manifest.' }
$actual = @(Get-ChildItem -LiteralPath $root -File -Filter '*.nupkg' | ForEach-Object Name)
if (Compare-Object ($expected | Sort-Object) ($actual | Sort-Object)) { throw 'Unexpected release package files.' }
foreach ($package in $receipt.packages) {
    $path = Join-Path $root $package.file
    if ($package.sha256 -cnotmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $package.sha256) { throw "Release package hash mismatch: $($package.file)" }
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $id = $package.file.Substring(0, $package.file.Length - ".$Version.nupkg".Length)
        $reader = [IO.StreamReader]::new($archive.GetEntry("$id.nuspec").Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $nuspec.package.metadata
        if ($metadata.id -cne $id -or $metadata.version -cne $Version -or $metadata.repository.commit -cne $ExpectedRevision -or $metadata.repository.url -cne 'https://github.com/io7/FreePDF' -or $metadata.license.InnerText -cne 'MIT') { throw "Release package metadata mismatch: $id" }
    } finally { $archive.Dispose() }
}
Write-Output "Verified $($expected.Count) release packages at $ExpectedRevision, version $Version."
