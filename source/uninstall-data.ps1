function Remove-DreamBackgroundGeneratedData {
 param([Parameter(Mandatory=$true)][string]$StateRoot)
 # Only generated directories are removed. Never follow links or delete the
 # independent Codex profile, which can contain login and chat information.
 $directories=@('active-theme','icons','images','previews','requests','runtime','runtime-downloads','compatible-video','compatible-video-v2','themes','shortcut-backups','upgrade-backups','pets','pet-imports','pet-downloads','pet-previews','updates')
 foreach($name in $directories){
  $path=Join-Path $StateRoot $name
  Assert-DreamBackgroundTree -Path $path -Parent $StateRoot
 }
 foreach($name in $directories){$path=Join-Path $StateRoot $name;if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop}}
 foreach($name in @('state.json','wallpaper-library.json','wallpaper-library.json.bak','pet-settings.json','pet-settings.json.bak','pet-settings.json.tmp','program-update-check-day.txt','injector-error.log','injector.log','verify.log','installer-service.log','keep-codex-open','startup-animation.disabled','last-preview-diagnostic.json','last-video-diagnostic.json','last-upgrade.json','paused')){
  $path=Join-Path $StateRoot $name
  if(Test-Path -LiteralPath $path){$item=Get-Item -LiteralPath $path -Force;if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw '卸载数据文件类型发生变化，已保留。'};Remove-Item -LiteralPath $path -Force -ErrorAction Stop}
 }
 if((Test-Path -LiteralPath $StateRoot) -and -not @(Get-ChildItem -LiteralPath $StateRoot -Force).Count){Remove-Item -LiteralPath $StateRoot -Force -ErrorAction Stop}
}
function Stop-DreamBackgroundMediaWorkers {
 param([string]$InstallRoot,[string]$StateRoot)
 Initialize-DreamSkinNativeInspection
 foreach($item in @(Get-CimInstance Win32_Process -Filter "Name='node.exe' OR Name='ffmpeg.exe'")){
  $id=[int]$item.ProcessId
  if(-not [CodexDreamSkin.NativeInspection]::CurrentUser($id)){continue}
  $path=[CodexDreamSkin.NativeInspection]::ProcessPath($id);$owned=$false
  if($path.StartsWith(([IO.Path]::GetFullPath($StateRoot).TrimEnd('\')+'\runtime\'),[StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($path) -ieq 'ffmpeg.exe'){$owned=$true}
  elseif(Test-DreamSkinPathEqual -Left $path -Right (Join-Path $InstallRoot 'engine\runtime\node\node.exe')){
   foreach($name in @('injector.mjs','prepare-video.mjs','preview-media.mjs','validate-video-file.mjs','convert-pet.mjs','prepare-bailong.mjs')){if(Test-DreamSkinCommandLineToken -CommandLine "$($item.CommandLine)" -Token (Join-Path $InstallRoot ('engine\scripts\'+$name))){$owned=$true;break}}
  }
  if($owned){$process=Get-Process -Id $id -ErrorAction SilentlyContinue;if($process){Stop-DreamBackgroundWorker -Identity ([pscustomobject]@{ProcessId=$id;StartedTicks=$process.StartTime.ToUniversalTime().Ticks;Executable=$path;Kind='background-media'})}}
 }
}
