# Builds candidate packages and runs an isolated package-only consumer. Does not publish.
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string[]]$Frameworks = @('net10.0')
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/..").Path
if (Test-Path $OutputDirectory) { throw 'Choose a new output directory so the consumer cannot reuse an older candidate from its package cache.' }
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null
$output = (Resolve-Path $OutputDirectory).Path
[xml]$versionProps = Get-Content "$repo/src/FreePDF.Version.props"
$settings = $versionProps.Project.PropertyGroup
$version = "$($settings.FreePDFVersionMajor).$($settings.FreePDFVersionMinor).$($settings.FreePDFVersionPatch)-$($settings.FreePDFVersionLabel)"
foreach ($project in @('FreePDF','MigraDoc')) {
    & dotnet pack "$repo/src/packages/$project/$project.csproj" -c Release -m:1 -o $output
    if ($LASTEXITCODE -ne 0) { throw "Package build failed: $project" }
}
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
        foreach ($file in @('LICENSE','PROVENANCE.md','README.md','THIRD-PARTY/BigGustave-LICENSE.txt','THIRD-PARTY/ICC-sRGB2014.txt')) { if (!$archive.GetEntry($file)) { throw "Missing notice: $file" } }
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
$results = @()
foreach ($framework in $Frameworks) {
    if ($framework -match '^net4' -and !$IsWindows) { throw 'Framework execution requires Windows.' }
    & dotnet restore $consumer --configfile $config --packages "$output/.packages" "-p:TargetFrameworks=$framework" "-p:FreePDFPackageVersion=$version"
    if ($LASTEXITCODE -ne 0) { throw "Consumer restore failed: $framework" }
    & dotnet build $consumer -c Release -f $framework --no-restore -m:1 "-p:TargetFrameworks=$framework" "-p:FreePDFPackageVersion=$version"
    if ($LASTEXITCODE -ne 0) { throw "Consumer build failed: $framework" }
    $binary = "$repo/tests/PackageSmoke/bin/Release/$framework/PackageSmoke"
    if ($framework -match '^net4') { & "$binary.exe" } else { & dotnet "$binary.dll" }
    if ($LASTEXITCODE -ne 0) { throw "Installed package execution failed: $framework" }
    $results += @{ framework=$framework; status='passed' }
}
$revision = (& git -C $repo rev-parse HEAD).Trim()
if (& git -C $repo status --porcelain) { $revision += '+worktree' }
@{ sourceRevision=$revision; version=$version; consumers=$results; packages=@(Get-ChildItem $output -Filter '*.nupkg' | ForEach-Object { @{ file=$_.Name; sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } }) } | ConvertTo-Json -Depth 6 | Set-Content "$output/validation.json" -Encoding utf8
