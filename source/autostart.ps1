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
  # Keep one documented login task; retire only this application's redundant entries.
  $runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
  $expected='"'+$hook+'" --guard'
  $run=(Get-ItemProperty -LiteralPath $runKey -Name CodexDreamSkinPort -ErrorAction SilentlyContinue).CodexDreamSkinPort
  if($run -ceq $expected){Remove-ItemProperty -LiteralPath $runKey -Name CodexDreamSkinPort -ErrorAction Stop}
  if(Get-Command Get-DreamBackgroundStartupLink -ErrorAction SilentlyContinue){$link=Get-DreamBackgroundStartupLink -StartupRoot $StartupRoot;if($link){Remove-Item -LiteralPath $link -Force -ErrorAction Stop}}
  $current=Get-ScheduledTask -TaskName $taskName
  if($current.State -ne 'Running'){Start-ScheduledTask -TaskName $taskName}
  $guard=Wait-DreamBackgroundService -StartupRoot $StartupRoot -Seconds 30
  return [pscustomobject]@{enabled=$true;task=$taskName;loginTrigger=$true;runFallback=$false;startupFolder=$false;codexLaunchRecovery=$true;guardPid=$guard.pid}
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
  return [pscustomobject]@{enabled=[bool]($task -and $task.Settings.Enabled -and $login);loginTrigger=$login;runFallback=$fallback;running=$running}
}
