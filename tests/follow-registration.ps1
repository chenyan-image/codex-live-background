$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\service-removal.ps1')
. (Join-Path $PSScriptRoot '..\source\native-lifecycle.ps1')

# All registry, service and process operations below are intercepted. Only this
# newly created tests/output fixture is written by the real lifecycle code.
$sandbox=Join-Path $PSScriptRoot ('output\follow-registration-'+[guid]::NewGuid().ToString('N'))
$root=Join-Path $sandbox '安装 app';$state=Join-Path $sandbox '状态 data';$package=Join-Path $sandbox package
$null=New-Item -ItemType Directory -Path $root,$state,$package -Force
$helper=Join-Path $root 'CodexHubUpdater.exe';$preferences=Join-Path $state 'pet-settings.json'
$expected='"'+$helper+'" --follow "'+[IO.Path]::GetFullPath($state)+'"'
$script:runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$script:cases=@();$script:runValue=$null;$script:events=@();$script:launches=@();$script:writes=@();$script:reads=0
function Assert-Follow([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Pass-Follow([string]$Name){$script:cases+=@($Name)}
function Reset-FollowMocks {$script:runValue=$null;$script:events=@();$script:launches=@();$script:writes=@();$script:reads=0}
function Assert-RunKey($Path,$Name){Assert-Follow ($Path -ceq $script:runKey -and $Name -ceq 'CodexHubFollow') 'Unexpected registry operation escaped the fixture'}
function Get-ItemProperty {param($LiteralPath,$Name,$ErrorAction) Assert-RunKey $LiteralPath $Name;$script:reads++;[pscustomobject]@{CodexHubFollow=$script:runValue}}
function Remove-ItemProperty {param($LiteralPath,$Name,$ErrorAction) Assert-RunKey $LiteralPath $Name;$script:runValue=$null;$script:events+=@('follow-remove')}
function New-Item {param($Path,$ItemType,[switch]$Force) if("$Path" -like 'HK*:*'){Assert-Follow ($Path -ceq $script:runKey) 'Unexpected registry key';return};Microsoft.PowerShell.Management\New-Item @PSBoundParameters}
function New-ItemProperty {param($LiteralPath,$Name,$Value,$PropertyType,[switch]$Force) Assert-RunKey $LiteralPath $Name;$script:runValue=$Value;$script:writes+=@([pscustomobject]@{Key=$LiteralPath;Name=$Name;Value=$Value;Type=$PropertyType});$script:events+=@('follow-register')}
function Start-Process {param($FilePath,$ArgumentList,$WorkingDirectory,$WindowStyle) $script:launches+=@([pscustomobject]@{FilePath=$FilePath;ArgumentList=$ArgumentList;WorkingDirectory=$WorkingDirectory;WindowStyle=$WindowStyle});$script:events+=@('follow-launch')}
function Test-DreamSkinPathEqual {param($Left,$Right) [IO.Path]::GetFullPath($Left).TrimEnd('\') -ieq [IO.Path]::GetFullPath($Right).TrimEnd('\')}
function Test-DreamSkinCommandLineToken {param($CommandLine,$Token) throw 'Unexpected command-line process candidate'}
function Get-ScheduledTask {param($TaskName,$ErrorAction) Assert-Follow ($TaskName -ceq 'CodexDreamSkinPort') 'Unexpected scheduled task lookup'}
function Remove-DreamBackgroundService {throw 'Unexpected real service cleanup path'}
function Restore-DreamBackgroundShortcuts {param($StateRoot) $script:events+=@('shortcuts-restore')}
function Stop-DreamBackgroundWorker {throw 'Unexpected process termination'}

$script:runValue=$expected
Remove-BackgroundFollowRegistration -Roots @((Join-Path $sandbox unrelated),$root) -StateRoot $state
Assert-Follow (-not $script:runValue -and ($script:events -join ',') -ceq 'follow-remove') 'Owned Run command was not removed exactly once'
Pass-Follow 'exact-owned-command-removed'

foreach($foreign in @(
 ('"'+(Join-Path $sandbox 'foreign\CodexHubUpdater.exe')+'" --follow "'+$state+'"'),
 ('"'+$helper+'" --follow "'+(Join-Path $sandbox 'other state')+'"'),
 ($expected+' --foreign-option')
)){
 Reset-FollowMocks;$script:runValue=$foreign
 Remove-BackgroundFollowRegistration -Roots @($root) -StateRoot $state
 Assert-Follow ($script:runValue -ceq $foreign -and $script:events.Count -eq 0) 'Foreign Run command was removed'
}
Pass-Follow 'foreign-helper-state-and-extra-arguments-preserved'

Reset-FollowMocks
Write-BackgroundFollowRegistration -Root $root -StateRoot $state
Assert-Follow ($script:launches.Count -eq 0) 'Missing helper or preferences caused registration'
[IO.File]::WriteAllText($helper,'fixture helper; never executed')
Write-BackgroundFollowRegistration -Root $root -StateRoot $state
Assert-Follow ($script:launches.Count -eq 0) 'Missing preferences caused registration'
Pass-Follow 'missing-helper-or-preferences-does-not-register'

foreach($setting in @('{"FollowCodex":false}','{}')){
 Reset-FollowMocks;[IO.File]::WriteAllText($preferences,$setting)
 Write-BackgroundFollowRegistration -Root $root -StateRoot $state
 Assert-Follow ($script:writes.Count -eq 0 -and $script:launches.Count -eq 0) 'Disabled or absent preference registered the helper'
}
Pass-Follow 'disabled-or-absent-preference-does-not-register'

Reset-FollowMocks;[IO.File]::WriteAllText($preferences,'{"FollowCodex":true}')
Write-BackgroundFollowRegistration -Root $root -StateRoot $state
Assert-Follow ($script:writes.Count -eq 0 -and $script:launches.Count -eq 0) 'Follow created a separate updater registration or process'
Pass-Follow 'enabled-follow-reuses-existing-guard'
Reset-FollowMocks;$script:runValue='"C:\foreign\other.exe"'
Write-BackgroundFollowRegistration -Root $root -StateRoot $state
Assert-Follow ($script:runValue -ceq '"C:\foreign\other.exe"' -and $script:writes.Count -eq 0 -and $script:launches.Count -eq 0) 'Foreign Run value was overwritten or launched'
Pass-Follow 'foreign-registration-preserved'
# Supply a managed inspection fixture in this fresh test process. No Win32
# inspection or Get-Process call can reach a real user process.
if('CodexDreamSkin.NativeInspection' -as [type]){throw 'Run this isolated test in a fresh PowerShell process'}
Add-Type -TypeDefinition @'
using System.Collections.Generic;
namespace CodexDreamSkin {
 public static class NativeInspection {
  public static Dictionary<int,string> Paths=new Dictionary<int,string>();
  public static bool CurrentUser(int id) { return id!=7103; }
  public static string ProcessPath(int id) { return Paths[id]; }
 }
}
'@
function Initialize-DreamSkinNativeInspection {}
[CodexDreamSkin.NativeInspection]::Paths[7101]=$helper
[CodexDreamSkin.NativeInspection]::Paths[7102]=Join-Path $sandbox 'foreign\CodexHubUpdater.exe'
[CodexDreamSkin.NativeInspection]::Paths[7103]=$helper
[CodexDreamSkin.NativeInspection]::Paths[7104]=$helper
function Get-CimInstance {param($ClassName,$Filter) Assert-Follow ($ClassName -ceq 'Win32_Process' -and $Filter.Contains("Name='CodexHubUpdater.exe'")) 'Updater missing from cleanup process inventory';foreach($fixtureId in 7101..7104){[pscustomobject]@{ProcessId=$fixtureId;Name='CodexHubUpdater.exe';CommandLine=$expected}}}
function Get-Process {param($Id,$ErrorAction) Assert-Follow ($Id -eq 7101) 'Foreign, other-user or excluded process was inspected';[pscustomobject]@{StartTime=[datetime]'2026-10-26T00:00:00Z'}}
$owned=@(Get-BackgroundOwnedProcesses -Roots @($root) -StateRoot $state -ExcludeIds @(7104))
Assert-Follow ($owned.Count -eq 1 -and $owned[0].ProcessId -eq 7101 -and $owned[0].Executable -ceq $helper -and $owned[0].StartedTicks -gt 0) 'Updater ownership inventory was incorrect'
Pass-Follow 'cleanup-inventory-includes-only-owned-current-user-nonexcluded-updater'

function Stop-BackgroundOwnedProcesses {param($Roots,$StateRoot,$ExcludeIds) $script:events+=@('processes-stop')}
foreach($uninstall in @($false,$true)){
 Reset-FollowMocks;$script:runValue=$expected
 Stop-BackgroundIntegrations -Roots @($root) -StateRoot $state -PackageRoot $package -Uninstall:$uninstall -ExcludeIds @($PID)
 Assert-Follow (-not $script:runValue -and ($script:events -join ',') -ceq 'shortcuts-restore,follow-remove,processes-stop') 'Integration cleanup did not remove follow registration before stopping components'
}
Pass-Follow 'update-and-uninstall-integrations-remove-registration-before-process-cleanup'

# Exercise the native dispatcher with real follow registration and isolated
# file copying. All unrelated lifecycle boundaries remain explicit mocks.
function Get-BackgroundPlan {param($InstallRoot,$PackageRoot) [pscustomobject]@{roots=@($InstallRoot);products=@()}}
function Get-BackgroundOwnedFiles {param($Root) @()}
function Remove-BackgroundDesktopEntries {param($Roots)}
function Write-BackgroundNativeRegistration {param($Root,$Version) $script:events+=@('native-register')}
function Remove-BackgroundNativeRegistration {param($Root) $script:events+=@('native-unregister')}
function Invoke-BackgroundNativeActivate {param($Root,$ManagerPid) $script:events+=@('activate')}
function Remove-DreamBackgroundGeneratedData {param($StateRoot) $script:events+=@('generated-data-cleanup')}
$packageHelper=Join-Path $package 'CodexHubUpdater.exe';$setup=Join-Path $sandbox 'Setup.exe'
[IO.File]::WriteAllText($packageHelper,'fixture helper; never executed');[IO.File]::WriteAllText($setup,'fixture uninstaller; never executed')
[IO.File]::WriteAllText((Join-Path $package 'manifest.json'),(@{version='1.7.0.1';files=@(@{path='CodexHubUpdater.exe';bytes=(Get-Item -LiteralPath $packageHelper).Length;sha256=(Get-FileHash -LiteralPath $packageHelper).Hash})}|ConvertTo-Json -Depth 5))
$call=@{InstallRoot=$root;StateRoot=$state;PackageRoot=$package;SetupExecutable=$setup;OperationRoot=$sandbox;ManagerPid=$PID}
foreach($operation in @('install','update')){
 Reset-FollowMocks;if($operation -ceq 'update'){$script:runValue=$expected}
 Invoke-BackgroundNativeAction @call -Action Install
 Assert-Follow (-not $script:runValue -and $script:launches.Count -eq 0 -and $script:writes.Count -eq 0) 'Native install/update left a separate updater registration'

 if($operation -ceq 'update'){Assert-Follow ([Array]::IndexOf($script:events,'follow-remove') -lt [Array]::IndexOf($script:events,'activate')) 'Update did not clean the old follow registration'}
}
Pass-Follow 'native-install-and-update-use-embedded-follow'

Reset-FollowMocks;$script:runValue=$expected
Invoke-BackgroundNativeAction @call -Action Uninstall
Assert-Follow (-not $script:runValue -and $script:launches.Count -eq 0 -and $script:writes.Count -eq 0 -and $script:events -contains 'native-unregister') 'Native uninstall left or relaunched follow registration'
Pass-Follow 'native-uninstall-removes-follow-without-relaunch'

$report=Join-Path $sandbox 'result.json'
$result=[ordered]@{pass=$true;cases=$script:cases.Count;checks=$script:cases;realRegistryModified=$false;realServicesModified=$false;realProcessesInspectedOrStopped=$false;fixtureRoot=$sandbox;report=$report}
[IO.File]::WriteAllText($report,($result|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
$result|ConvertTo-Json -Depth 6 -Compress
