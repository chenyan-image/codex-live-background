function Invoke-YzBackgroundRegistration {
  param([string]$StartupRoot, [ValidateSet('Install','Remove')][string]$Action)
  $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
  $lines = @(& $powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File (Join-Path $StartupRoot 'register-port.ps1') -Action $Action 2>&1)
  if ($LASTEXITCODE -ne 0) { throw ("后台服务注册操作失败 ($Action)：" + ($lines -join "`r`n")) }
}

function Get-YzBackgroundMonitorProcesses {
  param([string]$StartupRoot, [int]$GuardProcessId)
  if ($GuardProcessId -lt 1) { return }
  $expectedScript = Join-Path $StartupRoot 'monitor-skin.ps1'
  $expectedExecutable = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
  foreach ($info in @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $GuardProcessId" -ErrorAction Stop)) {
    if ("$($info.Name)" -ine 'powershell.exe' -or
        -not (Test-DreamSkinCommandLineToken -CommandLine "$($info.CommandLine)" -Token $expectedScript)) { continue }
    $processId = [int]$info.ProcessId
    try {
      if (-not [CodexDreamSkin.NativeInspection]::CurrentUser($processId)) { continue }
      if (-not (Test-DreamSkinPathEqual -Left ([CodexDreamSkin.NativeInspection]::ProcessPath($processId)) -Right $expectedExecutable)) { continue }
      $process = Get-Process -Id $processId -ErrorAction Stop
      [pscustomobject]@{ ProcessId=$processId; StartedTicks=$process.StartTime.ToUniversalTime().Ticks }
    } catch {
      if (Get-Process -Id $processId -ErrorAction SilentlyContinue) { throw }
    }
  }
}

function Test-YzBackgroundProcessAlive {
  param($Identity)
  if ($null -eq $Identity) { return $false }
  $process = Get-Process -Id ([int]$Identity.ProcessId) -ErrorAction SilentlyContinue
  return $process -and $process.StartTime.ToUniversalTime().Ticks -eq $Identity.StartedTicks
}

function Stop-YzBackgroundServiceForUpgrade {
  param([string]$StartupRoot)
  Initialize-DreamSkinNativeInspection
  $oldGuardId = 0
  $oldGuard = $null
  $guardPath = Join-Path $StartupRoot 'guard-state.json'
  if (Test-Path -LiteralPath $guardPath -PathType Leaf) {
    $saved = [IO.File]::ReadAllText($guardPath) | ConvertFrom-Json
    if ($saved.pid) {
      $oldGuardId = [int]$saved.pid
      $process = Get-Process -Id ([int]$saved.pid) -ErrorAction SilentlyContinue
      if ($process) {
        if (-not [CodexDreamSkin.NativeInspection]::CurrentUser($process.Id) -or
            -not (Test-DreamSkinPathEqual -Left ([CodexDreamSkin.NativeInspection]::ProcessPath($process.Id)) -Right (Join-Path $StartupRoot 'DreamSkinPortHook.exe'))) {
          throw '旧启动服务的进程身份无法确认，未停止或覆盖现有组件。'
        }
        $oldGuardId = $process.Id
        $oldGuard = [pscustomobject]@{ProcessId=$process.Id; StartedTicks=$process.StartTime.ToUniversalTime().Ticks}
      }
    }
  }
  $oldMonitors = @(Get-YzBackgroundMonitorProcesses -StartupRoot $StartupRoot -GuardProcessId $oldGuardId)
  Invoke-YzBackgroundRegistration -StartupRoot $StartupRoot -Action Remove
  $deadline = [DateTime]::UtcNow.AddSeconds(180)
  while (Test-YzBackgroundProcessAlive -Identity $oldGuard) {
    if ([DateTime]::UtcNow -ge $deadline) { throw '旧后台守护服务尚未退出；现有组件已保留，请稍后重试。' }
    Start-Sleep -Milliseconds 250
  }
  # The guard may have created one final monitor immediately before noticing its stop marker.
  $oldMonitors += @(Get-YzBackgroundMonitorProcesses -StartupRoot $StartupRoot -GuardProcessId $oldGuardId)
  do {
    $remaining = @($oldMonitors | Where-Object { Test-YzBackgroundProcessAlive -Identity $_ })
    if ($remaining.Count -eq 0) { return }
    if ([DateTime]::UtcNow -ge $deadline) { throw '旧背景操作尚未退出，未强制终止操作或关闭 Codex。请稍后重新装载服务。' }
    Start-Sleep -Milliseconds 250
  } while ($true)
}

