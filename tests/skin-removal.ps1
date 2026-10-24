$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\skin-removal.ps1')
$sandbox=Join-Path $PSScriptRoot ('output\removal-'+[guid]::NewGuid().ToString('N'))
$package=Join-Path $sandbox 'package'
$state=Join-Path $sandbox 'state'
$install=Join-Path $sandbox 'install'
$null=New-Item -ItemType Directory -Path (Join-Path $package 'engine\scripts'),$state,$install -Force
@'
function Set-DreamSkinPaused { param($Paused,$StateRoot) [IO.File]::WriteAllText((Join-Path $StateRoot 'paused'),'true') }
function Read-DreamSkinState { param($Path) return $null }
function Stop-DreamSkinRecordedInjector { param($State) throw 'Unexpected injector stop' }
'@ | Set-Content -LiteralPath (Join-Path $package 'engine\scripts\theme-windows.ps1') -Encoding UTF8
$script:foreign=$false;$script:run=$null;$script:disconnected=$false
function Get-ScheduledTask { param($TaskName,$ErrorAction) if($script:foreign){return @{Foreign=$true}}; return $null }
function Get-DreamBackgroundServicePlan { param($Task,$StartupRoot,$ProfilesPath) throw 'Foreign task preserved' }
function Get-ItemProperty { param($LiteralPath,$Name,$ErrorAction) return @{CodexDreamSkinPort=$script:run} }
function Remove-ItemProperty { param($LiteralPath,$Name,$ErrorAction) $script:run=$null }
function Invoke-Engine { param($Operation) if($script:disconnected){throw 'Disconnected renderer'}; return @{rendererRemoved=$true} }
function Assert($Condition,$Message){if(-not $Condition){throw $Message}}
[IO.File]::WriteAllText((Join-Path $install 'installed.json'),'{}')
[IO.File]::WriteAllText((Join-Path $state 'my-wallpaper.mp4'),'preserve')
$result=Remove-DreamBackgroundSkin -InstallRoot $install -StateRoot $state -PackageRoot $package
Assert ($result -match '皮肤已卸载') 'Removal success missing'
Assert (Test-Path -LiteralPath (Join-Path $state 'paused')) 'Pause must persist'
Assert (-not (Test-Path -LiteralPath (Join-Path $install 'installed.json'))) 'Installed marker remains'
Assert (([IO.File]::ReadAllText((Join-Path $state 'my-wallpaper.mp4'))) -eq 'preserve') 'Media changed'
$null=Remove-DreamBackgroundSkin -InstallRoot $install -StateRoot $state -PackageRoot $package
$script:foreign=$true
try{$null=Remove-DreamBackgroundSkin -InstallRoot $install -StateRoot $state -PackageRoot $package;throw 'Foreign task accepted'}catch{Assert ($_.Exception.Message -eq 'Foreign task preserved') 'Foreign task not preserved'}
$script:foreign=$false;$script:run='foreign.exe'
try{$null=Remove-DreamBackgroundSkin -InstallRoot $install -StateRoot $state -PackageRoot $package;throw 'Foreign Run accepted'}catch{Assert ($_.Exception.Message -match '同名自启动') 'Foreign Run not preserved'}
$script:run='"'+(Join-Path $state 'startup\DreamSkinPortHook.exe')+'" --guard';$script:disconnected=$true
try{$null=Remove-DreamBackgroundSkin -InstallRoot $install -StateRoot $state -PackageRoot $package;throw 'Disconnected success'}catch{Assert ($_.Exception.Message -match '未确认卸下') 'Disconnected removal misreported'}
Assert ($null -eq $script:run) 'Owned Run entry remains after disconnect'
Assert (Test-Path -LiteralPath (Join-Path $state 'my-wallpaper.mp4')) 'Disconnected removal lost media'
[pscustomobject]@{pass=$true;cases=5;repeatedRemoval=$true;foreignEntriesPreserved=$true;disconnectedRemovalReported=$true;userMediaPreserved=$true}|ConvertTo-Json -Compress
