function Remove-DreamBackgroundSkin {
  param([string]$InstallRoot,[string]$StateRoot,[string]$PackageRoot)
  $startupRoot=Join-Path $StateRoot 'startup'
  $hook=Join-Path $startupRoot 'DreamSkinPortHook.exe'
  $task=Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue
  # Verify ownership before changing any persistent settings.
  if($task){$null=Get-DreamBackgroundServicePlan -Task $task -StartupRoot $startupRoot -ProfilesPath (Join-Path $PackageRoot 'service-compatibility.json')}
  $runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
  $expected='"'+$hook+'" --guard'
  $run=(Get-ItemProperty -LiteralPath $runKey -Name CodexDreamSkinPort -ErrorAction SilentlyContinue).CodexDreamSkinPort
  if($run -and $run -cne $expected){throw '同名自启动入口不属于当前背景服务，已保留原设置。'}
  $liveFailure=$null
  if(Test-Path -LiteralPath $StateRoot){
    try{$null=Invoke-Engine 'Pause'}catch{$liveFailure=$_.Exception.Message}
    # Persist pause even when a disconnected renderer cannot acknowledge removal.
    . (Join-Path $PackageRoot 'engine\scripts\theme-windows.ps1')
    $null=Set-DreamSkinPaused -Paused $true -StateRoot $StateRoot
  }
  if(Test-Path -LiteralPath $startupRoot){
    [IO.File]::WriteAllText((Join-Path $startupRoot 'stop-guard'),'User removed the skin and background startup.')
  }
  if($task){
    $powershell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $startupRoot 'register-port.ps1') -Action Remove | Out-Null
    if($LASTEXITCODE -ne 0){throw '皮肤已暂停，但后台启动设置未完全移除，请重试。'}
  }
  if($run -ceq $expected){Remove-ItemProperty -LiteralPath $runKey -Name CodexDreamSkinPort -ErrorAction Stop}
  . (Join-Path $PackageRoot 'engine\scripts\theme-windows.ps1')
  $saved=Read-DreamSkinState -Path (Join-Path $StateRoot 'state.json')
  if($saved){$null=Stop-DreamSkinRecordedInjector -State $saved}
  Remove-Item -LiteralPath (Join-Path $InstallRoot 'installed.json') -Force -ErrorAction SilentlyContinue
  # Keep the manager shortcut so the user can easily re-enable the skin later.
  if($liveFailure){throw ('自启动和后台恢复已移除，素材已保留；当前窗口未确认卸下皮肤。可再次点击卸载皮肤，或在下次手动打开 Codex 时恢复原界面。原因：'+$liveFailure)}
  Write-Output '皮肤已卸载，自启动与后台恢复已关闭。Codex 聊天继续保留，原素材和独立登录资料仍在；以后可点击“装载服务”重新启用。'
}
