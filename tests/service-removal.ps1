$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\service-removal.ps1')
$sandbox=Join-Path $PSScriptRoot ('output\service-'+[guid]::NewGuid().ToString('N'))
$package=Join-Path $sandbox 'package';$state=Join-Path $sandbox 'state';$install=Join-Path $sandbox 'install'
$null=New-Item -ItemType Directory -Path (Join-Path $package 'engine\scripts'),$state,$install -Force
@'
function Set-DreamSkinPaused { param($Paused,$StateRoot) [IO.File]::WriteAllText((Join-Path $StateRoot 'paused'),'true') }
function Read-DreamSkinState { param($Path) if(Test-Path -LiteralPath $Path){return @{injectorPid=999999}};return $null }
function Stop-DreamSkinRecordedInjector { param($State) $script:injectorStopped=$true;return $true }
'@ | Set-Content -LiteralPath (Join-Path $package 'engine\scripts\theme-windows.ps1') -Encoding UTF8
function Assert($Condition,$Message){if(-not $Condition){throw $Message}}
function Test-DreamSkinPathEqual {param($Left,$Right) return [IO.Path]::GetFullPath($Left).TrimEnd('\') -ieq [IO.Path]::GetFullPath($Right).TrimEnd('\')}
$script:task=$null;$script:run=$null;$script:hooks=@();$script:workers=@();$script:disconnected=$false;$script:injectorStopped=$false;$script:disabled=@()
function Get-ScheduledTask {param($TaskName,$ErrorAction) return $script:task}
function Disable-ScheduledTask {param($TaskName,$ErrorAction)}
function Unregister-ScheduledTask {param($TaskName,$Confirm,$ErrorAction) $script:task=$null}
function Get-ItemProperty {param($LiteralPath,$Name,$ErrorAction) return @{CodexDreamSkinPort=$script:run}}
function Remove-ItemProperty {param($LiteralPath,$Name,$ErrorAction) if($LiteralPath -like '*\Run'){$script:run=$null}}
function Get-DreamBackgroundStartupLink {param($StartupRoot) return $null}
function Get-DreamBackgroundPackageHooks {param($StartupRoot) return $script:hooks}
function Disable-DreamBackgroundPackageHook {param($Package) $script:disabled+=@($Package);$script:hooks=@($script:hooks|Where-Object{$_.Package -ne $Package})}
function Get-DreamBackgroundWorkers {param($StartupRoot) return $script:workers}
function Stop-DreamBackgroundWorker {param($Identity) $script:workers=@($script:workers|Where-Object{$_.ProcessId -ne $Identity.ProcessId})}
function Enter-DreamSkinOperationLock {param($TimeoutMilliseconds) return @{mock=$true}}
function Exit-DreamSkinOperationLock {param($Mutex)}
function Invoke-Engine {param($Operation) if($script:disconnected){throw 'Disconnected renderer'};return @{rendererRemoved=$true}}
$call=@{InstallRoot=$install;StateRoot=$state;PackageRoot=$package;GraceSeconds=0}
function Seed {
 $null=New-Item -ItemType Directory -Path (Join-Path $state 'startup'),(Join-Path $state 'active-theme'),(Join-Path $state 'cdp-profile') -Force
 [IO.File]::WriteAllText((Join-Path $state 'startup\DreamSkinPortHook.exe'),'test helper')
 [IO.File]::WriteAllText((Join-Path $state 'active-theme\wallpaper.mp4'),'keep wallpaper')
 [IO.File]::WriteAllText((Join-Path $state 'cdp-profile\Login Data'),'keep login')
 [IO.File]::WriteAllText((Join-Path $state 'state.json'),'{}');[IO.File]::WriteAllText((Join-Path $install 'installed.json'),'{}')
}
Seed
$script:task=@{Actions=@(@{Execute=(Join-Path $state 'startup\DreamSkinPortHook.exe');Arguments='--guard'});Principal=@{UserId=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;LogonType='Interactive'}}
$script:run='"'+(Join-Path $state 'startup\DreamSkinPortHook.exe')+'" --guard'
$script:hooks=@(@{Package='owned-package'});$script:workers=@(@{ProcessId=123;Kind='monitor'})
$result=Remove-DreamBackgroundService @call
Assert ($result -match '彻底卸载') 'Success missing'
Assert (-not (Test-Path -LiteralPath (Join-Path $state 'startup'))) 'Startup files remain'
Assert (-not (Test-Path -LiteralPath (Join-Path $state 'state.json'))) 'Session record remains'
Assert ($null -eq $script:task -and $null -eq $script:run -and $script:hooks.Count -eq 0 -and $script:workers.Count -eq 0) 'Integration remains'
Assert $script:injectorStopped 'Injector not stopped'
Assert (Test-Path -LiteralPath (Join-Path $state 'active-theme\wallpaper.mp4')) 'Wallpaper lost'
Assert (Test-Path -LiteralPath (Join-Path $state 'cdp-profile\Login Data')) 'Login lost'
$null=Remove-DreamBackgroundService @call
Seed;$script:run='foreign.exe'
try{$null=Remove-DreamBackgroundService @call;throw 'Foreign accepted'}catch{Assert ($_.Exception.Message -match '属于其他程序') 'Foreign Run not preserved'}
Assert (Test-Path -LiteralPath (Join-Path $state 'startup')) 'Foreign cleanup touched files'
$script:run=$null;$script:task=@{Actions=@(@{Execute='C:\foreign.exe';Arguments='--guard'});Principal=@{}}
try{$null=Remove-DreamBackgroundService @call;throw 'Foreign accepted'}catch{Assert ($_.Exception.Message -match '属于其他程序') 'Foreign task not preserved'}
$script:task=$null;$script:disconnected=$true
try{$null=Remove-DreamBackgroundService @call;throw 'Disconnect misreported'}catch{Assert ($_.Exception.Message -match '未确认卸下') 'Disconnect misreported'}
Assert (-not (Test-Path -LiteralPath (Join-Path $state 'startup'))) 'Disconnected service files remain'
Assert (Test-Path -LiteralPath (Join-Path $state 'state.json')) 'Retry session lost'
Seed;$result=Remove-DreamBackgroundService @call -AllowDisconnected
Assert ($result -match '未确认卸下' -and -not (Test-Path -LiteralPath (Join-Path $state 'startup'))) 'Package uninstall did not complete safe service cleanup'
$script:disconnected=$false
Seed;$null=Remove-DreamBackgroundService @call -ForUpgrade
Assert (Test-Path -LiteralPath (Join-Path $state 'state.json')) 'Upgrade discarded live session identity'
Seed
$outside=Join-Path $sandbox 'outside';$null=New-Item -ItemType Directory -Path $outside
[IO.File]::WriteAllText((Join-Path $outside 'keep.txt'),'keep')
$junction=Join-Path $state 'startup\linked'
$null=New-Item -ItemType Junction -Path $junction -Target $outside
try{$null=Remove-DreamBackgroundService @call;throw 'Junction accepted'}catch{Assert ($_.Exception.Message -match '链接或联接') 'Junction boundary not checked'}
Assert (([IO.File]::ReadAllText((Join-Path $outside 'keep.txt'))) -eq 'keep') 'Outside file changed'
try{Assert-DreamBackgroundTree -Path (Join-Path $junction 'missing\target') -Parent $state;throw 'Ancestor junction accepted'}catch{Assert ($_.Exception.Message -match '链接或联接') 'Ancestor junction not checked'}
[IO.Directory]::Delete($junction)
try{Assert-DreamBackgroundTree -Path $outside -Parent $state;throw 'Escaping path accepted'}catch{Assert ($_.Exception.Message -match '超出') 'Escaping path not rejected'}
$programStartup=Join-Path $install 'startup';$null=New-Item -ItemType Directory -Path $programStartup -Force
[IO.File]::WriteAllText((Join-Path $programStartup 'DreamSkinPortHook.exe'),'MSI-owned payload')
$null=Remove-DreamBackgroundService @call -StartupRoot $programStartup
Assert (Test-Path -LiteralPath (Join-Path $programStartup 'DreamSkinPortHook.exe')) 'Service removal deleted MSI-owned payload'
try{$null=Remove-DreamBackgroundService @call -StartupRoot $outside;throw 'Foreign startup accepted'}catch{Assert ($_.Exception.Message -match '不属于') 'Foreign startup root was not rejected'}
[pscustomobject]@{pass=$true;cases=12;msiPayloadPreserved=$true;fullRemoval=$true;repeatable=$true;foreignEntriesPreserved=$true;disconnectReported=$true;packageUninstallDisconnected=$true;upgradeRetainsSession=$true;junctionRejected=$true;outsideUntouched=$true}|ConvertTo-Json -Compress
