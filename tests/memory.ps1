param([Parameter(Mandatory=$true)][string]$PackageRoot,[switch]$Baseline,[string]$SourceDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot;$out=Join-Path $PSScriptRoot ('output\memory-'+[guid]::NewGuid().ToString('N'));$null=New-Item -ItemType Directory -Path $out
if(-not $SourceDirectory){$SourceDirectory=Join-Path $repo 'source'}
$files=@('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','SoftwareUpdate.cs','ManagerUpdates.cs','BuiltinDownloads.cs','CompanionCore.cs','WindowResize.cs','CompanionBehavior.cs','CompanionVideo.cs','CompanionOverlay.cs','CompanionSettings.cs','CompanionQuota.cs','CompanionWindow.cs','CompanionPage.cs','CompanionActivity.cs','CompanionBailong.cs','CompanionTokens.cs','HubPopupMenu.cs')|ForEach-Object{Join-Path $SourceDirectory $_}
$exe=Join-Path $PackageRoot 'MemoryTests.exe';$report=Join-Path $out 'result.json'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /main:MemoryTests /platform:x64 /codepage:65001 ("/out:$exe") /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Accessibility.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $files (Join-Path $PSScriptRoot 'MemoryTests.cs')
if($LASTEXITCODE -ne 0){throw 'Memory tests failed to compile'}
$arguments='"'+[IO.Path]::GetFullPath($PackageRoot)+'" "'+$report+'"';if(-not $Baseline){$arguments+=' --strict'}
try{$process=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru;if(-not $process.WaitForExit(30000)){throw 'Memory tests timed out'};$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;if($process.ExitCode -ne 0 -or -not $data.pass){throw ($data|ConvertTo-Json -Compress)};$data|ConvertTo-Json -Compress;Write-Output ('Report: '+$report)}finally{Remove-Item -LiteralPath $exe -Force -ErrorAction SilentlyContinue}
