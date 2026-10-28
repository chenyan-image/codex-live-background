[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$PackageRoot,
 [Parameter(Mandatory=$true)][string]$OutputDirectory,
 [string]$PetState=(Join-Path $env:LOCALAPPDATA 'CodexDreamSkin')
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$package=[IO.Path]::GetFullPath($PackageRoot);$out=[IO.Path]::GetFullPath($OutputDirectory)
if(-not (Test-Path -LiteralPath (Join-Path $package 'source\assets\pets'))){throw 'PackageRoot must contain existing pet assets.'}
if(Test-Path -LiteralPath $out){throw 'OutputDirectory must be new; this test does not remove existing files.'}
$null=New-Item -ItemType Directory -Path $out
$files=@('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','SoftwareUpdate.cs','ManagerUpdates.cs','ManagerPetAppearance.cs','ManagerFollow.cs','CodexFollow.cs','PetCardPalette.cs','DailyUpdates.cs','CloudPets.cs','BuiltinDownloads.cs','CompanionCore.cs','WindowResize.cs','CompanionBehavior.cs','CompanionVideo.cs','CompanionOverlay.cs','CompanionSettings.cs','CompanionQuota.cs','CompanionWindow.cs','CompanionPage.cs','DouyinSpark.cs','DouyinAccessibility.cs','DouyinWorker.cs','DouyinLauncher.cs','DouyinNativeUia.cs','DouyinVision.cs','CompanionAutomation.cs','ManagerDouyin.cs','CompanionActivity.cs','CompanionBailong.cs','CompanionTokens.cs','HubPopupMenu.cs')|ForEach-Object{Join-Path $repo ('source\'+$_)}
$exe=Join-Path $out 'PetClickInteractionTests.exe';$report=Join-Path $out 'result.json'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /main:PetClickInteractionTests /platform:x64 /codepage:65001 ("/out:$exe") /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Accessibility.dll /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll /r:"$repo\source\assets\windows\Interop.UIAutomationClient.dll" /resource:"$repo\source\assets\windows\Interop.UIAutomationClient.dll,CodexHub.UIAutomationInterop" "/lib:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF" /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $files (Join-Path $PSScriptRoot 'PetClickInteractionTests.cs')
if($LASTEXITCODE -ne 0){throw 'Pet click interaction tests failed to compile.'}
$arguments='"'+$package+'" "'+[IO.Path]::GetFullPath($PetState)+'" "'+$report+'"'
$process=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $out -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(45000)){try{$process.Kill()}catch{};throw 'Pet click interaction tests timed out.'}
if(-not (Test-Path -LiteralPath $report)){throw ('Test did not produce a report; exit code '+$process.ExitCode)}
$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
if($process.ExitCode -ne 0 -or -not $data.pass){throw ($data|ConvertTo-Json -Depth 8 -Compress)}
$data|ConvertTo-Json -Depth 8 -Compress
Write-Output ('Report: '+$report)
