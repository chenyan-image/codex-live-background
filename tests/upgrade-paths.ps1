param([Parameter(Mandatory=$true)][string]$PackageRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
. (Join-Path $repo 'engine\scripts\common-windows.ps1')
. (Join-Path $repo 'source\service-compatibility.ps1')
. (Join-Path $repo 'source\service-removal.ps1')
. (Join-Path $repo 'source\upgrade.ps1')
. (Join-Path $repo 'source\native-lifecycle.ps1')
$sandbox=Join-Path $PSScriptRoot ('output\upgrade-paths-'+[guid]::NewGuid().ToString('N'))
$script:registeredRoot=Join-Path $sandbox 'CustomDrive\Codex'
$fresh=Join-Path $sandbox 'NewInstall\CodexHub';$state=Join-Path $sandbox 'State';$package=Join-Path $sandbox 'Package'
$startup=Join-Path $script:registeredRoot 'startup'
$null=New-Item -ItemType Directory -Path $startup,$state,$package -Force
Copy-Item -LiteralPath (Join-Path $PackageRoot 'CodexLiveBackground.exe') -Destination $script:registeredRoot
Copy-Item -LiteralPath (Join-Path $PackageRoot 'manifest.json') -Destination $package
Copy-Item -LiteralPath (Join-Path $repo 'source\service-compatibility.json') -Destination $package
foreach($name in @('DreamSkinPortHook.exe','monitor-skin.ps1','register-port.ps1')){Copy-Item -LiteralPath (Join-Path $PackageRoot ('startup\'+$name)) -Destination $startup}
[IO.File]::WriteAllText((Join-Path $state 'wallpaper.keep'),'preserve personal media')
$script:task=[pscustomobject]@{Actions=@([pscustomobject]@{Execute=('"'+(Join-Path $startup 'DreamSkinPortHook.exe')+'"');Arguments='--guard'});Principal=[pscustomobject]@{UserId=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;LogonType='Interactive'}}
$script:stopped=0;$script:removed=$null;$checks=0
function Need($Condition,$Message){if(-not $Condition){throw $Message};$script:checks++}
function Blocked([scriptblock]$Action){$blocked=$false;try{& $Action}catch{$blocked=$true};Need $blocked 'Unsafe/foreign fixture was accepted'}
function Get-DreamBackgroundRegisteredInstallRoots {$script:registeredRoot}
function Get-ScheduledTask {param($TaskName,$ErrorAction);$script:task}
function Stop-DreamBackgroundManagers {param($Installs,$ManagerPid);$script:stopped++}
function Get-CimInstance {param($ClassName,$Filter);@()}
function Remove-DreamBackgroundService {param($InstallRoot,$StateRoot,$PackageRoot,$StartupRoot,[switch]$ForUpgrade);Need $ForUpgrade 'Migration changed live wallpaper';$script:removed=$StartupRoot;$script:task=$null}
$localBefore=$env:LOCALAPPDATA
try{
  $env:LOCALAPPDATA=Join-Path $sandbox 'LocalAppData'
  $installs=@(Get-DreamBackgroundLegacyInstalls -InstallRoot $fresh)
  Need ($installs.Count -eq 1 -and (Test-DreamSkinPathEqual -Left $installs[0].Root -Right $script:registeredRoot)) 'Custom registered EXE installation was missed'
  $resolved=Resolve-DreamBackgroundUpgradeStartup -Task $script:task -Installs $installs -StateRoot $state -PackageRoot $package
  Need (Test-DreamSkinPathEqual -Left $resolved -Right $startup) 'Quoted custom service path was not resolved'
  Assert-DreamBackgroundTaskOwner -Task $script:task -StartupRoot $startup
  function Get-BackgroundLegacyMsi {param($CatalogPath,$FallbackRoot);@()}
  $script:NativePackageRoot=$PackageRoot
  $marker=Join-Path $script:registeredRoot '.codex-background-install.json'
  [IO.File]::WriteAllText($marker,(@{productId='codex-live-background/exe';root=$script:registeredRoot;files=@('CodexLiveBackground.exe')+$script:BackgroundRetiredFiles}|ConvertTo-Json -Depth 4))
  $native=Get-BackgroundPlan -InstallRoot $fresh -PackageRoot $PackageRoot
  Need ($native.roots.Count -eq 1 -and (Test-DreamSkinPathEqual -Left $native.roots[0] -Right $script:registeredRoot)) 'EXE installer did not discover the registered previous install'
  $known=[IO.File]::ReadAllText($marker)
  [IO.File]::WriteAllText($marker,(@{productId='codex-live-background/exe';root=$script:registeredRoot;files=@('foreign-user-document.txt')}|ConvertTo-Json -Depth 4))
  Blocked {Get-BackgroundOwnedFiles -Root $script:registeredRoot}
  [IO.File]::WriteAllText($marker,$known)
  $script:task.Actions[0].Execute=Join-Path $sandbox 'Foreign\DreamSkinPortHook.exe'
  Blocked {Prepare-DreamBackgroundUpgrade -InstallRoot $fresh -StateRoot $state -PackageRoot $package}
  Need ($script:stopped -eq 0) 'A failed preflight stopped the manager'
  $script:task.Actions[0].Execute='"'+(Join-Path $startup 'DreamSkinPortHook.exe')+'"'
  $script:task.Actions[0].Arguments='--other'
  Blocked {Resolve-DreamBackgroundUpgradeStartup -Task $script:task -Installs $installs -StateRoot $state -PackageRoot $package}
  $script:task.Actions[0].Arguments='--guard'
  $original=[IO.File]::ReadAllBytes((Join-Path $startup 'monitor-skin.ps1'))
  [IO.File]::WriteAllText((Join-Path $startup 'monitor-skin.ps1'),'foreign content')
  Blocked {Prepare-DreamBackgroundUpgrade -InstallRoot $fresh -StateRoot $state -PackageRoot $package}
  Need ($script:stopped -eq 0) 'Unknown service bytes stopped the manager'
  [IO.File]::WriteAllBytes((Join-Path $startup 'monitor-skin.ps1'),$original)
  $drive=[IO.Path]::GetPathRoot($sandbox)
  Assert-DreamBackgroundUpgradeTree -Path (Join-Path $drive ('CodexHubPathCheck-'+[guid]::NewGuid().ToString('N'))) -StateRoot $state
  Blocked {Assert-DreamBackgroundUpgradeTree -Path $drive -StateRoot $state}
  Blocked {Assert-DreamBackgroundUpgradeTree -Path $state -StateRoot $state}
  $null=Prepare-DreamBackgroundUpgrade -InstallRoot $fresh -StateRoot $state -PackageRoot $package
  $record=[IO.File]::ReadAllText((Join-Path $state 'last-upgrade.json'))|ConvertFrom-Json
  Need ($script:stopped -eq 1 -and (Test-DreamSkinPathEqual -Left $script:removed -Right $startup)) 'Verified custom service was not handed to removal'
  Need ($record.backups.Count -eq 1 -and (Test-Path -LiteralPath (Join-Path $record.backups[0].backup 'CodexLiveBackground.exe')) -and -not(Test-Path -LiteralPath $script:registeredRoot)) 'Custom installation backup failed'
  $null=Restore-DreamBackgroundUpgrade -InstallRoot $fresh -StateRoot $state -PackageRoot $package
  Need (Test-Path -LiteralPath (Join-Path $script:registeredRoot 'CodexLiveBackground.exe')) 'Registered custom destination could not be rolled back'
  Need ([IO.File]::ReadAllText((Join-Path $state 'wallpaper.keep')) -eq 'preserve personal media') 'Migration modified user media'
  [ordered]@{pass=$true;checks=$checks;customExeDiscovery=$true;quotedTaskPath=$true;identityAndHashesProtected=$true;preflightBeforeStopping=$true;customRootRollback=$true;servicesChanged=$false;fixture=$sandbox}|ConvertTo-Json -Compress
}finally{$env:LOCALAPPDATA=$localBefore}
