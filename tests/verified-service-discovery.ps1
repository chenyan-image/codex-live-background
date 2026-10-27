param([Parameter(Mandatory=$true)][string]$Repo)
$ErrorActionPreference='Stop'
. (Join-Path $Repo 'engine\scripts\common-windows.ps1')
. (Join-Path $Repo 'source\service-compatibility.ps1')
. (Join-Path $Repo 'source\service-removal.ps1')
. (Join-Path $Repo 'source\upgrade.ps1')
. (Join-Path $Repo 'source\native-lifecycle.ps1')
$sandbox=Join-Path ([IO.Path]::GetTempPath()) ('HubDiscover-'+[guid]::NewGuid().ToString('N').Substring(0,10))
$root=Join-Path $sandbox 'portable with spaces';$startup=Join-Path $root startup;$package=Join-Path $sandbox package;$state=Join-Path $sandbox state
$null=New-Item -ItemType Directory -Path $startup,$package,$state -Force
$hashes=@{};foreach($name in @('DreamSkinPortHook.exe','monitor-skin.ps1','register-port.ps1')){[IO.File]::WriteAllText((Join-Path $startup $name),('fixture '+$name));$hashes[$name]=(Get-FileHash -LiteralPath (Join-Path $startup $name)).Hash}
[IO.File]::WriteAllText((Join-Path $package 'service-compatibility.json'),(@{profiles=@(@{name='verified-test';hashes=$hashes})}|ConvertTo-Json -Depth 6))
$personal=Join-Path $root 'personal-wallpaper.jpg';[IO.File]::WriteAllText($personal,'preserve original');$before=(Get-FileHash -LiteralPath $personal).Hash
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$script:task=[pscustomobject]@{Actions=@([pscustomobject]@{Execute=(Join-Path $startup 'DreamSkinPortHook.exe');Arguments='--guard'});Principal=[pscustomobject]@{UserId=$sid;LogonType='Interactive'}}
function Get-ScheduledTask {param($TaskName,$ErrorAction) $script:task}
function Get-DreamBackgroundRegisteredInstallRoots {}
function Get-BackgroundLegacyMsi {param($CatalogPath,$FallbackRoot)}
$checks=0
function Need($Condition,$Message){$script:checks++;if(-not $Condition){throw $Message}}
function Reject([scriptblock]$Action,$Message){$blocked=$false;try{& $Action|Out-Null}catch{$blocked=$true};Need $blocked $Message}
function Resolve {Resolve-DreamBackgroundUpgradeStartup -Task $script:task -Installs @() -StateRoot $state -PackageRoot $package}
Need ((Resolve) -eq $startup) 'Verified detached service was rejected'
$oldLocal=$env:LOCALAPPDATA
try{
 $env:LOCALAPPDATA=Join-Path $sandbox Local
 $script:NativePackageRoot=$package
 $plan=Get-BackgroundPlan -InstallRoot (Join-Path $sandbox target) -PackageRoot $package
 Need ($root -in $plan.roots) 'Detached service absent from integration shutdown plan'
 Need (@(Get-BackgroundOwnedFiles -Root $root).Count -eq 0) 'Service discovery trusted an arbitrary file removal list'
 Need ((Get-FileHash -LiteralPath $personal).Hash -eq $before) 'Discovery changed personal material'
 Need (-not(Test-Path -LiteralPath (Join-Path $root '.codex-background-install.json'))) 'Discovery invented an installation marker'
}finally{$env:LOCALAPPDATA=$oldLocal}
$script:task.Actions[0].Arguments='--guard extra';Reject {Resolve} 'Extra task arguments accepted';$script:task.Actions[0].Arguments='--guard'
$script:task.Principal.UserId='S-1-5-18';Reject {Resolve} 'Foreign task owner accepted';$script:task.Principal.UserId=$sid
$script:task.Principal.LogonType='ServiceAccount';Reject {Resolve} 'Noninteractive service task accepted';$script:task.Principal.LogonType='Interactive'
$script:task.Actions[0].Execute=Join-Path $startup 'foreign.exe';Reject {Resolve} 'Foreign task executable accepted';$script:task.Actions[0].Execute=Join-Path $startup 'DreamSkinPortHook.exe'
[IO.File]::AppendAllText((Join-Path $startup 'monitor-skin.ps1'),' changed');Reject {Resolve} 'Changed service file accepted'
[IO.File]::WriteAllText((Join-Path $startup 'monitor-skin.ps1'),'fixture monitor-skin.ps1')
Need ((Resolve) -eq $startup) 'Restored exact hashes were rejected'
$script:task.Actions+=@([pscustomobject]@{Execute='foreign.exe';Arguments=''});Reject {Resolve} 'Multiple task actions accepted'
@{pass=$true;checks=$checks;detachedVerifiedServiceAccepted=$true;unknownFilesNotOwned=$true;foreignOwnerAndChangedFilesRejected=$true;realServicesModified=$false;realRegistrationModified=$false}|ConvertTo-Json -Compress
