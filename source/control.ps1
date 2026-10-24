[CmdletBinding()]
param(
  [ValidateSet('Install','Apply','Launch','Pause','Resume','Remove','Status','Check')][string]$Action = 'Status',
  [string]$Media,
  [ValidateRange(0.1,1.0)][double]$Opacity = 0.72
)
$ErrorActionPreference = 'Stop'
$env:PSModulePath = Join-Path $PSHOME 'Modules'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$root = $PSScriptRoot
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\CodexLiveBackground'
$stateRoot = Join-Path $env:LOCALAPPDATA 'CodexDreamSkin'
$startup = Join-Path $stateRoot 'startup'
$ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$engine = Join-Path $root 'engine'
. (Join-Path $engine 'scripts\common-windows.ps1')
. (Join-Path $root 'service-compatibility.ps1')
. (Join-Path $root 'service-upgrade.ps1')
. (Join-Path $root 'autostart.ps1')
function Invoke-Engine([string]$Operation, [string[]]$Extra = @()) {
  $lines = @(& $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $engine 'scripts\manager-actions.ps1') -Action $Operation -SkillRoot $engine @Extra 2>&1)
  $code = $LASTEXITCODE
  $output = ($lines | ForEach-Object { "$_" }) -join "`r`n"
  if ($code -ne 0) { throw $output }
  return $output | ConvertFrom-Json
}
function Assert-Installed {
  if (-not (Test-Path -LiteralPath (Join-Path $installRoot 'installed.json'))) { throw '请先点击“装载服务”。' }
  if (-not (Test-Path -LiteralPath (Join-Path $startup 'DreamSkinPortHook.exe'))) { throw '启动组件缺失，请重新安装。' }
  $status=Get-YzBackgroundAutostart -StartupRoot $startup
  if (-not $status.running) { throw '自动恢复服务没有运行，请点击“装载服务”修复。' }
}
function Ensure-YzService {
  $status=Get-YzBackgroundAutostart -StartupRoot $startup
  $installed=$null
  try{$installed=[IO.File]::ReadAllText((Join-Path $installRoot 'installed.json'))|ConvertFrom-Json}catch{}
  if($installed.version -ne '1.1.0' -or -not $status.enabled -or -not $status.running){
    $output=@(& $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'control.ps1') -Action Install 2>&1)
    if($LASTEXITCODE -ne 0){throw (($output|ForEach-Object{"$_"})-join "`r`n")}
  }
  Assert-Installed
}
function Connect-YzCodex {
  $codex=Get-DreamSkinCodexInstall
  $identity=Get-DreamSkinVerifiedCdpIdentity -Port 9335 -Codex $codex
  if($identity){return $identity}
  $registered=Get-DreamSkinVerifiedCdpIdentityForAnyRegistered -Port 9335
  if($registered){return $registered.Identity}
  $running=@(Get-DreamSkinRegisteredCodexInstalls|ForEach-Object{Get-DreamSkinCodexProcesses -Codex $_})
  if($running.Count -gt 0){throw '当前 Codex 是未开启背景连接的普通启动实例。首次启用请保存输入并手动完全退出一次，再点击“换壁纸”；之后服务会自动准备连接。'}
  if(-not (Test-DreamSkinPortAvailable -Port 9335)){throw '本机 9335 端口被其他程序占用，暂时无法连接背景。'}
  $profile=Join-Path $stateRoot 'cdp-profile'
  $null=New-Item -ItemType Directory -Path $profile -Force
  $null=Start-DreamSkinCodexDirect -Codex $codex -Arguments @('--remote-debugging-address=127.0.0.1','--remote-debugging-port=9335',('--user-data-dir='+$profile))
  $deadline=[DateTime]::UtcNow.AddSeconds(45)
  do{
    Start-Sleep -Milliseconds 500
    $identity=Get-DreamSkinVerifiedCdpIdentity -Port 9335 -Codex $codex
    if($identity){return $identity}
  }while([DateTime]::UtcNow -lt $deadline)
  throw '已请求打开 Codex，背景连接尚未就绪。等主窗口完成加载后，再点“换壁纸”。'
}
try {
  switch ($Action) {
    'Check' {
      $node = Get-DreamSkinNodeRuntime
      $codex = Get-DreamSkinCodexInstall
      Write-Output ("环境检查通过：Codex {0}，Node {1}；安装目录将自动使用当前用户路径。" -f $codex.Version, $node.Version)
    }
    'Install' {
      $null = Get-DreamSkinNodeRuntime
      $codex = Get-DreamSkinCodexInstall
      $task = Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue
      $plan = Get-DreamBackgroundServicePlan -Task $task -StartupRoot $startup -ProfilesPath (Join-Path $root 'service-compatibility.json')
      New-Item -ItemType Directory -Force -Path $installRoot,$stateRoot,$startup | Out-Null
      if ([IO.Path]::GetFullPath($root).TrimEnd('\') -ine [IO.Path]::GetFullPath($installRoot).TrimEnd('\')) {
        Copy-DreamBackgroundPackage -SourceRoot $root -DestinationRoot $installRoot
      }
      [IO.File]::WriteAllText((Join-Path $stateRoot 'keep-codex-open'), 'Attach only. Never close a running Codex.')
      [IO.File]::WriteAllText((Join-Path $stateRoot 'startup-animation.disabled'), 'Startup animation disabled.')
      $upgrade=Update-YzBackgroundService -PackageStartup (Join-Path $installRoot 'startup') -StartupRoot $startup -Mode $plan.Mode
      $autostart=Enable-YzBackgroundAutostart -StartupRoot $startup
      $guard = Wait-DreamBackgroundService -StartupRoot $startup
      $monitor=Wait-YzBackgroundMonitor -StartupRoot $startup -EngineRoot (Join-Path $installRoot 'engine') -Seconds 45
      $installation = [ordered]@{version='1.1.0';installedAt=[DateTime]::UtcNow.ToString('o');serviceMode=$plan.Mode;serviceProfile=$plan.Profile;guardPid=$guard.pid;monitorPid=$monitor.pid;autostart=$autostart;serviceUpgraded=$upgrade.changed}
      [IO.File]::WriteAllText((Join-Path $installRoot 'installed.json'), ($installation | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
      $shell = New-Object -ComObject WScript.Shell
      try {
        $desktop=[Environment]::GetFolderPath('Desktop')
        $link = $shell.CreateShortcut((Join-Path $desktop 'Codex 动态背景.lnk'))
        $link.TargetPath = Join-Path $installRoot 'CodexLiveBackground.exe'
        $link.WorkingDirectory = $installRoot
        $link.Save()
        $oldLink=Join-Path $desktop 'yz Codex 动态背景.lnk'
        if(Test-Path -LiteralPath $oldLink){
          $old=$shell.CreateShortcut($oldLink)
          if($old.TargetPath -ieq (Join-Path $installRoot 'CodexLiveBackground.exe')){Remove-Item -LiteralPath $oldLink -Force}
        }
      } finally { [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null }
      Write-Output '服务已装载，登录 Windows 后会在后台自动运行。后台监控已验证正常；以后直接点“换壁纸”，选中素材即可应用。'
    }
    'Apply' {
      if (-not $Media -or -not (Test-Path -LiteralPath $Media -PathType Leaf)) { throw '请选择本地视频或 GIF 文件。' }
      if ([IO.Path]::GetExtension($Media).ToLowerInvariant() -notin @('.mp4','.gif','.png','.jpg','.jpeg','.webp')) { throw '仅支持 MP4、GIF、PNG、JPG、WebP。' }
      if ((Get-Item -LiteralPath $Media).Length -gt 128MB) { throw '素材超过 128 MiB。请先压缩到限制以内；本工具不会修改原始素材。' }
      Ensure-YzService
      $identity=Connect-YzCodex
      $connection=Read-DreamSkinState -Path (Join-Path $stateRoot 'state.json')
      # The MP4 decoder uses a verified saved browser identity, not merely an
      # open TCP port. Establish that state before validating the first video.
      if(-not $connection -or "$($connection.browserId)" -cne "$($identity.BrowserId)" -or $connection.port -ne 9335){
        $output=@(& $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $engine 'scripts\start-dream-skin.ps1') -Port 9335 -ConnectOnly -OperationLockTimeoutMilliseconds 180000 2>&1)
        if($LASTEXITCODE -ne 0){throw (($output|ForEach-Object{"$_"})-join "`r`n")}
      }
      $needsConnect=$false
      try{
        $result = Invoke-Engine 'ApplyTheme' @('-ImagePath',$Media,'-Name',[IO.Path]::GetFileNameWithoutExtension($Media),'-ThemeId',('custom-' + [guid]::NewGuid().ToString('N')),'-Category','custom','-TaskMode','full','-SurfaceOpacity',$Opacity.ToString([Globalization.CultureInfo]::InvariantCulture))
        $needsConnect=-not $result.rendererApplied
      }catch{
        if($_.Exception.Message -notmatch 'DREAM_SKIN_LIVE_APPLY_FAILED'){throw}
        $needsConnect=$true
      }
      if($needsConnect){
        $output=@(& $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $engine 'scripts\start-dream-skin.ps1') -Port 9335 -OperationLockTimeoutMilliseconds 180000 2>&1)
        if($LASTEXITCODE -ne 0){throw (($output|ForEach-Object{"$_"})-join "`r`n")}
        $applied=Invoke-Engine 'Resume'
        if(-not $applied.rendererApplied){throw '素材已保存，当前窗口还在加载。等主窗口就绪后再点“换壁纸”。'}
      }
      if(-not $result){$result=Get-Content -LiteralPath (Join-Path $stateRoot 'active-theme\theme.json') -Raw|ConvertFrom-Json}
      Write-Output ('已应用并保存：' + $result.name + '。动态素材静音循环播放，重新打开 Codex 后会自动恢复。')
    }
    'Launch' {
      Assert-Installed
      $codex = Get-DreamSkinCodexInstall
      if ($null -ne (Get-DreamSkinVerifiedCdpIdentity -Port 9335 -Codex $codex)) { Write-Output 'Codex 已连接，可以直接选择并应用背景。'; break }
      if ((Get-DreamSkinCodexProcesses -Codex $codex).Count -gt 0) { throw 'Codex 仍在运行且尚未开启背景连接。请保存输入，手动从托盘退出 Codex，再重试。工具不会强制关闭聊天窗口。' }
      if (-not (Test-DreamSkinPortAvailable -Port 9335)) { throw '端口 9335 被其他程序占用。请先释放该端口；不要为此添加防火墙公网放行规则。' }
      $null = Start-DreamSkinCodex -Codex $codex -Arguments @()
      $deadline = [DateTime]::UtcNow.AddSeconds(30)
      do {
        Start-Sleep -Milliseconds 750
        $identity = Get-DreamSkinVerifiedCdpIdentity -Port 9335 -Codex $codex
      } while ($null -eq $identity -and [DateTime]::UtcNow -lt $deadline)
      if ($null -eq $identity) { throw 'Codex 已请求打开，但连接还未就绪。等待主窗口出现后重试；诊断文件位于 LocalAppData\CodexDreamSkin\startup。' }
      Write-Output 'Codex 已打开且背景连接已就绪。现在可以选择并应用素材。首次使用独立的背景配置目录时，可能需要重新登录。'
    }
    'Pause' { Assert-Installed; $null = Invoke-Engine 'Pause'; Write-Output '背景已暂停，窗口已恢复原貌。' }
    'Resume' { Assert-Installed; $result = Invoke-Engine 'Resume'; if (-not $result.rendererApplied) { throw '已取消暂停，等待 Codex 连接；也可打开 Codex 后再点恢复。' }; Write-Output '已恢复保存的背景。' }
    'Remove' {
      if (-not (Test-Path -LiteralPath (Join-Path $installRoot 'installed.json'))) { throw '尚未安装此工具，无需移除启动设置。' }
      $null = Invoke-Engine 'Pause'
      & $ps -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $startup 'register-port.ps1') -Action Remove | Out-Null
      if ($LASTEXITCODE -ne 0) { throw '自动启动设置未完全移除，请重试。' }
      . (Join-Path $engine 'scripts\theme-windows.ps1')
      $saved = Read-DreamSkinState -Path (Join-Path $stateRoot 'state.json')
      if ($saved) { $null = Stop-DreamSkinRecordedInjector -State $saved }
      $shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Codex 动态背景.lnk'
      Remove-Item -LiteralPath $shortcut -Force -ErrorAction SilentlyContinue
      Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Codex 动态背景.lnk') -Force -ErrorAction SilentlyContinue
      $runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
      $run=(Get-ItemProperty -LiteralPath $runKey -Name CodexDreamSkinPort -ErrorAction SilentlyContinue).CodexDreamSkinPort
      if($run -eq ('"'+(Join-Path $startup 'DreamSkinPortHook.exe')+'" --guard')){Remove-ItemProperty -LiteralPath $runKey -Name CodexDreamSkinPort}
      Remove-Item -LiteralPath (Join-Path $installRoot 'installed.json') -Force -ErrorAction SilentlyContinue
      Write-Output '背景及自动启动已停用。当前聊天继续保留。关闭本工具后，可手动删除 LocalAppData\Programs\CodexLiveBackground；素材和独立登录目录保留在 LocalAppData\CodexDreamSkin。'
    }
    'Status' {
      $codex = Get-DreamSkinCodexInstall
      $task = Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue
      $auto=Get-YzBackgroundAutostart -StartupRoot $startup
      $identity = Get-DreamSkinVerifiedCdpIdentity -Port 9335 -Codex $codex
      $themePath = Join-Path $stateRoot 'active-theme\theme.json'
      $themeName = '尚未设置'
      if (Test-Path -LiteralPath $themePath) { $themeName = (Get-Content -LiteralPath $themePath -Raw | ConvertFrom-Json).name }
      Write-Output ("登录自启动：{0}`r`n后台服务：{1}`r`n窗口连接：{2}`r`n当前壁纸：{3}`r`n点「换壁纸」，选文件后自动应用。" -f $(if($auto.enabled){'已开启'}else{'未配置完整，请点装载服务'}), $(if ($auto.running) {'运行中'} else {'未装载'}), $(if ($identity) {'已连接'} else {'等待 Codex 打开'}), $themeName)
    }
  }
} catch {
  [Console]::Error.WriteLine($_.Exception.Message)
  exit 1
}
