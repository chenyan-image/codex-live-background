param([Parameter(Mandatory=$true)][string]$ReleaseDirectory,[string]$CurrentManagerPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
. (Join-Path $PSScriptRoot '..\source\upgrade.ps1')
. (Join-Path $PSScriptRoot '..\source\service-removal.ps1')
function Test-DreamSkinPathEqual {param($Left,$Right) [IO.Path]::GetFullPath($Left) -ieq [IO.Path]::GetFullPath($Right)}
function Stop-DreamBackgroundManagers {param($Installs,$ManagerPid)}
function Initialize-DreamSkinNativeInspection {}
function Get-CimInstance {param($ClassName,$Filter) @()}
function Get-ScheduledTask {param($TaskName,$ErrorAction) $null}
function Remove-DreamBackgroundService {param($InstallRoot,$StateRoot,$PackageRoot,[switch]$ForUpgrade) if(-not $ForUpgrade){throw 'Unexpected skin removal'}}
$sandbox=Join-Path $PSScriptRoot ('output\legacy-'+[guid]::NewGuid().ToString('N'));$null=New-Item -ItemType Directory -Path $sandbox
$fresh=Join-Path $sandbox 'fresh\Programs\CodexLiveBackground'
if(@(Get-DreamBackgroundLegacyInstalls -InstallRoot $fresh).Count -ne 0){throw 'Fresh computer incorrectly requires an old installation'}
$freshState=Join-Path $sandbox 'fresh\State';$null=Prepare-DreamBackgroundUpgrade -InstallRoot $fresh -StateRoot $freshState -PackageRoot (Join-Path $sandbox 'NewPackage')
if(@(([IO.File]::ReadAllText((Join-Path $freshState 'last-upgrade.json'))|ConvertFrom-Json).backups).Count -ne 0){throw 'Fresh installation attempted to replace an old program'}
foreach($version in @('1.0.0','1.0.1','1.0.2')){
 $root=Join-Path $sandbox ($version+'\Programs\CodexLiveBackground');$null=New-Item -ItemType Directory -Path $root -Force
 $zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $ReleaseDirectory ('Codex-Dynamic-Background-'+$version+'-win-x64.zip')))
 try{$entry=$zip.Entries|Where-Object{$_.FullName -match '/CodexLiveBackground.exe$'}|Select-Object -First 1;[IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $root 'CodexLiveBackground.exe'))}finally{$zip.Dispose()}
 $detected=@(Get-DreamBackgroundLegacyInstalls -InstallRoot $root)
 if($detected.Count -ne 1 -or $detected[0].Version -ne $version){throw ('Authentic '+$version+' was not identified')}
 $state=Join-Path $sandbox ($version+'\State');$null=New-Item -ItemType Directory -Path $state
 [IO.File]::WriteAllText((Join-Path $state 'wallpaper.mp4'),'keep wallpaper');[IO.File]::WriteAllText((Join-Path $state 'login-test.txt'),'keep login')
 $null=Prepare-DreamBackgroundUpgrade -InstallRoot $root -StateRoot $state -PackageRoot (Join-Path $sandbox 'NewPackage')
 $record=[IO.File]::ReadAllText((Join-Path $state 'last-upgrade.json'))|ConvertFrom-Json
 if($record.backups.Count -ne 1 -or -not (Test-Path -LiteralPath (Join-Path $record.backups[0].backup 'CodexLiveBackground.exe')) -or (Test-Path -LiteralPath $root)){throw ('Migration failed for '+$version)}
 if([IO.File]::ReadAllText((Join-Path $state 'wallpaper.mp4')) -ne 'keep wallpaper' -or [IO.File]::ReadAllText((Join-Path $state 'login-test.txt')) -ne 'keep login'){throw 'User assets changed during migration'}
}
$cases=9
if($CurrentManagerPath){
 $current=Join-Path $sandbox 'current\Programs\CodexLiveBackground';$null=New-Item -ItemType Directory -Path $current -Force
 Copy-Item -LiteralPath $CurrentManagerPath -Destination (Join-Path $current 'CodexLiveBackground.exe')
 $null=Prepare-DreamBackgroundUpgrade -InstallRoot $current -StateRoot (Join-Path $sandbox 'current\State') -PackageRoot $current
 if(-not (Test-Path -LiteralPath (Join-Path $current 'CodexLiveBackground.exe'))){throw 'MSI post-copy setup moved its own manager'}
 $cases++
}
$unknown=Join-Path $sandbox 'unknown\Programs\CodexLiveBackground';$null=New-Item -ItemType Directory -Path $unknown -Force;[IO.File]::WriteAllText((Join-Path $unknown 'CodexLiveBackground.exe'),'unknown file')
try{$null=Get-DreamBackgroundLegacyInstalls -InstallRoot $unknown;throw 'Foreign program accepted'}catch{if($_.Exception.Message -notmatch '身份无法确认'){throw}}
@{pass=$true;cases=$cases;freshInstall=$true;authenticVersions=@('1.0.0','1.0.1','1.0.2');fullMigration=$true;userDataPreserved=$true;unknownPreserved=$true;currentManagerAccepted=[bool]$CurrentManagerPath}|ConvertTo-Json -Compress
