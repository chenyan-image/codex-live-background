param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot;$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'OutputDirectory must be new; no existing files are removed.'}
$null=New-Item -ItemType Directory -Path $out
$files=@('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','SoftwareUpdate.cs','ManagerUpdates.cs','ManagerPetAppearance.cs','ManagerFollow.cs','CodexFollow.cs','PetCardPalette.cs','DailyUpdates.cs','CloudPets.cs','BuiltinDownloads.cs','CompanionCore.cs','WindowResize.cs','CompanionBehavior.cs','CompanionVideo.cs','CompanionOverlay.cs','CompanionSettings.cs','CompanionQuota.cs','CompanionWindow.cs','CompanionPage.cs','DouyinSpark.cs','DouyinAccessibility.cs','DouyinWorker.cs','DouyinLauncher.cs','DouyinNativeUia.cs','DouyinVision.cs','CompanionAutomation.cs','ManagerDouyin.cs','CompanionActivity.cs','CompanionBailong.cs','CompanionTokens.cs','HubPopupMenu.cs')|ForEach-Object{Join-Path $repo ('source\'+$_)}
$exe=Join-Path $out 'WindowRestoreBoundsTests.exe';$report=Join-Path $out 'result.json'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /main:WindowRestoreBoundsTests /platform:x64 /codepage:65001 ("/out:$exe") /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Accessibility.dll /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll /r:"$repo\source\assets\windows\Interop.UIAutomationClient.dll" /resource:"$repo\source\assets\windows\Interop.UIAutomationClient.dll,CodexHub.UIAutomationInterop" "/lib:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF" /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $files (Join-Path $PSScriptRoot 'WindowRestoreBoundsTests.cs')
if($LASTEXITCODE -ne 0){throw 'Restore bounds fixture compilation failed.'}
& $exe $report
if($LASTEXITCODE -ne 0){throw ([IO.File]::ReadAllText($report))}
Get-Content -LiteralPath $report -Raw
