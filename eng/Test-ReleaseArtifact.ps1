# Inspect the exact CI packages without loading or executing their assemblies.
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$ExpectedRevision,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string[]]$RequiredFrameworks = @('net8.0','net9.0','net10.0'),
    # GDI/WPF package consumers; only Windows receipts carry them.
    [string[]]$RequiredWindowsFrameworks = @()
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path $PackageDirectory).Path
$receipt = Get-Content (Join-Path $root 'validation.json') -Raw | ConvertFrom-Json
if ($receipt.sourceRevision -cne $ExpectedRevision) { throw 'Release source revision mismatch.' }
if ($receipt.version -cne $Version) { throw 'Release version mismatch.' }
function ExpectConsumer([string]$variant, [string]$framework) {
    $consumers = @($receipt.consumers | Where-Object { $_.framework -ceq $framework -and "$($_.variant)" -ceq $variant })
    if ($consumers.Count -ne 1 -or $consumers[0].status -cne 'passed') { throw "Missing successful consumer: $variant $framework".Replace('  ', ' ') }
}
foreach ($framework in $RequiredFrameworks) { ExpectConsumer '' $framework }
foreach ($framework in $RequiredWindowsFrameworks) { foreach ($variant in @('Gdi','Wpf')) { ExpectConsumer $variant $framework } }
if ($receipt.accessibilityTool.status -cne 'passed' -or $receipt.accessibilityTool.packageId -cne 'io7.FreePDF.Accessibility.Tool' -or $receipt.accessibilityTool.framework -cne 'net10.0' -or $receipt.accessibilityTool.command -cne 'freepdf-a11y') { throw 'Missing successful accessibility tool installation.' }
$expected = @('io7.FreePDF','io7.FreePDF.Layout','io7.FreePDF.Gdi','io7.FreePDF.Layout.Gdi','io7.FreePDF.Wpf','io7.FreePDF.Layout.Wpf','io7.FreePDF.Accessibility.Tool') | ForEach-Object { "$_.$Version.nupkg" }
if (@($receipt.packages).Count -ne $expected.Count -or (Compare-Object ($expected | Sort-Object) (@($receipt.packages.file) | Sort-Object))) { throw 'Unexpected release package manifest.' }
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
        if ($metadata.icon -cne 'icon.png') { throw "Release package icon metadata mismatch: $id" }
        $icon = $archive.GetEntry('icon.png')
        if (!$icon -or $icon.Length -eq 0 -or $icon.Length -gt 1MB) { throw "Missing or invalid release package icon: $id" }
        if ($id -ceq 'io7.FreePDF.Accessibility.Tool') {
            if (@($metadata.packageTypes.packageType.name) -cnotcontains 'DotnetTool') { throw 'Invalid accessibility tool package type.' }
            $settings = $archive.GetEntry('tools/net10.0/any/DotnetToolSettings.xml')
            if (!$settings -or !$archive.GetEntry('tools/net10.0/any/FreePDF.Accessibility.dll')) { throw 'Missing accessibility tool executable.' }
            $reader = [IO.StreamReader]::new($settings.Open())
            try { [xml]$toolSettings = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $commands = @($toolSettings.DotNetCliTool.Commands.Command)
            if ($commands.Count -ne 1 -or $commands[0].Name -cne 'freepdf-a11y' -or $commands[0].EntryPoint -cne 'FreePDF.Accessibility.dll' -or $commands[0].Runner -cne 'dotnet') { throw 'Invalid accessibility tool command.' }
            foreach ($notice in @('LICENSE','PROVENANCE.md','README.md','THIRD-PARTY/Matterhorn-NOTICE.txt','THIRD-PARTY/BigGustave/LICENSE','THIRD-PARTY/ICC-sRGB2014.txt','THIRD-PARTY/Adobe-Core14-AFM.txt','THIRD-PARTY/DotNet-MIT-LICENSE.txt',
                'THIRD-PARTY/Microsoft.Extensions.DependencyInjection.Abstractions/THIRD-PARTY-NOTICES.TXT','THIRD-PARTY/Microsoft.Extensions.Logging.Abstractions/THIRD-PARTY-NOTICES.TXT','THIRD-PARTY/System.Security.Cryptography.Pkcs/THIRD-PARTY-NOTICES.TXT')) {
                if (!$archive.GetEntry($notice)) { throw "Missing tool notice: $notice" }
            }
        }
    } finally { $archive.Dispose() }
}
Write-Output "Verified $($expected.Count) release packages at $ExpectedRevision, version $Version."
