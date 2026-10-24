$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\skin-removal.ps1')
$sandbox=Join-Path $PSScriptRoot ('output\skin-'+[guid]::NewGuid().ToString('N'))
$package=Join-Path $sandbox 'package';$state=Join-Path $sandbox 'state';$install=Join-Path $sandbox 'install'
$null=New-Item -ItemType Directory -Path (Join-Path $package 'engine\scripts'),$state,$install -Force
@'
function Set-DreamSkinPaused { param($Paused,$StateRoot) [IO.File]::WriteAllText((Join-Path $StateRoot 'paused'),'true') }
'@ | Set-Content -LiteralPath (Join-Path $package 'engine\scripts\theme-windows.ps1') -Encoding UTF8
$script:disconnected=$false
function Invoke-Engine {param($Operation)if($script:disconnected){throw 'Disconnected'};[IO.File]::WriteAllText((Join-Path $state 'paused'),'true');return @{rendererRemoved=$true}}
function Assert($Condition,$Message){if(-not $Condition){throw $Message}}
[IO.File]::WriteAllText((Join-Path $install 'installed.json'),'{}');[IO.File]::WriteAllText((Join-Path $state 'state.json'),'keep session')
$result=Remove-DreamBackgroundSkin -InstallRoot $install -StateRoot $state -PackageRoot $package
Assert ($result -match '后台服务仍保留') 'Skin removal must preserve service'
Assert (Test-Path -LiteralPath (Join-Path $install 'installed.json')) 'Installed marker lost'
Assert (([IO.File]::ReadAllText((Join-Path $state 'state.json'))) -eq 'keep session') 'Injector record lost'
$script:disconnected=$true
try{$null=Remove-DreamBackgroundSkin -InstallRoot $install -StateRoot $state -PackageRoot $package;throw 'Unexpected success'}catch{Assert ($_.Exception.Message -match '未确认卸下') 'Missing partial status'}
Assert (Test-Path -LiteralPath (Join-Path $state 'paused')) 'Pause must persist'
[pscustomobject]@{pass=$true;cases=4;serviceRetained=$true;sessionRetained=$true;disconnectReported=$true}|ConvertTo-Json -Compress
