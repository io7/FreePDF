# Inventory the resolved graph, including build-only inputs, with retained notices.
param([Parameter(Mandatory)][string]$AssetsPath, [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null
$assets = Get-Content $AssetsPath -Raw | ConvertFrom-Json -AsHashtable
$components = @()
foreach ($identity in $assets.libraries.Keys | Sort-Object) {
    $library = $assets.libraries[$identity]
    if ($library.type -ne 'package') { continue }
    $folder = @($assets.packageFolders.Keys | ForEach-Object { Join-Path $_ $library.path } | Where-Object { Test-Path $_ })[0]
    if (!$folder) { throw "Package cache missing: $identity" }
    [xml]$nuspec = Get-Content (Get-ChildItem $folder -Filter '*.nuspec' | Select-Object -First 1).FullName -Raw
    $metadata = $nuspec.package.metadata
    $license = [string]$metadata.license.InnerText
    $licenseEvidence = [string]$metadata.licenseUrl
    $runtimeTargets = @($assets.targets.Keys | Where-Object {
        $item = $assets.targets[$_][$identity]
        $item -and (@($item.runtime.Keys) + @($item.native.Keys) + @($item.runtimeTargets.Keys) | Where-Object { $_ -and $_ -notlike '*/_._' }).Count -gt 0
    })
    if (!$license -and $identity -eq 'NETStandard.Library/2.0.3') {
        $licenseFile = Join-Path $folder 'LICENSE.TXT'
        if ((Get-FileHash $licenseFile -Algorithm SHA256).Hash -ne 'fc95de1436a321aadbfafd21f8a8506a2f4678bfc003bd806d6b59d3088efadc') { throw 'NETStandard licence changed.' }
        $license = 'MIT'; $licenseEvidence = 'Package LICENSE.TXT, SHA-256 verified'
    }
    if (!$license -and $identity -eq 'Microsoft.NETCore.Platforms/1.1.0' -and !$runtimeTargets.Count) {
        $licenseFile = Join-Path $folder 'dotnet_library_license.txt'
        if ((Get-FileHash $licenseFile -Algorithm SHA256).Hash -ne 'f1db688d8481c91a452fabcea5060a23da9ea5088329b58c478a040e2e426297') { throw 'Legacy Microsoft licence changed.' }
        $license = 'LicenseRef-Microsoft-DotNet-Library'; $licenseEvidence = 'Package dotnet_library_license.txt, SHA-256 verified; build metadata only'
    }
    if (!$license -and $identity -match '^Microsoft\.NETFramework\.ReferenceAssemblies(\.net\d+)?/1\.0\.3$' -and !$runtimeTargets.Count -and $licenseEvidence -eq 'https://github.com/Microsoft/dotnet/blob/master/LICENSE') {
        $license = 'MIT'; $licenseEvidence += ' (reviewed publisher licence URL; build references only)'
    }
    if ($license -notin @('MIT','LicenseRef-Microsoft-DotNet-Library')) { throw "Unreviewed dependency licence: $identity ($license)" }
    $noticeDirectory = Join-Path $OutputDirectory ($identity -replace '/', '-')
    New-Item -ItemType Directory $noticeDirectory -Force | Out-Null
    $notices = @()
    foreach ($file in Get-ChildItem $folder -File | Where-Object { $_.Name -match '(?i)(licen[cs]e|notice)' }) {
        Copy-Item $file.FullName $noticeDirectory
        $notices += @{ file=$file.Name; sha256=(Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $archive = Get-ChildItem $folder -Filter '*.nupkg' | Select-Object -First 1
    if (!$archive) { throw "Package archive missing: $identity" }
    $components += @{ id=[string]$metadata.id; version=[string]$metadata.version; sha256=(Get-FileHash $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant();
        license=$license; licenseEvidence=$licenseEvidence; runtimeTargets=$runtimeTargets; notices=$notices }
}
@{ frameworks=@($assets.targets.Keys); components=$components } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'dependency-inventory.json') -Encoding utf8
Write-Host "PASS: inventoried $($components.Count) packages and their licence evidence."
