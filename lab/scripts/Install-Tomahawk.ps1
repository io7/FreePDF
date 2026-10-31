# Run only after the operator approves the shared-host installation described in lab/README.md.
# Requires elevated PowerShell 7. Creates new paths/tasks only; never upgrades an existing installation.
param([string]$AppDirectory='D:\apps\FreePDFLab', [string]$DataDirectory='D:\data\FreePDFLab', [int]$Port=4817)
$ErrorActionPreference = 'Stop'
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Invalid port.' }
foreach ($path in @($AppDirectory,$DataDirectory)) { if (Test-Path $path) { throw "Path already exists: $path" } }
foreach ($name in @('FreePDFLab-Viewer','FreePDFLab-Nightly')) { if (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) { throw "Task already exists: $name" } }
if (Get-NetFirewallRule -Name 'FreePDFLab-LAN' -ErrorAction SilentlyContinue) { throw 'Firewall rule already exists.' }
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) { throw 'Port is in use.' }
$bundle = Get-Content "$PSScriptRoot/bundle.json" -Raw | ConvertFrom-Json
foreach ($entry in (Get-Content "$PSScriptRoot/checksums.json" -Raw | ConvertFrom-Json)) {
    $actual = (Get-FileHash (Join-Path $PSScriptRoot $entry.path) -Algorithm SHA256).Hash
    if ($actual -ne $entry.sha256) { throw "Bundle checksum mismatch: $($entry.path)" }
}
New-Item -ItemType Directory $AppDirectory,$DataDirectory | Out-Null
Copy-Item "$PSScriptRoot/*" $AppDirectory -Recurse
$tools = Join-Path $AppDirectory 'tools'
New-Item -ItemType Directory $tools | Out-Null
$archive = Join-Path $tools 'mupdf-1.28.5-windows.zip'
Invoke-WebRequest 'https://github.com/ArtifexSoftware/mupdf-downloads/releases/download/1.28.5/mupdf-1.28.5-windows.zip' -OutFile $archive
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne '13476a53e84f5527983158acb6442c7853b3cdc022f3b24880780dcfba1fcc99') { throw 'Renderer checksum mismatch.' }
Expand-Archive $archive $tools
$renderer = Join-Path $tools 'mupdf-1.28.5-windows/mutool.exe'
& "$PSScriptRoot/Install-RendererRuntime.ps1" -RendererDirectory (Split-Path $renderer)
& $renderer -v
if ($LASTEXITCODE -ne 0) { throw "Renderer startup failed with exit code $LASTEXITCODE." }
foreach ($framework in @('net10.0','net462')) {
    $profile = if ($framework -eq 'net462') { '.NET Framework 4.6.2 target' } else { '.NET 10' }
    @{
        dataDirectory=$DataDirectory; sourceRevision=$bundle.sourceRevision; profile=$profile
        reference=@{fileName="$AppDirectory/reference/$framework/Reference.Generator.exe";arguments=@()}
        candidate=@{fileName="$AppDirectory/candidate/$framework/FreePDF.Generator.exe";arguments=@()}
        renderer=$renderer; retainRuns=10; retainBytes=2000000000; timeoutSeconds=30
    } | ConvertTo-Json -Depth 5 | Set-Content "$AppDirectory/runner-$framework.json" -Encoding utf8
}
# LocalService can execute the immutable installation and write only the dedicated result directory.
& icacls $AppDirectory /grant '*S-1-5-19:(OI)(CI)RX' /T /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Application ACL failed.' }
& icacls $DataDirectory /grant '*S-1-5-19:(OI)(CI)M' /T /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Data ACL failed.' }
$principal = New-ScheduledTaskPrincipal -UserId 'NT AUTHORITY\LOCAL SERVICE' -LogonType ServiceAccount -RunLevel Limited
$viewerAction = New-ScheduledTaskAction -Execute "$AppDirectory/viewer/FreePDF.Lab.exe" -Argument "--contentRoot `"$AppDirectory/viewer`" --urls http://0.0.0.0:$Port --Lab:DataDirectory `"$DataDirectory`"" -WorkingDirectory "$AppDirectory/viewer"
$viewerSettings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew
$viewerTriggers = @(
    (New-ScheduledTaskTrigger -AtStartup),
    (New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1))
)
Register-ScheduledTask -TaskName 'FreePDFLab-Viewer' -Action $viewerAction -Trigger $viewerTriggers -Principal $principal -Settings $viewerSettings | Out-Null
$nightlyAction = New-ScheduledTaskAction -Execute "$PSHOME/pwsh.exe" -Argument "-NoProfile -File `"$AppDirectory/Run-Nightly.ps1`"" -WorkingDirectory $AppDirectory
$nightlySettings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -StartWhenAvailable -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName 'FreePDFLab-Nightly' -Action $nightlyAction -Trigger (New-ScheduledTaskTrigger -Daily -At '03:17') -Principal $principal -Settings $nightlySettings | Out-Null
New-NetFirewallRule -Name 'FreePDFLab-LAN' -DisplayName 'FreePDF Lab LAN viewer' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -RemoteAddress '192.168.1.0/24' -Profile Any | Out-Null
Start-ScheduledTask -TaskName 'FreePDFLab-Viewer'
Start-ScheduledTask -TaskName 'FreePDFLab-Nightly'
Write-Output "Installed. Verify http://localhost:$Port/health and the first two recorded runs."
