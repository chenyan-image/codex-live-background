$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\service-removal.ps1')
$startup=Join-Path $PSScriptRoot 'output\fake-owned-startup'
$package='OpenAI.Codex_26.1016.8622.0_x64__2p2nqsd0c76g0'
$script:native=$false;$script:exists=$true;$script:removed=0;$script:children=0;$script:debugger='"'+(Join-Path $startup 'DreamSkinPortHook.exe')+'"'
function Invoke-DreamBackgroundNativeDebugRemoval {param($Package) if($script:native -is [string] -and $script:native -ceq 'denied'){throw 'simulated access denied'};return $script:native}
function Test-DreamSkinPathEqual {param($Left,$Right) [IO.Path]::GetFullPath($Left) -ieq [IO.Path]::GetFullPath($Right)}
function Test-Path {param($LiteralPath) return $script:exists}
function Get-Item {param($LiteralPath,$ErrorAction) $entry=[pscustomobject]@{SubKeyCount=$script:children};$entry|Add-Member -MemberType ScriptMethod -Name GetValue -Value {param($name)return $script:debugger};return $entry}
function Remove-Item {param($LiteralPath,[switch]$Force,$ErrorAction) if($LiteralPath -cne ('HKCU:\Software\Microsoft\Windows\CurrentVersion\PackagedAppXDebug\'+$package)){throw 'Wrong registry key'};$script:exists=$false;$script:removed++}
Disable-DreamBackgroundPackageHook -Package $package -StartupRoot $startup
if($script:exists -or $script:removed -ne 1){throw 'Owned missing-package hook did not clear'}
Disable-DreamBackgroundPackageHook -Package $package -StartupRoot $startup
if($script:removed -ne 1){throw 'Repeated cleanup was not idempotent'}
$script:native=$true;$script:exists=$true;Disable-DreamBackgroundPackageHook -Package $package -StartupRoot $startup
if($script:removed -ne 1){throw 'Native success repeated registry cleanup'}
$script:native=$false;$script:debugger='C:\foreign.exe'
try{Disable-DreamBackgroundPackageHook -Package $package -StartupRoot $startup;throw 'Foreign hook accepted'}catch{if($_.Exception.Message -notmatch '其他程序'){throw}}
$script:debugger=Join-Path $startup 'DreamSkinPortHook.exe';$script:children=1
try{Disable-DreamBackgroundPackageHook -Package $package -StartupRoot $startup;throw 'Unknown subkey accepted'}catch{if($_.Exception.Message -notmatch '其他程序'){throw}}
$script:native='denied';try{Disable-DreamBackgroundPackageHook -Package $package -StartupRoot $startup;throw 'Access denied ignored'}catch{if($_.Exception.Message -ne 'simulated access denied'){throw}}
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\source\service-removal.ps1'))
$definition=[regex]::Match($source,'(?s)Add-Type -TypeDefinition @"\r?\n(.*?)\r?\n"@').Groups[1].Value
if(-not $definition){throw 'Native cleanup definition missing'}
Add-Type -TypeDefinition $definition
if(-not [CodexBackground.DebugCleanup]::CheckRemovalResult(0)){throw 'Native success not recognized'}
if([CodexBackground.DebugCleanup]::CheckRemovalResult(-2147023728)){throw 'Missing package was not classified'}
try{[CodexBackground.DebugCleanup]::CheckRemovalResult(-2147024891);throw 'Native access denied ignored'}catch{if($_.Exception.ToString() -notmatch '80070005|denied|拒绝'){throw}}
@{pass=$true;cases=9;ownedMissingHookRemoved=$true;repeatable=$true;foreignHookPreserved=$true;unexpectedErrorsPropagate=$true;realRegistryModified=$false}|ConvertTo-Json -Compress
