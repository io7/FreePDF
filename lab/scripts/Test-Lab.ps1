param([ValidateSet('net10.0','net462')][string]$Framework='net10.0', [Parameter(Mandatory)][string]$Renderer, [Parameter(Mandatory)][string]$DataDirectory)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
foreach ($engine in @('Reference','FreePDF')) {
    & dotnet build "$repo/lab/Generators/$engine/$engine.Generator.csproj" -c Release -f $Framework -m:1
    if ($LASTEXITCODE -ne 0) { throw "Generator build failed: $engine" }
}
& dotnet build "$repo/lab/FreePDF.Lab" -c Release -m:1
if ($LASTEXITCODE -ne 0) { throw 'Lab build failed.' }
$app = "$repo/lab/FreePDF.Lab/bin/Release/net10.0/FreePDF.Lab.dll"
& dotnet $app --self-test
if ($LASTEXITCODE -ne 0) { throw 'Lab self-test failed.' }
function Generator([string]$Engine) {
    $base = "$repo/lab/Generators/$Engine/bin/Release/$Framework/$Engine.Generator"
    if ($Framework -eq 'net462') { return @{fileName="$base.exe";arguments=@()} }
    return @{fileName=(Get-Command dotnet).Source;arguments=@("$base.dll")}
}
New-Item -ItemType Directory $DataDirectory -Force | Out-Null
$configuration = Join-Path $DataDirectory 'runner.json'
$revision = (& git -C $repo rev-parse HEAD).Trim()
if (& git -C $repo status --porcelain) { $revision += '+worktree' }
@{ dataDirectory=(Resolve-Path $DataDirectory).Path; reference=(Generator 'Reference'); candidate=(Generator 'FreePDF'); renderer=(Resolve-Path $Renderer).Path; sourceRevision=$revision; profile=$Framework; retainRuns=10; retainBytes=2000000000; timeoutSeconds=30 } | ConvertTo-Json -Depth 5 | Set-Content $configuration -Encoding utf8
& dotnet $app --run $configuration
exit $LASTEXITCODE
