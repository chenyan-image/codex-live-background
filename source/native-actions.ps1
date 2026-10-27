param([ValidateSet('Plan','Install','Uninstall','Scan','CleanResiduals')][string]$Action,[Parameter(Mandatory=$true)][string]$InstallRoot,[string]$SetupExecutable,[string]$OperationRoot,[int]$ManagerPid,[string]$ScanReport)
$ErrorActionPreference='Stop';[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$env:PSModulePath=Join-Path $PSHOME Modules
$package=$PSScriptRoot;$state=Join-Path $env:LOCALAPPDATA CodexDreamSkin
$script:NativeSetupExecutable=$SetupExecutable; $script:NativePackageRoot=$package
. (Join-Path $package 'engine\scripts\common-windows.ps1')
foreach($file in @('service-removal.ps1','service-compatibility.ps1','shortcuts.ps1','uninstall-data.ps1','upgrade.ps1','native-lifecycle.ps1','install-scan.ps1')){. (Join-Path $package $file)}
function Invoke-Engine {param([string]$Operation) $ps=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe';$lines=@(& $ps -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File (Join-Path $package 'engine\scripts\manager-actions.ps1') -Action $Operation -SkillRoot (Join-Path $package engine) 2>&1);if($LASTEXITCODE -ne 0){throw ($lines -join "`n")}}
function Write-BackgroundManagerShortcut {
 param([string]$Root)
 $shell=New-Object -ComObject WScript.Shell;try{$link=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'CodexHub.lnk'));$link.TargetPath=Join-Path $Root 'CodexLiveBackground.exe';$link.WorkingDirectory=$Root;$link.IconLocation=(Join-Path $Root 'CodexBackground.ico')+',0';$link.Save()}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)}
}
function Invoke-BackgroundNativeActivate {
 param([string]$Root,[int]$ManagerPid)
 try{Write-BackgroundManagerShortcut -Root $Root}catch{[Console]::Out.WriteLine('程序文件已安装；桌面入口暂未创建：'+$_.Exception.Message)}
 $installs=@();try{$installs=@(Get-DreamSkinRegisteredCodexInstalls)}catch{[Console]::Out.WriteLine('Codex 检测暂不可用：'+$_.Exception.Message)}
 if(-not $installs.Count){
  [Console]::Out.WriteLine('CodexHub 已安装。尚未检测到官方 Codex，安装 Codex 后可在服务与设置中装载服务。');return
 }
 $ps=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe';& $ps -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File (Join-Path $Root control.ps1) -Action Install -ManagerPid $ManagerPid
 if($LASTEXITCODE -ne 0){[Console]::Out.WriteLine('程序安装完成，但背景服务尚未就绪。请在服务与设置中重试装载；具体原因见以上运行记录。')}
}
try{if($Action -eq 'Scan'){Invoke-BackgroundDeepScan -InstallRoot $InstallRoot -PackageRoot $package -OperationRoot $OperationRoot;exit 0};if($Action -eq 'Plan'){[Console]::Out.WriteLine('DREAM_PLAN:'+((Get-BackgroundPlan -InstallRoot $InstallRoot -PackageRoot $package)|ConvertTo-Json -Depth 6 -Compress));exit 0};$mutex=[Threading.Mutex]::new($false,('Local\CodexLiveBackground.NativeLifecycle.'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value));$owned=$false;try{try{$owned=$mutex.WaitOne(30000)}catch [Threading.AbandonedMutexException]{$owned=$true};if(-not $owned){throw '另一个安装或卸载操作正在进行。'};if($Action -eq 'CleanResiduals'){Invoke-BackgroundResidualCleanup -ScanReport $ScanReport -PackageRoot $package -OperationRoot $OperationRoot -ManagerPid $ManagerPid}else{Invoke-BackgroundNativeAction -Action $Action -InstallRoot $InstallRoot -StateRoot $state -PackageRoot $package -SetupExecutable $SetupExecutable -OperationRoot $OperationRoot -ManagerPid $ManagerPid}}finally{if($owned){$mutex.ReleaseMutex()};$mutex.Dispose()}}catch{[Console]::Error.WriteLine($_.Exception.Message);exit 1}
