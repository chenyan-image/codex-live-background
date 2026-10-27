param([Parameter(Mandatory=$true)][string]$PackageRoot,[string]$OutputDirectory)
$Repo=Split-Path -Parent $PSScriptRoot;if([string]::IsNullOrWhiteSpace($OutputDirectory)){$OutputDirectory=Join-Path $PSScriptRoot ('output\quota-visuals-'+[guid]::NewGuid().ToString('N'))}
$ErrorActionPreference='Stop';$null=New-Item -ItemType Directory -Path $OutputDirectory -Force
$files=@('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','SoftwareUpdate.cs','ManagerUpdates.cs','ManagerPetAppearance.cs','ManagerFollow.cs','CodexFollow.cs','PetCardPalette.cs','DailyUpdates.cs','CloudPets.cs','BuiltinDownloads.cs','CompanionCore.cs','WindowResize.cs','CompanionBehavior.cs','CompanionVideo.cs','CompanionOverlay.cs','CompanionSettings.cs','CompanionQuota.cs','CompanionWindow.cs','CompanionPage.cs','CompanionActivity.cs','CompanionBailong.cs','CompanionTokens.cs','HubPopupMenu.cs')|ForEach-Object{Join-Path $Repo ('source\'+$_)}
$exe=Join-Path $OutputDirectory 'PetQuotaVisualTests.exe'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /main:PetQuotaVisualTests /platform:x64 /codepage:65001 ('/out:'+$exe) /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Accessibility.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $files (Join-Path $PSScriptRoot 'PetQuotaVisualTests.cs')
if($LASTEXITCODE -ne 0){throw 'Quota visual tests compile failed'}
$p=Start-Process -FilePath $exe -ArgumentList ('"'+$PackageRoot+'" "'+$OutputDirectory+'"') -WindowStyle Hidden -PassThru
if(-not $p.WaitForExit(30000)){throw 'Quota visual tests timeout'}
$result=Get-Content -LiteralPath (Join-Path $OutputDirectory 'result.json') -Raw|ConvertFrom-Json
$result|ConvertTo-Json -Compress
if($p.ExitCode -ne 0 -or -not $result.pass){throw 'Quota visual tests failed'}