function Update-YzBackgroundService {
  param(
    [Parameter(Mandatory=$true)][string]$PackageStartup,
    [Parameter(Mandatory=$true)][string]$StartupRoot,
    [Parameter(Mandatory=$true)][ValidateSet('new','reuse')][string]$Mode
  )
  Initialize-DreamSkinNativeInspection
  $names = @('DreamSkinPortHook.exe','monitor-skin.ps1','register-port.ps1')
  $changed = $Mode -eq 'new'
  foreach ($name in $names) {
    $source = Join-Path $PackageStartup $name
    $target = Join-Path $StartupRoot $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw ('新启动组件缺失：' + $name) }
    if ($Mode -eq 'reuse') {
      if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw ('已验证的旧启动组件缺失：' + $name) }
      if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -cne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) { $changed = $true }
    }
  }
  if (-not $changed) {
    Invoke-YzBackgroundRegistration -StartupRoot $StartupRoot -Action Install
    return [pscustomobject]@{changed=$false; backup=$null}
  }
  New-Item -ItemType Directory -Path $StartupRoot -Force | Out-Null
  $backup = $null
  if ($Mode -eq 'reuse') {
    $backup = Join-Path $StartupRoot ('upgrade-backup-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backup -ErrorAction Stop | Out-Null
    foreach ($name in $names) { Copy-Item -LiteralPath (Join-Path $StartupRoot $name) -Destination (Join-Path $backup $name) -ErrorAction Stop }
  }
  $deploymentStarted = $false
  try {
    if ($Mode -eq 'reuse') { Stop-YzBackgroundServiceForUpgrade -StartupRoot $StartupRoot }
    $deploymentStarted = $true
    foreach ($name in $names) { Copy-Item -LiteralPath (Join-Path $PackageStartup $name) -Destination (Join-Path $StartupRoot $name) -Force -ErrorAction Stop }
    Invoke-YzBackgroundRegistration -StartupRoot $StartupRoot -Action Install
    return [pscustomobject]@{changed=$true; backup=$backup}
  } catch {
    $failure = $_.Exception.Message
    if ($backup) {
      try {
        if ($deploymentStarted) {
          Stop-YzBackgroundServiceForUpgrade -StartupRoot $StartupRoot
          foreach ($name in $names) { Copy-Item -LiteralPath (Join-Path $backup $name) -Destination (Join-Path $StartupRoot $name) -Force -ErrorAction Stop }
        }
        Invoke-YzBackgroundRegistration -StartupRoot $StartupRoot -Action Install
      } catch { throw ($failure + "`r`n恢复旧服务时也遇到问题：" + $_.Exception.Message + "`r`n旧组件备份：" + $backup) }
      throw ($failure + "`r`n已恢复旧后台服务。备份：" + $backup)
    }
    throw
  }
}

function Wait-YzBackgroundMonitor {
  param([Parameter(Mandatory=$true)][string]$StartupRoot, [Parameter(Mandatory=$true)][string]$EngineRoot, [int]$Seconds=45)
  if (-not (Get-Command Initialize-DreamSkinNativeInspection -ErrorAction SilentlyContinue)) { . (Join-Path $EngineRoot 'scripts\common-windows.ps1') }
  Initialize-DreamSkinNativeInspection
  $statePath = Join-Path $StartupRoot 'monitor-state.json'
  $guardPath = Join-Path $StartupRoot 'guard-state.json'
  $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
  $stableIdentity = $null
  $stableSince = [DateTime]::MinValue
  $lastReason = '没有收到新的背景监控心跳。'
  do {
    $candidate = $null
    $state = $null
    try {
      $state = [IO.File]::ReadAllText($statePath) | ConvertFrom-Json
      $guard = [IO.File]::ReadAllText($guardPath) | ConvertFrom-Json
      $heartbeat = [DateTimeOffset]::Parse("$($state.time)", [Globalization.CultureInfo]::InvariantCulture).UtcDateTime
      $age = ([DateTime]::UtcNow - $heartbeat).TotalSeconds
      if ($age -lt -5 -or $age -gt 40) { throw '背景监控心跳已过期。' }
      if (-not $guard.registered -or -not $guard.pid) { throw '背景守护服务尚未就绪。' }
      if ("$($state.phase)" -notin @('waiting-for-codex','running','applying','paused','retrying')) { throw '背景监控尚未进入工作状态。' }
      $candidate = @(Get-YzBackgroundMonitorProcesses -StartupRoot $StartupRoot -GuardProcessId ([int]$guard.pid) | Where-Object { $_.ProcessId -eq [int]$state.pid }) | Select-Object -First 1
      if (-not $candidate -or $candidate.StartedTicks -gt $heartbeat.AddSeconds(2).Ticks) { throw '无法确认背景监控进程的身份与心跳。' }
    } catch { $lastReason = $_.Exception.Message; $candidate = $null }
    if ($candidate) {
      if ("$($state.phase)" -eq 'retrying') { throw ('背景监控启动失败：' + "$($state.message)") }
      if (-not $stableIdentity -or $stableIdentity.ProcessId -ne $candidate.ProcessId -or $stableIdentity.StartedTicks -ne $candidate.StartedTicks) {
        $stableIdentity = $candidate
        $stableSince = [DateTime]::UtcNow
      } elseif (([DateTime]::UtcNow - $stableSince).TotalSeconds -ge 2 -and (Test-YzBackgroundProcessAlive -Identity $candidate)) { return $state }
    } else { $stableIdentity = $null }
    Start-Sleep -Milliseconds 250
  } while ([DateTime]::UtcNow -lt $deadline)
  throw ('背景服务未能持续运行：' + $lastReason + ' 请查看 CodexDreamSkin\startup\startup.log。')
}
