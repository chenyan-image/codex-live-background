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
function Invoke-Engine([string]$Operation, [string[]]$Extra = @()) {
  $lines = @(& $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $engine 'scripts\manager-actions.ps1') -Action $Operation -SkillRoot $engine @Extra 2>&1)
  $code = $LASTEXITCODE
  $output = ($lines | ForEach-Object { "$_" }) -join "`r`n"
  if ($code -ne 0) { throw $output }
  return $output | ConvertFrom-Json
}
function Assert-Installed {
  if (-not (Test-Path -LiteralPath (Join-Path $installRoot 'installed.json'))) { throw '请先点击“安装 / 启用”。' }
  if (-not (Test-Path -LiteralPath (Join-Path $startup 'DreamSkinPortHook.exe'))) { throw '启动组件缺失，请重新安装。' }
  $task = Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue
  if (-not $task -or $task.State -ne 'Running') { throw '自动恢复服务没有运行，请点击“安装 / 启用”修复。' }
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
      if ($task -and -not (Test-Path -LiteralPath (Join-Path $installRoot 'installed.json'))) {
        throw '检测到已有 Dream Skin 启动服务。请先在原安装中移除启动设置，再安装此包，避免覆盖现有配置。'
      }
      if ($task) {
        & $ps -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $startup 'register-port.ps1') -Action Remove | Out-Null
        if ($LASTEXITCODE -ne 0) { throw '旧服务未能停止，安装已中止。' }
        $saved = Read-DreamSkinState -Path (Join-Path $stateRoot 'state.json')
        if ($saved) { $null = Stop-DreamSkinRecordedInjector -State $saved }
      }
      New-Item -ItemType Directory -Force -Path $installRoot,$stateRoot,$startup | Out-Null
      if ([IO.Path]::GetFullPath($root).TrimEnd('\') -ine [IO.Path]::GetFullPath($installRoot).TrimEnd('\')) {
        foreach ($name in @('CodexLiveBackground.exe','control.ps1','engine','startup','LICENSE','THIRD_PARTY_NOTICES.md','使用说明.txt')) {
          Copy-Item -LiteralPath (Join-Path $root $name) -Destination $installRoot -Recurse -Force
        }
      }
      foreach ($name in @('DreamSkinPortHook.exe','monitor-skin.ps1','register-port.ps1')) {
        Copy-Item -LiteralPath (Join-Path $installRoot ('startup\' + $name)) -Destination $startup -Force
      }
      [IO.File]::WriteAllText((Join-Path $stateRoot 'keep-codex-open'), 'Attach only. Never close a running Codex.')
      [IO.File]::WriteAllText((Join-Path $stateRoot 'startup-animation.disabled'), 'Startup animation disabled.')
      [IO.File]::WriteAllText((Join-Path $installRoot 'installed.json'), ('{"version":"1.0.0","installedAt":"' + [DateTime]::UtcNow.ToString('o') + '"}'))
      & $ps -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $startup 'register-port.ps1') -Action Install | Out-Null
      if ($LASTEXITCODE -ne 0) { throw '启动服务注册失败。请在 Windows 任务计划程序中检查当前用户的权限，然后重新安装。' }
      $shell = New-Object -ComObject WScript.Shell
      try {
        $link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Codex 动态背景.lnk'))
        $link.TargetPath = Join-Path $installRoot 'CodexLiveBackground.exe'
        $link.WorkingDirectory = $installRoot
        $link.Save()
      } finally { [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null }
      Write-Output ('安装完成，已识别 Codex {0}。首次启用：保存聊天输入，手动完全退出 Codex，再点击“打开 Codex”。以后更换背景无需退出。' -f $codex.Version)
    }
    'Apply' {
      Assert-Installed
      if (-not $Media -or -not (Test-Path -LiteralPath $Media -PathType Leaf)) { throw '请选择本地视频或 GIF 文件。' }
      if ([IO.Path]::GetExtension($Media).ToLowerInvariant() -notin @('.mp4','.gif','.png','.jpg','.jpeg','.webp')) { throw '仅支持 MP4、GIF、PNG、JPG、WebP。' }
      if ((Get-Item -LiteralPath $Media).Length -gt 128MB) { throw '素材超过 128 MiB。请先压缩到限制以内；本工具不会修改原始素材。' }
      $codex = Get-DreamSkinCodexInstall
      $identity = Get-DreamSkinVerifiedCdpIdentity -Port 9335 -Codex $codex
      if ($null -eq $identity) { throw '尚未连接到 Codex。首次启用请保存输入并手动完全退出 Codex，然后点击“打开 Codex”；端口 9335 必须空闲。' }
      $result = Invoke-Engine 'ApplyTheme' @('-ImagePath',$Media,'-Name',[IO.Path]::GetFileNameWithoutExtension($Media),'-ThemeId',('custom-' + [guid]::NewGuid().ToString('N')),'-Category','custom','-TaskMode','full','-SurfaceOpacity',$Opacity.ToString([Globalization.CultureInfo]::InvariantCulture))
      if (-not $result.rendererApplied) { throw '素材已保存，但窗口应用未通过验证。请切回 Codex 主窗口后重试。' }
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
      Remove-Item -LiteralPath (Join-Path $installRoot 'installed.json') -Force -ErrorAction SilentlyContinue
      Write-Output '背景及自动启动已停用。当前聊天继续保留。关闭本工具后，可手动删除 LocalAppData\Programs\CodexLiveBackground；素材和独立登录目录保留在 LocalAppData\CodexDreamSkin。'
    }
    'Status' {
      $codex = Get-DreamSkinCodexInstall
      $task = Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue
      $identity = Get-DreamSkinVerifiedCdpIdentity -Port 9335 -Codex $codex
      $themePath = Join-Path $stateRoot 'active-theme\theme.json'
      $themeName = '尚未设置'
      if (Test-Path -LiteralPath $themePath) { $themeName = (Get-Content -LiteralPath $themePath -Raw | ConvertFrom-Json).name }
      Write-Output ("Codex：{0}`r`n自动恢复服务：{1}`r`n窗口连接：{2}`r`n当前背景：{3}" -f $codex.Version, $(if ($task -and $task.State -eq 'Running') {'运行中'} else {'未启用'}), $(if ($identity) {'已验证 127.0.0.1:9335'} else {'未连接'}), $themeName)
    }
  }
} catch {
  [Console]::Error.WriteLine($_.Exception.Message)
  exit 1
}
