<#
.SYNOPSIS
Compiles public PDFsharp 6.2.4 consumers twice, against their own PDFsharp packages and against FreePDF, and reports
the compile errors that appear only with FreePDF.

.DESCRIPTION
Each consumer in the manifest is sparse-cloned at its pinned commit. The consumer's own project files are never built
or imported: a separate project, generated outside the clone, compiles the consumer's C# sources (and those of the
projects it references) against nothing but the PDF library under comparison and Microsoft's shared frameworks.
The consumer's other NuGet packages are not restored, so their types are unresolved in both builds. Errors present in
both builds (those unresolved types, XAML, generated code) are ignored. Errors that appear only in the FreePDF build
are migration breaks.

Consumer repositories are untrusted input and are only read and compiled, never executed: no consumer MSBuild file is
evaluated, no consumer package is restored, and analyzers and source generators are switched off. The only packages
restored are PDFsharp/MigraDoc as the consumer declares them and io7.FreePDF.

Limitation: an expression whose type comes from an unresolved package is not checked, so a break hidden behind such
an expression is not counted.

.EXAMPLE
./eng/research/Survey-Consumers.ps1 -Manifest docs/research/consumer-survey/repos.json -WorkDirectory /tmp/survey
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Manifest,
    [Parameter(Mandatory)] [string] $WorkDirectory,
    [string] $CandidateVersion = '7.0.1',
    # Folder with locally packed io7.FreePDF*.nupkg files; nuget.org is used when omitted.
    [string] $CandidateSource,
    [string[]] $Only,
    [switch] $IncludeWindowsVariants
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$NuGetOrg = 'https://api.nuget.org/v3/index.json'
$PdfPackage = '^(PDFsharp|PdfSharp|MigraDoc)([-.].*)?$'
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
New-Item -ItemType Directory -Force -Path $WorkDirectory | Out-Null

function Invoke-Git([string] $Repo, [string[]] $Arguments) {
    $output = & git -C $Repo @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed in ${Repo}: $output" }
    return $output
}

