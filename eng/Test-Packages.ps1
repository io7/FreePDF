# Builds candidate packages and runs an isolated package-only consumer. Does not publish.
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string[]]$Frameworks = @('net10.0'),
    [switch]$StableRehearsal
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/..").Path
$revision = (& git -C $repo rev-parse HEAD).Trim()
$startedDirty = @(& git -C $repo status --porcelain).Count -ne 0
if ($env:CI -eq 'true' -and $startedDirty) { throw 'CI package checks require a clean source checkout.' }
if (Test-Path $OutputDirectory) { throw 'Choose a new output directory so the consumer cannot reuse an older candidate from its package cache.' }
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null
$output = (Resolve-Path $OutputDirectory).Path
[xml]$versionProps = Get-Content "$repo/src/FreePDF.Version.props"
$settings = $versionProps.Project.PropertyGroup
$version = "$($settings.FreePDFVersionMajor).$($settings.FreePDFVersionMinor).$($settings.FreePDFVersionPatch)"
if (!$StableRehearsal -and $settings.FreePDFVersionLabel) { $version += "-$($settings.FreePDFVersionLabel)" }
[string[]]$versionProperties = if ($StableRehearsal) { @('-p:FreePDFVersionLabel=') } else { @() }
foreach ($project in @('FreePDF','MigraDoc')) {
    & dotnet pack "$repo/src/packages/$project/$project.csproj" -c Release -m:1 -o $output @versionProperties
    if ($LASTEXITCODE -ne 0) { throw "Package build failed: $project" }
}
& "$PSScriptRoot/Write-PackageInventory.ps1" -AssetsPath "$repo/src/packages/FreePDF/obj/project.assets.json" -OutputDirectory "$output/library-dependencies"
& "$PSScriptRoot/Test-PackageAdvisories.ps1" -Project "$repo/src/packages/FreePDF/FreePDF.csproj" -OutputPath "$output/library-advisories.json"
$expected = @{
    'io7.FreePDF' = @('PdfSharp','PdfSharp.Shared','PdfSharp.System','PdfSharp.Imaging','PdfSharp.OpenType','PdfSharp.BarCodes','PdfSharp.Charting','PdfSharp.Cryptography')
    'io7.FreePDF.MigraDoc' = @('MigraDoc.DocumentObjectModel','MigraDoc.Rendering','MigraDoc.RtfRendering')
}
foreach ($id in $expected.Keys) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $output "$id.$version.nupkg"))
    try {
        $reader = [IO.StreamReader]::new($archive.GetEntry("$id.nuspec").Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($nuspec.package.metadata.id -ne $id -or $nuspec.package.metadata.version -ne $version) { throw 'Incorrect package identity.' }
        if ($nuspec.package.metadata.repository.url -ne 'https://github.com/io7/FreePDF') { throw 'Incorrect repository metadata.' }
        foreach ($file in @('LICENSE','PROVENANCE.md','README.md','THIRD-PARTY/BigGustave/LICENSE','THIRD-PARTY/ICC-sRGB2014.txt')) { if (!$archive.GetEntry($file)) { throw "Missing notice: $file" } }
        foreach ($tfm in @('net8.0','net9.0','net10.0','netstandard2.0')) {
            $names = @($archive.Entries | Where-Object { $_.FullName -like "lib/$tfm/*.dll" } | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.FullName) })
            if (Compare-Object ($expected[$id] | Sort-Object) ($names | Sort-Object)) { throw "Incorrect assemblies in $id/$tfm" }
        }
        foreach ($group in $nuspec.package.metadata.dependencies.group) {
            $dependencies = @($group.dependency)
            if ($dependencies.id -match '^(PDFsharp|PDFsharp-MigraDoc)(-|$)') { throw 'Upstream package dependency is forbidden.' }
            if ($id -eq 'io7.FreePDF.MigraDoc' -and !($dependencies | Where-Object { $_.id -eq 'io7.FreePDF' -and $_.version -match [regex]::Escape($version) })) { throw 'Missing FreePDF dependency.' }
        }
    } finally { $archive.Dispose() }
}
$localSource = [Security.SecurityElement]::Escape($output)
$config = Join-Path $output 'NuGet.Config'
@"
<configuration>
  <packageSources><clear/><add key="candidate" value="$localSource"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping><packageSource key="candidate"><package pattern="io7.FreePDF*"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping>
</configuration>
"@ | Set-Content $config -Encoding utf8
$consumer = "$repo/tests/PackageSmoke/PackageSmoke.csproj"
$certificatePath = Join-Path $output 'synthetic-signing.pfx'
$key = [Security.Cryptography.RSA]::Create(2048)
try {
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=FreePDF synthetic signing fixture', $key, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(7))
    try { [IO.File]::WriteAllBytes($certificatePath, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, 'test-only')) }
    finally { $certificate.Dispose() }
} finally { $key.Dispose() }
$results = @()
foreach ($framework in $Frameworks) {
    if ($framework -match '^net4' -and !$IsWindows) { throw 'Framework execution requires Windows.' }
    & dotnet restore $consumer --configfile $config --packages "$output/.packages" "-p:TargetFrameworks=$framework" "-p:FreePDFPackageVersion=$version"
    if ($LASTEXITCODE -ne 0) { throw "Consumer restore failed: $framework" }
    & dotnet build $consumer -c Release -f $framework --no-restore -m:1 "-p:TargetFrameworks=$framework" "-p:FreePDFPackageVersion=$version"
    if ($LASTEXITCODE -ne 0) { throw "Consumer build failed: $framework" }
    $binary = "$repo/tests/PackageSmoke/bin/Release/$framework/PackageSmoke"
    $fixtures = Join-Path $output "consumer-$framework"
    if ($framework -match '^net4') { & "$binary.exe" $fixtures $certificatePath } else { & dotnet "$binary.dll" $fixtures $certificatePath }
    if ($LASTEXITCODE -ne 0) { throw "Installed package execution failed: $framework" }
    & "$PSScriptRoot/Write-PackageInventory.ps1" -AssetsPath "$repo/tests/PackageSmoke/obj/project.assets.json" -OutputDirectory "$fixtures/dependencies"
    & "$PSScriptRoot/Test-PackageAdvisories.ps1" -Project $consumer -OutputPath "$fixtures/advisories.json"
    $results += @{ framework=$framework; status='passed'; fixtures="consumer-$framework/consumer-results.xml" }
}
if ((& git -C $repo rev-parse HEAD).Trim() -ne $revision) { throw 'Source revision changed during packaging; rebuild from one revision.' }
$endedDirty = @(& git -C $repo status --porcelain).Count -ne 0
if ($env:CI -eq 'true' -and $endedDirty) { throw 'CI source checkout changed during package checks.' }
if ($startedDirty -or $endedDirty) { $revision += '+worktree' }
@{ sourceRevision=$revision; version=$version; consumers=$results; packages=@(Get-ChildItem $output -Filter '*.nupkg' | ForEach-Object { @{ file=$_.Name; sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } }) } | ConvertTo-Json -Depth 6 | Set-Content "$output/validation.json" -Encoding utf8
