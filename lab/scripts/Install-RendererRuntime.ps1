# Private runtime for the standalone renderer; does not run the redistributable installer.
# Requires PowerShell 7 and the built-in Windows expand.exe command.
param([Parameter(Mandatory)][string]$RendererDirectory)
$ErrorActionPreference = 'Stop'
$url = 'https://download.visualstudio.microsoft.com/download/pr/ebdab8e5-1d7b-4d9f-a11b-cbb1720c3b12/843068991DAAA1F73AD9F6239BCE4D0F6A07A51F18C37EA2A867E9BECA71295C/VC_redist.x64.exe'
$archiveHash = '843068991daaa1f73ad9f6239bce4d0f6a07a51f18c37ea2a867e9beca71295c'
$files = @{
    'vcruntime140.dll'='d1f4225df2cd877dbf130d5668a021dce3f94118455ff5ec952061c30afc9ce7'
    'vcruntime140_1.dll'='a7146c08f89fe5b04541ab507cdb59ff7b44534d4ba3c668a426c6450a03434e'
    'msvcp140.dll'='7c26614e1d733892c2deac7e245ce115504b1d80592dd0a01b08e3e5a55f89ca'
}
$stage = Join-Path $RendererDirectory 'runtime-source'
New-Item -ItemType Directory $stage -Force | Out-Null
$archive = Join-Path $stage 'VC_redist.x64.exe'
if (!(Test-Path $archive)) { Invoke-WebRequest $url -OutFile $archive }
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $archiveHash) { throw 'C++ runtime archive checksum mismatch.' }
$signature = Get-AuthenticodeSignature $archive
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'C++ runtime signature is not valid Microsoft code.' }
# The offsets and payload name belong only to the exact hash checked above.
# Extract cabinet data, never execute the EXE/MSI or modify shared runtime registration.
$bytes = [IO.File]::ReadAllBytes($archive)
$container = Join-Path $stage 'payload.cab'
$output = [IO.File]::Create($container)
try { $output.Write($bytes,630000,18091661) } finally { $output.Dispose() }
$payload = Join-Path $stage 'payload'
$expanded = Join-Path $stage 'expanded'
New-Item -ItemType Directory $payload,$expanded -Force | Out-Null
& "$env:SystemRoot/System32/expand.exe" $container '-F:a4' $payload | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'C++ payload extraction failed.' }
$minimum = Join-Path $stage 'minimum.cab'
Copy-Item (Join-Path $payload 'a4') $minimum -Force
foreach ($name in $files.Keys) {
    & "$env:SystemRoot/System32/expand.exe" $minimum "-F:$($name)_amd64" $expanded | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "C++ file extraction failed: $name" }
    $source = Join-Path $expanded "$($name)_amd64"
    if ((Get-FileHash $source -Algorithm SHA256).Hash -ne $files[$name]) { throw "C++ file checksum mismatch: $name" }
    $signature = Get-AuthenticodeSignature $source
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw "C++ file signature invalid: $name" }
    Copy-Item $source (Join-Path $RendererDirectory $name) -Force
}
@{version='14.51.36247.0';source=$url;sha256=$archiveHash;files=$files;deployment='Application-local, renderer only';license='https://aka.ms/VisualStudio-Licensing'} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $RendererDirectory 'runtime-provenance.json') -Encoding utf8
Write-Output 'Installed verified Microsoft C++ 14.51.36247 runtime DLLs beside the renderer.'
