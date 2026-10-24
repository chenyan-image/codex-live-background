$ErrorActionPreference = 'Stop'
$env:PSModulePath = Join-Path $PSHOME 'Modules'
$install = Join-Path $env:LOCALAPPDATA 'Programs\CodexLiveBackground'
$scripts = Join-Path $install 'engine\scripts'
$stateRoot = Join-Path $env:LOCALAPPDATA 'CodexDreamSkin'
$stopMarker = Join-Path $PSScriptRoot 'stop-guard'
$statusPath = Join-Path $PSScriptRoot 'monitor-state.json'
$statePath = Join-Path $stateRoot 'state.json'
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$mutex = [Threading.Mutex]::new($false, "Local\CodexDreamSkin.SkinMonitor.$sid")
$acquired = $false
try {
  try { $acquired = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $acquired = $true }
  if (-not $acquired) { return }
  . (Join-Path $scripts 'common-windows.ps1')
  . (Join-Path $scripts 'theme-windows.ps1')
  $lastPhase = ''
  $lastBrowser = ''
  $lastSaved = [DateTime]::MinValue
  function Save-Monitor([string]$phase, [string]$browser, [string]$message = '') {
    if ($phase -eq $script:lastPhase -and $browser -eq $script:lastBrowser -and
        ([DateTime]::UtcNow - $script:lastSaved).TotalSeconds -lt 30) { return }
    $script:lastPhase = $phase; $script:lastBrowser = $browser; $script:lastSaved = [DateTime]::UtcNow
    $report = [ordered]@{ time = [DateTime]::UtcNow.ToString('o'); pid = $PID; phase = $phase; browserId = $browser; port = 9335; message = $message; closesExistingCodex = $false }
    [IO.File]::WriteAllText($statusPath, ($report | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
  }
  function Test-CurrentWatcher($state, [string]$browser) {
    if ($null -eq $state -or "$($state.browserId)" -cne $browser -or -not $state.injectorPid -or -not $state.injectorStartedAt) { return $false }
    try {
      $process = Get-Process -Id ([int]$state.injectorPid) -ErrorAction Stop
      if ($process.ProcessName -ine 'node') { return $false }
      if (-not (Test-DreamSkinTimestampEqual -Left $process.StartTime.ToUniversalTime().ToString('o') -Right "$($state.injectorStartedAt)")) { return $false }
      $path = [CodexDreamSkin.NativeInspection]::ProcessPath($process.Id)
      return [CodexDreamSkin.NativeInspection]::CurrentUser($process.Id) -and
        (Test-DreamSkinPathEqual -Left $path -Right "$($state.nodePath)")
    } catch { return $false }
  }
  $codex = $null; $nextInstallRefresh = [DateTime]::MinValue
  while (-not (Test-Path -LiteralPath $stopMarker)) {
    try {
      if (Test-DreamSkinPaused -StateRoot $stateRoot) {
        Save-Monitor 'paused' '' 'The user paused the skin.'
        Start-Sleep -Seconds 3
        continue
      }
      if ($null -eq $codex -or [DateTime]::UtcNow -ge $nextInstallRefresh) {
        $codex = Get-DreamSkinCodexInstall
        $nextInstallRefresh = [DateTime]::UtcNow.AddSeconds(60)
      }
      $identity = Get-DreamSkinVerifiedCdpIdentity -Port 9335 -Codex $codex
      if ($null -eq $identity) {
        Save-Monitor 'waiting-for-codex' '' 'Waiting for a verified official Codex session; Codex is never launched or closed here.'
        Start-Sleep -Seconds 3
        continue
      }
      $savedState = Read-DreamSkinState -Path $statePath
      if (Test-CurrentWatcher $savedState $identity.BrowserId) {
        Save-Monitor 'running' $identity.BrowserId
        Start-Sleep -Seconds 3
        continue
      }
      Save-Monitor 'applying' $identity.BrowserId 'Connecting the saved theme to the current verified session.'
      $output = @(& $powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File (Join-Path $scripts 'start-dream-skin.ps1') -Port 9335 -OperationLockTimeoutMilliseconds 30000 2>&1)
      $exitCode = $LASTEXITCODE
      [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'skin-startup.log'), ($output -join "`r`n"), [Text.UTF8Encoding]::new($false))
      if ($exitCode -ne 0) { throw "Skin application returned $exitCode; it will be retried without closing Codex." }
      Save-Monitor 'running' $identity.BrowserId
      Start-Sleep -Seconds 3
    } catch {
      Save-Monitor 'retrying' '' $_.Exception.Message
      Start-Sleep -Seconds 10
    }
  }
  Save-Monitor 'stopped' ''
} finally {
  if ($acquired) { $mutex.ReleaseMutex() }
  $mutex.Dispose()
}
