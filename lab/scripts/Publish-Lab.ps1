param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
if (Test-Path $OutputDirectory) { throw 'Choose a new output directory; existing bundles are not overwritten.' }
$revision = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Could not identify source revision.' }
if (& git -C $repo status --porcelain) { $revision += '+worktree' }
New-Item -ItemType Directory $OutputDirectory | Out-Null
function Publish([string]$Project, [string]$Destination) {
    & dotnet publish "$repo/$Project" -c Release -f net10.0 -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.13 -m:1 -o "$OutputDirectory/$Destination"
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $Project" }
}
Publish 'lab/FreePDF.Lab/FreePDF.Lab.csproj' 'viewer'
Publish 'lab/Generators/Reference/Reference.Generator.csproj' 'reference/net10.0'
Publish 'lab/Generators/FreePDF/FreePDF.Generator.csproj' 'candidate/net10.0'
foreach ($engine in @('Reference','FreePDF')) {
    & dotnet build "$repo/lab/Generators/$engine/$engine.Generator.csproj" -c Release -f net462 -m:1
    if ($LASTEXITCODE -ne 0) { throw "Framework build failed: $engine" }
    $destination = if ($engine -eq 'Reference') { 'reference' } else { 'candidate' }
    New-Item -ItemType Directory "$OutputDirectory/$destination/net462" -Force | Out-Null
    Copy-Item "$repo/lab/Generators/$engine/bin/Release/net462/*" "$OutputDirectory/$destination/net462" -Recurse
}
Copy-Item "$repo/lab/scripts/Install-Tomahawk.ps1", "$repo/lab/scripts/Run-Nightly.ps1", "$repo/LICENSE", "$repo/PROVENANCE.md", "$repo/lab/README.md" $OutputDirectory
@{ sourceRevision=$revision; runtime='10.0.13'; reference='PDFsharp-MigraDoc 7.0.0-preview-1 (MIT), Pkcs 10.0.13 override'; renderer='MuPDF 1.28.5 (installed separately, AGPL)' } | ConvertTo-Json | Set-Content "$OutputDirectory/bundle.json" -Encoding utf8
Get-ChildItem $OutputDirectory -Recurse -File | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{path=[IO.Path]::GetRelativePath((Resolve-Path $OutputDirectory).Path,$_.FullName); sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
} | ConvertTo-Json | Set-Content "$OutputDirectory/checksums.json" -Encoding utf8
Write-Output "Bundle prepared: $OutputDirectory"
