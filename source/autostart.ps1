function Enable-YzBackgroundAutostart {
  param([string]$StartupRoot)
  $hook=Join-Path $StartupRoot 'DreamSkinPortHook.exe'
  if(-not (Test-Path -LiteralPath $hook -PathType Leaf)){throw '后台启动组件缺失，请重新装载服务。'}
  $taskName='CodexDreamSkinPort'
  $existing=Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
  if($existing -and (@($existing.Actions).Count -ne 1 -or -not (Test-DreamSkinPathEqual -Left $existing.Actions[0].Execute -Right $hook))){throw '同名后台任务不属于当前安装，已保留原任务。'}
  $user=[Security.Principal.WindowsIdentity]::GetCurrent().Name
  $action=New-ScheduledTaskAction -Execute $hook -Argument '--guard' -WorkingDirectory $StartupRoot
  $trigger=New-ScheduledTaskTrigger -AtLogOn -User $user
  $trigger.Delay='PT5S'
  $principal=New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
  $settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
  if($existing){
    $null=Set-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings
  }else{
    $null=Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings
  }
  $null=Enable-ScheduledTask -TaskName $taskName
  # The native helper holds a per-user mutex, so both login entries still run
  # exactly one guard. The Run entry also covers delayed Task Scheduler startup.
  $runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
  $command='"'+$hook+'" --guard'
  if(-not (Test-Path -LiteralPath $runKey)){$null=New-Item -Path $runKey -Force}
  $null=New-ItemProperty -LiteralPath $runKey -Name CodexDreamSkinPort -Value $command -PropertyType String -Force
  $startupLink=Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex 动态背景服务.lnk'
  $shell=New-Object -ComObject WScript.Shell
  try{
    if(Test-Path -LiteralPath $startupLink){$old=$shell.CreateShortcut($startupLink);if(-not (Test-DreamSkinPathEqual -Left $old.TargetPath -Right $hook)){throw '同名启动快捷方式属于其他程序。'}}
    $link=$shell.CreateShortcut($startupLink);$link.TargetPath=$hook;$link.Arguments='--guard';$link.WorkingDirectory=$StartupRoot;$link.WindowStyle=7;$link.Description='Codex 背景守护与自动恢复';$link.Save()
  }finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)}
  $startupApproval='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder'
  if(Test-Path -LiteralPath $startupApproval){Remove-ItemProperty -LiteralPath $startupApproval -Name 'Codex 动态背景服务.lnk' -ErrorAction SilentlyContinue}
  $approved='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
  if(Test-Path -LiteralPath $approved){Remove-ItemProperty -LiteralPath $approved -Name CodexDreamSkinPort -ErrorAction SilentlyContinue}
  $current=Get-ScheduledTask -TaskName $taskName
  if($current.State -ne 'Running'){Start-ScheduledTask -TaskName $taskName}
  $guard=Wait-DreamBackgroundService -StartupRoot $StartupRoot -Seconds 30
  return [pscustomobject]@{enabled=$true;task=$taskName;loginTrigger=$true;runFallback=$true;startupFolder=$true;codexLaunchRecovery=$true;guardPid=$guard.pid}
}

function Get-YzBackgroundAutostart {
  param([string]$StartupRoot)
  $task=Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue
  $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
  $name=[Security.Principal.WindowsIdentity]::GetCurrent().Name
  $login=@($task.Triggers|Where-Object {"$($_.CimClass.CimClassName)" -eq 'MSFT_TaskLogonTrigger' -and $_.Enabled -ne $false -and (-not $_.UserId -or $_.UserId -in @($sid,$name))}).Count -gt 0
  $expected='"'+(Join-Path $StartupRoot 'DreamSkinPortHook.exe')+'" --guard'
  $run=(Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name CodexDreamSkinPort -ErrorAction SilentlyContinue).CodexDreamSkinPort
  $fallback=$run -ceq $expected
  $approved='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
  $approval=(Get-ItemProperty -LiteralPath $approved -Name CodexDreamSkinPort -ErrorAction SilentlyContinue).CodexDreamSkinPort
  if($approval -and $approval.Length -gt 0 -and $approval[0] -in @(3,7)){$fallback=$false}
  $running=$false
  try{
    Initialize-DreamSkinNativeInspection
    $guard=[IO.File]::ReadAllText((Join-Path $StartupRoot 'guard-state.json'))|ConvertFrom-Json
    $running=[bool]($guard.registered -and $guard.pid -and
      [CodexDreamSkin.NativeInspection]::CurrentUser([int]$guard.pid) -and
      (Test-DreamSkinPathEqual -Left ([CodexDreamSkin.NativeInspection]::ProcessPath([int]$guard.pid)) -Right (Join-Path $StartupRoot 'DreamSkinPortHook.exe')))
  }catch{}
  return [pscustomobject]@{enabled=[bool]($task -and $task.Settings.Enabled -and $login -and $fallback);loginTrigger=$login;runFallback=$fallback;running=$running}
}
