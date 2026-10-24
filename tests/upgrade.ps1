$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\service-removal.ps1')
. (Join-Path $PSScriptRoot '..\source\upgrade.ps1')
$sandbox=Join-Path $PSScriptRoot ('output\upgrade-'+[guid]::NewGuid().ToString('N'))
$programs=Join-Path $sandbox 'Programs';$install=Join-Path $programs 'CodexLiveBackground';$legacy=Join-Path $programs 'CodexDreamSkinManager';$state=Join-Path $sandbox 'State';$package=Join-Path $sandbox 'NewPackage'
$null=New-Item -ItemType Directory -Path $programs,$state,$package -Force
function Assert($Condition,$Message){if(-not $Condition){throw $Message}}
function Test-DreamSkinPathEqual {param($Left,$Right) [IO.Path]::GetFullPath($Left).TrimEnd('\') -ieq [IO.Path]::GetFullPath($Right).TrimEnd('\')}
$script:future=$false;$script:stopped=$false;$script:removed=0;$script:failLegacy=$false
function Get-DreamBackgroundLegacyInstalls {param($InstallRoot)
 if(Test-Path -LiteralPath $install){[pscustomobject]@{Root=$install;Executable=(Join-Path $install 'CodexLiveBackground.exe');Version=$(if($script:future){'9.0.0.0'}else{'1.2.0.0'})}}
 if(Test-Path -LiteralPath $legacy){[pscustomobject]@{Root=$legacy;Executable=(Join-Path $legacy 'CodexDreamSkinManager.exe');Version='2.0.3.0'}}
}
function Stop-DreamBackgroundManagers {param($Installs,$ManagerPid) $script:stopped=$true}
function Initialize-DreamSkinNativeInspection {}
function Get-CimInstance {param($ClassName,$Filter) @()}
function Get-ScheduledTask {param($TaskName,$ErrorAction) $null}
function Remove-DreamBackgroundService {param($InstallRoot,$StateRoot,$PackageRoot,[switch]$ForUpgrade) Assert $ForUpgrade 'Upgrade paused live wallpaper';$script:removed++}
function Move-Item {param($LiteralPath,$Destination,$ErrorAction) if($script:failLegacy -and $LiteralPath -eq $legacy){throw 'simulated file lock'};Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination -ErrorAction Stop}
function Seed {
 $null=New-Item -ItemType Directory -Path $install,$legacy,(Join-Path $state 'startup'),(Join-Path $state 'cdp-profile') -Force
 [IO.File]::WriteAllText((Join-Path $install 'old.txt'),'canonical old');[IO.File]::WriteAllText((Join-Path $legacy 'old.txt'),'legacy old')
 [IO.File]::WriteAllText((Join-Path $state 'cdp-profile\Login Data'),'login preserved');[IO.File]::WriteAllText((Join-Path $state 'wallpaper.mp4'),'wallpaper preserved')
}
$call=@{InstallRoot=$install;StateRoot=$state;PackageRoot=$package}
Seed;$null=Prepare-DreamBackgroundUpgrade @call
$record=[IO.File]::ReadAllText((Join-Path $state 'last-upgrade.json'))|ConvertFrom-Json
Assert ($script:stopped -and $script:removed -eq 1) 'Old components were not stopped'
Assert ($record.backups.Count -eq 2 -and -not $record.complete) 'Both install checkpoints missing'
Assert (-not (Test-Path -LiteralPath $install) -and -not (Test-Path -LiteralPath $legacy)) 'Old active directories remain'
Assert ([IO.File]::ReadAllText((Join-Path $state 'cdp-profile\Login Data')) -eq 'login preserved') 'Login was lost'
Assert ([IO.File]::ReadAllText((Join-Path $state 'wallpaper.mp4')) -eq 'wallpaper preserved') 'Wallpaper was lost'
$null=New-Item -ItemType Directory -Path $install;[IO.File]::WriteAllText((Join-Path $install 'new.txt'),'failed new install')
$null=Restore-DreamBackgroundUpgrade @call
Assert ([IO.File]::ReadAllText((Join-Path $install 'old.txt')) -eq 'canonical old') 'Canonical rollback failed'
Assert ([IO.File]::ReadAllText((Join-Path $legacy 'old.txt')) -eq 'legacy old') 'Legacy rollback failed'
$script:failLegacy=$true
try{$null=Prepare-DreamBackgroundUpgrade @call;throw 'Failure not simulated'}catch{Assert ($_.Exception.Message -eq 'simulated file lock') 'Unexpected migration failure'}
$record=[IO.File]::ReadAllText((Join-Path $state 'last-upgrade.json'))|ConvertFrom-Json
Assert ($record.backups.Count -eq 1) 'Partial migration checkpoint lost'
$script:failLegacy=$false;$null=Restore-DreamBackgroundUpgrade @call
Assert (Test-Path -LiteralPath (Join-Path $install 'old.txt')) 'Partial rollback failed'
$script:future=$true;$script:stopped=$false
try{$null=Prepare-DreamBackgroundUpgrade @call;throw 'Downgrade accepted'}catch{Assert ($_.Exception.Message -match '更高版本') 'Future version not protected'}
Assert (-not $script:stopped) 'Downgrade stopped existing manager'
$script:future=$false
$inPlace=@{InstallRoot=$install;StateRoot=$state;PackageRoot=$install}
$null=Prepare-DreamBackgroundUpgrade @inPlace
Assert (Test-Path -LiteralPath (Join-Path $install 'old.txt')) 'In-place package moved while running'
@{pass=$true;cases=6;preservesData=$true;rollback=$true;partialRollback=$true;downgradeProtected=$true;isolatedRoot=$sandbox}|ConvertTo-Json -Compress
