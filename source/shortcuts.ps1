function Initialize-DreamBackgroundShellIdentity {
 if('CodexBackgroundShell.Identity' -as [type]){return}
 $paths=@((Join-Path $PSScriptRoot 'startup\ShellIdentity.cs'),(Join-Path (Split-Path -Parent $PSScriptRoot) 'startup\ShellIdentity.cs'))
 $path=$paths|Where-Object{Test-Path -LiteralPath $_ -PathType Leaf}|Select-Object -First 1
 if(-not $path){throw '缺少任务栏身份组件，未修改快捷方式。'}
 Add-Type -Path $path
}
function Get-DreamBackgroundLaunchChannels {
 param($Codex)
 $result=@($Codex)
 try{
  $package=Get-AppxPackage -Name OpenAI.CodexBeta -ErrorAction Stop
  if($package -and $package.SignatureKind -eq 'Store' -and $package.Status -eq 'Ok' -and -not $package.IsDevelopmentMode -and $package.Publisher -ceq 'CN=50BDFD77-8903-4850-9FFE-6E8522F64D5B' -and $package.PackageFullName -cmatch '^OpenAI\.CodexBeta_\d+\.\d+\.\d+\.\d+_x64__2p2nqsd0c76g0$'){
   $manifest=Get-AppxPackageManifest -Package $package -ErrorAction Stop;$app=@($manifest.Package.Applications.Application|Where-Object{"$($_.Executable)".Replace('/','\') -ieq 'app\ChatGPT.exe'})
   $id=if($app.Count -eq 1){$package.PackageFamilyName+'!'+$app[0].Id}else{''};$exe=Join-Path $package.InstallLocation 'app\ChatGPT.exe'
   if([CodexBackgroundShell.Identity]::Supported($id) -and (Test-Path -LiteralPath $exe)){$result+=@([pscustomobject]@{Executable=$exe;AppUserModelId=$id;PackageRoot=$package.InstallLocation;PackageFamilyName=$package.PackageFamilyName;PackageFullName=$package.PackageFullName})}
  }
 }catch{Write-Verbose ('Beta 启动入口未参与迁移：'+$_.Exception.Message)}
 return $result
}
function Get-DreamBackgroundShortcutChannel {
 param($Link,[object[]]$Channels,[string[]]$OwnedHooks)
 $target=$Link.TargetPath;$arguments=$Link.Arguments
 if($arguments -ceq '--launch' -and @($OwnedHooks|Where-Object{Test-DreamSkinPathEqual -Left $_ -Right $target}).Count){return $Channels[0]}
 foreach($channel in $Channels){
  $id=$channel.AppUserModelId
  if([IO.Path]::GetFileName($target) -ieq 'explorer.exe' -and $arguments.Trim('"') -ieq ('shell:AppsFolder\'+$id)){return $channel}
  $direct=Test-DreamSkinPathEqual -Left $target -Right $channel.Executable
  $match=[regex]::Match("$target",'(?i)\\WindowsApps\\(OpenAI\.Codex(?:Beta)?)_\d+\.\d+\.\d+\.\d+_x64__2p2nqsd0c76g0\\app\\ChatGPT\.exe$')
  $oldVersion=$match.Success -and $id -ieq ($match.Groups[1].Value+'_2p2nqsd0c76g0!App')
  if(-not $direct -and -not $oldVersion){continue}
  if($arguments -and $arguments -cnotmatch '^\s*(?:--remote-debugging-(?:address=127\.0\.0\.1|port=9335)\s*)+$'){continue}
  return $channel
 }
 return $null
}
function Repair-DreamBackgroundShortcuts {
 param($Codex,[string]$StateRoot,[string[]]$Directories,[string]$StartupRoot,[string]$DesktopDirectory,[object[]]$LaunchChannels)
 Initialize-DreamBackgroundShellIdentity
 if(-not $LaunchChannels){$LaunchChannels=@(Get-DreamBackgroundLaunchChannels -Codex $Codex)}
 foreach($channel in $LaunchChannels){if(-not [CodexBackgroundShell.Identity]::Supported($channel.AppUserModelId)){throw '快捷方式应用身份不受支持。'}}
 $desktopFolder=if($DesktopDirectory){$DesktopDirectory}else{[Environment]::GetFolderPath('Desktop')}
 if(-not $Directories){$quick=Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch';$Directories=@((Join-Path $quick 'User Pinned\TaskBar'),$quick,[Environment]::GetFolderPath('Programs'),$desktopFolder)}
 if(-not $StartupRoot){$StartupRoot=Join-Path $StateRoot 'startup'}
 $hook=Join-Path $StartupRoot 'DreamSkinPortHook.exe'
 $officialIcon=$null
 if(Get-Command Get-DreamBackgroundCodexIcon -ErrorAction SilentlyContinue){try{$officialIcon=Get-DreamBackgroundCodexIcon -Codex $Codex -StateRoot $StateRoot}catch{[Console]::Out.WriteLine('Codex 图标缓存暂不可用：'+$_.Exception.Message)}}
 $iconLocation=if($officialIcon){$officialIcon+',0'}else{$Codex.Executable+',0'}
 $icons=@{};$icons[$Codex.AppUserModelId]=$iconLocation
 foreach($channel in $LaunchChannels|Select-Object -Skip 1){$image=$null;if(Get-Command Get-DreamBackgroundCodexIcon -ErrorAction SilentlyContinue){try{$image=Get-DreamBackgroundCodexIcon -Codex $channel -StateRoot $StateRoot}catch{}};$icons[$channel.AppUserModelId]=if($image){$image+',0'}else{$channel.Executable+',0'}}
 $backup=Join-Path $StateRoot 'shortcut-backups';$record=Join-Path $backup 'entries.json'
 $null=New-Item -ItemType Directory -Path $backup -Force
 $saved=@();if(Test-Path -LiteralPath $record){$data=[IO.File]::ReadAllText($record)|ConvertFrom-Json;$saved=@($data.entries|ForEach-Object{[pscustomobject]@{path=$_.path;backup=$_.backup;modifiedSha256=$_.modifiedSha256;officialAppId=$_.officialAppId}})}
 $shell=New-Object -ComObject WScript.Shell;$changed=0
 try{
  $oldEntry=Join-Path $desktopFolder 'Codex（动态背景）.lnk'
  if(Test-Path -LiteralPath $oldEntry){$old=$shell.CreateShortcut($oldEntry);$ownedHook=($old.Arguments.Trim() -ceq '--launch' -and ((Test-DreamSkinPathEqual -Left $old.TargetPath -Right $hook) -or (Test-DreamSkinPathEqual -Left $old.TargetPath -Right (Join-Path $StateRoot 'startup\DreamSkinPortHook.exe'))));$retiredOfficial=($old.Description -ceq '启动官方 Codex 并恢复动态背景' -and (Test-DreamSkinPathEqual -Left $old.TargetPath -Right $Codex.Executable));if($ownedHook -or $retiredOfficial){Remove-Item -LiteralPath $oldEntry -Force -ErrorAction Stop}}
  $pending=[Collections.Generic.Queue[object]]::new();$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);foreach($directory in $Directories){if(Test-Path -LiteralPath $directory){$pending.Enqueue([pscustomobject]@{path=$directory;depth=0;recurse=($directory -ine $desktopFolder)})}}
  $count=0
  while($pending.Count -gt 0){$current=$pending.Dequeue();if(-not $seen.Add($current.path)){continue};foreach($item in Get-ChildItem -LiteralPath $current.path -Force -ErrorAction SilentlyContinue){
   if(++$count -gt 3000){$pending.Clear();[Console]::Out.WriteLine('快捷方式较多，部分入口未扫描；可使用桌面的 Codex 动态背景启动入口。');break}
   if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){continue}
   if($item.PSIsContainer){if($current.recurse -and $current.depth -lt 4){$pending.Enqueue([pscustomobject]@{path=$item.FullName;depth=($current.depth+1);recurse=$true})};continue}
   if($item.Extension -ine '.lnk'){continue}
   $link=$shell.CreateShortcut($item.FullName)
   $channel=Get-DreamBackgroundShortcutChannel -Link $link -Channels $LaunchChannels -OwnedHooks @($hook,(Join-Path $StateRoot 'startup\DreamSkinPortHook.exe'))
   if(-not $channel){continue}
   $id=$channel.AppUserModelId;$target=Join-Path $env:WINDIR explorer.exe;$arguments='shell:AppsFolder\'+$id;$icon=$icons[$id]
   if((Test-DreamSkinPathEqual -Left $link.TargetPath -Right $target) -and $link.Arguments -ceq $arguments -and $link.IconLocation -ceq $icon -and [CodexBackgroundShell.Identity]::ReadShortcut($item.FullName) -ceq $id){continue}
   $copy=Join-Path $backup ([guid]::NewGuid().ToString('N')+'.lnk');Copy-Item -LiteralPath $item.FullName -Destination $copy -ErrorAction Stop
   $temporary=Join-Path $item.DirectoryName ('.codex-shortcut-'+[guid]::NewGuid().ToString('N')+'.lnk')
   try{Copy-Item -LiteralPath $item.FullName -Destination $temporary;$candidate=$shell.CreateShortcut($temporary);$candidate.TargetPath=$target;$candidate.Arguments=$arguments;$candidate.WorkingDirectory=$env:WINDIR;$candidate.IconLocation=$icon;$candidate.Save();[CodexBackgroundShell.Identity]::SetShortcut($temporary,$id);[IO.File]::Replace($temporary,$item.FullName,[NullString]::Value);[CodexBackgroundShell.Identity]::NotifyShortcut($item.FullName)}finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}}
   $saved=@($saved|Where-Object{$_.path -ine $item.FullName})+@([pscustomobject]@{path=$item.FullName;backup=$copy;modifiedSha256=(Get-FileHash -LiteralPath $item.FullName).Hash;officialAppId=$id})
   [IO.File]::WriteAllText($record,(@{schemaVersion=1;entries=@($saved)}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false));$changed++
  }}
 }finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)}
 return [pscustomobject]@{repaired=$changed;shortcut=$null}
}
function Restore-DreamBackgroundShortcuts {
 param([string]$StateRoot,[string]$DesktopDirectory)
 $record=Join-Path $StateRoot 'shortcut-backups\entries.json';$data=@{entries=@()};if(Test-Path -LiteralPath $record){$data=[IO.File]::ReadAllText($record)|ConvertFrom-Json}
 foreach($entry in $data.entries){
  if(-not $entry.path -or -not $entry.backup){continue}
  if(-not (Test-Path -LiteralPath $entry.path) -or -not (Test-Path -LiteralPath $entry.backup)){continue}
  if((Get-FileHash -LiteralPath $entry.path).Hash -ceq $entry.modifiedSha256){
   # Canonical Store links no longer depend on this service. Restoring an old
   # executable version here would reintroduce broken pins after uninstall.
   if($entry.officialAppId -cmatch '^OpenAI\.Codex(?:Beta)?_2p2nqsd0c76g0!App$'){continue}
   Copy-Item -LiteralPath $entry.backup -Destination $entry.path -Force -ErrorAction Stop
  }
 }
 $desktop=if($DesktopDirectory){$DesktopDirectory}else{[Environment]::GetFolderPath('Desktop')};$path=Join-Path $desktop 'Codex（动态背景）.lnk'
 if(Test-Path -LiteralPath $path){$shell=New-Object -ComObject WScript.Shell;try{$link=$shell.CreateShortcut($path);if(Test-DreamSkinPathEqual -Left $link.TargetPath -Right (Join-Path $StateRoot 'startup\DreamSkinPortHook.exe')){Remove-Item -LiteralPath $path -Force -ErrorAction Stop}}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)}}
}
