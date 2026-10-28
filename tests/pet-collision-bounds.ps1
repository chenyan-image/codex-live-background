param([Parameter(Mandatory=$true)][string]$PackageRoot,[Parameter(Mandatory=$true)][string]$OutputDirectory,[string]$WindowSource,[switch]$Baseline,[string]$PetState=(Join-Path $env:LOCALAPPDATA 'CodexDreamSkin'))
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot;$out=[IO.Path]::GetFullPath($OutputDirectory);$null=New-Item -ItemType Directory -Path $out -Force
$files=@('Manager.cs','IconAssets.cs','WallpaperLibrary.cs','GalleryControls.cs','GalleryManager.cs','MediaPreviewJob.cs','SoftwareUpdate.cs','ManagerUpdates.cs','ManagerPetAppearance.cs','ManagerFollow.cs','CodexFollow.cs','PetCardPalette.cs','DailyUpdates.cs','CloudPets.cs','BuiltinDownloads.cs','CompanionCore.cs','WindowResize.cs','CompanionBehavior.cs','CompanionVideo.cs','CompanionOverlay.cs','CompanionSettings.cs','CompanionQuota.cs','CompanionWindow.cs','CompanionPage.cs','CompanionActivity.cs','CompanionBailong.cs','CompanionTokens.cs','HubPopupMenu.cs')|ForEach-Object{if($_ -eq 'CompanionWindow.cs' -and $WindowSource){[IO.Path]::GetFullPath($WindowSource)}else{Join-Path $repo ('source\'+$_)}}
$exe=Join-Path $out 'PetCollisionBoundsTests.exe';$report=Join-Path $out 'result.json'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /main:PetCollisionBoundsTests /platform:x64 /codepage:65001 ("/out:$exe") /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Accessibility.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $files (Join-Path $PSScriptRoot 'PetCollisionBoundsTests.cs')
if($LASTEXITCODE -ne 0){throw 'Collision bounds test compilation failed'}
$arguments=@([IO.Path]::GetFullPath($PackageRoot),([IO.Path]::GetFullPath($PetState)),$report);if(-not $Baseline){$arguments+='--strict'}
& $exe @arguments
if($LASTEXITCODE -ne 0){throw ([IO.File]::ReadAllText($report))}
Get-Content -LiteralPath $report -Raw
