# Install and exercise the exact candidate CLI package. Does not publish.
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/..").Path
$packages = (Resolve-Path $PackageDirectory).Path
if (Test-Path $OutputDirectory) { throw 'Choose a new tool test directory.' }
New-Item -ItemType Directory $OutputDirectory | Out-Null
$output = (Resolve-Path $OutputDirectory).Path
$id = 'io7.FreePDF.Accessibility.Tool'
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $packages "$id.$Version.nupkg"))
try {
    foreach ($notice in @('LICENSE','PROVENANCE.md','README.md','icon.png','THIRD-PARTY/Matterhorn-NOTICE.txt','THIRD-PARTY/BigGustave/LICENSE','THIRD-PARTY/ICC-sRGB2014.txt','THIRD-PARTY/Adobe-Core14-AFM.txt','THIRD-PARTY/DotNet-MIT-LICENSE.txt',
        'THIRD-PARTY/Microsoft.Extensions.DependencyInjection.Abstractions/THIRD-PARTY-NOTICES.TXT','THIRD-PARTY/Microsoft.Extensions.Logging.Abstractions/THIRD-PARTY-NOTICES.TXT','THIRD-PARTY/System.Security.Cryptography.Pkcs/THIRD-PARTY-NOTICES.TXT')) {
        if (!$archive.GetEntry($notice)) { throw "Missing tool notice: $notice" }
    }
} finally { $archive.Dispose() }
& dotnet tool install $id --tool-path "$output/tool" --source $packages --version $Version --no-cache | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Candidate tool installation failed.' }
& dotnet build "$repo/eng/corpus/CorpusGenerator/CorpusGenerator.csproj" -c Release -m:1 | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Accessibility fixture generator build failed.' }
& dotnet "$repo/eng/corpus/CorpusGenerator/bin/Release/net10.0/CorpusGenerator.dll" --fixtures "$output/fixtures" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Accessibility fixture generation failed.' }
[IO.File]::WriteAllText("$output/fixtures/malformed.pdf", 'not a PDF')
$command = Join-Path "$output/tool" $(if ($IsWindows) { 'freepdf-a11y.exe' } else { 'freepdf-a11y' })
$inventory = & $command --requirements --format json | Out-String
if ($LASTEXITCODE -ne 0 -or ($inventory | ConvertFrom-Json).requirements.Count -ne 137) { throw 'Packaged requirements inventory is incomplete.' }
$inventory | Set-Content "$output/requirements.json" -Encoding utf8
$cases = @(
    @{name='valid'; exit=2; status='NeedsReview'; rule='FPDF-A11Y-REVIEW'},
    @{name='missing-alt'; exit=1; status='Failed'; rule='FPDF-A11Y-ALT'},
    @{name='missing-language'; exit=1; status='Failed'; rule='FPDF-A11Y-LANG'},
    @{name='untagged'; exit=1; status='Failed'; rule='FPDF-A11Y-TREE'},
    # A detected failure decides the exit code even when the scan is incomplete.
    @{name='incomplete-missing-language'; exit=1; status='Incomplete'; rule='FPDF-A11Y-LANG'},
    @{name='nested-form'; exit=2; status='NeedsReview'; rule='FPDF-A11Y-REVIEW'},
    @{name='reused-tagged-form'; exit=1; status='Failed'; rule='FPDF-A11Y-MCID'},
    @{name='untagged-form-paint'; exit=1; status='Failed'; rule='FPDF-A11Y-CONTENT'},
    @{name='malformed'; exit=3; status='Incomplete'; rule='FPDF-A11Y-READ'}
)
foreach ($case in $cases) {
    $pdf = "$output/fixtures/$($case.name).pdf"
    $before = (Get-FileHash $pdf -Algorithm SHA256).Hash
    $report = & $command $pdf --format json | Out-String
    if ($LASTEXITCODE -ne $case.exit) { throw "Wrong tool exit for $($case.name): $LASTEXITCODE" }
    $result = $report | ConvertFrom-Json
    if ($result.status -cne $case.status -or $result.coverage -cne 'Partial' -or $case.rule -cnotin $result.findings.ruleId) { throw "Wrong accessibility result for $($case.name)." }
    $report | Set-Content "$output/$($case.name).json" -Encoding utf8
    if ((Get-FileHash $pdf -Algorithm SHA256).Hash -cne $before) { throw 'Tool changed the input PDF.' }
}
$text = & $command "$output/fixtures/valid.pdf" | Out-String
if ($LASTEXITCODE -ne 2 -or $text -notmatch 'Human review required') { throw 'Default tool summary is incorrect.' }
$text | Set-Content "$output/summary.txt" -Encoding utf8
& $command "$output/fixtures/valid.pdf" --policy automated --format json | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Explicit automated policy failed for the positive control.' }
foreach ($format in @('html','sarif')) {
    & $command "$output/fixtures/missing-alt.pdf" --format $format --output "$output/report.$format" | Out-Host
    if ($LASTEXITCODE -ne 1 -or !(Test-Path "$output/report.$format")) { throw "Packaged tool did not write $format." }
}
$html = Get-Content "$output/report.html" -Raw
if ($html -match '<script' -or $html -notmatch 'Human accessibility review') { throw 'Unsafe or incomplete HTML report.' }
$sarif = Get-Content "$output/report.sarif" -Raw | ConvertFrom-Json
if ($sarif.version -cne '2.1.0' -or 'FPDF-A11Y-ALT' -cnotin $sarif.runs[0].results.ruleId) { throw 'Invalid SARIF report.' }
$receipt = @{ packageId=$id; framework='net10.0'; command='freepdf-a11y'; status='passed'; fixtures=$cases }
$receipt | ConvertTo-Json -Depth 5 | Set-Content "$output/validation.json" -Encoding utf8
Write-Host "PASS: installed accessibility tool, $($cases.Count) fixture outcomes, original-byte preservation and all report formats."
return $receipt
