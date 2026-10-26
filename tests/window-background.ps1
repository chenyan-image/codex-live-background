param([Parameter(Mandatory=$true)][string]$FfmpegExecutable)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot;$output=Join-Path $PSScriptRoot ('output\window-background-'+[guid]::NewGuid().ToString('N'));$null=New-Item -ItemType Directory -Path $output
& $FfmpegExecutable -hide_banner -loglevel error -f lavfi -i 'testsrc2=size=128x72:rate=6' -t 1 -loop 0 (Join-Path $output preview.gif)
if($LASTEXITCODE -ne 0){throw 'GIF fixture generation failed'}
$files=@('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','CompanionCore.cs','CompanionQuota.cs','CompanionWindow.cs','CompanionPage.cs','CompanionActivity.cs','CompanionBailong.cs','CompanionTokens.cs','HubPopupMenu.cs')|ForEach-Object{Join-Path $repo ('source\'+$_)}
$exe=Join-Path $output WindowBackgroundTest.exe;$report=Join-Path $output result.json
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /main:WindowBackgroundTest /platform:x64 /codepage:65001 ("/out:$exe") /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Accessibility.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $files (Join-Path $PSScriptRoot WindowBackground.cs)
if($LASTEXITCODE -ne 0){throw 'Window background fixture compilation failed'}
$p=Start-Process $exe -ArgumentList ('"'+$report+'"') -WindowStyle Hidden -PassThru
if(-not $p.WaitForExit(30000)){throw 'Window background fixture timed out'}
$result=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;if($p.ExitCode -ne 0 -or -not $result.pass){throw $result.error}
Copy-Item -LiteralPath $report -Destination (Join-Path $PSScriptRoot 'output\window-background-result.json')
$result|ConvertTo-Json -Compress
