param([ValidateSet('Plan','Install','Uninstall')][string]$Action,[Parameter(Mandatory=$true)][string]$InstallRoot,[string]$SetupExecutable,[string]$OperationRoot,[int]$ManagerPid)
$ErrorActionPreference='Stop';[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$env:PSModulePath=Join-Path $PSHOME Modules
$package=$PSScriptRoot;$state=Join-Path $env:LOCALAPPDATA CodexDreamSkin
$script:NativeSetupExecutable=$SetupExecutable; $script:NativePackageRoot=$package
. (Join-Path $package 'engine\scripts\common-windows.ps1')
foreach($file in @('service-removal.ps1','service-compatibility.ps1','shortcuts.ps1','uninstall-data.ps1','upgrade.ps1','native-lifecycle.ps1')){. (Join-Path $package $file)}
function Invoke-Engine {param([string]$Operation) $ps=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe';$lines=@(& $ps -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File (Join-Path $package 'engine\scripts\manager-actions.ps1') -Action $Operation -SkillRoot (Join-Path $package engine) 2>&1);if($LASTEXITCODE -ne 0){throw ($lines -join "`n")}}
function Invoke-BackgroundNativeActivate {
 param([string]$Root,[int]$ManagerPid)
 if(-not @(Get-DreamSkinRegisteredCodexInstalls).Count){
  $shell=New-Object -ComObject WScript.Shell;try{$link=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'CodexHub.lnk'));$link.TargetPath=Join-Path $Root 'CodexLiveBackground.exe';$link.WorkingDirectory=$Root;$link.IconLocation=(Join-Path $Root 'CodexBackground.ico')+',0';$link.Save()}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)}
  [Console]::Out.WriteLine('CodexHub 已安装。尚未检测到官方 Codex，安装 Codex 后可在服务与设置中装载服务。');return
 }
 $ps=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe';& $ps -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File (Join-Path $Root control.ps1) -Action Install -ManagerPid $ManagerPid;if($LASTEXITCODE -ne 0){throw '服务安装未通过验证，文件已保留，点击重试可修复。'}
}
try{if($Action -eq 'Plan'){[Console]::Out.WriteLine('DREAM_PLAN:'+((Get-BackgroundPlan -InstallRoot $InstallRoot -PackageRoot $package)|ConvertTo-Json -Depth 6 -Compress));exit 0};$mutex=[Threading.Mutex]::new($false,('Local\CodexLiveBackground.NativeLifecycle.'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value));$owned=$false;try{try{$owned=$mutex.WaitOne(30000)}catch [Threading.AbandonedMutexException]{$owned=$true};if(-not $owned){throw '另一个安装或卸载操作正在进行。'};Invoke-BackgroundNativeAction -Action $Action -InstallRoot $InstallRoot -StateRoot $state -PackageRoot $package -SetupExecutable $SetupExecutable -OperationRoot $OperationRoot -ManagerPid $ManagerPid}finally{if($owned){$mutex.ReleaseMutex()};$mutex.Dispose()}}catch{[Console]::Error.WriteLine($_.Exception.Message);exit 1}
