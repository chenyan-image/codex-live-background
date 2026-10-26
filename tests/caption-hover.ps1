$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot;$output=Join-Path $PSScriptRoot ('output\caption-hover-'+[guid]::NewGuid().ToString('N'));$null=New-Item -ItemType Directory -Path $output
$files=@('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','CompanionCore.cs','CompanionQuota.cs','CompanionWindow.cs','CompanionPage.cs','CompanionActivity.cs','CompanionBailong.cs','CompanionTokens.cs','HubPopupMenu.cs')|ForEach-Object{Join-Path $repo ('source\'+$_)}
$exe=Join-Path $output CaptionHoverTest.exe;$report=Join-Path $output result.json
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /main:CaptionHoverLifecycleTest /platform:x64 /codepage:65001 ("/out:$exe") /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Accessibility.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $files (Join-Path $PSScriptRoot CaptionHoverLifecycle.cs)
if($LASTEXITCODE -ne 0){throw 'Caption hover test compilation failed'}
$process=Start-Process $exe -ArgumentList ('"'+$report+'"') -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(15000)){throw 'Caption hover fixture timed out'}
$result=[IO.File]::ReadAllText($report)|ConvertFrom-Json
if($process.ExitCode -ne 0 -or -not $result.pass){throw $result.error}
$result|ConvertTo-Json -Compress
