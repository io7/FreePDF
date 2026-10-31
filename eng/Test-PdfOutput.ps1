# Independent, bounded validation of original package-consumer or lab PDFs.
# Validators are external tools, never FreePDF package dependencies.
[CmdletBinding(DefaultParameterSetName='Packages')]
param(
    [Parameter(Mandatory,ParameterSetName='Packages')][string]$PackageDirectory,
    [Parameter(Mandatory,ParameterSetName='Lab')][string]$LabDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Qpdf = 'qpdf',
    [string]$VeraPdf = 'verapdf'
)
$ErrorActionPreference = 'Stop'
if (Test-Path $OutputDirectory) { throw 'Choose a new output directory for independent validation.' }
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null
$output = (Resolve-Path $OutputDirectory).Path
$checks = [Collections.Generic.List[object]]::new()
$results = [Collections.Generic.List[object]]::new()
function Invoke-Validator([string]$Tool, [string[]]$Arguments, [int]$ExpectedExit = 0) {
    $start = [Diagnostics.ProcessStartInfo]::new($Tool)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.Environment['LC_ALL'] = 'C.UTF-8'
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(60000)) { $process.Kill($true); $process.WaitForExit(); throw "$Tool exceeded 60 seconds." }
        $record = @{ tool=$Tool; arguments=$Arguments; exitCode=$process.ExitCode; stdout=$stdout.Result; stderr=$stderr.Result }
        $checks.Add($record)
        if ($process.ExitCode -ne $ExpectedExit) { throw "$Tool exited $($process.ExitCode): $($record.stderr)" }
        return $record
    } finally { $process.Dispose() }
}
function Require([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
$documents = @()
if ($PSCmdlet.ParameterSetName -eq 'Packages') {
    foreach ($file in Get-ChildItem $PackageDirectory -Filter consumer-results.xml -Recurse) {
        [xml]$manifest = Get-Content $file.FullName -Raw
        foreach ($document in $manifest.releaseChecks.document) {
            $documents += @{ id="$($file.Directory.Name)/$($document.file)"; path=(Join-Path $file.DirectoryName $document.file); pages=[int]$document.pages;
                profile=[string]$document.pdfa; password=[string]$document.password; digest=[string]$document.signature;
                text=@($document.text); attachments=@($document.attachment) }
        }
    }
} else {
    $reports = @(Get-ChildItem $LabDirectory -Filter report.json -Recurse)
    Require ($reports.Count -gt 0) 'No lab reports found.'
    foreach ($file in $reports) {
        $report = Get-Content $file.FullName -Raw | ConvertFrom-Json
        foreach ($case in $report.cases) {
            Require (!$case.candidateError -and !$case.structuralErrors.Count) "Candidate failed: $($case.id)"
            $documents += @{ id="$($report.id)/$($case.id)"; path=(Join-Path $file.DirectoryName "$($case.id)/candidate/document.pdf");
                pages=@($case.pages).Count; profile=$(if ($case.id -eq 'pdfa') {'1b'} else {''}); password=''; digest=''; text=@(); attachments=@() }
        }
    }
}
Require ($documents.Count -gt 0) 'No generated PDF fixtures found.'
$failure = $null
try {
    $versions = @{
        qpdf=(Invoke-Validator $Qpdf @('--version')).stdout.Trim()
        poppler=(Invoke-Validator 'pdftoppm' @('-v')).stderr.Trim()
        veraPDF=(Invoke-Validator $VeraPdf @('--version')).stdout.Trim()
    }
    foreach ($document in $documents) {
        $path = $document.path
        $directory = Join-Path $output ($document.id -replace '[^a-zA-Z0-9._-]', '_')
        New-Item -ItemType Directory $directory -Force | Out-Null
        [string[]]$qPassword = if ($document.password) { @("--password=$($document.password)") } else { @() }
        [string[]]$pPassword = if ($document.password) { @('-upw', $document.password) } else { @() }
        $null = Invoke-Validator $Qpdf ($qPassword + @('--check', $path))
        $pageCount = [int](Invoke-Validator $Qpdf ($qPassword + @('--show-npages', $path))).stdout.Trim()
        Require ($pageCount -eq $document.pages) "Page count changed: $($document.id)"
        $raster = Invoke-Validator 'pdftoppm' ($pPassword + @('-r','72','-png',$path,(Join-Path $directory 'page')))
        Require (!$raster.stderr.Trim()) "Poppler rendering warning: $($raster.stderr)"
        Require (@(Get-ChildItem $directory -Filter 'page-*.png').Count -eq $pageCount) "Poppler missed pages: $($document.id)"
        $text = Invoke-Validator 'pdftotext' ($pPassword + @('-enc','UTF-8',$path,'-'))
        Require (!$text.stderr.Trim()) "Poppler text warning: $($text.stderr)"
        foreach ($expected in $document.text) { Require ($text.stdout.Contains([string]$expected)) "Missing extracted text '$expected': $($document.id)" }
        foreach ($attachment in $document.attachments) {
            if (!$attachment) { continue }
            $data = Invoke-Validator $Qpdf @("--show-attachment=$($attachment.name)", $path)
            Require ($data.stdout -ceq $attachment.InnerText) "Attachment changed: $($document.id)"
        }
        if ($document.password) {
            $rejected = Invoke-Validator $Qpdf @('--password=incorrect-fixture-password','--check',$path) 2
            Require ($rejected.stderr -match 'invalid password') "Unexpected password rejection: $($document.id)"
            $encryption = (Invoke-Validator $Qpdf ($qPassword + @('--show-encryption',$path))).stdout
            $algorithm = if ($document.id -match 'aes256') { 'AESv3' } else { 'AESv2' }
            Require ($encryption.Contains("stream encryption method: $algorithm") -and $encryption.Contains("string encryption method: $algorithm")) "Incorrect encryption: $($document.id)"
        }
        if ($document.profile) {
            $validation = Invoke-Validator $VeraPdf @('--format','json','--flavour',$document.profile,$path)
            $validation.stdout | Set-Content (Join-Path $directory 'verapdf.json') -Encoding utf8
            $jobs = @(($validation.stdout | ConvertFrom-Json).report.jobs)
            Require ($jobs.Count -eq 1 -and @($jobs[0].validationResult).Count -eq 1) 'Unexpected veraPDF result count.'
            Require ($jobs[0].validationResult[0].compliant -eq $true) "veraPDF rejected $($document.id)"
        }
        if ($document.digest) {
            $signature = (Invoke-Validator 'pdfsig' @('-nocert','-no-ocsp',$path)).stdout
            Require ($signature.Contains('Signature Validation: Signature is Valid.') -and $signature.Contains('Total document signed')) "Signature failed: $($document.id)"
            $expectedDigest = $document.digest -replace '^SHA','SHA-'
            Require ($signature.Contains("Signing Hash Algorithm: $expectedDigest")) "Incorrect signature digest: $($document.id)"
            $negative = Join-Path (Split-Path $path) ("negative/" + [IO.Path]::GetFileNameWithoutExtension($path) + '-tampered.pdf')
            $null = Invoke-Validator $Qpdf @('--check',$negative)
            $tampered = (Invoke-Validator 'pdfsig' @('-nocert','-no-ocsp',$negative)).stdout
            Require ($tampered.Contains('Signature Validation: Signature is Invalid.')) "Tamper control failed: $($document.id)"
        }
        $results.Add(@{document=$document.id; sha256=(Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant(); pages=$pageCount; pdfa=$document.profile; signature=$document.digest; status='passed'})
        Write-Host "PASS: $($document.id)"
    }
} catch { $failure = $_.Exception.Message; throw }
finally {
    @{ tools=$versions; documents=$results.ToArray(); failure=$failure; checks=$checks.ToArray() } | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $output 'independent-validation.json') -Encoding utf8
}
