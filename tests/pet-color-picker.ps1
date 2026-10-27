[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot;$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'OutputDirectory must be new; no existing files are deleted.'}
$null=New-Item -ItemType Directory -Path $out
$files=@('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','SoftwareUpdate.cs','ManagerUpdates.cs','ManagerPetAppearance.cs','ManagerFollow.cs','CodexFollow.cs','PetCardPalette.cs','DailyUpdates.cs','CloudPets.cs','BuiltinDownloads.cs','CompanionCore.cs','WindowResize.cs','CompanionBehavior.cs','CompanionVideo.cs','CompanionOverlay.cs','CompanionSettings.cs','CompanionQuota.cs','CompanionWindow.cs','CompanionPage.cs','CompanionActivity.cs','CompanionBailong.cs','CompanionTokens.cs','HubPopupMenu.cs')|ForEach-Object{Join-Path $repo ('source\'+$_)}
$exe=Join-Path $out 'PetColorPickerTests.exe';$report=Join-Path $out 'result.json'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /main:PetColorPickerTests /platform:x64 /codepage:65001 ("/out:$exe") /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Accessibility.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $files (Join-Path $PSScriptRoot 'PetColorPickerTests.cs')
if($LASTEXITCODE -ne 0){throw 'Color picker tests failed to compile.'}
$process=Start-Process -FilePath $exe -ArgumentList ('"'+$report+'"') -WorkingDirectory $out -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(60000)){try{$process.Kill()}catch{};throw 'Color picker tests timed out.'}
if(-not (Test-Path -LiteralPath $report)){throw ('No color picker report was written; exit '+$process.ExitCode)}
$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
if($process.ExitCode -ne 0 -or -not $data.pass){throw ($data|ConvertTo-Json -Depth 8 -Compress)}
$data|ConvertTo-Json -Depth 8 -Compress;Write-Output ('Report: '+$report)
