function Get-DreamBackgroundLegacyInstalls {
  param([string]$InstallRoot)
  $programs=Split-Path -Parent $InstallRoot
  $registered=@(Get-DreamBackgroundRegisteredInstallRoots)
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
function Get-DreamBackgroundRegisteredInstallRoots {
  $keys=@('HKCU:\Software\CodexLiveBackground','HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLiveBackground','HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLiveBackground','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\CodexLiveBackground')
  $products='HKCU:\Software\CodexLiveBackground\Installs'
  if(Test-Path -LiteralPath $products){$keys+=@(Get-ChildItem -LiteralPath $products|ForEach-Object{$_.PSPath})}
  foreach($key in $keys){
    if(-not(Test-Path -LiteralPath $key)){continue}
    $entry=Get-ItemProperty -LiteralPath $key
    foreach($property in @('InstallRoot','InstallLocation')){
      $value=$entry.PSObject.Properties[$property]
      if($value -and $value.Value){$path=[Environment]::ExpandEnvironmentVariables("$($value.Value)".Trim().Trim('"'));if([IO.Path]::IsPathRooted($path)){[IO.Path]::GetFullPath($path).TrimEnd('\')}}
    }
  }
}
function Resolve-DreamBackgroundUpgradeStartup {
  param($Task,[object[]]$Installs,[string]$StateRoot,[string]$PackageRoot)
  $startupRoot=Join-Path $StateRoot 'startup'
  if(-not $Task){return $startupRoot}
  $actions=@($Task.Actions)
  if($actions.Count -ne 1){throw '旧服务包含无法确认的启动操作，原服务已保留。'}
  $executable="$($actions[0].Execute)".Trim().Trim('"')
  if(-not[IO.Path]::IsPathRooted($executable) -or $executable.IndexOf('"') -ge 0){throw '旧服务启动路径无效，原服务已保留。'}
  $taskRoot=Split-Path -Parent $executable
  $approved=@($startupRoot)+@($Installs|ForEach-Object{Join-Path $_.Root 'startup'})
  if(-not @($approved|Where-Object{Test-DreamSkinPathEqual -Left $_ -Right $taskRoot}).Count){throw '旧服务路径不属于已验证的安装。'}
  $null=Get-DreamBackgroundServicePlan -Task $Task -StartupRoot $taskRoot -ProfilesPath (Join-Path $PackageRoot 'service-compatibility.json')
  return $taskRoot
}
function Assert-DreamBackgroundUpgradeTree {
  param([string]$Path,[string]$StateRoot)
  $target=[IO.Path]::GetFullPath($Path).TrimEnd('\');$parent=Split-Path -Parent $target
  $forbidden=@([IO.Path]::GetPathRoot($target).TrimEnd('\'),$env:USERPROFILE,$env:LOCALAPPDATA,$env:APPDATA,$env:WINDIR,[Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('ProgramFiles'),$StateRoot)
  if($target -in $forbidden -or $target.StartsWith($env:WINDIR.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or $target.StartsWith($StateRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw '旧安装路径不是独立的程序目录，已保留。'}
  if($parent.TrimEnd('\') -ne [IO.Path]::GetPathRoot($target).TrimEnd('\')){Assert-DreamBackgroundTree -Path $target -Parent $parent;return}
  # A verified custom installation such as D:\Codex is a named child of the
  # drive. Validate its own children; never pass the drive to a move/delete.
  if(-not(Test-Path -LiteralPath $target)){return}
  if((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw '旧安装路径包含链接或联接，已保留。'}
  foreach($child in Get-ChildItem -LiteralPath $target -Force){Assert-DreamBackgroundTree -Path $child.FullName -Parent $target}
}
function Get-DreamBackgroundPackageVersion {
  param([Parameter(Mandatory=$true)][string]$PackageRoot)
  $manifest=Join-Path $PackageRoot 'manifest.json'
  if(-not(Test-Path -LiteralPath $manifest -PathType Leaf)){throw '安装包版本清单缺失，未覆盖现有程序。'}
  $value=$null
  try{$value=([IO.File]::ReadAllText($manifest)|ConvertFrom-Json).version}catch{throw '安装包版本清单无效，未覆盖现有程序。'}
  $parsed=$null
  if(-not[version]::TryParse("$value",[ref]$parsed)){throw '安装包版本号无效，未覆盖现有程序。'}
  return [version]::new($parsed.Major,$parsed.Minor,[Math]::Max(0,$parsed.Build),[Math]::Max(0,$parsed.Revision))
}
function Assert-DreamBackgroundUpgradeVersion {
  param([string]$InstalledVersion,[version]$PackageVersion)
  $parsed=$null
  if(-not[version]::TryParse($InstalledVersion,[ref]$parsed)){throw '无法识别已安装版本，现有程序已保留。'}
  $installed=[version]::new($parsed.Major,$parsed.Minor,[Math]::Max(0,$parsed.Build),[Math]::Max(0,$parsed.Revision))
  if($installed -gt $PackageVersion){throw ('已安装版本 '+$installed+' 高于当前安装包 '+$PackageVersion+'，请使用较新的安装包。')}
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
  $packageVersion=Get-DreamBackgroundPackageVersion -PackageRoot $PackageRoot
  $installs=@(Get-DreamBackgroundLegacyInstalls -InstallRoot $InstallRoot)
  foreach($install in $installs){if([IO.Path]::GetFileName($install.Executable) -eq 'CodexLiveBackground.exe'){Assert-DreamBackgroundUpgradeVersion -InstalledVersion $install.Version -PackageVersion $packageVersion}}
  $task=Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue
  $startupRoot=Resolve-DreamBackgroundUpgradeStartup -Task $task -Installs $installs -StateRoot $StateRoot -PackageRoot $PackageRoot
  foreach($install in $installs){if(-not(Test-DreamSkinPathEqual -Left $install.Root -Right $PackageRoot)){Assert-DreamBackgroundUpgradeTree -Path $install.Root -StateRoot $StateRoot}}
  Stop-DreamBackgroundManagers -Installs $installs -ManagerPid $ManagerPid
  $hasService=(Test-Path -LiteralPath $startupRoot) -or (Test-Path -LiteralPath (Join-Path $StateRoot 'state.json')) -or (Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue)
  if($hasService){$serviceInstall=if(Test-DreamSkinPathEqual -Left $startupRoot -Right (Join-Path $StateRoot 'startup')){$InstallRoot}else{Split-Path -Parent $startupRoot};Remove-DreamBackgroundService -InstallRoot $serviceInstall -StateRoot $StateRoot -PackageRoot $PackageRoot -StartupRoot $startupRoot -ForUpgrade}
  $backups=@()
  $backupRoot=Join-Path $StateRoot ('upgrade-backups\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N'))
  $null=New-Item -ItemType Directory -Path $StateRoot -Force
  $record=Join-Path $StateRoot 'last-upgrade.json'
  [IO.File]::WriteAllText($record,(@{version=$packageVersion.ToString();packageRoot=$PackageRoot;backups=@();complete=$false}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
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
    Assert-DreamBackgroundUpgradeTree -Path $install.Root -StateRoot $StateRoot
    $null=New-Item -ItemType Directory -Path $backupRoot -Force
    $destination=Join-Path $backupRoot (([guid]::NewGuid().ToString('N'))+'-'+[IO.Path]::GetFileName($install.Root))
    Assert-DreamBackgroundTree -Path $destination -Parent $StateRoot
    [Console]::Out.WriteLine('检测到旧版 '+$install.Version+'，正在备份并迁移…')
    Move-Item -LiteralPath $install.Root -Destination $destination -ErrorAction Stop
    $backups+=@([pscustomobject]@{source=$install.Root;backup=$destination})
    [IO.File]::WriteAllText($record,(@{version=$packageVersion.ToString();packageRoot=$PackageRoot;backups=@($backups);complete=$false}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
  }
  $null=New-Item -ItemType Directory -Path $StateRoot -Force
  [IO.File]::WriteAllText((Join-Path $StateRoot 'last-upgrade.json'),(@{version=$packageVersion.ToString();packageRoot=$PackageRoot;backups=@($backups);complete=$false}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
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
    Assert-DreamBackgroundUpgradeTree -Path $InstallRoot -StateRoot $StateRoot
    Assert-DreamBackgroundTree -Path $failed -Parent $StateRoot
    $null=New-Item -ItemType Directory -Path (Split-Path -Parent $failed) -Force
    Move-Item -LiteralPath $InstallRoot -Destination $failed -ErrorAction Stop
  }
  foreach($item in $saved.backups){
    if($item.source -notin (@($InstallRoot,(Join-Path $programs 'CodexDreamSkinManager'),(Join-Path $env:LOCALAPPDATA 'Programs\CodexLiveBackground'))+@(Get-DreamBackgroundRegisteredInstallRoots))){throw '升级恢复记录中的目标不受支持。'}
    Assert-DreamBackgroundTree -Path $item.backup -Parent (Join-Path $StateRoot 'upgrade-backups')
    Assert-DreamBackgroundUpgradeTree -Path $item.source -StateRoot $StateRoot
    if(Test-Path -LiteralPath $item.backup){Move-Item -LiteralPath $item.backup -Destination $item.source -ErrorAction Stop}
  }
  $saved.complete=$true
  [IO.File]::WriteAllText($record,($saved|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
  Write-Output '旧程序文件已恢复；个人素材及登录资料保持不变。'
}
