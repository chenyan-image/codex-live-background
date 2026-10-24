[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$EngineSource,
  [Parameter(Mandatory=$true)][string]$NodeExecutable,
  [Parameter(Mandatory=$true)][string]$NodeLicense,
  [Parameter(Mandatory=$true)][string]$PortSource,
  [Parameter(Mandatory=$true)][string]$ThirdPartySource,
  [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'OutputDirectory must be new; build does not delete existing files.' }
New-Item -ItemType Directory -Path $out | Out-Null
$engine = Join-Path $out 'engine'
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
$signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $nodeRoot 'node.exe')
if ($signature.Status -ne 'Valid') { throw ('Bundled Node.js signature is invalid: ' + $signature.Status) }
foreach ($file in @('control.ps1','autostart.ps1','skin-removal.ps1','service-removal.ps1','upgrade.ps1','shortcuts.ps1','codex-icons.ps1','legacy-managers.json','service-upgrade.ps1','service-compatibility.ps1','使用说明.txt')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $out }
$sourceOut = Join-Path $out 'source'
New-Item -ItemType Directory -Path $sourceOut | Out-Null
foreach ($file in @('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','BuiltinWallpapers.cs','app.manifest','build-msi.ps1','sign-release.ps1','control.ps1','autostart.ps1','skin-removal.ps1','service-removal.ps1','upgrade.ps1','shortcuts.ps1','codex-icons.ps1','legacy-managers.json','service-upgrade.ps1','service-compatibility.ps1','使用说明.txt','build.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $sourceOut }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets') -Destination (Join-Path $sourceOut 'assets') -Recurse
$port = Join-Path $out 'startup'
New-Item -ItemType Directory -Path $port | Out-Null
Copy-Item -LiteralPath (Join-Path $PortSource 'DreamSkinPortHook.cs'),(Join-Path $PortSource 'register-port.ps1') -Destination $port
$monitor = [IO.File]::ReadAllText((Join-Path $PortSource 'monitor-skin.ps1'))
$monitor = [regex]::Replace($monitor, '(?m)^\$install = .*$', '$install = Join-Path $env:LOCALAPPDATA ''Programs\CodexLiveBackground''')
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
$managerSources=@('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
& $csc /nologo /target:winexe /main:Manager /platform:x64 /codepage:65001 /optimize+ "/win32icon:$iconPath" ("/win32manifest:"+(Join-Path $PSScriptRoot 'app.manifest')) "/out:$out\CodexLiveBackground.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Accessibility.dll @managerSources
if ($LASTEXITCODE -ne 0) { throw 'Manager compilation failed.' }
$wallpaperBuilder=Join-Path $out 'GenerateWallpapers.exe'
& $csc /nologo /target:winexe /main:BuiltinWallpapers /codepage:65001 "/out:$wallpaperBuilder" /r:System.Drawing.dll (Join-Path $PSScriptRoot 'BuiltinWallpapers.cs')
if($LASTEXITCODE -ne 0){throw 'Wallpaper artwork compilation failed.'}
$art=Start-Process -FilePath $wallpaperBuilder -ArgumentList ('"'+(Join-Path $out 'wallpapers')+'"') -WindowStyle Hidden -Wait -PassThru
if($art.ExitCode -ne 0){throw 'Wallpaper artwork generation failed.'}
Remove-Item -LiteralPath $wallpaperBuilder -Force
& $csc /nologo /target:winexe /platform:x64 /codepage:65001 /optimize+ ("/win32manifest:"+(Join-Path $PSScriptRoot 'app.manifest')) "/out:$port\DreamSkinPortHook.exe" (Join-Path $port 'DreamSkinPortHook.cs')
if ($LASTEXITCODE -ne 0) { throw 'Startup guard compilation failed.' }
$repoRoot = Split-Path -Parent $EngineSource
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE'),(Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $out
foreach($name in @('README.md','CHANGELOG.md','NOTICE.md')){
 if(Test-Path -LiteralPath (Join-Path $repoRoot $name)){Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $out}
}
if(Test-Path -LiteralPath (Join-Path $repoRoot 'docs\manager.png')){
  $null=New-Item -ItemType Directory -Path (Join-Path $out 'docs') -Force
  foreach($name in @('manager.png','icon.png','windows-trust.md')){if(Test-Path -LiteralPath (Join-Path $repoRoot ('docs\'+$name))){Copy-Item -LiteralPath (Join-Path $repoRoot ('docs\'+$name)) -Destination (Join-Path $out 'docs')}}
}
$thirdParty = Join-Path $out 'THIRD_PARTY'
New-Item -ItemType Directory -Path $thirdParty | Out-Null
Copy-Item -LiteralPath (Join-Path $ThirdPartySource 'Codex-Dream-Skin') -Destination $thirdParty -Recurse
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
Original user wallpapers and original bundled preset media are excluded.
Two engine JPEG placeholders are locally generated neutral gradients. Six additional original wallpaper illustrations are generated by BuiltinWallpapers.cs. Official Codex icons are read from the local installed app and are not redistributed.
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
$compatibility.profiles = @($compatibility.profiles) + @([pscustomobject]@{name='packaged-1.5.0';hashes=$currentHashes})
foreach ($destination in @($out,$sourceOut)) {
  [IO.File]::WriteAllText((Join-Path $destination 'service-compatibility.json'),($compatibility | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
}
$entries = @(Get-ChildItem -LiteralPath $out -Recurse -File | ForEach-Object {
  [ordered]@{path=$_.FullName.Substring($out.Length+1);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
[ordered]@{version='1.5.0';platform='windows-x64';files=$entries} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'manifest.json') -Encoding UTF8
Write-Output $out
