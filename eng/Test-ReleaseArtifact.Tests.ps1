param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$ExpectedRevision,
    [Parameter(Mandatory)][string]$Version,
    [string[]]$RequiredFrameworks = @('net8.0','net9.0','net10.0')
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path $PackageDirectory).Path
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("freepdf-release-guards-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $scratch | Out-Null
function ExpectRejected([string]$Name, [scriptblock]$Change, [string]$Message) {
    $directory = Join-Path $scratch $Name
    New-Item -ItemType Directory $directory | Out-Null
    Copy-Item "$source/*.nupkg", "$source/validation.json" $directory
    & $Change $directory
    $failure = $null
    try { & "$PSScriptRoot/Test-ReleaseArtifact.ps1" -PackageDirectory $directory -ExpectedRevision $ExpectedRevision -Version $Version -RequiredFrameworks $RequiredFrameworks | Out-Null }
    catch { $failure = $_.Exception.Message }
    if (!$failure -or $failure -notlike "$Message*") { throw "Expected rejection for $Name; got '$failure'." }
    Write-Output "PASS: rejected $Name"
}
try {
    & "$PSScriptRoot/Test-ReleaseArtifact.ps1" -PackageDirectory $source -ExpectedRevision $ExpectedRevision -Version $Version -RequiredFrameworks $RequiredFrameworks
    ExpectRejected 'wrong-source' {
        param($directory)
        $receipt = Get-Content "$directory/validation.json" -Raw | ConvertFrom-Json
        $receipt.sourceRevision = '0' * 40
        $receipt | ConvertTo-Json -Depth 10 | Set-Content "$directory/validation.json"
    } 'Release source revision mismatch.'
    ExpectRejected 'changed-package-bytes' {
        param($directory)
        $path = Join-Path $directory "io7.FreePDF.$Version.nupkg"
        $file = [IO.File]::Open($path, [IO.FileMode]::Append)
        try { $file.WriteByte(0) } finally { $file.Dispose() }
    } 'Release package hash mismatch:'
    ExpectRejected 'failed-consumer' {
        param($directory)
        $receipt = Get-Content "$directory/validation.json" -Raw | ConvertFrom-Json
        ($receipt.consumers | Where-Object framework -EQ $RequiredFrameworks[0]).status = 'failed'
        $receipt | ConvertTo-Json -Depth 10 | Set-Content "$directory/validation.json"
    } 'Missing successful consumer:'
    ExpectRejected 'upstream-package' {
        param($directory)
        Copy-Item "$directory/io7.FreePDF.$Version.nupkg" "$directory/PDFsharp.$Version.nupkg"
    } 'Unexpected release package files.'
    ExpectRejected 'wrong-nuspec-with-valid-hash' {
        param($directory)
        $path = Join-Path $directory "io7.FreePDF.$Version.nupkg"
        $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Update)
        try {
            $entry = $archive.GetEntry('io7.FreePDF.nuspec')
            $reader = [IO.StreamReader]::new($entry.Open())
            try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $entry.Delete()
            $writer = [IO.StreamWriter]::new($archive.CreateEntry('io7.FreePDF.nuspec').Open())
            try { $writer.Write($text.Replace("<version>$Version</version>", "<version>$Version-test</version>")) } finally { $writer.Dispose() }
        } finally { $archive.Dispose() }
        $receipt = Get-Content "$directory/validation.json" -Raw | ConvertFrom-Json
        ($receipt.packages | Where-Object file -EQ "io7.FreePDF.$Version.nupkg").sha256 = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $receipt | ConvertTo-Json -Depth 10 | Set-Content "$directory/validation.json"
    } 'Release package metadata mismatch:'
} finally { Remove-Item -LiteralPath $scratch -Recurse -Force }
