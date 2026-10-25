$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\service-removal.ps1')
. (Join-Path $PSScriptRoot '..\source\uninstall-data.ps1')
. (Join-Path $PSScriptRoot '..\source\native-lifecycle.ps1')
function Test-DreamSkinPathEqual {param($Left,$Right) [IO.Path]::GetFullPath($Left).TrimEnd('\') -ieq [IO.Path]::GetFullPath($Right).TrimEnd('\')}
$sandbox=Join-Path $PSScriptRoot ('output\native-'+[guid]::NewGuid().ToString('N'));$root=Join-Path $sandbox '自定义 app';$package=Join-Path $sandbox package;$state=Join-Path $sandbox state;$operation=Join-Path $sandbox operation;$setup=Join-Path $sandbox Setup.exe
$null=New-Item -ItemType Directory -Path $package,$state,$operation,(Join-Path $state 'cdp-profile'),(Join-Path $state runtime) -Force
[IO.File]::WriteAllText($setup,'test executable');[IO.File]::WriteAllText((Join-Path $state 'cdp-profile\Login Data'),'keep login');[IO.File]::WriteAllText((Join-Path $state 'runtime\ffmpeg.exe'),'generated cache')
function Seed([string]$value){$file=Join-Path $package CodexLiveBackground.exe;[IO.File]::WriteAllText($file,$value);[IO.File]::WriteAllText((Join-Path $package manifest.json),(@{version='1.7.0.0';platform='windows-x64';files=@(@{path='CodexLiveBackground.exe';bytes=(Get-Item $file).Length;sha256=(Get-FileHash $file).Hash})}|ConvertTo-Json -Depth 5))}
$script:activationFails=$false;$script:registered=$false;$script:msiCalled=$false;$script:order=@()
function Get-BackgroundPlan {param($InstallRoot,$PackageRoot) [pscustomobject]@{roots=$(if(Test-Path -LiteralPath $InstallRoot){@($InstallRoot)}else{@()});products=@()}}
function Stop-BackgroundIntegrations {param($Roots,$StateRoot,$PackageRoot,$Uninstall,$ExcludeIds) $script:order+=@('stop')}
function Stop-BackgroundOwnedProcesses {param($Roots,$StateRoot,$ExcludeIds) $script:order+=@('verify-exit')}
function Invoke-BackgroundNativeActivate {param($Root,$ManagerPid) $script:order+=@('activate');$null=New-Item -ItemType Directory -Path (Join-Path $Root 'startup') -Force;[IO.File]::WriteAllText((Join-Path $Root 'startup\guard-state.json'),'{}');if($script:activationFails){throw 'simulated service failure'}}
function Write-BackgroundNativeRegistration {param($Root,$Version) $script:registered=$true}
function Remove-BackgroundNativeRegistration {param($Root) $script:registered=$false}
function Invoke-BackgroundMsiRemove {throw 'New EXE lifecycle called MSI'}
$call=@{InstallRoot=$root;StateRoot=$state;PackageRoot=$package;SetupExecutable=$setup;OperationRoot=$operation;ManagerPid=$PID}
Seed 'baseline 1.5.4';Invoke-BackgroundNativeAction @call -Action Install
$marker=Get-Content (Join-Path $root '.codex-background-install.json') -Raw|ConvertFrom-Json
if($marker.pending -or -not $script:registered -or $marker.baseVersion -ne '1.5.4'){throw 'Native install did not complete'}
if($script:order[0] -ne 'stop' -or -not(Test-Path -LiteralPath (Join-Path $root '卸载 CodexHub.exe'))){throw 'Stop order or standalone uninstaller missing'}
[IO.File]::WriteAllText((Join-Path $root 'my-wallpaper.jpg'),'keep original')
Seed 'updated baseline';Invoke-BackgroundNativeAction @call -Action Install
if([IO.File]::ReadAllText((Join-Path $root CodexLiveBackground.exe)) -cne 'updated baseline'){throw 'Native update failed'}
if(-not(Test-Path -LiteralPath (Join-Path $root 'my-wallpaper.jpg'))){throw 'Unknown original file lost'}
$script:activationFails=$true
try{Invoke-BackgroundNativeAction @call -Action Install;throw 'Service failure ignored'}catch{if($_.Exception.Message -ne 'simulated service failure'){throw}}
if(-not (Get-Content (Join-Path $root '.codex-background-install.json') -Raw|ConvertFrom-Json).pending){throw 'Incomplete service reported as complete'}
$script:activationFails=$false;Invoke-BackgroundNativeAction @call -Action Install
foreach($name in @('pets','pet-imports','pet-downloads')){$null=New-Item -ItemType Directory -Path (Join-Path $state $name) -Force;[IO.File]::WriteAllText((Join-Path $state ($name+'\owned.json')),'{}')}
foreach($name in @('pet-settings.json','pet-settings.json.bak','pet-settings.json.tmp')){[IO.File]::WriteAllText((Join-Path $state $name),'{}')}
Invoke-BackgroundNativeAction @call -Action Uninstall
if($script:registered -or (Test-Path -LiteralPath (Join-Path $root CodexLiveBackground.exe)) -or (Test-Path -LiteralPath (Join-Path $root '卸载 CodexHub.exe'))){throw 'Native uninstall left program or registration'}
if(-not(Test-Path -LiteralPath (Join-Path $root 'my-wallpaper.jpg')) -or -not(Test-Path -LiteralPath (Join-Path $state 'cdp-profile\Login Data'))){throw 'Original media or login lost'}
if(Test-Path -LiteralPath (Join-Path $root startup)){throw 'Service runtime records survived'};if(Test-Path -LiteralPath (Join-Path $state runtime)){throw 'Downloaded cache survived'}
foreach($name in @('pets','pet-imports','pet-downloads','pet-settings.json','pet-settings.json.bak','pet-settings.json.tmp')){if(Test-Path -LiteralPath (Join-Path $state $name)){throw ('Pet-owned data survived uninstall: '+$name)}}
try{Get-BackgroundSafePath -Root $root -Relative '..\outside.txt';throw 'Escape accepted'}catch{if($_.Exception.Message -notmatch '超出'){throw}}
$outside=Join-Path $sandbox outside;$null=New-Item -ItemType Directory -Path $outside
$link=Join-Path $root linked;$null=New-Item -ItemType Junction -Path $link -Target $outside
try{Remove-BackgroundOwnedFiles -Root $root -Files @('my-wallpaper.jpg');throw 'Junction accepted'}catch{if($_.Exception.Message -notmatch '链接或联接'){throw}};[IO.Directory]::Delete($link)
@{pass=$true;cases=12;directExeInstall=$true;customPath=$true;updateAndRetry=$true;uninstallerIncluded=$true;msiUsedForNative=$false;originalsAndLoginPreserved=$true;realServicesModified=$false}|ConvertTo-Json -Compress
