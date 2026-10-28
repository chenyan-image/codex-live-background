param([Parameter(Mandatory=$true)][string]$BasePackage,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$base=[IO.Path]::GetFullPath($BasePackage);$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'OutputDirectory must be new'}
if(-not(Test-Path -LiteralPath (Join-Path $base 'engine\runtime\node\node.exe'))){throw 'BasePackage must be a complete CodexHub package'}
Copy-Item -LiteralPath $base -Destination $out -Recurse
$names=@('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','SoftwareUpdate.cs','ManagerUpdates.cs','ManagerPetAppearance.cs','ManagerFollow.cs','CodexFollow.cs','PetCardPalette.cs','DailyUpdates.cs','CloudPets.cs','BuiltinDownloads.cs','CompanionCore.cs','WindowResize.cs','CompanionBehavior.cs','CompanionVideo.cs','CompanionOverlay.cs','CompanionSettings.cs','CompanionQuota.cs','CompanionWindow.cs','CompanionPage.cs','CompanionActivity.cs','CompanionBailong.cs','CompanionTokens.cs','HubPopupMenu.cs','DouyinSpark.cs','DouyinAccessibility.cs','DouyinWorker.cs','DouyinLauncher.cs','DouyinNativeUia.cs','CompanionAutomation.cs','ManagerDouyin.cs')
$files=$names|ForEach-Object{Join-Path $PSScriptRoot $_}
foreach($name in $names){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $out ('source\'+$name)) -Force}
foreach($name in @('DouyinPreviewLauncher.cs','build-douyin-preview.ps1','build.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $out ('source\'+$name)) -Force}
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe';$wpf=Join-Path (Split-Path -Parent $csc) 'WPF';$icon=Join-Path $out 'CodexBackground.ico'
& $csc /nologo /target:winexe /main:Manager /platform:x64 /codepage:65001 /optimize+ "/lib:$wpf" "/out:$out\CodexLiveBackground.exe" "/win32icon:$icon" "/resource:$icon,CodexHub.AppIcon" ("/resource:"+(Join-Path $PSScriptRoot 'assets\brand\codexhub.png')+",CodexHub.Brand") ("/resource:"+(Join-Path $PSScriptRoot 'assets\sounds\click.wav')+",CodexHub.ClickSound") ("/win32manifest:"+(Join-Path $PSScriptRoot 'app.manifest')) /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Accessibility.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll /r:"$PSScriptRoot\assets\windows\Interop.UIAutomationClient.dll" /resource:"$PSScriptRoot\assets\windows\Interop.UIAutomationClient.dll,CodexHub.UIAutomationInterop" @files
if($LASTEXITCODE -ne 0){throw 'Preview manager build failed'}
$launcher=Join-Path $PSScriptRoot 'DouyinPreviewLauncher.cs'
& $csc /nologo /target:winexe /platform:x64 /codepage:65001 /optimize+ "/out:$out\CodexHub-抖音预览.exe" "/win32icon:$icon" /r:System.Windows.Forms.dll $launcher
if($LASTEXITCODE -ne 0){throw 'Preview launcher build failed'}
$manifest=Get-Content -LiteralPath (Join-Path $out 'manifest.json') -Raw|ConvertFrom-Json
Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'docs\douyin-spark.md') -Destination (Join-Path $out '抖音预览说明.md')
New-Item -ItemType Directory -Path (Join-Path $out 'source\assets\windows') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\windows\Interop.UIAutomationClient.dll'),(Join-Path $PSScriptRoot 'assets\windows\README.md') -Destination (Join-Path $out 'source\assets\windows')
$manifest.version='1.8.0.3'
$manifest.files=@(Get-ChildItem -LiteralPath $out -File -Recurse|Where-Object{$_.Name -ne 'manifest.json'}|ForEach-Object{[ordered]@{path=$_.FullName.Substring($out.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
[IO.File]::WriteAllText((Join-Path $out 'manifest.json'),($manifest|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
[ordered]@{path=$out;version='1.8.0.3';launcher=(Join-Path $out 'CodexHub-抖音预览.exe');profile=(Join-Path $out 'DouyinPreviewData');stableInstallModified=$false}|ConvertTo-Json
