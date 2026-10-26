function Get-DreamBackgroundLegacyInstalls {
  param([string]$InstallRoot)
  $programs=Split-Path -Parent $InstallRoot
  $registered=@();if(Test-Path -LiteralPath 'HKCU:\Software\CodexLiveBackground\Installs'){foreach($key in Get-ChildItem -LiteralPath 'HKCU:\Software\CodexLiveBackground\Installs'){$value=(Get-ItemProperty -LiteralPath $key.PSPath -Name InstallRoot -ErrorAction SilentlyContinue).InstallRoot;if($value){$registered+=@($value)}}}
  $candidates=@($InstallRoot,(Join-Path $programs 'CodexDreamSkinManager'),(Join-Path $env:LOCALAPPDATA 'Programs\CodexLiveBackground'))+@($registered)
  foreach($candidate in @($candidates|Select-Object -Unique)){
    if(-not (Test-Path -LiteralPath $candidate)){continue}
    $exe=if(Test-Path -LiteralPath (Join-Path $candidate 'CodexLiveBackground.exe')){Join-Path $candidate 'CodexLiveBackground.exe'}elseif(Test-Path -LiteralPath (Join-Path $candidate 'CodexDreamSkinManager.exe')){Join-Path $candidate 'CodexDreamSkinManager.exe'}else{$null}
    if(-not $exe){if((Get-ChildItem -LiteralPath $candidate -Force|Measure-Object).Count -gt 0){throw '发现不完整的旧安装目录，无法确认归属。请保留该目录供修复。'};continue}
    $version=(Get-Item -LiteralPath $exe).VersionInfo
    $legacy=$null;$catalog=Join-Path $PSScriptRoot 'legacy-managers.json'
    if(Test-Path -LiteralPath $catalog){$hash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash;$legacy=([IO.File]::ReadAllText($catalog)|ConvertFrom-Json).releases|Where-Object{$_.sha256 -ceq $hash -and $_.filename -ieq [IO.Path]::GetFileName($exe)}|Select-Object -First 1}
    if(-not $legacy -and $version.ProductName -notmatch '^(CodexHub|Codex 动态背景|Codex Dream Skin Manager|CodexDreamSkinManager|CodexLiveBackground)$'){throw '旧程序身份无法确认，未覆盖现有目录。'}
    $detectedVersion=if($legacy){$legacy.version}else{$version.FileVersion}
    [pscustomobject]@{Root=[IO.Path]::GetFullPath($candidate);Executable=[IO.Path]::GetFullPath($exe);Version=$detectedVersion}
  }
}
function Stop-DreamBackgroundManagers {
  param([object[]]$Installs,[int]$ManagerPid)
  Initialize-DreamSkinNativeInspection
  foreach($item in @(Get-Process -Name CodexLiveBackground,CodexDreamSkinManager -ErrorAction SilentlyContinue)){
    if($item.Id -eq $ManagerPid){continue}
    if(-not [CodexDreamSkin.NativeInspection]::CurrentUser($item.Id)){continue}
    $path=[CodexDreamSkin.NativeInspection]::ProcessPath($item.Id)
    if(-not @($Installs|Where-Object{Test-DreamSkinPathEqual -Left $_.Executable -Right $path}).Count){continue}
    $started=$item.StartTime.ToUniversalTime().Ticks
    $null=$item.CloseMainWindow()
    if(-not $item.WaitForExit(5000)){
      $current=Get-Process -Id $item.Id -ErrorAction SilentlyContinue
      if($current -and $current.StartTime.ToUniversalTime().Ticks -eq $started -and (Test-DreamSkinPathEqual -Left ([CodexDreamSkin.NativeInspection]::ProcessPath($item.Id)) -Right $path)){Stop-Process -InputObject $current -Force -ErrorAction Stop;$null=$current.WaitForExit(10000)}
    }
  }
}
function Prepare-DreamBackgroundUpgrade {
  param([string]$InstallRoot,[string]$StateRoot,[string]$PackageRoot,[int]$ManagerPid)
  $installs=@(Get-DreamBackgroundLegacyInstalls -InstallRoot $InstallRoot)
  foreach($install in $installs){if([IO.Path]::GetFileName($install.Executable) -eq 'CodexLiveBackground.exe' -and [version]$install.Version -gt [version]'1.6.2.0'){throw '检测到更高版本，请使用最新安装程序，避免旧包覆盖新版本。'}}
  Stop-DreamBackgroundManagers -Installs $installs -ManagerPid $ManagerPid
  $startupRoot=Join-Path $StateRoot 'startup'
  $task=Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue
  if($task){$taskRoot=Split-Path -Parent "$($task.Actions[0].Execute)";$approved=@($startupRoot)+@($installs|ForEach-Object{Join-Path $_.Root 'startup'});if(-not @($approved|Where-Object{Test-DreamSkinPathEqual -Left $_ -Right $taskRoot}).Count){throw '旧服务路径不属于已验证的安装。'};$startupRoot=$taskRoot}
  $hasService=(Test-Path -LiteralPath $startupRoot) -or (Test-Path -LiteralPath (Join-Path $StateRoot 'state.json')) -or (Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue)
  if($hasService){$serviceInstall=if(Test-DreamSkinPathEqual -Left $startupRoot -Right (Join-Path $StateRoot 'startup')){$InstallRoot}else{Split-Path -Parent $startupRoot};Remove-DreamBackgroundService -InstallRoot $serviceInstall -StateRoot $StateRoot -PackageRoot $PackageRoot -StartupRoot $startupRoot -ForUpgrade}
  $backups=@()
  $backupRoot=Join-Path $StateRoot ('upgrade-backups\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N'))
  $null=New-Item -ItemType Directory -Path $StateRoot -Force
  $record=Join-Path $StateRoot 'last-upgrade.json'
  [IO.File]::WriteAllText($record,(@{version='1.7.0.1';packageRoot=$PackageRoot;backups=@();complete=$false}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
  Initialize-DreamSkinNativeInspection
  foreach($process in @(Get-CimInstance Win32_Process -Filter "Name='node.exe'")){
    if(-not [CodexDreamSkin.NativeInspection]::CurrentUser([int]$process.ProcessId)){continue}
    $executable=[CodexDreamSkin.NativeInspection]::ProcessPath([int]$process.ProcessId)
    foreach($install in $installs){
      if(-not $executable.StartsWith($install.Root+'\',[StringComparison]::OrdinalIgnoreCase)){continue}
      $match=[regex]::Match("$($process.CommandLine)",'(?:"([^"\r\n]+(?:injector|prepare-video|preview-media|validate-video-file)\.mjs)"|(\S+(?:injector|prepare-video|preview-media|validate-video-file)\.mjs))')
      if(-not $match.Success){continue}
      $scriptPath=if($match.Groups[1].Success){$match.Groups[1].Value}else{$match.Groups[2].Value}
      if(-not [IO.Path]::GetFullPath($scriptPath).StartsWith($install.Root+'\',[StringComparison]::OrdinalIgnoreCase)){continue}
      $handle=Get-Process -Id ([int]$process.ProcessId) -ErrorAction SilentlyContinue
      if($handle){Stop-DreamBackgroundWorker -Identity ([pscustomobject]@{ProcessId=$handle.Id;StartedTicks=$handle.StartTime.ToUniversalTime().Ticks;Executable=$executable;Kind='old-background-tool'})}
    }
  }
  foreach($install in $installs){
    if(Test-DreamSkinPathEqual -Left $install.Root -Right $PackageRoot){continue}
    $programs=Split-Path -Parent $install.Root
    Assert-DreamBackgroundTree -Path $install.Root -Parent $programs
    $null=New-Item -ItemType Directory -Path $backupRoot -Force
    $destination=Join-Path $backupRoot ([IO.Path]::GetFileName($install.Root))
    Assert-DreamBackgroundTree -Path $destination -Parent $StateRoot
    [Console]::Out.WriteLine('检测到旧版 '+$install.Version+'，正在备份并迁移…')
    Move-Item -LiteralPath $install.Root -Destination $destination -ErrorAction Stop
    $backups+=@([pscustomobject]@{source=$install.Root;backup=$destination})
    [IO.File]::WriteAllText($record,(@{version='1.7.0.1';packageRoot=$PackageRoot;backups=@($backups);complete=$false}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
  }
  $null=New-Item -ItemType Directory -Path $StateRoot -Force
  [IO.File]::WriteAllText((Join-Path $StateRoot 'last-upgrade.json'),(@{version='1.7.0.1';packageRoot=$PackageRoot;backups=@($backups);complete=$false}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
  Write-Output '旧版本检查完成。个人壁纸和登录资料已保留。'
}
function Restore-DreamBackgroundUpgrade {
  param([string]$InstallRoot,[string]$StateRoot,[string]$PackageRoot)
  $record=Join-Path $StateRoot 'last-upgrade.json'
  if(-not (Test-Path -LiteralPath $record)){return}
  $saved=[IO.File]::ReadAllText($record)|ConvertFrom-Json
  if($saved.complete -or -not @($saved.backups).Count){return}
  $programs=Split-Path -Parent $InstallRoot
  if(Test-Path -LiteralPath $InstallRoot){
    Remove-DreamBackgroundService -InstallRoot $InstallRoot -StateRoot $StateRoot -PackageRoot $PackageRoot -ForUpgrade
    $failed=Join-Path $StateRoot ('upgrade-backups\failed-'+[guid]::NewGuid().ToString('N'))
    Assert-DreamBackgroundTree -Path $InstallRoot -Parent $programs
    Assert-DreamBackgroundTree -Path $failed -Parent $StateRoot
    $null=New-Item -ItemType Directory -Path (Split-Path -Parent $failed) -Force
    Move-Item -LiteralPath $InstallRoot -Destination $failed -ErrorAction Stop
  }
  foreach($item in $saved.backups){
    if($item.source -notin @($InstallRoot,(Join-Path $programs 'CodexDreamSkinManager'),(Join-Path $env:LOCALAPPDATA 'Programs\CodexLiveBackground'))){throw '升级恢复记录中的目标不受支持。'}
    Assert-DreamBackgroundTree -Path $item.backup -Parent (Join-Path $StateRoot 'upgrade-backups')
    Assert-DreamBackgroundTree -Path $item.source -Parent (Split-Path -Parent $item.source)
    if(Test-Path -LiteralPath $item.backup){Move-Item -LiteralPath $item.backup -Destination $item.source -ErrorAction Stop}
  }
  $saved.complete=$true
  [IO.File]::WriteAllText($record,($saved|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
  Write-Output '旧程序文件已恢复；个人素材及登录资料保持不变。'
}
