[CmdletBinding()]
param(
  [ValidateSet('Install','Apply','Launch','Pause','Resume','Remove','RemoveSkin','RemoveService','PrepareUpgrade','RollbackUpgrade','Status','Connection','Check')][string]$Action = 'Status',
  [string]$Media,
  [int]$ManagerPid=0,
  [switch]$AllowDisconnected,
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
. (Join-Path $root 'skin-removal.ps1')
. (Join-Path $root 'service-removal.ps1')
. (Join-Path $root 'upgrade.ps1')
. (Join-Path $root 'codex-icons.ps1')
. (Join-Path $root 'shortcuts.ps1')
function Invoke-Engine([string]$Operation, [string[]]$Extra = @()) {
  $previousErrorActionPreference = $ErrorActionPreference
  try {
    $ErrorActionPreference = 'Continue'
    $lines = @(& $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File (Join-Path $engine 'scripts\manager-actions.ps1') -Action $Operation -SkillRoot $engine @Extra 2>&1)
    $code = $LASTEXITCODE
  } finally {
    $ErrorActionPreference = $previousErrorActionPreference
  }
  $output = ($lines | ForEach-Object { "$_" }) -join "`r`n"
  if ($code -ne 0) { throw $output }
  return $output | ConvertFrom-Json
}
function Get-YzRendererHealth($Identity) {
  if(-not $Identity){return [pscustomobject]@{status='disconnected'}}
  try {
    $node=Get-DreamSkinNodeRuntime
    $output=@(& $node.Path (Join-Path $engine 'scripts\injector.mjs') --background-status --port 9335 --browser-id "$($Identity.BrowserId)" --timeout-ms 4000 2>$null)
    if($LASTEXITCODE -eq 0){return ($output -join "`n")|ConvertFrom-Json}
  }catch{}
  return [pscustomobject]@{status='unknown'}
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
  if($installed.version -ne '1.5.3' -or -not $status.enabled -or -not $status.running){
    $output=@(& $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File (Join-Path $root 'control.ps1') -Action Install -ManagerPid $ManagerPid 2>&1)
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
function Prepare-YzVideo([string]$Path,[switch]$Preview) {
  if([IO.Path]::GetExtension($Path).ToLowerInvariant() -notin @('.mp4','.webm','.gif') -or (-not $Preview -and [IO.Path]::GetExtension($Path).ToLowerInvariant() -eq '.gif')){return [pscustomobject]@{path=$Path;converted=$false}}
  $node=Get-DreamSkinNodeRuntime
  $null=New-Item -ItemType Directory -Path $stateRoot -Force
  $requestPath=Join-Path $stateRoot ('video-request-'+[guid]::NewGuid().ToString('N')+'.json')
  [IO.File]::WriteAllText($requestPath,(@{media=[IO.Path]::GetFullPath($Path);stateRoot=$stateRoot}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
  try{
    $info=[Diagnostics.ProcessStartInfo]::new()
    $info.FileName=$node.Path
    $helper=if($Preview){'scripts\preview-media.mjs'}else{'scripts\prepare-video.mjs'}
    $info.Arguments='"'+(Join-Path $engine $helper)+'" "'+$requestPath+'"'
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.StandardOutputEncoding=[Text.UTF8Encoding]::new($false);$info.StandardErrorEncoding=[Text.UTF8Encoding]::new($false)
    $process=[Diagnostics.Process]::Start($info)
    try{
      $errors=$process.StandardError.ReadToEndAsync()
      $result=$null
      while(-not $process.StandardOutput.EndOfStream){
        $line=$process.StandardOutput.ReadLine()
        if($line.StartsWith('DREAM_PROGRESS:')){[Console]::Out.WriteLine($line.Substring(15))}
        elseif($line.StartsWith('DREAM_RESULT:')){$result=$line.Substring(13)|ConvertFrom-Json}
      }
      $process.WaitForExit()
      if($process.ExitCode -ne 0){throw $errors.Result.Trim()}
      if(-not $result -or -not $result.path){throw '视频兼容处理没有返回有效结果。'}
      return $result
    }finally{$process.Dispose()}
  }finally{Remove-Item -LiteralPath $requestPath -Force -ErrorAction SilentlyContinue}
}
$upgradeMutex=$null;$upgradeOwned=$false
try {
  if($Action -in @('Install','PrepareUpgrade','RollbackUpgrade','Remove','RemoveService')){
    $upgradeMutex=[Threading.Mutex]::new($false,('Local\CodexLiveBackground.Upgrade.'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value))
    try{$upgradeOwned=$upgradeMutex.WaitOne(30000)}catch [Threading.AbandonedMutexException]{$upgradeOwned=$true}
    if(-not $upgradeOwned){throw '另一个安装或服务操作正在进行，请等待原操作完成后重试。'}
  }
  switch ($Action) {
    'Check' {
      $node = Get-DreamSkinNodeRuntime
      $codex = Get-DreamSkinCodexInstall
      Write-Output ("环境检查通过：Codex {0}，Node {1}；安装目录将自动使用当前用户路径。" -f $codex.Version, $node.Version)
    }
    'RollbackUpgrade' { Restore-DreamBackgroundUpgrade -InstallRoot $installRoot -StateRoot $stateRoot -PackageRoot $root }
    'PrepareUpgrade' { Prepare-DreamBackgroundUpgrade -InstallRoot $installRoot -StateRoot $stateRoot -PackageRoot $root -ManagerPid $ManagerPid }
    'Install' {
      $null = Get-DreamSkinNodeRuntime
      $codex = Get-DreamSkinCodexInstall
      $prepared=$false
      try{$prior=[IO.File]::ReadAllText((Join-Path $stateRoot 'last-upgrade.json'))|ConvertFrom-Json;$prepared=(-not $prior.complete -and $prior.version -eq '1.5.3' -and (Test-DreamSkinPathEqual -Left $prior.packageRoot -Right $root))}catch{}
      if(-not $prepared -and (-not (Test-Path -LiteralPath (Join-Path $installRoot 'installed.json')) -or ([IO.File]::ReadAllText((Join-Path $installRoot 'installed.json'))|ConvertFrom-Json).version -ne '1.5.3')){Prepare-DreamBackgroundUpgrade -InstallRoot $installRoot -StateRoot $stateRoot -PackageRoot $root -ManagerPid $ManagerPid}
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
      $shortcuts=Repair-DreamBackgroundShortcuts -Codex $codex -StateRoot $stateRoot
      $guard = Wait-DreamBackgroundService -StartupRoot $startup
      $monitor=Wait-YzBackgroundMonitor -StartupRoot $startup -EngineRoot (Join-Path $installRoot 'engine') -Seconds 45
      $installation = [ordered]@{version='1.5.3';installedAt=[DateTime]::UtcNow.ToString('o');serviceMode=$plan.Mode;serviceProfile=$plan.Profile;guardPid=$guard.pid;monitorPid=$monitor.pid;autostart=$autostart;serviceUpgraded=$upgrade.changed}
      [IO.File]::WriteAllText((Join-Path $installRoot 'installed.json'), ($installation | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
      $shell = New-Object -ComObject WScript.Shell
      try {
        $desktop=[Environment]::GetFolderPath('Desktop')
        $link = $shell.CreateShortcut((Join-Path $desktop 'Codex 动态背景.lnk'))
        $link.TargetPath = Join-Path $installRoot 'CodexLiveBackground.exe'
        $link.WorkingDirectory = $installRoot
        $link.IconLocation = (Join-Path $installRoot 'CodexBackground.ico')+',0'
        $link.Save()
        $oldLink=Join-Path $desktop 'yz Codex 动态背景.lnk'
        if(Test-Path -LiteralPath $oldLink){
          $old=$shell.CreateShortcut($oldLink)
          if($old.TargetPath -ieq (Join-Path $installRoot 'CodexLiveBackground.exe')){Remove-Item -LiteralPath $oldLink -Force}
        }
      } finally { [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null }
      $upgradeRecord=Join-Path $stateRoot 'last-upgrade.json'
      if(Test-Path -LiteralPath $upgradeRecord){$savedUpgrade=[IO.File]::ReadAllText($upgradeRecord)|ConvertFrom-Json;$savedUpgrade.complete=$true;[IO.File]::WriteAllText($upgradeRecord,($savedUpgrade|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))}
      Write-Output '服务已装载：登录 Windows 时自动运行，Codex 启动时也会检查并恢复服务。后台监控已验证正常，无需再次打开管理器；以后直接点“换壁纸”即可。'
    }
    'Apply' {
      if (-not $Media -or -not (Test-Path -LiteralPath $Media -PathType Leaf)) { throw '请选择本地视频或图片文件。' }
      if ([IO.Path]::GetExtension($Media).ToLowerInvariant() -notin @('.mp4','.webm','.gif','.png','.jpg','.jpeg','.webp')) { throw '仅支持 MP4、WebM、GIF、PNG、JPG、WebP。' }
      $mediaLimit=if([IO.Path]::GetExtension($Media).ToLowerInvariant() -in @('.mp4','.webm')){2GB}else{512MB}
      if ((Get-Item -LiteralPath $Media).Length -gt $mediaLimit) { throw '视频源文件最大 2 GiB，图片最大 512 MiB；请选择较小文件或先压缩。' }
      $preview=$null
      try{$preview=Prepare-YzVideo -Path $Media -Preview;[Console]::Out.WriteLine('DREAM_PREVIEW:'+$preview.path)}catch{Write-Output ('预览不可用：'+$_.Exception.Message+' 实际壁纸将继续验证。')}
      Ensure-YzService
      $identity=Connect-YzCodex
      $connection=Read-DreamSkinState -Path (Join-Path $stateRoot 'state.json')
      # The MP4 decoder uses a verified saved browser identity, not merely an
      # open TCP port. Establish that state before validating the first video.
      if(-not $connection -or "$($connection.browserId)" -cne "$($identity.BrowserId)" -or $connection.port -ne 9335){
        $output=@(& $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File (Join-Path $engine 'scripts\start-dream-skin.ps1') -Port 9335 -ConnectOnly -OperationLockTimeoutMilliseconds 180000 2>&1)
        if($LASTEXITCODE -ne 0){throw (($output|ForEach-Object{"$_"})-join "`r`n")}
      }
      $prepared=Prepare-YzVideo -Path $Media
      $applyMedia=$prepared.path
      $needsConnect=$false
      try{
        $result = Invoke-Engine 'ApplyTheme' @('-ImagePath',$applyMedia,'-Name',[IO.Path]::GetFileNameWithoutExtension($Media),'-ThemeId',('custom-' + [guid]::NewGuid().ToString('N')),'-Category','custom','-TaskMode','full','-Appearance','dark','-SurfaceOpacity',$Opacity.ToString([Globalization.CultureInfo]::InvariantCulture))
        $needsConnect=-not $result.rendererApplied
      }catch{
        if($_.Exception.Message -notmatch 'DREAM_SKIN_LIVE_APPLY_FAILED'){throw}
        $needsConnect=$true
      }
      if($needsConnect){
        $output=@(& $ps -NoLogo -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File (Join-Path $engine 'scripts\start-dream-skin.ps1') -Port 9335 -OperationLockTimeoutMilliseconds 180000 2>&1)
        if($LASTEXITCODE -ne 0){throw (($output|ForEach-Object{"$_"})-join "`r`n")}
        $applied=Invoke-Engine 'Resume'
        if(-not $applied.rendererApplied){throw '素材已保存，当前窗口还在加载。等主窗口就绪后再点“换壁纸”。'}
      }
      if(-not $result){$result=Get-Content -LiteralPath (Join-Path $stateRoot 'active-theme\theme.json') -Raw|ConvertFrom-Json}
      if($preview){$theme=[IO.File]::ReadAllText((Join-Path $stateRoot 'active-theme\theme.json'))|ConvertFrom-Json;[IO.File]::WriteAllText((Join-Path $stateRoot 'manager-preview.json'),(@{themeId=$theme.id;path=$preview.path}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))}
      $config=Join-Path $env:USERPROFILE '.codex\config.toml'
      if(Test-Path -LiteralPath $config){try{. (Join-Path $engine 'scripts\config-utf8.ps1');$null=Install-DreamSkinBaseTheme -ConfigPath $config -BackupPath (Join-Path $stateRoot 'config.before-dream-skin.toml') -AppearanceTheme dark}catch{Write-Output '当前壁纸已应用深色外观；桌面偏好设置暂未同步。'}}
      if($prepared.converted){Write-Output ('已使用 VP9 兼容副本：'+$prepared.width+' × '+$prepared.height+'，原视频保持不变。')}
      $health=Get-YzRendererHealth $identity
      $playbackNote=if($health.status -eq 'waiting-visible'){'视频已保存；Codex 窗口显示后自动静音循环播放。'}elseif($health.status -eq 'playing'){'视频正在静音循环播放。'}elseif($health.status -eq 'image-ready'){'图片已铺满 Codex 窗口。'}elseif($health.status -eq 'reduced-motion'){'系统启用了减少动态效果，视频当前暂停。'}else{'可在左下角查看实际壁纸状态。'}
      Write-Output ('已应用并保存：' + $result.name + '。'+$playbackNote+' 重新打开 Codex 后会自动恢复。')
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
    'RemoveSkin' { Remove-DreamBackgroundSkin -InstallRoot $installRoot -StateRoot $stateRoot -PackageRoot $root }
    {$_ -in @('Remove','RemoveService')} { Remove-DreamBackgroundService -InstallRoot $installRoot -StateRoot $stateRoot -PackageRoot $root -AllowDisconnected:$AllowDisconnected }
    'Connection' {
      $codex=$null;$identity=$null;$auto=$null
      try{$codex=Get-DreamSkinCodexInstall;$identity=Get-DreamSkinVerifiedCdpIdentity -Port 9335 -Codex $codex}catch{}
      try{$auto=Get-YzBackgroundAutostart -StartupRoot $startup}catch{}
      $health=Get-YzRendererHealth $identity
      $data=@{installed=[bool]$codex;version=$(if($codex){"$($codex.Version)"}else{''});connected=[bool]$identity;serviceRunning=[bool]($auto -and $auto.running);autostart=[bool]($auto -and $auto.enabled);background=$health.status}
      [Console]::Out.WriteLine('DREAM_STATUS:'+($data|ConvertTo-Json -Compress))
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
} finally {
  if($upgradeMutex){if($upgradeOwned){$upgradeMutex.ReleaseMutex()};$upgradeMutex.Dispose()}
}
