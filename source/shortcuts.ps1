function Repair-DreamBackgroundShortcuts {
 param($Codex,[string]$StateRoot,[string[]]$Directories)
 $desktopFolder=[Environment]::GetFolderPath('Desktop')
 if(-not $Directories){$quick=Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch';$Directories=@((Join-Path $quick 'User Pinned\TaskBar'),$quick,[Environment]::GetFolderPath('Programs'),$desktopFolder)}
 $hook=Join-Path $StateRoot 'startup\DreamSkinPortHook.exe'
 $backup=Join-Path $StateRoot 'shortcut-backups';$record=Join-Path $backup 'entries.json'
 $null=New-Item -ItemType Directory -Path $backup -Force
 $saved=@();if(Test-Path -LiteralPath $record){$data=[IO.File]::ReadAllText($record)|ConvertFrom-Json;$saved=@($data.entries|ForEach-Object{[pscustomobject]@{path=$_.path;backup=$_.backup;modifiedSha256=$_.modifiedSha256}})}
 $shell=New-Object -ComObject WScript.Shell;$changed=0
 try{
  $pending=[Collections.Generic.Queue[object]]::new();$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);foreach($directory in $Directories){if(Test-Path -LiteralPath $directory){$pending.Enqueue([pscustomobject]@{path=$directory;depth=0;recurse=($directory -ine $desktopFolder)})}}
  $count=0
  while($pending.Count -gt 0){$current=$pending.Dequeue();if(-not $seen.Add($current.path)){continue};foreach($item in Get-ChildItem -LiteralPath $current.path -Force -ErrorAction SilentlyContinue){
   if(++$count -gt 3000){$pending.Clear();[Console]::Out.WriteLine('快捷方式较多，部分入口未扫描；可使用桌面的 Codex 动态背景启动入口。');break}
   if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){continue}
   if($item.PSIsContainer){if($current.recurse -and $current.depth -lt 4){$pending.Enqueue([pscustomobject]@{path=$item.FullName;depth=($current.depth+1);recurse=$true})};continue}
   if($item.Extension -ine '.lnk'){continue}
   $link=$shell.CreateShortcut($item.FullName)
   $direct=Test-DreamSkinPathEqual -Left $link.TargetPath -Right $Codex.Executable
   $packaged=([IO.Path]::GetFileName($link.TargetPath) -ieq 'explorer.exe' -and $link.Arguments.Trim('"') -ieq ('shell:AppsFolder\'+$Codex.AppUserModelId))
   if(-not $direct -and -not $packaged){continue}
   # Preserve links carrying a deep-link argument; their semantics must not be lost.
   if($direct -and $link.Arguments -and $link.Arguments -notmatch '^\s*--remote-debugging-(port|address)='){continue}
   $copy=Join-Path $backup ([guid]::NewGuid().ToString('N')+'.lnk');Copy-Item -LiteralPath $item.FullName -Destination $copy -ErrorAction Stop
   $link.TargetPath=$hook;$link.Arguments='--launch';$link.WorkingDirectory=Split-Path -Parent $hook
   if(-not $link.IconLocation){$link.IconLocation=$Codex.Executable+',0'};$link.Save()
   $saved+=@(@{path=$item.FullName;backup=$copy;modifiedSha256=(Get-FileHash -LiteralPath $item.FullName).Hash})
   [IO.File]::WriteAllText($record,(@{schemaVersion=1;entries=@($saved)}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false));$changed++
  }}
  # A normal shortcut can be pinned by Windows without editing Taskband registry data.
  $desktop=if($Directories.Count -eq 1){$Directories[0]}else{[Environment]::GetFolderPath('Desktop')}
  $entry=Join-Path $desktop 'Codex（动态背景）.lnk'
  if(-not (Test-Path -LiteralPath $entry)){$link=$shell.CreateShortcut($entry);$link.TargetPath=$hook;$link.Arguments='--launch';$link.WorkingDirectory=Split-Path -Parent $hook;$link.IconLocation=$Codex.Executable+',0';$link.Description='启动官方 Codex 并恢复动态背景';$link.Save()}
 }finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)}
 return [pscustomobject]@{repaired=$changed;shortcut=$entry}
}
function Restore-DreamBackgroundShortcuts {
 param([string]$StateRoot)
 $record=Join-Path $StateRoot 'shortcut-backups\entries.json';if(-not (Test-Path -LiteralPath $record)){return}
 $data=[IO.File]::ReadAllText($record)|ConvertFrom-Json
 foreach($entry in $data.entries){
  if(-not $entry.path -or -not $entry.backup){continue}
  if(-not (Test-Path -LiteralPath $entry.path) -or -not (Test-Path -LiteralPath $entry.backup)){continue}
  if((Get-FileHash -LiteralPath $entry.path).Hash -ceq $entry.modifiedSha256){Copy-Item -LiteralPath $entry.backup -Destination $entry.path -Force -ErrorAction Stop}
 }
 $path=Join-Path ([Environment]::GetFolderPath('Desktop')) 'Codex（动态背景）.lnk'
 if(Test-Path -LiteralPath $path){$shell=New-Object -ComObject WScript.Shell;try{$link=$shell.CreateShortcut($path);if(Test-DreamSkinPathEqual -Left $link.TargetPath -Right (Join-Path $StateRoot 'startup\DreamSkinPortHook.exe')){$codex=Get-DreamSkinCodexInstall;$link.TargetPath=$codex.Executable;$link.Arguments='';$link.WorkingDirectory=Split-Path -Parent $codex.Executable;$link.Save()}}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)}}
}
