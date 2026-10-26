param([string]$PackageRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\upgrade.ps1')
$checks=0
$version=Get-DreamBackgroundPackageVersion -PackageRoot $PackageRoot
foreach($older in @('1.5.4.4','1.6.2.0','1.7.0.1','1.7.0.2','1.7.0.3','1.7.0')){Assert-DreamBackgroundUpgradeVersion -InstalledVersion $older -PackageVersion $version;$checks++}
$blocked=$false;try{Assert-DreamBackgroundUpgradeVersion -InstalledVersion '1.7.0.5' -PackageVersion $version}catch{$blocked=$_.Exception.Message -like '*1.7.0.5*1.7.0.4*'}
if(-not $blocked){throw 'A genuinely newer installation was not protected'};$checks++
$blocked=$false;try{Assert-DreamBackgroundUpgradeVersion -InstalledVersion 'broken' -PackageVersion $version}catch{$blocked=$true};if(-not $blocked){throw 'Invalid installed version accepted'};$checks++
function Get-DreamBackgroundLegacyInstalls {param($InstallRoot);[pscustomobject]@{Executable='C:\fixture\CodexLiveBackground.exe';Version='1.7.0.2';Root='C:\fixture'}}
function Get-ScheduledTask {param($TaskName,$ErrorAction);$null}
function Test-DreamSkinPathEqual {param($Left,$Right);[IO.Path]::GetFullPath($Left) -ieq [IO.Path]::GetFullPath($Right)}
function Assert-DreamBackgroundUpgradeTree {param($Path,$StateRoot)}
function Stop-DreamBackgroundManagers {param($Installs,$ManagerPid);throw 'fixture-approved-before-mutations'}
$reached=$false;try{Prepare-DreamBackgroundUpgrade -PackageRoot $PackageRoot -InstallRoot 'C:\fixture' -StateRoot 'C:\fixture-state' -ManagerPid 0}catch{$reached=$_.Exception.Message -eq 'fixture-approved-before-mutations'}
if(-not $reached){throw 'Same-version repair is blocked before upgrade can begin'};$checks++
[ordered]@{pass=$true;checks=$checks;packageVersion=$version.ToString();sameVersionAllowed=$true;newerVersionProtected=$true;servicesChanged=$false}|ConvertTo-Json -Compress
