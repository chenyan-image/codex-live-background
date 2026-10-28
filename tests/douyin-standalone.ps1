param([Parameter(Mandatory=$true)][string]$PackageRoot,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory);if(Test-Path -LiteralPath $out){throw 'OutputDirectory must be new'};$null=New-Item -ItemType Directory -Path $out
$repo=Split-Path -Parent $PSScriptRoot
$files=@('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','SoftwareUpdate.cs','ManagerUpdates.cs','ManagerPetAppearance.cs','ManagerFollow.cs','CodexFollow.cs','PetCardPalette.cs','DailyUpdates.cs','CloudPets.cs','BuiltinDownloads.cs','CompanionCore.cs','WindowResize.cs','CompanionBehavior.cs','CompanionVideo.cs','CompanionOverlay.cs','CompanionSettings.cs','CompanionQuota.cs','CompanionWindow.cs','CompanionPage.cs','DouyinSpark.cs','DouyinAccessibility.cs','DouyinWorker.cs','DouyinLauncher.cs','DouyinNativeUia.cs','CompanionAutomation.cs','ManagerDouyin.cs','CompanionActivity.cs','CompanionBailong.cs','CompanionTokens.cs','HubPopupMenu.cs')|ForEach-Object{Join-Path $repo ('source\'+$_)}
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe';$wpf=Join-Path (Split-Path -Parent $csc) 'WPF';$exe=Join-Path $out 'DouyinStandaloneTests.exe'
& $csc /nologo /target:exe /main:DouyinStandaloneTests /platform:x64 /codepage:65001 "/out:$exe" "/lib:$wpf" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Accessibility.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll /r:"$repo\source\assets\windows\Interop.UIAutomationClient.dll" /resource:"$repo\source\assets\windows\Interop.UIAutomationClient.dll,CodexHub.UIAutomationInterop" @files (Join-Path $PSScriptRoot 'DouyinStandaloneTests.cs')
if($LASTEXITCODE -ne 0){throw 'Standalone proof compilation failed'}
& $exe ([IO.Path]::GetFullPath($PackageRoot)) $out
if($LASTEXITCODE -ne 0){throw 'Standalone proof failed'}
Get-Content -LiteralPath (Join-Path $out 'result.json') -Raw
