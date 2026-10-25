function Assert-DreamBackgroundTree {
  param([string]$Path,[string]$Parent)
  $root=[IO.Path]::GetFullPath($Parent).TrimEnd('\')
  $target=[IO.Path]::GetFullPath($Path).TrimEnd('\')
  if($root -eq [IO.Path]::GetPathRoot($root).TrimEnd('\') -or -not $target.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw '服务清理路径超出应用数据目录，已停止。'}
  $ancestors=@($root);$cursor=$root
  foreach($component in $target.Substring($root.Length+1).Split('\')){$cursor=Join-Path $cursor $component;$ancestors+=@($cursor)}
  foreach($ancestor in $ancestors){
    if(Test-Path -LiteralPath $ancestor){if((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw '服务目录包含链接或联接，未执行清理。'}}
  }
  if(-not (Test-Path -LiteralPath $target)){return}
  $pending=[Collections.Generic.Stack[string]]::new();$pending.Push($target);$count=0
  while($pending.Count){
    foreach($item in Get-ChildItem -LiteralPath $pending.Pop() -Force){
      if(++$count -gt 20000){throw '服务目录内容异常，未执行清理。'}
      if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw '服务目录包含链接或联接，未执行清理。'}
      if($item.PSIsContainer){$pending.Push($item.FullName)}
    }
  }
}
function Assert-DreamBackgroundTaskOwner {
  param($Task,[string]$StartupRoot)
  if(-not $Task){return}
  $actions=@($Task.Actions);$hook=Join-Path $StartupRoot 'DreamSkinPortHook.exe'
  if($actions.Count -ne 1 -or -not (Test-DreamSkinPathEqual -Left "$($actions[0].Execute)" -Right $hook) -or "$($actions[0].Arguments)".Trim() -cne '--guard'){throw '同名计划任务属于其他程序，已保留原设置。'}
  $identity=[Security.Principal.WindowsIdentity]::GetCurrent();$owner="$($Task.Principal.UserId)"
  try{if($owner -notmatch '^S-1-'){$owner=([Security.Principal.NTAccount]::new($owner)).Translate([Security.Principal.SecurityIdentifier]).Value}}catch{throw '无法验证后台任务的用户，未卸载。'}
  if($owner -ine $identity.User.Value -or "$($Task.Principal.LogonType)" -ne 'Interactive'){throw '后台任务不属于当前用户，未卸载。'}
}
function Get-DreamBackgroundPackageHooks {
  param([string]$StartupRoot)
  $base='HKCU:\Software\Microsoft\Windows\CurrentVersion\PackagedAppXDebug'
  if(-not (Test-Path -LiteralPath $base)){return}
  $expected=Join-Path $StartupRoot 'DreamSkinPortHook.exe'
  foreach($key in Get-ChildItem -LiteralPath $base){
    if($key.PSChildName -notmatch '^OpenAI\.Codex_\d+\.\d+\.\d+\.\d+_x64__2p2nqsd0c76g0$'){continue}
    $debugger="$( $key.GetValue('') )".Trim().Trim('"')
    if(Test-DreamSkinPathEqual -Left $debugger -Right $expected){[pscustomobject]@{Package=$key.PSChildName;Key=$key.PSPath}}
  }
}
function Invoke-DreamBackgroundNativeDebugRemoval {
  param([string]$Package)
  if(-not ('CodexBackground.DebugCleanup' -as [type])){
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
namespace CodexBackground {
 [ComImport,Guid("F27C3930-8029-4AD1-94E3-3DBA417810C1"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IPackageDebug { [PreserveSig] int EnableDebugging([MarshalAs(UnmanagedType.LPWStr)]string package,[MarshalAs(UnmanagedType.LPWStr)]string debugger,IntPtr environment); [PreserveSig] int DisableDebugging([MarshalAs(UnmanagedType.LPWStr)]string package); }
 public static class DebugCleanup { public static bool CheckRemovalResult(int result) { if(result==unchecked((int)0x80070490))return false; Marshal.ThrowExceptionForHR(result);return true; } public static bool Remove(string package) { object settings=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("B1AEC16F-2383-4852-B0E9-8F0B1DC66B4D"))); try { return CheckRemovalResult(((IPackageDebug)settings).DisableDebugging(package)); } finally { Marshal.ReleaseComObject(settings); } } }
}
"@
  }
  return [CodexBackground.DebugCleanup]::Remove($Package)
}
function Disable-DreamBackgroundPackageHook {
  param([string]$Package,[string]$StartupRoot)
  if(Invoke-DreamBackgroundNativeDebugRemoval -Package $Package){return}
  # Codex updates can remove an old package while leaving our old debugger key.
  # Only the specific already-missing package case reaches this branch.
  if($Package -notmatch '^OpenAI\.Codex_\d+\.\d+\.\d+\.\d+_x64__2p2nqsd0c76g0$' -or -not $StartupRoot){throw '无法验证旧启动挂接，已保留原设置。'}
  $key='HKCU:\Software\Microsoft\Windows\CurrentVersion\PackagedAppXDebug\'+$Package
  if(-not(Test-Path -LiteralPath $key)){return}
  $entry=Get-Item -LiteralPath $key -ErrorAction Stop
  $debugger="$($entry.GetValue(''))".Trim().Trim('"')
  if(-not(Test-DreamSkinPathEqual -Left $debugger -Right (Join-Path $StartupRoot 'DreamSkinPortHook.exe')) -or $entry.SubKeyCount -ne 0){throw '旧启动挂接已变化，未移除其他程序的设置。'}
  Remove-Item -LiteralPath $key -Force -ErrorAction Stop
}
function Get-DreamBackgroundWorkers {
  param([string]$StartupRoot)
  Initialize-DreamSkinNativeInspection
  $hook=Join-Path $StartupRoot 'DreamSkinPortHook.exe'
  $monitor=Join-Path $StartupRoot 'monitor-skin.ps1'
  $ps=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
  foreach($item in @(Get-CimInstance Win32_Process -Filter "Name='DreamSkinPortHook.exe' OR Name='powershell.exe'")){
    try{
      if(-not [CodexDreamSkin.NativeInspection]::CurrentUser([int]$item.ProcessId)){continue}
      $path=[CodexDreamSkin.NativeInspection]::ProcessPath([int]$item.ProcessId)
      $kind=if(Test-DreamSkinPathEqual -Left $path -Right $hook){if(Test-DreamSkinCommandLineToken -CommandLine "$($item.CommandLine)" -Token '--guard'){'guard'}else{'activation'}}elseif((Test-DreamSkinPathEqual -Left $path -Right $ps) -and (Test-DreamSkinCommandLineToken -CommandLine "$($item.CommandLine)" -Token $monitor)){'monitor'}else{continue}
      $process=Get-Process -Id ([int]$item.ProcessId) -ErrorAction Stop
      [pscustomobject]@{ProcessId=$process.Id;StartedTicks=$process.StartTime.ToUniversalTime().Ticks;Executable=$path;Kind=$kind}
    }catch{if(Get-Process -Id ([int]$item.ProcessId) -ErrorAction SilentlyContinue){throw}}
  }
}
function Stop-DreamBackgroundWorker {
  param($Identity)
  if($Identity.Kind -eq 'activation'){throw 'Codex 正在启动，等待主窗口出现后重试服务操作。'}
  $process=Get-Process -Id $Identity.ProcessId -ErrorAction SilentlyContinue
  if(-not $process -or $process.StartTime.ToUniversalTime().Ticks -ne $Identity.StartedTicks){return}
  if(-not [CodexDreamSkin.NativeInspection]::CurrentUser($process.Id) -or -not (Test-DreamSkinPathEqual -Left ([CodexDreamSkin.NativeInspection]::ProcessPath($process.Id)) -Right $Identity.Executable)){throw '后台进程身份已变化，未终止该进程。'}
  Stop-Process -InputObject $process -Force -ErrorAction Stop
  if(-not $process.WaitForExit(15000)){throw '后台进程未确认退出，请重试。'}
}
function Get-DreamBackgroundStartupLink {
  param([string]$StartupRoot)
  $path=Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex 动态背景服务.lnk'
  if(-not (Test-Path -LiteralPath $path)){return}
  $shell=New-Object -ComObject WScript.Shell
  try{
    $link=$shell.CreateShortcut($path)
    if(-not (Test-DreamSkinPathEqual -Left $link.TargetPath -Right (Join-Path $StartupRoot 'DreamSkinPortHook.exe')) -or $link.Arguments.Trim() -cne '--guard'){throw '同名登录启动快捷方式属于其他程序，未修改。'}
    return $path
  }finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)}
}
function Remove-DreamBackgroundService {
  param([string]$InstallRoot,[string]$StateRoot,[string]$PackageRoot,[switch]$ForUpgrade,[switch]$AllowDisconnected,[ValidateRange(0,30)][int]$GraceSeconds=12)
  $startupRoot=Join-Path ([IO.Path]::GetFullPath($StateRoot)) 'startup'
  Assert-DreamBackgroundTree -Path $startupRoot -Parent $StateRoot
  $task=Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue
  Assert-DreamBackgroundTaskOwner -Task $task -StartupRoot $startupRoot
  $runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
  $run=(Get-ItemProperty -LiteralPath $runKey -Name CodexDreamSkinPort -ErrorAction SilentlyContinue).CodexDreamSkinPort
  $expected='"'+(Join-Path $startupRoot 'DreamSkinPortHook.exe')+'" --guard'
  if($run -and $run -cne $expected){throw '同名自启动入口属于其他程序，已保留原设置。'}
  $startupLink=Get-DreamBackgroundStartupLink -StartupRoot $startupRoot
  $liveFailure=$null
  . (Join-Path $PackageRoot 'engine\scripts\theme-windows.ps1')
  if(-not $ForUpgrade -and (Test-Path -LiteralPath $StateRoot)){
    try{$null=Invoke-Engine 'Pause'}catch{$liveFailure=$_.Exception.Message}
    $null=Set-DreamSkinPaused -Paused $true -StateRoot $StateRoot
  }
  $operationLock=Enter-DreamSkinOperationLock -TimeoutMilliseconds 180000
  try{
  if(-not $ForUpgrade -and (Get-Command Restore-DreamBackgroundShortcuts -ErrorAction SilentlyContinue)){Restore-DreamBackgroundShortcuts -StateRoot $StateRoot}
  if($task){$null=Disable-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction Stop;Unregister-ScheduledTask -TaskName CodexDreamSkinPort -Confirm:$false -ErrorAction Stop}
  if($run){Remove-ItemProperty -LiteralPath $runKey -Name CodexDreamSkinPort -ErrorAction Stop}
  if($startupLink){Remove-Item -LiteralPath $startupLink -Force -ErrorAction Stop}
  $approved='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
  if(Test-Path -LiteralPath $approved){Remove-ItemProperty -LiteralPath $approved -Name CodexDreamSkinPort -ErrorAction SilentlyContinue}
  $folderApproval='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder'
  if(Test-Path -LiteralPath $folderApproval){Remove-ItemProperty -LiteralPath $folderApproval -Name 'Codex 动态背景服务.lnk' -ErrorAction SilentlyContinue}
  if(Test-Path -LiteralPath $startupRoot){[IO.File]::WriteAllText((Join-Path $startupRoot 'stop-guard'),'Background service removal requested.')}
  $workers=@(Get-DreamBackgroundWorkers -StartupRoot $startupRoot)
  if($workers.Count){
    [Console]::Out.WriteLine('正在等待后台守护和监控退出…')
    $deadline=[DateTime]::UtcNow.AddSeconds($GraceSeconds)
    do{$remaining=@(Get-DreamBackgroundWorkers -StartupRoot $startupRoot);if(-not $remaining.Count){break};Start-Sleep -Milliseconds 250}while([DateTime]::UtcNow -lt $deadline)
    foreach($worker in @(Get-DreamBackgroundWorkers -StartupRoot $startupRoot)){Stop-DreamBackgroundWorker -Identity $worker}
  }
  foreach($hook in @(Get-DreamBackgroundPackageHooks -StartupRoot $startupRoot)){Disable-DreamBackgroundPackageHook -Package $hook.Package -StartupRoot $startupRoot}
  $saved=Read-DreamSkinState -Path (Join-Path $StateRoot 'state.json')
  if($saved){$null=Stop-DreamSkinRecordedInjector -State $saved}
  if(Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue){throw '后台任务尚未完全移除，请重试。'}
  if(@(Get-DreamBackgroundPackageHooks -StartupRoot $startupRoot).Count -or @(Get-DreamBackgroundWorkers -StartupRoot $startupRoot).Count){throw '后台启动挂接或进程尚未移除，请重试。'}
  # Recheck the whole tree immediately before the only recursive deletion.
  Assert-DreamBackgroundTree -Path $startupRoot -Parent $StateRoot
  if(Test-Path -LiteralPath $startupRoot){Remove-Item -LiteralPath $startupRoot -Recurse -Force -ErrorAction Stop}
  if(-not $ForUpgrade -and -not $liveFailure){Remove-Item -LiteralPath (Join-Path $StateRoot 'state.json') -Force -ErrorAction SilentlyContinue}
  Remove-Item -LiteralPath (Join-Path $InstallRoot 'installed.json') -Force -ErrorAction SilentlyContinue
  if($liveFailure){$message='后台服务、自启动和服务文件已移除；当前窗口未确认卸下皮肤。聊天与素材保留。原因：'+$liveFailure;if($AllowDisconnected){Write-Output $message;return};throw $message}
  if($ForUpgrade){Write-Output '旧后台服务已退出，正在迁移到新版。'}else{Write-Output '背景服务已彻底卸载：自启动、启动挂接、守护、监控和服务目录已清除。管理器、壁纸及登录资料保留；可随时重新装载服务。'}
  }finally{Exit-DreamSkinOperationLock -Mutex $operationLock}
}