function ConvertTo-RepoPath([string] $Root, [string] $Path) {
    $relative = [IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
    if ($relative -eq '.') { return '' }
    return $relative
}

function Read-Xml([string] $Path) {
    $doc = [xml]::new()
    $doc.PreserveWhitespace = $false
    $doc.Load($Path)
    return $doc
}

function Get-Items([xml] $Doc, [string] $Name) {
    return @($Doc.SelectNodes("//*[local-name()='$Name']"))
}

function Get-ProjectReferences([string] $ProjectPath) {
    $doc = Read-Xml $ProjectPath
    $dir = Split-Path $ProjectPath -Parent
    foreach ($item in (Get-Items $doc 'ProjectReference')) {
        $include = $item.GetAttribute('Include')
        if (!$include -or $include.Contains('$(')) { continue }
        [IO.Path]::GetFullPath((Join-Path $dir $include.Replace('\', '/')))
    }
}

# Sparse-clone the consumer at its commit, widening the checkout until every referenced project is present.
function Get-Consumer($Entry) {
    $repo = Join-Path $WorkDirectory 'src' $Entry.name
    if (!(Test-Path (Join-Path $repo '.git'))) {
        New-Item -ItemType Directory -Force -Path (Split-Path $repo -Parent) | Out-Null
        # A manifest entry may give its own clone URL (used by the harness self-test fixture).
        $url = if ($Entry.PSObject.Properties['url']) { $Entry.url } else { "https://github.com/$($Entry.repo).git" }
        & git clone --quiet --filter=blob:none --no-checkout $url $repo 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Clone failed: $($Entry.repo)" }
    }
    $main = [IO.Path]::GetFullPath((Join-Path $repo $Entry.project))
    $projects = [Collections.Generic.List[string]]::new()
    $projects.Add($main)
    $checkedOut = $false
    while ($true) {
        $dirs = @($projects | ForEach-Object { ConvertTo-RepoPath $repo (Split-Path $_ -Parent) } | Sort-Object -Unique)
        if ($dirs -contains '') {
            Invoke-Git $repo @('sparse-checkout', 'disable') | Out-Null
        } else {
            Invoke-Git $repo (@('sparse-checkout', 'set', '--cone') + $dirs) | Out-Null
        }
        if (!$checkedOut) { Invoke-Git $repo @('checkout', '--quiet', '--detach', $Entry.commit) | Out-Null; $checkedOut = $true }
        $added = $false
        foreach ($project in @($projects)) {
            if (!(Test-Path $project)) { throw "Project missing after checkout: $(ConvertTo-RepoPath $repo $project)" }
            foreach ($reference in (Get-ProjectReferences $project)) {
                if (!$projects.Contains($reference)) { $projects.Add($reference); $added = $true }
            }
        }
        if (!$added) { break }
    }
    return [pscustomobject]@{ Root = $repo; Main = $main; Projects = @($projects | Where-Object { Test-Path $_ }) }
}

# Properties from Directory.Build.props files above the project (outermost first), then the project itself.
function Get-Properties([string] $Root, [string] $ProjectPath) {
    $files = [Collections.Generic.List[string]]::new()
    $dir = Split-Path $ProjectPath -Parent
    while ($dir -and $dir.StartsWith($Root)) {
        $props = Join-Path $dir 'Directory.Build.props'
        if (Test-Path $props) { $files.Insert(0, $props) }
        if ($dir -eq $Root) { break }
        $dir = Split-Path $dir -Parent
    }
    $files.Add($ProjectPath)
    $values = @{}
    foreach ($file in $files) {
        $doc = Read-Xml $file
        foreach ($group in (Get-Items $doc 'PropertyGroup')) {
            foreach ($node in $group.ChildNodes) {
                if ($node.NodeType -ne 'Element') { continue }
                $text = $node.InnerText.Trim()
                $values[$node.LocalName] = [regex]::Replace($text, '\$\((\w+)\)', {
                    param($m) if ($values.ContainsKey($m.Groups[1].Value)) { $values[$m.Groups[1].Value] } else { '' } })
            }
        }
    }
    return $values
}

function Get-CentralVersions([string] $Root, [string] $ProjectPath) {
    $versions = @{}
    $dir = Split-Path $ProjectPath -Parent
    $chain = [Collections.Generic.List[string]]::new()
    while ($dir -and $dir.StartsWith($Root)) {
        $file = Join-Path $dir 'Directory.Packages.props'
        if (Test-Path $file) { $chain.Insert(0, $file) }
        if ($dir -eq $Root) { break }
        $dir = Split-Path $dir -Parent
    }
    foreach ($file in $chain) {
        foreach ($item in (Get-Items (Read-Xml $file) 'PackageVersion')) {
            $id = $item.GetAttribute('Include')
            if ($id) { $versions[$id.ToLowerInvariant()] = $item.GetAttribute('Version') }
        }
    }
    return $versions
}

function Get-Packages($Consumer) {
    $packages = [ordered]@{}
    $frameworkReferences = [Collections.Generic.HashSet[string]]::new()
    foreach ($project in $Consumer.Projects) {
        $central = Get-CentralVersions $Consumer.Root $project
        $props = Get-Properties $Consumer.Root $project
        $doc = Read-Xml $project
        foreach ($item in (Get-Items $doc 'PackageReference')) {
            $id = $item.GetAttribute('Include')
            if (!$id) { continue }
            $version = $item.GetAttribute('Version')
            if (!$version) { $version = $item.GetAttribute('VersionOverride') }
            if (!$version) { $child = $item.SelectSingleNode("*[local-name()='Version']"); if ($child) { $version = $child.InnerText } }
            if (!$version) { $version = $central[$id.ToLowerInvariant()] }
            if ($version -and $version.Contains('$(')) {
                $version = [regex]::Replace($version, '\$\((\w+)\)', { param($m) $props[$m.Groups[1].Value] })
            }
            if (!$version) { $version = '*' }
            $packages[$id.ToLowerInvariant()] = [pscustomobject]@{ Id = $id; Version = $version.Trim() }
        }
        foreach ($item in (Get-Items $doc 'FrameworkReference')) {
            $name = $item.GetAttribute('Include'); if ($name) { $frameworkReferences.Add($name) | Out-Null }
        }
    }
    return [pscustomobject]@{ Packages = @($packages.Values); FrameworkReferences = @($frameworkReferences) }
}

function Select-TargetFramework([string] $Value) {
    $all = @($Value -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $modern = @($all | Where-Object { $_ -match '^net(\d+)\.(\d+)' } | Sort-Object { [version]($_ -replace '^net(\d+\.\d+).*', '$1') } -Descending)
    if ($modern) { return $modern[0] }
    foreach ($pattern in @('^netstandard2\.1$', '^netstandard2\.0$', '^netcoreapp', '^net4')) {
        $match = @($all | Where-Object { $_ -match $pattern } | Sort-Object -Descending)
        if ($match) { return $match[0] }
    }
    return 'net8.0'
}

function Get-Sources($Consumer) {
    $files = [Collections.Generic.List[string]]::new()
    $projectDirs = @($Consumer.Projects | ForEach-Object { Split-Path $_ -Parent })
    foreach ($dir in ($projectDirs | Sort-Object -Unique)) {
        # Nested folders that hold another project belong to that project, not this one.
        $nested = @(Get-ChildItem $dir -Recurse -Filter *.csproj -File | ForEach-Object { $_.DirectoryName } |
            Where-Object { $_ -ne $dir -and $projectDirs -notcontains $_ })
        foreach ($file in (Get-ChildItem $dir -Recurse -Filter *.cs -File)) {
            $path = $file.FullName
            if ($path -match '[\\/](bin|obj)[\\/]') { continue }
            if (@($nested | Where-Object { $path.StartsWith($_ + [IO.Path]::DirectorySeparatorChar) }).Count) { continue }
            $files.Add($path)
        }
    }
    return @($files | Sort-Object -Unique)
}

function Write-SurveyProject($Path, $Consumer, $Props, $Tfm, $Packages, $FrameworkReferences, $Sources) {
    $sdk = 'Microsoft.NET.Sdk'
    $mainDoc = Read-Xml $Consumer.Main
    $declared = $mainDoc.DocumentElement.GetAttribute('Sdk')
    if ($declared -match '^Microsoft\.NET\.Sdk\.(Web|Razor|Worker)$') { $sdk = $declared }
    $esc = { param($s) [Security.SecurityElement]::Escape([string]$s) }
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add("<Project Sdk=`"$sdk`">")
    $lines.Add('  <PropertyGroup>')
    $lines.Add("    <TargetFramework>$(& $esc $Tfm)</TargetFramework>")
    $lines.Add('    <OutputType>Library</OutputType>')
    $lines.Add('    <EnableDefaultItems>false</EnableDefaultItems>')
    $lines.Add("    <LangVersion>$(if ($Props['LangVersion']) { & $esc $Props['LangVersion'] } else { 'latest' })</LangVersion>")
    $lines.Add("    <Nullable>$(if ($Props['Nullable']) { & $esc $Props['Nullable'] } else { 'disable' })</Nullable>")
    $lines.Add("    <ImplicitUsings>$(if ($Props['ImplicitUsings']) { & $esc $Props['ImplicitUsings'] } else { 'disable' })</ImplicitUsings>")
    $lines.Add('    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>')
    $lines.Add('    <EnableWindowsTargeting>true</EnableWindowsTargeting>')
    if ($Props['UseWindowsForms'] -eq 'true') { $lines.Add('    <UseWindowsForms>true</UseWindowsForms>') }
    if ($Props['UseWPF'] -eq 'true') { $lines.Add('    <UseWPF>true</UseWPF>') }
    if ($Props['DefineConstants']) { $lines.Add("    <DefineConstants>`$(DefineConstants);$(& $esc $Props['DefineConstants'])</DefineConstants>") }
    $lines.Add('    <RunAnalyzers>false</RunAnalyzers>')
    $lines.Add('    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>')
    $lines.Add('    <WarningLevel>0</WarningLevel>')
    $lines.Add('    <NuGetAudit>false</NuGetAudit>')
    $lines.Add('    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>')
    $lines.Add('    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>')
    $lines.Add('    <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>')
    $lines.Add('    <SignAssembly>false</SignAssembly>')
    $lines.Add('    <NoWarn>$(NoWarn);NU1603;NU1605;NU1608;NU1701;NU1702;NETSDK1138</NoWarn>')
    $lines.Add('  </PropertyGroup>')
    $lines.Add('  <ItemGroup>')
    foreach ($source in $Sources) { $lines.Add("    <Compile Include=`"$(& $esc $source)`" />") }
    foreach ($package in $Packages) { $lines.Add("    <PackageReference Include=`"$(& $esc $package.Id)`" Version=`"$(& $esc $package.Version)`" />") }
    foreach ($name in $FrameworkReferences) { $lines.Add("    <FrameworkReference Include=`"$(& $esc $name)`" />") }
    $lines.Add('  </ItemGroup>')
    $lines.Add('</Project>')
    New-Item -ItemType Directory -Force -Path (Split-Path $Path -Parent) | Out-Null
    Set-Content -Path $Path -Value $lines -Encoding utf8
}

# Restores the generated project (PDF package only) and asks MSBuild for the resolved reference assemblies, implicit
# usings and preprocessor symbols. Nothing is compiled by MSBuild.
function Get-References([string] $Project, [string[]] $Feeds, [string] $Log) {
    $arguments = @('msbuild', $Project, '-restore', '-nologo', '-v:q', '-t:ResolveReferences',
        "-p:RestoreSources=$($Feeds -join ';')", '-getItem:ReferencePath', '-getItem:Using',
        '-getProperty:DefineConstants', '-getProperty:Nullable')
    $output = & dotnet @arguments 2>&1
    Set-Content -Path $Log -Value $output -Encoding utf8
    if ($LASTEXITCODE -ne 0) { return $null }
    $json = ($output -join "`n")
    $start = $json.IndexOf('{')
    if ($start -lt 0) { return $null }
    return $json.Substring($start) | ConvertFrom-Json
}

$Checker = Join-Path $PSScriptRoot 'ConsumerCompile/bin/Release/net10.0/ConsumerCompile.dll'

# Compiles with Roslyn's GetDiagnostics(), which reports method-body errors even when declarations fail to bind.
function Invoke-Check($Resolved, [string[]] $Sources, [string] $Root, [string] $Directory) {
    $usings = @()
    if ($Resolved.Items.PSObject.Properties['Using']) {
        $usings = @($Resolved.Items.Using | Where-Object { !$_.PSObject.Properties['Static'] -or $_.Static -ne 'true' } |
            Where-Object { !$_.PSObject.Properties['Alias'] -or !$_.Alias } | ForEach-Object { $_.Identity })
    }
    $request = [ordered]@{
        root = $Root
        sources = $Sources
        references = @($Resolved.Items.ReferencePath | ForEach-Object { $_.Identity })
        defines = @($Resolved.Properties.DefineConstants -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        globalUsings = $usings
        nullable = $Resolved.Properties.Nullable
    }
    $requestPath = Join-Path $Directory 'request.json'
    $errorsPath = Join-Path $Directory 'errors.json'
    Set-Content -Path $requestPath -Value ($request | ConvertTo-Json -Depth 4) -Encoding utf8
    $output = & dotnet $Checker $requestPath $errorsPath 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Checker failed: $output" }
    return @(Get-Content $errorsPath -Raw | ConvertFrom-Json)
}

$entries = (Get-Content $Manifest -Raw | ConvertFrom-Json).entries
# pwsh -File passes "a,b" as one string, so accept comma-separated names as well.
if ($Only) {
    $names = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $entries = @($entries | Where-Object { $names -contains $_.name })
}
$resultsDir = Join-Path $WorkDirectory 'results'
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

foreach ($entry in $entries) {
    Write-Host "== $($entry.repo) @ $($entry.commit.Substring(0, 10))"
    $result = [ordered]@{ name = $entry.name; repo = $entry.repo; commit = $entry.commit; project = $entry.project }
    try {
        $consumer = Get-Consumer $entry
        $props = Get-Properties $consumer.Root $consumer.Main
        $tfmValue = if ($props['TargetFrameworks']) { $props['TargetFrameworks'] } else { $props['TargetFramework'] }
        $tfm = Select-TargetFramework $tfmValue
        $collected = Get-Packages $consumer
        $pdf = @($collected.Packages | Where-Object { $_.Id -match $PdfPackage })
        $result.otherPackagesNotRestored = @($collected.Packages | Where-Object { $_.Id -notmatch $PdfPackage }).Count
        $windowsVariant = @($pdf | Where-Object { $_.Id -match '-(gdi|wpf)$' }).Count -gt 0
        $result.tfm = $tfm
        $result.pdfPackages = @($pdf | ForEach-Object { "$($_.Id) $($_.Version)" })
        $result.projects = @($consumer.Projects | ForEach-Object { ConvertTo-RepoPath $consumer.Root $_ })
        if (!$pdf) { throw 'No PDFsharp package found in the project closure.' }
        if ($windowsVariant -and !$IncludeWindowsVariants) {
            $result.outcome = 'skipped'; $result.reason = 'GDI/WPF variant; no FreePDF package yet'
            continue
        }
        $usesLayout = @($pdf | Where-Object { $_.Id -match 'MigraDoc' }).Count -gt 0
        $variant = if ($windowsVariant) { (@($pdf | Where-Object { $_.Id -match '-(gdi|wpf)$' })[0].Id -replace '.*-', '') } else { '' }
        $candidateId = if ($usesLayout) { 'io7.FreePDF.Layout' } else { 'io7.FreePDF' }
        if ($variant) { $candidateId += '.' + (Get-Culture).TextInfo.ToTitleCase($variant.ToLowerInvariant()) }
        $sources = @(Get-Sources $consumer)
        $result.sourceFiles = $sources.Count
        $runs = @{}
        foreach ($side in @('baseline', 'candidate')) {
            $dir = Join-Path $WorkDirectory 'build' $entry.name $side
            if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
            # Only the PDF library under comparison is restored; the consumer's other packages stay unresolved.
            $packages = if ($side -eq 'baseline') { @($pdf) } else { @([pscustomobject]@{ Id = $candidateId; Version = $CandidateVersion }) }
            $frameworks = @($collected.FrameworkReferences | Where-Object { $_ -like 'Microsoft.*' })
            $project = Join-Path $dir 'Survey.csproj'
            Write-SurveyProject $project $consumer $props $tfm $packages $frameworks $sources
            $feeds = @($NuGetOrg)
            if ($side -eq 'candidate' -and $CandidateSource) { $feeds += [IO.Path]::GetFullPath($CandidateSource) }
            $resolved = Get-References $project $feeds (Join-Path $dir 'resolve.log')
            if (!$resolved) { throw "Reference resolution failed for the $side build; see $(Join-Path $dir 'resolve.log')" }
            $runs[$side] = [pscustomobject]@{ Errors = @(Invoke-Check $resolved $sources $consumer.Root $dir) }
        }
        $baselineKeys = [Collections.Generic.HashSet[string]]::new()
        foreach ($e in $runs.baseline.Errors) { $baselineKeys.Add("$($e.File)|$($e.Line)|$($e.Code)") | Out-Null }
        $new = @($runs.candidate.Errors | Where-Object { !$baselineKeys.Contains("$($_.File)|$($_.Line)|$($_.Code)") })
        $result.baselineErrors = $runs.baseline.Errors.Count
        $result.candidateErrors = $runs.candidate.Errors.Count
        $result.newErrors = $new
        $result.outcome = if ($new.Count -eq 0) { 'compiles-unchanged' } else { 'new-errors' }
    } catch {
        $result.outcome = 'harness-error'
        $result.reason = $_.Exception.Message
    } finally {
        $json = $result | ConvertTo-Json -Depth 6
        Set-Content -Path (Join-Path $resultsDir "$($entry.name).json") -Value $json -Encoding utf8
        $count = if ($result.Contains('newErrors')) { @($result.newErrors).Count } else { '-' }
        Write-Host ("   {0}; baseline errors {1}; new errors {2}{3}" -f $result.outcome,
            $(if ($result.Contains('baselineErrors')) { $result.baselineErrors } else { '-' }), $count,
            $(if ($result.Contains('reason')) { "; $($result.reason)" } else { '' }))
    }
}
