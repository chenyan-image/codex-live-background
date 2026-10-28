$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot;$output=Join-Path $PSScriptRoot ('output\caption-hover-'+[guid]::NewGuid().ToString('N'));$null=New-Item -ItemType Directory -Path $output
$files=@('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','CompanionCore.cs','WindowResize.cs','CompanionBehavior.cs','CompanionVideo.cs','CompanionOverlay.cs','CompanionSettings.cs','CompanionQuota.cs','CompanionWindow.cs','CompanionPage.cs','DouyinSpark.cs','DouyinAccessibility.cs','DouyinWorker.cs','DouyinLauncher.cs','DouyinNativeUia.cs','DouyinVision.cs','CompanionAutomation.cs','ManagerDouyin.cs','CompanionActivity.cs','CompanionBailong.cs','CompanionTokens.cs','HubPopupMenu.cs')|ForEach-Object{Join-Path $repo ('source\'+$_)}
$exe=Join-Path $output CaptionHoverTest.exe;$report=Join-Path $output result.json
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /main:CaptionHoverLifecycleTest /platform:x64 /codepage:65001 ("/out:$exe") ("/resource:"+(Join-Path $repo 'source\assets\sounds\click.wav')+",CodexHub.ClickSound") /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Accessibility.dll /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll /r:"$repo\source\assets\windows\Interop.UIAutomationClient.dll" /resource:"$repo\source\assets\windows\Interop.UIAutomationClient.dll,CodexHub.UIAutomationInterop" "/lib:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF" /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $files (Join-Path $PSScriptRoot CaptionHoverLifecycle.cs)
if($LASTEXITCODE -ne 0){throw 'Caption hover test compilation failed'}
$process=Start-Process $exe -ArgumentList ('"'+$report+'"') -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(15000)){throw 'Caption hover fixture timed out'}
$result=[IO.File]::ReadAllText($report)|ConvertFrom-Json
if($process.ExitCode -ne 0 -or -not $result.pass){throw $result.error}
$result|ConvertTo-Json -Compress
