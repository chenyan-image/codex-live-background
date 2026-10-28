[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$EngineSource,
  [Parameter(Mandatory=$true)][string]$NodeExecutable,
  [Parameter(Mandatory=$true)][string]$NodeLicense,
  [Parameter(Mandatory=$true)][string]$PortSource,
  [Parameter(Mandatory=$true)][string]$ThirdPartySource,
  [string]$FFmpegRuntimeDirectory,
  [switch]$BundleFFmpegRuntime=$true,
  [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$EngineSource=[IO.Path]::GetFullPath($EngineSource)
$NodeExecutable=[IO.Path]::GetFullPath($NodeExecutable)
$NodeLicense=[IO.Path]::GetFullPath($NodeLicense)
$PortSource=[IO.Path]::GetFullPath($PortSource)
$ThirdPartySource=[IO.Path]::GetFullPath($ThirdPartySource)
if(-not [string]::IsNullOrWhiteSpace($FFmpegRuntimeDirectory)){$FFmpegRuntimeDirectory=[IO.Path]::GetFullPath($FFmpegRuntimeDirectory)}
Add-Type -AssemblyName System.Drawing
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'OutputDirectory must be new; build does not delete existing files.' }
New-Item -ItemType Directory -Path $out | Out-Null
$engine = Join-Path $out 'engine'
$repoRoot = Split-Path -Parent $EngineSource
New-Item -ItemType Directory -Path $engine | Out-Null
foreach ($folder in @('scripts','assets')) {
  $destination = Join-Path $engine $folder
  New-Item -ItemType Directory -Path $destination | Out-Null
  Get-ChildItem -LiteralPath (Join-Path $EngineSource $folder) -File | Where-Object { $_.Name -ne 'play-startup-animation.mjs' } | Copy-Item -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $EngineSource 'VERSION') -Destination $engine
function New-NeutralImage([string]$Path) {
  $bitmap = New-Object Drawing.Bitmap 1280,720
  $graphics = [Drawing.Graphics]::FromImage($bitmap)
  $brush = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.Point]::new(0,0)),([Drawing.Point]::new(1280,720)),([Drawing.Color]::FromArgb(20,34,60)),([Drawing.Color]::FromArgb(83,110,132))
  try { $graphics.FillRectangle($brush,0,0,1280,720); $bitmap.Save($Path,[Drawing.Imaging.ImageFormat]::Jpeg) }
  finally { $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
New-NeutralImage (Join-Path $engine 'assets\dream-reference.jpg')
$preset = Join-Path $engine 'presets\preset-gothic-void-crusade'
New-Item -ItemType Directory -Path $preset -Force | Out-Null
$theme = Get-Content -LiteralPath (Join-Path $EngineSource 'presets\preset-gothic-void-crusade\theme.json') -Raw | ConvertFrom-Json
$theme.name = 'Neutral Gradient'
$theme.tagline = 'A locally generated neutral gradient.'
$theme.statusText = 'READY'
$theme.promoSub = 'Neutral Gradient'
[IO.File]::WriteAllText((Join-Path $preset 'theme.json'), ($theme | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
New-NeutralImage (Join-Path $preset 'background.jpg')
$nodeRoot = Join-Path $engine 'runtime\node'
New-Item -ItemType Directory -Path $nodeRoot -Force | Out-Null
Copy-Item -LiteralPath $NodeExecutable -Destination (Join-Path $nodeRoot 'node.exe')
Copy-Item -LiteralPath $NodeLicense -Destination (Join-Path $nodeRoot 'LICENSE')
if($BundleFFmpegRuntime){
if([string]::IsNullOrWhiteSpace($FFmpegRuntimeDirectory)){throw 'FFmpegRuntimeDirectory is required for bundled transparent pet animations.'}
$decoderConfig=[IO.File]::ReadAllText((Join-Path $EngineSource 'assets\ffmpeg-runtime.json'))|ConvertFrom-Json
$decoderMarker=[IO.File]::ReadAllText((Join-Path $FFmpegRuntimeDirectory 'runtime.json'))|ConvertFrom-Json
if($decoderMarker.archiveSha256 -ne $decoderConfig.sha256){throw 'Decoder archive revision mismatch.'}
$decoderOut=Join-Path $engine 'runtime\ffmpeg';$null=New-Item -ItemType Directory -Path $decoderOut
foreach($item in $decoderConfig.files){
 $decoderInput=Join-Path $FFmpegRuntimeDirectory $item.name;$record=@($decoderMarker.files|Where-Object name -eq $item.name)
 if($record.Count -ne 1 -or (Get-Item -LiteralPath $decoderInput).Length -ne $item.bytes -or (Get-FileHash -LiteralPath $decoderInput).Hash -ne $record[0].sha256){throw ('Decoder verification failed: '+$item.name)}
 Copy-Item -LiteralPath $decoderInput -Destination $decoderOut
}
Copy-Item -LiteralPath (Join-Path $FFmpegRuntimeDirectory 'runtime.json') -Destination $decoderOut
}
$signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $nodeRoot 'node.exe')
if ($signature.Status -ne 'Valid') { throw ('Bundled Node.js signature is invalid: ' + $signature.Status) }
foreach ($file in @('control.ps1','native-actions.ps1','native-lifecycle.ps1','install-scan.ps1','legacy-msi-products.json','uninstall-data.ps1','autostart.ps1','skin-removal.ps1','service-removal.ps1','upgrade.ps1','shortcuts.ps1','codex-icons.ps1','legacy-managers.json','historical-install-files.json','cloud-pets.json','service-upgrade.ps1','service-compatibility.ps1','使用说明.txt')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $out }
$sourceOut = Join-Path $out 'source'
New-Item -ItemType Directory -Path $sourceOut | Out-Null
foreach ($file in @('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','SoftwareUpdate.cs','ManagerUpdates.cs','ManagerPetAppearance.cs','ManagerFollow.cs','CodexFollow.cs','PetCardPalette.cs','DailyUpdates.cs','CloudPets.cs','GuardVersion.cs','BuiltinDownloads.cs','CompanionCore.cs','WindowResize.cs','CompanionBehavior.cs','CompanionVideo.cs','CompanionOverlay.cs','CompanionSettings.cs','CompanionQuota.cs','CompanionWindow.cs','CompanionPage.cs','CompanionActivity.cs','CompanionBailong.cs','CompanionTokens.cs','HubPopupMenu.cs','BuiltinWallpapers.cs','NativeUninstall.cs','build-update.ps1','Setup.cs','build-setup.ps1','native-actions.ps1','native-lifecycle.ps1','install-scan.ps1','legacy-msi-products.json','uninstall-data.ps1','app.manifest','build-msi.ps1','sign-release.ps1','control.ps1','autostart.ps1','skin-removal.ps1','service-removal.ps1','upgrade.ps1','shortcuts.ps1','codex-icons.ps1','legacy-managers.json','historical-install-files.json','cloud-pets.json','service-upgrade.ps1','service-compatibility.ps1','使用说明.txt','build.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $sourceOut }
$assetOut=Join-Path $sourceOut 'assets';$null=New-Item -ItemType Directory -Path $assetOut
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assets') | Where-Object Name -notin @('wallpapers','pets') | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $assetOut -Recurse}
$petOut=Join-Path $assetOut 'pets';$null=New-Item -ItemType Directory -Path $petOut
foreach($id in @('deepseek','bailong','deepseek-fat-fish','naifrog')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('assets\pets\'+$id)) -Destination $petOut -Recurse}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\pets\sources.json') -Destination $petOut
$checks=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'assets\pets\material-checksums.json'))|ConvertFrom-Json
$checks.files=@($checks.files|Where-Object{Test-Path -LiteralPath (Join-Path $petOut $_.path)})
foreach($entry in $checks.files){if($entry.path -eq 'deepseek-fat-fish/pet.json'){$file=Get-Item -LiteralPath (Join-Path $petOut $entry.path);$entry.bytes=$file.Length;$entry.sha256=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()}}
[IO.File]::WriteAllText((Join-Path $petOut 'material-checksums.json'),($checks|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
$port = Join-Path $out 'startup'
New-Item -ItemType Directory -Path $port | Out-Null
Copy-Item -LiteralPath (Join-Path $PortSource 'DreamSkinPortHook.cs'),(Join-Path $PortSource 'ShellIdentity.cs'),(Join-Path $PortSource 'register-port.ps1') -Destination $port
$monitor = [IO.File]::ReadAllText((Join-Path $PortSource 'monitor-skin.ps1'))
$monitor = [regex]::Replace($monitor, '(?m)^\$install = .*$', '$install = Split-Path -Parent $PSScriptRoot')
$monitor = $monitor.Replace("'windows\scripts'", "'engine\scripts'")
[IO.File]::WriteAllText((Join-Path $port 'monitor-skin.ps1'), $monitor, [Text.UTF8Encoding]::new($true))
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$iconBuilder=Join-Path $out 'BuildIcon.exe'
& $csc /nologo /target:winexe /main:IconAssets /codepage:65001 "/out:$iconBuilder" /r:System.Drawing.dll (Join-Path $PSScriptRoot 'IconAssets.cs')
if ($LASTEXITCODE -ne 0) { throw 'Icon compilation failed.' }
$iconPath=Join-Path $sourceOut 'assets\app.ico'
$iconPreview=Join-Path $sourceOut 'assets\app.png'
$iconProcess=Start-Process -FilePath $iconBuilder -ArgumentList ('"'+$iconPath+'" "'+$iconPreview+'"') -WindowStyle Hidden -Wait -PassThru
if($iconProcess.ExitCode -ne 0){throw 'Icon generation failed.'}
Copy-Item -LiteralPath $iconPath -Destination (Join-Path $out 'CodexBackground.ico')
Remove-Item -LiteralPath $iconBuilder -Force
$managerSources=@('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','SoftwareUpdate.cs','ManagerUpdates.cs','ManagerPetAppearance.cs','ManagerFollow.cs','CodexFollow.cs','PetCardPalette.cs','DailyUpdates.cs','CloudPets.cs','BuiltinDownloads.cs','CompanionCore.cs','WindowResize.cs','CompanionBehavior.cs','CompanionVideo.cs','CompanionOverlay.cs','CompanionSettings.cs','CompanionQuota.cs','CompanionWindow.cs','CompanionPage.cs','CompanionActivity.cs','CompanionBailong.cs','CompanionTokens.cs','HubPopupMenu.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
& $csc /nologo /target:winexe /main:Manager /platform:x64 /codepage:65001 /optimize+ "/win32icon:$iconPath" "/resource:$iconPath,CodexHub.AppIcon" ("/resource:"+(Join-Path $PSScriptRoot 'assets\brand\codexhub.png')+",CodexHub.Brand") ("/win32manifest:"+(Join-Path $PSScriptRoot 'app.manifest')) "/out:$out\CodexLiveBackground.exe" ("/resource:"+(Join-Path $PSScriptRoot 'assets\sounds\click.wav')+",CodexHub.ClickSound") /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Accessibility.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll @managerSources
if ($LASTEXITCODE -ne 0) { throw 'Manager compilation failed.' }
& $csc /nologo /target:winexe /main:DreamSkinPortHook /platform:x64 /codepage:65001 /optimize+ "/win32icon:$iconPath" ("/win32manifest:"+(Join-Path $PSScriptRoot 'app.manifest')) "/out:$port\DreamSkinPortHook.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $port 'DreamSkinPortHook.cs') (Join-Path $port 'ShellIdentity.cs') (Join-Path $PSScriptRoot 'CodexFollow.cs') (Join-Path $PSScriptRoot 'SoftwareUpdate.cs')
if ($LASTEXITCODE -ne 0) { throw 'Startup guard compilation failed.' }
& $csc /nologo /target:winexe /main:SoftwareUpdateHost /platform:x64 /codepage:65001 /optimize+ "/win32icon:$iconPath" "/resource:$iconPath,CodexHub.AppIcon" ("/resource:"+(Join-Path $PSScriptRoot 'assets\brand\codexhub.png')+",CodexHub.Brand") ("/win32manifest:"+(Join-Path $PSScriptRoot 'app.manifest')) "/out:$out\CodexHubUpdater.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $PSScriptRoot 'SoftwareUpdate.cs') (Join-Path $PSScriptRoot 'CodexFollow.cs') ("/resource:"+(Join-Path $port 'DreamSkinPortHook.exe')+",CodexHub.GuardProgram") ("/resource:"+(Join-Path $port 'DreamSkinPortHook.cs')+",CodexHub.GuardSource") ("/resource:"+(Join-Path $port 'ShellIdentity.cs')+",CodexHub.ShellIdentitySource") ("/resource:"+(Join-Path $PSScriptRoot 'cloud-pets.json')+",CodexHub.CloudPetCatalog") ("/resource:"+(Join-Path $PSScriptRoot 'historical-install-files.json')+",CodexHub.HistoricalInstallCatalog") (Join-Path $PSScriptRoot 'GuardVersion.cs')
if($LASTEXITCODE -ne 0){throw 'Program updater compilation failed.'}
& $csc /nologo /target:winexe /main:NativeUninstall /platform:x64 /codepage:65001 /optimize+ ("/win32manifest:"+(Join-Path $PSScriptRoot 'app.manifest')) "/win32icon:$iconPath" "/out:$out\卸载 CodexHub.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'NativeUninstall.cs')
if($LASTEXITCODE -ne 0){throw 'Native uninstaller compilation failed.'}
$wallpaperBuilder=Join-Path $out 'GenerateWallpapers.exe'
& $csc /nologo /target:winexe /main:BuiltinWallpapers /codepage:65001 "/out:$wallpaperBuilder" /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'BuiltinWallpapers.cs')
if($LASTEXITCODE -ne 0){throw 'Wallpaper artwork compilation failed.'}
$wallpaperSource=Join-Path $PSScriptRoot 'assets\wallpapers'
if(-not(Test-Path -LiteralPath (Join-Path $wallpaperSource 'catalog.json'))){$wallpaperSource=Join-Path (Split-Path -Parent $PSScriptRoot) 'wallpapers'}
$art=Start-Process -FilePath $wallpaperBuilder -ArgumentList ('"'+(Join-Path $out 'wallpapers')+'" "'+$wallpaperSource+'"') -WindowStyle Hidden -Wait -PassThru
if($art.ExitCode -ne 0){throw 'Wallpaper artwork generation failed.'}
Remove-Item -LiteralPath $wallpaperBuilder -Force
$repoRoot = Split-Path -Parent $EngineSource
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE'),(Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $out
foreach($name in @('README.md','CHANGELOG.md','NOTICE.md')){
 if(Test-Path -LiteralPath (Join-Path $repoRoot $name)){Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $out}
}
if(Test-Path -LiteralPath (Join-Path $repoRoot 'docs')){
  $null=New-Item -ItemType Directory -Path (Join-Path $out 'docs') -Force
  foreach($name in @('windows-trust.md','guide.md','bundled-wallpapers.md','desktop-pets.md')){if(Test-Path -LiteralPath (Join-Path $repoRoot ('docs\'+$name))){Copy-Item -LiteralPath (Join-Path $repoRoot ('docs\'+$name)) -Destination (Join-Path $out 'docs')}}
  # Retain our exact release-note filenames in the trusted payload manifest.
  # Old install markers can still reference them after software-only updates.
  foreach($note in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'docs') -Filter 'release-*.md' -File){Copy-Item -LiteralPath $note.FullName -Destination (Join-Path $out 'docs')}
  if(Test-Path -LiteralPath (Join-Path $repoRoot 'docs\screenshots')){Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\screenshots') -Destination (Join-Path $out 'docs\screenshots') -Recurse}
  if(Test-Path -LiteralPath (Join-Path $repoRoot 'docs\badges')){Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\badges') -Destination (Join-Path $out 'docs\badges') -Recurse}
}
$thirdParty = Join-Path $out 'THIRD_PARTY'
New-Item -ItemType Directory -Path $thirdParty | Out-Null
Copy-Item -LiteralPath (Join-Path $ThirdPartySource 'Codex-Dream-Skin') -Destination $thirdParty -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY\Pet-Assets') -Destination $thirdParty -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\fonts\ChakraPetch-OFL.txt') -Destination (Join-Path $thirdParty 'ChakraPetch-OFL.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $thirdParty 'Dream-Skin-Manager-LICENSE')
Copy-Item -LiteralPath $NodeLicense -Destination (Join-Path $thirdParty 'Node-LICENSE')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\Phosphor-LICENSE') -Destination (Join-Path $thirdParty 'Phosphor-LICENSE')
$runtimeNotices=Join-Path $ThirdPartySource 'FFmpeg-Runtime'
if(-not (Test-Path -LiteralPath $runtimeNotices -PathType Container)){$runtimeNotices=Join-Path $repoRoot 'ffmpeg-notices'}
$runtimeNoticeOut=Join-Path $thirdParty 'FFmpeg-Runtime'
New-Item -ItemType Directory -Path $runtimeNoticeOut | Out-Null
foreach($name in @('runtime-download.json','README.md','COPYING.LGPLv3','COPYING.GPLv3','publisher-checksums.sha256')){
 Copy-Item -LiteralPath (Join-Path $runtimeNotices $name) -Destination $runtimeNoticeOut
}
@'
This distribution adds a Windows launcher, startup-port guard, no-close behavior,
visible-thread selector repairs, and theme recovery to the MIT-licensed engine.
Source: https://github.com/xxloocee/Codex-Dream-Skin-Manager
Upstream: https://github.com/Fei-Away/Codex-Dream-Skin (MIT)
No original engine preset media are included. The six former generated backgrounds have been removed.
Two engine JPEG placeholders are locally generated neutral gradients. Only the selected Flower Peach static wallpaper is bundled. Sources and rights are described in wallpapers/NOTICE.md; those media are not covered by the code MIT license. Official Codex icons are read from the local installed app and are not redistributed.
The OpenAI application, trademarks and accounts are not included.
'@ | Set-Content -LiteralPath (Join-Path $thirdParty 'MODIFICATIONS.txt') -Encoding UTF8
# Windows PowerShell 5.1 requires BOM for scripts containing Chinese strings.
Get-ChildItem -LiteralPath $out -Recurse -File -Filter '*.ps1' | ForEach-Object {
  $content = [IO.File]::ReadAllText($_.FullName)
  [IO.File]::WriteAllText($_.FullName, $content, [Text.UTF8Encoding]::new($true))
}
$compatibility = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'service-compatibility.json')) | ConvertFrom-Json
$currentHashes = [ordered]@{}
foreach ($name in @('DreamSkinPortHook.exe','monitor-skin.ps1','register-port.ps1')) {
  $currentHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $port $name)).Hash
}
$compatibility.profiles = @($compatibility.profiles) + @([pscustomobject]@{name='packaged-1.7.0.26';hashes=$currentHashes})
foreach ($destination in @($out,$sourceOut)) {
  [IO.File]::WriteAllText((Join-Path $destination 'service-compatibility.json'),($compatibility | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
}
$entries = @(Get-ChildItem -LiteralPath $out -Recurse -File | ForEach-Object {
  [ordered]@{path=$_.FullName.Substring($out.Length+1);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
[ordered]@{version='1.7.0.26';platform='windows-x64';files=$entries} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'manifest.json') -Encoding UTF8
Write-Output $out
