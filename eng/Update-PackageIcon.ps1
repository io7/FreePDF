# Regenerate the shared package PNG from the original, font-free SVG.
# Requires PowerShell 7 and Inkscape 1.4 (e7c3feb100, 2024-10-09).
# From the repository root: pwsh -NoProfile -File eng/Update-PackageIcon.ps1
param(
    [string]$Inkscape = 'inkscape',
    [string]$OutputPath = "$PSScriptRoot/../src/packages/icon.png"
)
$ErrorActionPreference = 'Stop'
$expectedRenderer = 'Inkscape 1.4 (e7c3feb100, 2024-10-09)'
$renderer = (& $Inkscape --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $renderer -cne $expectedRenderer) {
    throw "Use $expectedRenderer for reproducible output; found '$renderer'."
}
$source = (Resolve-Path "$PSScriptRoot/../src/packages/icon.svg").Path
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("freepdf-icon-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $scratch | Out-Null
$previousProfile = $env:INKSCAPE_PROFILE_DIR
try {
    # An empty profile and explicit export settings avoid local preference changes.
    $env:INKSCAPE_PROFILE_DIR = Join-Path $scratch 'profile'
    $png = Join-Path $scratch 'icon.png'
    & $Inkscape $source --export-type=png --export-area-page --export-width=128 --export-height=128 `
        --export-background-opacity=0 --export-png-color-mode=RGBA_8 --export-png-use-dithering=false `
        --export-png-compression=9 --export-png-antialias=2 "--export-filename=$png"
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $png)) { throw 'Package icon rendering failed.' }
    Copy-Item -LiteralPath $png -Destination $OutputPath -Force
    Write-Output "Rendered 128x128 package icon with $renderer."
    Write-Output "SHA256: $((Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant())"
} finally {
    $env:INKSCAPE_PROFILE_DIR = $previousProfile
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
