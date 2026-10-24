param([ValidateSet('Install','Remove')][string]$Action = 'Install', [switch]$RefreshOnly)
$ErrorActionPreference = 'Stop'
$env:PSModulePath = Join-Path $PSHOME 'Modules'
$stateRoot = Join-Path $env:LOCALAPPDATA 'CodexDreamSkin'
$hook = Join-Path $PSScriptRoot 'DreamSkinPortHook.exe'
$taskName = 'CodexDreamSkinPort'
$stopMarker = Join-Path $PSScriptRoot 'stop-guard'
$guardStatePath = Join-Path $PSScriptRoot 'guard-state.json'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$legacyRun = (Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).CodexDreamSkinPort
if ($legacyRun -and $legacyRun.Contains($PSCommandPath)) {
  Remove-ItemProperty -LiteralPath $runKey -Name CodexDreamSkinPort
}
function Stop-PortGuard {
  [IO.File]::WriteAllText($stopMarker, 'Requested removal of the port startup entrance.')
  $deadline = [DateTime]::UtcNow.AddSeconds(20)
  do {
    $stopped = $false
    if (Test-Path -LiteralPath $guardStatePath) {
      $guard = [IO.File]::ReadAllText($guardStatePath) | ConvertFrom-Json
      $stopped = -not $guard.registered
      if (-not $stopped -and -not (Get-Process -Id $guard.pid -ErrorAction SilentlyContinue)) { $stopped = $true }
    } else { $stopped = $true }
    if ($stopped) { return }
    Start-Sleep -Milliseconds 250
  } while ([DateTime]::UtcNow -lt $deadline)
  throw 'The port startup guard did not confirm removal.'
}
if ($Action -eq 'Install') {
  if (-not (Test-Path -LiteralPath $hook -PathType Leaf)) { throw 'The port helper is missing.' }
  $existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
  if ($existingTask -and $existingTask.Actions.Execute -ne $hook) { throw 'A different program already uses this task name.' }
  if (-not $existingTask) { Stop-PortGuard }
  Remove-Item -LiteralPath $stopMarker -Force -ErrorAction SilentlyContinue
  [IO.File]::WriteAllText((Join-Path $stateRoot 'keep-codex-open'), 'Theme operations attach without restarting Codex.')
  $userName = [Security.Principal.WindowsIdentity]::GetCurrent().Name
  $taskAction = New-ScheduledTaskAction -Execute $hook -Argument '--guard' -WorkingDirectory $PSScriptRoot
  $principal = New-ScheduledTaskPrincipal -UserId $userName -LogonType Interactive -RunLevel Limited
  $trigger = New-ScheduledTaskTrigger -AtLogOn -User $userName
  $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
  $null = Register-ScheduledTask -TaskName $taskName -Action $taskAction -Principal $principal -Trigger $trigger -Settings $settings -Force
  Start-ScheduledTask -TaskName $taskName
  $deadline = [DateTime]::UtcNow.AddSeconds(20)
  $confirmed = $false
  do {
    if (Test-Path -LiteralPath $guardStatePath) {
      $guardState = [IO.File]::ReadAllText($guardStatePath) | ConvertFrom-Json
      if ($guardState.registered -and (Get-Process -Id $guardState.pid -ErrorAction SilentlyContinue)) { $confirmed = $true; break }
    }
    Start-Sleep -Milliseconds 250
  } while ([DateTime]::UtcNow -lt $deadline)
  if (-not $confirmed) { throw 'The persistent port startup guard could not be confirmed.' }
} else {
  Stop-PortGuard
  Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
  Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
  $guardState = if (Test-Path -LiteralPath $guardStatePath) { [IO.File]::ReadAllText($guardStatePath) | ConvertFrom-Json } else { $null }
}
$report = [ordered]@{ time = [DateTime]::UtcNow.ToString('o'); action = $Action; task = $taskName; port = 9335; helper = $hook; closesExistingCodex = $false; startupAnimationEnabled = $false; guard = $guardState }
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'registration.json'), ($report | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
$report | ConvertTo-Json -Depth 5
