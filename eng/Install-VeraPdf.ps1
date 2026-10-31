# Test-only tool installation. Pin the archive; never execute unverified downloads.
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null
$output = (Resolve-Path $OutputDirectory).Path
$archive = Join-Path $output 'verapdf-installer.zip'
Invoke-WebRequest 'https://software.verapdf.org/releases/1.30/verapdf-greenfield-1.30.3-installer.zip' -OutFile $archive
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne 'b512bf24945503a33fc10738ecffc1f2dccaaf8593a448e94ee9ac86a9559a59') {
    throw 'veraPDF 1.30.3 archive hash mismatch. Review the official signature before updating the pin.'
}
Expand-Archive $archive (Join-Path $output 'installer') -Force
$target = [Security.SecurityElement]::Escape((Join-Path $output 'cli'))
@"
<AutomatedInstallation langpack="eng">
  <com.izforge.izpack.panels.htmlhello.HTMLHelloPanel id="welcome"/>
  <com.izforge.izpack.panels.target.TargetPanel id="install_dir"><installpath>$target</installpath></com.izforge.izpack.panels.target.TargetPanel>
  <com.izforge.izpack.panels.packs.PacksPanel id="sdk_pack_select">
    <pack index="0" name="veraPDF GUI" selected="false"/>
    <pack index="1" name="veraPDF CLI" selected="true"/>
    <pack index="2" name="veraPDF Documentation" selected="true"/>
    <pack index="3" name="veraPDF Sample Plugins" selected="false"/>
  </com.izforge.izpack.panels.packs.PacksPanel>
  <com.izforge.izpack.panels.install.InstallPanel id="install"/>
  <com.izforge.izpack.panels.finish.FinishPanel id="finish"/>
</AutomatedInstallation>
"@ | Set-Content (Join-Path $output 'install.xml') -Encoding utf8
& java -jar (Join-Path $output 'installer/verapdf-greenfield-1.30.3/verapdf-izpack-installer-1.30.3.jar') (Join-Path $output 'install.xml')
if ($LASTEXITCODE -ne 0) { throw 'veraPDF installation failed.' }
& (Join-Path $output 'cli/verapdf') --version
if ($LASTEXITCODE -ne 0) { throw 'veraPDF executable failed.' }
