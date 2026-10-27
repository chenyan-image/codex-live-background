param([Parameter(Mandatory=$true)][string]$PackageRoot,[string]$PetState,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if([string]::IsNullOrWhiteSpace($OutputDirectory)){$OutputDirectory=Join-Path $PSScriptRoot ('output\random-actions-'+[guid]::NewGuid().ToString('N'))}
$out=[IO.Path]::GetFullPath($OutputDirectory);$null=New-Item -ItemType Directory -Path $out -Force
$files=@('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','SoftwareUpdate.cs','ManagerUpdates.cs','ManagerPetAppearance.cs','ManagerFollow.cs','CodexFollow.cs','PetCardPalette.cs','DailyUpdates.cs','CloudPets.cs','BuiltinDownloads.cs','CompanionCore.cs','WindowResize.cs','CompanionBehavior.cs','CompanionVideo.cs','CompanionOverlay.cs','CompanionSettings.cs','CompanionQuota.cs','CompanionWindow.cs','CompanionPage.cs','CompanionActivity.cs','CompanionBailong.cs','CompanionTokens.cs','HubPopupMenu.cs')|ForEach-Object{Join-Path $repo ('source\'+$_)}
$exe=Join-Path $out 'PetRandomActionTests.exe';$report=Join-Path $out 'result.json'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /main:PetRandomActionTests /platform:x64 /codepage:65001 ("/out:$exe") /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Accessibility.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $files (Join-Path $PSScriptRoot 'PetRandomActionTests.cs')
if($LASTEXITCODE -ne 0){throw 'Random action tests failed to compile'}
$testArguments=@([IO.Path]::GetFullPath($PackageRoot),$report);if(-not [string]::IsNullOrEmpty($PetState)){$testArguments+=([IO.Path]::GetFullPath($PetState))}
& $exe @testArguments
if($LASTEXITCODE -ne 0){throw ([IO.File]::ReadAllText($report))}
$data=[IO.File]::ReadAllText($report)|ConvertFrom-Json
if(-not $data.pass){throw ($data|ConvertTo-Json -Compress)}
$data|ConvertTo-Json -Depth 6 -Compress
Write-Output ('Report: '+$report)