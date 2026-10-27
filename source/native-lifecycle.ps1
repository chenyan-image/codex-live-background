$script:NativeBackgroundId='codex-live-background/exe'
$script:BackgroundRuntimeFiles=@('installed.json','startup\startup.log','startup\guard-state.json','startup\monitor-state.json','startup\stop-guard','startup\registration.json','startup\skin-startup.log')
$script:BackgroundRetiredFiles=@('wallpapers\amber-dunes.jpg','wallpapers\blue-tide.jpg','wallpapers\forest-mist.jpg','wallpapers\midnight-ridge.jpg','wallpapers\quiet-moon.jpg','wallpapers\star-atlas.jpg','wallpapers\README.txt')
function Get-BackgroundSafePath {
 param([string]$Root,[string]$Relative)
 if(-not $Relative -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':')){throw '程序文件清单路径无效。'}
 $base=[IO.Path]::GetFullPath($Root);$path=[IO.Path]::GetFullPath([IO.Path]::Combine($base,$Relative))
 if(-not $path.StartsWith($base.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw '程序文件路径超出安装目录。'}
 return $path
}
function Assert-BackgroundInstallPath {
 param([string]$Root)
 $path=[IO.Path]::GetFullPath($Root)
 if((Test-DreamSkinPathEqual -Left $path -Right $env:WINDIR) -or $path.StartsWith($env:WINDIR.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Windows 系统文件目录不能作为程序安装位置。'}
 $cursor=$path;while($cursor){if(Test-Path -LiteralPath $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw '安装路径包含链接或联接，已停止。'}};$parent=Split-Path -Parent $cursor;if($parent -eq $cursor){break};$cursor=$parent}
 if((Test-Path -LiteralPath $path) -and -not(Test-Path -LiteralPath $path -PathType Container)){throw '安装位置是文件，请选择文件夹或盘符。'}
}
function Assert-BackgroundDestination {
 param([string]$Root,[string]$PackageRoot)
 $owned=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 foreach($file in @(Get-BackgroundOwnedFiles -Root $Root)){$null=$owned.Add($file.Replace('/','\'))}
 $manifest=[IO.File]::ReadAllText((Join-Path $PackageRoot 'manifest.json'))|ConvertFrom-Json
 foreach($relative in @($manifest.files|ForEach-Object{$_.path})+@('manifest.json','.codex-background-install.json')){
  $path=Get-BackgroundSafePath -Root $Root -Relative $relative;Assert-DreamBackgroundTree -Path $path -Parent $Root
  if((Test-Path -LiteralPath $path) -and -not $owned.Contains($relative.Replace('/','\'))){throw ('安装位置已有同名文件，已保留原文件：'+$path+'。可改用此盘中的任意其他文件夹。')}
 }
 if([IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Root)) -match '^[a-zA-Z]:\\$'){
  $disk=[IO.DriveInfo]::new([IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Root)));if(-not $disk.IsReady){throw '安装磁盘不可用，请检查盘符或磁盘连接。'}
  $required=[long](($manifest.files|Measure-Object bytes -Sum).Sum)+16MB;$reclaim=0L
  foreach($relative in $owned){$file=Get-BackgroundSafePath -Root $Root -Relative $relative;if(Test-Path -LiteralPath $file -PathType Leaf){$reclaim+=(Get-Item -LiteralPath $file).Length}}
  if($disk.AvailableFreeSpace+$reclaim -lt $required){throw ('安装磁盘空间不足，程序文件需要约 '+[math]::Ceiling($required/1MB)+' MiB。现有文件和服务未改动。')}
 }
}
function Get-BackgroundKnownManagerFiles {
 param([string]$Root)
 foreach($filename in @('CodexLiveBackground.exe','CodexDreamSkinManager.exe')){
  $exe=Join-Path $Root $filename;if(-not(Test-Path -LiteralPath $exe -PathType Leaf)){continue}
  $hash=(Get-FileHash -LiteralPath $exe).Hash
  $catalog=Join-Path $PSScriptRoot 'legacy-managers.json'
  if(Test-Path -LiteralPath $catalog){$known=([IO.File]::ReadAllText($catalog)|ConvertFrom-Json).releases|Where-Object{$_.sha256 -ieq $hash -and $_.filename -ieq $filename}|Select-Object -First 1;if($known){return @($known.files|Where-Object{$_})+@($filename,'manifest.json','.codex-background-install.json')+$script:BackgroundRuntimeFiles}}
  $trustedManifest=Join-Path $script:NativePackageRoot 'manifest.json'
  if(Test-Path -LiteralPath $trustedManifest){$trusted=[IO.File]::ReadAllText($trustedManifest)|ConvertFrom-Json;$entry=@($trusted.files|Where-Object {$_.path -ieq $filename})|Select-Object -First 1;if($entry -and $hash -ieq $entry.sha256){return @($trusted.files|ForEach-Object {$_.path})+@('manifest.json','.codex-background-install.json')+$script:BackgroundRuntimeFiles}}
  $history=Join-Path $PSScriptRoot 'historical-install-files.json'
  if(Test-Path -LiteralPath $history){$release=([IO.File]::ReadAllText($history)|ConvertFrom-Json).releases|Where-Object {$_.managerSha256 -and $_.managerSha256 -ieq $hash}|Select-Object -First 1;if($release){return @($release.files)+@($filename,'manifest.json','.codex-background-install.json')+$script:BackgroundRuntimeFiles}}
 }
 return @()
}
function Get-BackgroundOwnedFiles {
 param([string]$Root)
 $manifest=Join-Path $Root 'manifest.json';$marker=Join-Path $Root '.codex-background-install.json'
 if(Test-Path -LiteralPath $marker){
  try{
  $data=[IO.File]::ReadAllText($marker)|ConvertFrom-Json;if($data.productId -cne $script:NativeBackgroundId -or -not(Test-DreamSkinPathEqual -Left $data.root -Right $Root)){throw '安装标记归属不匹配。'}
  $trusted=[IO.File]::ReadAllText((Join-Path $script:NativePackageRoot 'manifest.json'))|ConvertFrom-Json
  $allowed=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach($item in @($trusted.files|ForEach-Object{$_.path})+@('manifest.json','卸载 Codex 动态背景.exe','卸载 CodexHub.exe','.codex-background-install.json')+$script:BackgroundRuntimeFiles+$script:BackgroundRetiredFiles){$null=$allowed.Add($item.Replace('/','\'))}
  $history=Join-Path $PSScriptRoot 'historical-install-files.json';if(Test-Path -LiteralPath $history){if((Get-Item -LiteralPath $history).Length -gt 1048576){throw '历史安装清单过大。'};$catalog=[IO.File]::ReadAllText($history)|ConvertFrom-Json;if($catalog.schema -ne 1){throw '历史安装清单无效。'};foreach($release in $catalog.releases){foreach($item in $release.files){$null=Get-BackgroundSafePath -Root $Root -Relative $item;$null=$allowed.Add($item.Replace('/','\'))}}}
  $files=@();foreach($file in $data.files){$null=Get-BackgroundSafePath -Root $Root -Relative $file;$canonical=$file.Replace('/','\');if(-not $allowed.Contains($canonical)){throw ('安装标记包含未经核验的文件，已停止：'+$file)};$files+=@($canonical)};return @($files|Select-Object -Unique)
  }catch{
   $reason=$_.Exception.Message;$known=@(Get-BackgroundKnownManagerFiles -Root $Root)
   if(-not $known.Count){throw $reason}
   [Console]::Out.WriteLine('旧安装标记异常，已根据程序 SHA-256 与发行清单恢复归属：'+$Root)
   return $known
  }
 }
 return @(Get-BackgroundKnownManagerFiles -Root $Root)
}
function Get-BackgroundLegacyMsi {
 param([string]$CatalogPath,[string]$FallbackRoot)
 $catalog=[IO.File]::ReadAllText($CatalogPath)|ConvertFrom-Json
 $installer=New-Object -ComObject WindowsInstaller.Installer;$get=[Reflection.BindingFlags]::GetProperty
 try{foreach($item in $catalog.products){
  try{$state=$installer.GetType().InvokeMember('ProductState',$get,$null,$installer,@([string]$item.productCode));if($state -ne 5){continue}
   $name=$installer.GetType().InvokeMember('ProductInfo',$get,$null,$installer,@([string]$item.productCode,'ProductName'));if($name -cne 'Codex 动态背景' -and $name -cne 'Codex Background isolated package test'){throw '旧 MSI 产品名称不匹配。'}
   $cache=$installer.GetType().InvokeMember('ProductInfo',$get,$null,$installer,@([string]$item.productCode,'LocalPackage'))
   $root=(Get-ItemProperty -LiteralPath ('HKCU:\Software\CodexLiveBackground\Installs\'+$item.productCode) -Name InstallRoot -ErrorAction SilentlyContinue).InstallRoot
   if(-not $root){try{$root=$installer.GetType().InvokeMember('ProductInfo',$get,$null,$installer,@([string]$item.productCode,'InstallLocation'))}catch{}}
   if(-not $root -or -not [IO.Path]::IsPathRooted($root)){$root=$FallbackRoot}
   Assert-BackgroundInstallPath $root
   [pscustomobject]@{ProductCode=$item.productCode;Version=$item.version;Root=[IO.Path]::GetFullPath($root);Cache=$cache;SourceName=$item.fileName;SourceUrl=$item.url;SourceSha256=$item.sha256;RecoverySource=$null}
  }catch [Runtime.InteropServices.COMException]{if($_.Exception.HResult -ne -2147023291){throw}}
 }}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)}
}
function Get-BackgroundMsiFiles {
 param($Product)
 if(-not(Test-Path -LiteralPath $Product.Cache)){throw '旧 MSI 缓存缺失，未伪造删除安装记录。请先用原版安装包修复卸载。'}
 $installer=New-Object -ComObject WindowsInstaller.Installer;$invoke=[Reflection.BindingFlags]::InvokeMethod;$get=[Reflection.BindingFlags]::GetProperty
 $db=$installer.GetType().InvokeMember('OpenDatabase',$invoke,$null,$installer,@([string]$Product.Cache,0))
 function Rows([string]$sql,[int]$columns){$view=$db.GetType().InvokeMember('OpenView',$invoke,$null,$db,@($sql));try{$null=$view.GetType().InvokeMember('Execute',$invoke,$null,$view,@());while($r=$view.GetType().InvokeMember('Fetch',$invoke,$null,$view,@())){try{$values=@();for($i=1;$i -le $columns;$i++){$values+=@($r.GetType().InvokeMember('StringData',$get,$null,$r,@($i)))};,[object[]]$values}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($r)}}}finally{$null=$view.GetType().InvokeMember('Close',$invoke,$null,$view,@());[void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)}}
 try{
  $dirs=@{};foreach($row in Rows 'SELECT `Directory`,`Directory_Parent`,`DefaultDir` FROM `Directory`' 3){$dirs[$row[0]]=@{parent=$row[1];name=($row[2] -split '\|')[-1]}}
  $components=@{};foreach($row in Rows 'SELECT `Component`,`Directory_` FROM `Component`' 2){$components[$row[0]]=$row[1]}
  foreach($row in Rows 'SELECT `Component_`,`FileName` FROM `File`' 2){$folder=$components[$row[0]];$parts=@();$depth=0;while($folder -ne 'INSTALLDIR'){if(-not $dirs.ContainsKey($folder) -or ++$depth -gt 30){throw 'MSI 文件不在本产品目录内。'};if($dirs[$folder].name -ne '.'){$parts=@($dirs[$folder].name)+$parts};$folder=$dirs[$folder].parent};$relative=(@($parts)+@(($row[1] -split '\|')[-1])) -join '\';$null=Get-BackgroundSafePath -Root $Product.Root -Relative $relative;$relative}
 }finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($db);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)}
}
function Resolve-BackgroundMsiCache {
 param($Product,[string]$OperationRoot)
 if(Test-Path -LiteralPath $Product.Cache){return}
 if(-not $Product.SourceName -or $Product.SourceSha256 -notmatch '^[A-Fa-f0-9]{64}$'){throw '旧 MSI 缓存缺失，且无已验证的原版缓存，未修改 Windows Installer 注册数据。'}
 [Console]::Out.WriteLine('正在恢复旧版卸载缓存（仅用于注销旧 MSI）…')
 $directory=Join-Path $OperationRoot 'msi-cache';$null=New-Item -ItemType Directory -Path $directory -Force;$target=Get-BackgroundSafePath -Root $directory -Relative $Product.SourceName
 $candidates=@();if($Product.LocalRecoveryFile){$candidates+=@($Product.LocalRecoveryFile)}
 foreach($root in @((Split-Path -Parent $script:NativeSetupExecutable),(Join-Path $env:USERPROFILE Downloads),[Environment]::GetFolderPath('Desktop'))){if($root -and (Test-Path -LiteralPath $root)){$candidates+=@(Get-ChildItem -LiteralPath $root -File -Filter $Product.SourceName -ErrorAction SilentlyContinue|ForEach-Object{$_.FullName})}}
 $found=$false;foreach($file in $candidates){if((Test-Path -LiteralPath $file) -and (Get-FileHash -LiteralPath $file).Hash -ieq $Product.SourceSha256){Copy-Item -LiteralPath $file -Destination $target -Force;$found=$true;break}}
 if(-not $found){$uri=[uri]$Product.SourceUrl;if($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com' -or -not ($uri.AbsolutePath.StartsWith('/chenyan-image/codex-live-background/releases/download/') -or $uri.AbsolutePath.StartsWith('/chenyan-image/codex-live-background-pet/releases/download/'))){throw '旧缓存下载来源无效。'};[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;$client=[Net.WebClient]::new();try{$client.DownloadFile($uri,$target)}finally{$client.Dispose()}}
 if((Get-FileHash -LiteralPath $target).Hash -ine $Product.SourceSha256){throw '旧卸载缓存校验失败，未继续迁移。'}
 $Product.Cache=$target;$Product|Add-Member -NotePropertyName RecoverySource -NotePropertyValue $target -Force
}
function Repair-BackgroundMsiCache {
 param($Product,[string]$Log)
 $args='/fv "'+$Product.RecoverySource+'" /qn /norestart SKIPBACKGROUND=1 DREAM_MSI_PROBE=0 LAUNCHMANAGER=0 INSTALLDIR="'+$Product.Root.TrimEnd('\')+'" /L*V "'+$Log+'"'
 $p=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\msiexec.exe') -ArgumentList $args -WindowStyle Hidden -PassThru
 if(-not $p.WaitForExit(300000)){try{$p.Kill();$null=$p.WaitForExit(15000)}catch{};throw ('DREAM_MSI_BUSY:旧缓存恢复超时，已终止本次启动的安装器客户端，恢复文件保留。日志：'+$Log)}
 if($p.ExitCode -notin @(0,3010)){throw ('旧卸载缓存恢复未完成。日志：'+$Log)}
}
function Remove-BackgroundOwnedFiles {
 param([string]$Root,[string[]]$Files)
 Assert-BackgroundInstallPath $Root
 foreach($relative in @($Files|Select-Object -Unique)){$path=Get-BackgroundSafePath -Root $Root -Relative $relative;Assert-DreamBackgroundTree -Path $path -Parent $Root;if(Test-Path -LiteralPath $path){if((Get-Item -LiteralPath $path -Force).PSIsContainer){throw '程序文件已变成目录，已保留。'};Remove-Item -LiteralPath $path -Force -ErrorAction Stop}}
 $base=[IO.Path]::GetFullPath($Root).TrimEnd('\');$directories=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 foreach($relative in $Files){$parent=Split-Path -Parent (Get-BackgroundSafePath -Root $Root -Relative $relative);while($parent -and $parent.TrimEnd('\') -ne $base -and $parent.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){$null=$directories.Add($parent);$parent=Split-Path -Parent $parent}}
 foreach($directory in @($directories|Sort-Object Length -Descending)){if((Test-Path -LiteralPath $directory -PathType Container) -and -not @(Get-ChildItem -LiteralPath $directory -Force).Count){Remove-Item -LiteralPath $directory -Force -ErrorAction Stop}}
 if($base -ne [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Root)).TrimEnd('\') -and (Test-Path -LiteralPath $Root -PathType Container) -and -not @(Get-ChildItem -LiteralPath $Root -Force).Count){Remove-Item -LiteralPath $Root -Force -ErrorAction Stop}
}
function Invoke-BackgroundMsiRemove {
 param($Product,[string]$Log)
 $source=if($Product.RecoverySource){'"'+$Product.RecoverySource+'"'}else{$Product.ProductCode};$args='/x '+$source+' /qn /norestart SKIPBACKGROUND=1 DREAM_MSI_PROBE=0 LAUNCHMANAGER=0 /L*V "'+$Log+'"'
 $p=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\msiexec.exe') -ArgumentList $args -WindowStyle Hidden -PassThru
 if(-not $p.WaitForExit(300000)){try{$p.Kill();$null=$p.WaitForExit(15000)}catch{};throw 'DREAM_MSI_BUSY:旧版卸载超时，已终止本次启动的安装器客户端，恢复文件保留。'}
 if($p.ExitCode -notin @(0,1605,3010)){throw ('旧 MSI 卸载未完成，代码 '+$p.ExitCode+'。日志：'+$Log)}
 $installer=New-Object -ComObject WindowsInstaller.Installer;try{if($installer.GetType().InvokeMember('ProductState',[Reflection.BindingFlags]::GetProperty,$null,$installer,@([string]$Product.ProductCode)) -eq 5){throw '旧 MSI 安装记录仍有效，未继续部署新版。'}}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)}
}
function Remove-BackgroundLegacyMsi {
 param($Product,[string]$BackupRoot)
 $null=New-Item -ItemType Directory -Path $BackupRoot -Force;Resolve-BackgroundMsiCache -Product $Product -OperationRoot $BackupRoot
 $files=@(Get-BackgroundMsiFiles -Product $Product)+@('installed.json','startup\startup.log','startup\guard-state.json','startup\monitor-state.json','startup\stop-guard','startup\registration.json','startup\skin-startup.log');$backup=Join-Path $BackupRoot ($Product.ProductCode.Trim('{}'))
 foreach($relative in $files){$source=Get-BackgroundSafePath -Root $Product.Root -Relative $relative;if(Test-Path -LiteralPath $source){Assert-DreamBackgroundTree -Path $source -Parent $Product.Root;$target=Get-BackgroundSafePath -Root $backup -Relative $relative;$null=New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force;Copy-Item -LiteralPath $source -Destination $target -Force -ErrorAction Stop}}
 try{if($Product.RecoverySource){Repair-BackgroundMsiCache -Product $Product -Log (Join-Path $BackupRoot 'cache-repair.log')};Remove-BackgroundOwnedFiles -Root $Product.Root -Files $files;Invoke-BackgroundMsiRemove -Product $Product -Log (Join-Path $BackupRoot ($Product.ProductCode.Trim('{}')+'.log'))}
 catch{if($_.Exception.Message.StartsWith('DREAM_MSI_BUSY:')){throw};foreach($relative in $files){$source=Get-BackgroundSafePath -Root $backup -Relative $relative;if(Test-Path -LiteralPath $source){$target=Get-BackgroundSafePath -Root $Product.Root -Relative $relative;$null=New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force;Copy-Item -LiteralPath $source -Destination $target -Force -ErrorAction Stop}};throw}
}
function Test-BackgroundOwnedExecutable {
 param([string]$Executable,[string[]]$Roots,[string]$StateRoot)
 foreach($root in $Roots){foreach($relative in @('CodexLiveBackground.exe','CodexDreamSkinManager.exe','CodexHubUpdater.exe','startup\DreamSkinPortHook.exe','engine\runtime\node\node.exe','engine\runtime\ffmpeg\ffmpeg.exe','engine\runtime\ffmpeg\ffprobe.exe')){if(Test-DreamSkinPathEqual -Left $Executable -Right ([IO.Path]::Combine([IO.Path]::GetFullPath($root),$relative))){return $true}}}
 foreach($relative in @('startup','runtime')){$prefix=[IO.Path]::GetFullPath((Join-Path $StateRoot $relative)).TrimEnd('\')+'\';if($Executable.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){return $true}}
 return $false
}
function Get-BackgroundOwnedProcesses {
 param([string[]]$Roots,[string]$StateRoot,[int[]]$ExcludeIds)
 if(-not $Roots.Count -and -not(Test-Path -LiteralPath (Join-Path $StateRoot 'startup')) -and -not(Test-Path -LiteralPath (Join-Path $StateRoot 'runtime'))){return}
 Initialize-DreamSkinNativeInspection
 foreach($item in @(Get-CimInstance Win32_Process -Filter "Name='CodexHubUpdater.exe' OR Name='CodexLiveBackground.exe' OR Name='CodexDreamSkinManager.exe' OR Name='DreamSkinPortHook.exe' OR Name='node.exe' OR Name='ffmpeg.exe' OR Name='ffprobe.exe' OR Name='powershell.exe'")){
  $id=[int]$item.ProcessId;if($id -in $ExcludeIds){continue};$owned=$false
  try{if(-not [CodexDreamSkin.NativeInspection]::CurrentUser($id)){continue};$path=[CodexDreamSkin.NativeInspection]::ProcessPath($id)
   $owned=Test-BackgroundOwnedExecutable -Executable $path -Roots $Roots -StateRoot $StateRoot
   foreach($root in @($Roots)+@((Join-Path $StateRoot 'startup'))){if($item.Name -ieq 'powershell.exe'){foreach($relative in @('control.ps1','native-actions.ps1','startup\monitor-skin.ps1','monitor-skin.ps1','register-port.ps1')){if(Test-DreamSkinCommandLineToken -CommandLine "$($item.CommandLine)" -Token (Join-Path $root $relative)){$owned=$true;break}}}}
   if(-not $owned -and $item.Name -ieq 'powershell.exe'){foreach($root in $Roots){foreach($file in @('manager-actions.ps1','start-dream-skin.ps1','verify-dream-skin.ps1','restore-dream-skin.ps1','query-codex-package.ps1','install-dream-skin.ps1','apply-theme-and-recover.ps1','apply-community-theme.ps1','theme-windows.ps1','invoke-script-request.ps1')){if(Test-DreamSkinCommandLineToken -CommandLine "$($item.CommandLine)" -Token (Join-Path $root ('engine\scripts\'+$file))){$owned=$true;break}}}}
   if(-not $owned -and $item.Name -ieq 'node.exe'){foreach($root in $Roots){foreach($file in @('injector.mjs','prepare-video.mjs','preview-media.mjs','validate-video-file.mjs')){if(Test-DreamSkinCommandLineToken -CommandLine "$($item.CommandLine)" -Token (Join-Path $root ('engine\scripts\'+$file))){$owned=$true;break}}}}
   if(-not $owned -and $item.Name -ieq 'powershell.exe'){$match=[regex]::Match("$($item.CommandLine)",'"([^"\r\n]*\\CodexBackgroundSetup-[a-f0-9]{32}\\(?:native-actions|control)\.ps1)"');if($match.Success){$tempScript=[IO.Path]::GetFullPath($match.Groups[1].Value);$tempBoundary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\CodexBackgroundSetup-';if($tempScript.StartsWith($tempBoundary,[StringComparison]::OrdinalIgnoreCase)){$owned=$true}}}
   if($owned){$p=Get-Process -Id $id -ErrorAction Stop;[pscustomobject]@{ProcessId=$id;StartedTicks=$p.StartTime.ToUniversalTime().Ticks;Executable=$path;Kind='background-component'}}
  }catch{if($owned -and (Get-Process -Id $id -ErrorAction SilentlyContinue)){throw}}
 }
}
function Stop-BackgroundOwnedProcesses {
 param([string[]]$Roots,[string]$StateRoot,[int[]]$ExcludeIds)
 foreach($item in @(Get-BackgroundOwnedProcesses -Roots $Roots -StateRoot $StateRoot -ExcludeIds $ExcludeIds)){Stop-DreamBackgroundWorker -Identity $item}
 if(@(Get-BackgroundOwnedProcesses -Roots $Roots -StateRoot $StateRoot -ExcludeIds $ExcludeIds).Count){throw '仍有本项目进程运行，未继续删除程序文件。'}
}
function Get-BackgroundPlan {
 param([string]$InstallRoot,[string]$PackageRoot)
 Assert-BackgroundInstallPath $InstallRoot
 $registered=@(Get-DreamBackgroundRegisteredInstallRoots|Select-Object -Unique)
 $fallback=if($registered.Count){$registered[0]}else{Join-Path $env:LOCALAPPDATA 'Programs\CodexLiveBackground'}
 $products=@(Get-BackgroundLegacyMsi -CatalogPath (Join-Path $PackageRoot 'legacy-msi-products.json') -FallbackRoot $fallback)
 $roots=@($InstallRoot,$fallback,(Join-Path $env:LOCALAPPDATA 'Programs\CodexLiveBackground'),(Join-Path $env:LOCALAPPDATA 'Programs\CodexDreamSkinManager'))+@($registered)+@($products|ForEach-Object{$_.Root})
 $owned=@();foreach($root in @($roots|Select-Object -Unique)){
  if(-not(Test-Path -LiteralPath $root)){continue}
  try{if(@(Get-BackgroundOwnedFiles -Root $root).Count -or @($products|Where-Object{$_.Root -ieq $root}).Count){$owned+=@($root)}}
  catch{if(Test-DreamSkinPathEqual -Left $root -Right $InstallRoot){throw};[Console]::Out.WriteLine('保留无法核验的其他旧目录：'+$root+'；原因：'+$_.Exception.Message)}
 }
 # An already verified service may point at a portable/check copy outside
 # the stale installation registry. Include it for integration shutdown only;
 # Get-BackgroundOwnedFiles still independently controls every file removal.
 $task=Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue
 if($task){
  $state=Join-Path $env:LOCALAPPDATA 'CodexDreamSkin'
  $service=Resolve-DreamBackgroundUpgradeStartup -Task $task -Installs @((@($owned)+@($registered))|Select-Object -Unique|ForEach-Object{[pscustomobject]@{Root=$_}}) -StateRoot $state -PackageRoot $PackageRoot
  if(-not(Test-DreamSkinPathEqual -Left $service -Right (Join-Path $state 'startup'))){$owned=@($owned)+@(Split-Path -Parent $service);$owned=@($owned|Select-Object -Unique)}
 }
 $authorization=$products.Count -gt 0
 $parent=[IO.Path]::GetFullPath($InstallRoot);while(-not(Test-Path -LiteralPath $parent)){$parent=Split-Path -Parent $parent;if(-not $parent){throw '安装磁盘不可用。'}}
 $probe=Join-Path $parent ('.codex-write-'+[guid]::NewGuid().ToString('N')+'.tmp')
 try{$stream=[IO.FileStream]::new($probe,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::DeleteOnClose);$stream.Dispose()}catch [UnauthorizedAccessException]{$authorization=$true}
 [pscustomobject]@{root=[IO.Path]::GetFullPath($InstallRoot);roots=@($owned);products=@($products);requiresAuthorization=$authorization;baseVersion='1.5.4'}
}
function Remove-BackgroundFollowRegistration {
 param([string[]]$Roots,[string]$StateRoot)
 $key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
 $value=(Get-ItemProperty -LiteralPath $key -Name CodexHubFollow -ErrorAction SilentlyContinue).CodexHubFollow
 foreach($root in $Roots){$expected='"'+(Join-Path $root 'CodexHubUpdater.exe')+'" --follow "'+[IO.Path]::GetFullPath($StateRoot)+'"';if($value -and "$value" -ieq $expected){Remove-ItemProperty -LiteralPath $key -Name CodexHubFollow -ErrorAction Stop;break}}
}
function Stop-BackgroundIntegrations {
 param([string[]]$Roots,[string]$StateRoot,[string]$PackageRoot,[switch]$Uninstall,[int[]]$ExcludeIds)
 $task=Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue
 $legacy=Join-Path $StateRoot 'startup';$startup=if($task){Resolve-DreamBackgroundUpgradeStartup -Task $task -Installs @($Roots|ForEach-Object{[pscustomobject]@{Root=$_}}) -StateRoot $StateRoot -PackageRoot $PackageRoot}elseif(Test-Path -LiteralPath $legacy){$legacy}else{$null}
 $valid=@($legacy)+@($Roots|ForEach-Object{Join-Path $_ 'startup'})
 if($startup -and -not @($valid|Where-Object{Test-DreamSkinPathEqual -Left $_ -Right $startup}).Count){throw '同名启动服务不在已识别路径内，未移除。'}
 $candidates=@();if($startup){$candidates+=@($startup)};$candidates+=@($valid|Where-Object{Test-Path -LiteralPath $_})
 $first=$true;foreach($serviceRoot in @($candidates|Select-Object -Unique)){$install=if(Test-DreamSkinPathEqual -Left $serviceRoot -Right $legacy){$PackageRoot}else{Split-Path -Parent $serviceRoot};Remove-DreamBackgroundService -InstallRoot $install -StateRoot $StateRoot -PackageRoot $PackageRoot -StartupRoot $serviceRoot -ForUpgrade:((-not $Uninstall) -or (-not $first)) -AllowDisconnected -GraceSeconds 2;$first=$false}
 Restore-DreamBackgroundShortcuts -StateRoot $StateRoot
 Remove-BackgroundFollowRegistration -Roots $Roots -StateRoot $StateRoot
 Stop-BackgroundOwnedProcesses -Roots $Roots -StateRoot $StateRoot -ExcludeIds $ExcludeIds
}
function Write-BackgroundNativeRegistration {
 param([string]$Root,[string]$Version)
 $key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLiveBackground'
 $argument=$Root;if($argument.EndsWith('\')){$argument+='\'}
 $null=New-Item -Path $key -Force;$values=@{DisplayName='CodexHub';DisplayVersion=$Version;Publisher='codex-live-background contributors';InstallLocation=$Root;UninstallString=('"'+(Join-Path $Root '卸载 CodexHub.exe')+'" --uninstall "'+$argument+'"');DisplayIcon=(Join-Path $Root 'CodexBackground.ico');NoModify=1;NoRepair=1;URLInfoAbout='https://github.com/chenyan-image/codex-live-background-pet'}
 foreach($name in $values.Keys){$type=if($values[$name] -is [int]){'DWord'}else{'String'};$null=New-ItemProperty -LiteralPath $key -Name $name -Value $values[$name] -PropertyType $type -Force}
 $key='HKCU:\Software\CodexLiveBackground';$null=New-Item -Path $key -Force;$null=New-ItemProperty -LiteralPath $key -Name InstallRoot -Value $Root -PropertyType String -Force;$null=New-ItemProperty -LiteralPath $key -Name InstallKind -Value EXE -PropertyType String -Force
 Write-BackgroundInstallHistory -Root $Root -Version $Version -Status installed
}
function Write-BackgroundInstallHistory {
 param([string]$Root,[string]$Version,[ValidateSet('installed','removed')][string]$Status,[string]$HistoryDirectory=(Join-Path $env:LOCALAPPDATA 'CodexHub'))
 $directory=$HistoryDirectory;$file=Join-Path $directory 'installations.json';$entries=@()
 try{
  $null=New-Item -ItemType Directory -Path $directory -Force
  if(Test-Path -LiteralPath $file){try{$old=[IO.File]::ReadAllText($file)|ConvertFrom-Json;if($old.schema -eq 1 -and $old.productId -eq $script:NativeBackgroundId){$prior=@($old.installations|Where-Object{Test-DreamSkinPathEqual -Left "$($_.root)" -Right $Root});if(-not $Version -and $prior.Count){$Version=$prior[-1].version};$entries=@($old.installations|Where-Object{-not(Test-DreamSkinPathEqual -Left "$($_.root)" -Right $Root)})}}catch{Copy-Item -LiteralPath $file -Destination ($file+'.invalid-'+[guid]::NewGuid().ToString('N'))}}
  $entries+=@([ordered]@{root=[IO.Path]::GetFullPath($Root);version=$Version;status=$Status;updatedUtc=[DateTime]::UtcNow.ToString('o')})
  $temporary=$file+'.tmp-'+[guid]::NewGuid().ToString('N');[IO.File]::WriteAllText($temporary,([ordered]@{schema=1;productId=$script:NativeBackgroundId;installations=@($entries)}|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
  if(Test-Path -LiteralPath $file){[IO.File]::Replace($temporary,$file,[NullString]::Value)}else{[IO.File]::Move($temporary,$file)}
 }catch{[Console]::Out.WriteLine('安装路径记录未能保存：'+$_.Exception.Message)}
}
function Remove-BackgroundNativeRegistration {
 param([string]$Root)
 Write-BackgroundInstallHistory -Root $Root -Version '' -Status removed
 foreach($key in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLiveBackground','HKCU:\Software\CodexLiveBackground')){if(Test-Path -LiteralPath $key){$data=Get-ItemProperty -LiteralPath $key;$path=if($data.InstallLocation){$data.InstallLocation}else{$data.InstallRoot};if($path -and (Test-DreamSkinPathEqual -Left $path -Right $Root)){Remove-Item -LiteralPath $key -Recurse -Force -ErrorAction Stop}}}
}
function Write-BackgroundFollowRegistration {
 param([string]$Root,[string]$StateRoot)
 # The existing port guard now hosts follow detection; retire only our legacy entry.
 Remove-BackgroundFollowRegistration -Roots @($Root) -StateRoot $StateRoot
}
function Invoke-BackgroundNativeAction {
 param([ValidateSet('Install','Uninstall')][string]$Action,[string]$InstallRoot,[string]$StateRoot,[string]$PackageRoot,[string]$SetupExecutable,[string]$OperationRoot,[int]$ManagerPid)
 $script:NativePackageRoot=$PackageRoot
 if($Action -eq 'Install'){
  $preflight=[IO.File]::ReadAllText((Join-Path $PackageRoot 'manifest.json'))|ConvertFrom-Json
  foreach($entry in $preflight.files){$source=Get-BackgroundSafePath -Root $PackageRoot -Relative $entry.path;if(-not(Test-Path -LiteralPath $source -PathType Leaf) -or (Get-Item -LiteralPath $source).Length -ne $entry.bytes -or (Get-FileHash -LiteralPath $source).Hash -ine $entry.sha256){throw ('安装源文件校验失败，现有服务未停止：'+$entry.path)}}
 }
 $plan=Get-BackgroundPlan -InstallRoot $InstallRoot -PackageRoot $PackageRoot
 if($Action -eq 'Install'){Assert-BackgroundDestination -Root $InstallRoot -PackageRoot $PackageRoot}
 [Console]::Out.WriteLine('正在停止并验证旧后台组件…')
 Stop-BackgroundIntegrations -Roots $plan.roots -StateRoot $StateRoot -PackageRoot $PackageRoot -Uninstall:($Action -eq 'Uninstall') -ExcludeIds @($PID,$ManagerPid)
 foreach($product in $plan.products){[Console]::Out.WriteLine('正在正式移除旧 MSI 记录 '+$product.Version+'…');Remove-BackgroundLegacyMsi -Product $product -BackupRoot (Join-Path $OperationRoot 'legacy-backup')}
 Remove-BackgroundDesktopEntries -Roots (@($plan.roots)+@($InstallRoot))
 foreach($root in $plan.roots){$files=@(Get-BackgroundOwnedFiles -Root $root);if($files.Count){Remove-BackgroundOwnedFiles -Root $root -Files $files}}
 if($Action -eq 'Uninstall'){
  [Console]::Out.WriteLine('正在清理壁纸副本、预览和下载缓存…');Remove-DreamBackgroundGeneratedData -StateRoot $StateRoot
  $cleanupRoots=@($plan.roots)+@($InstallRoot);foreach($root in $cleanupRoots){Remove-BackgroundNativeRegistration -Root $root}
  Remove-BackgroundDesktopEntries -Roots $cleanupRoots
  Stop-BackgroundOwnedProcesses -Roots $plan.roots -StateRoot $StateRoot -ExcludeIds @($PID,$ManagerPid)
  [Console]::Out.WriteLine('卸载完成：本项目进程和服务已退出，原始壁纸与 Codex 登录资料保留。');return
 }
 [Console]::Out.WriteLine('正在安装 CodexHub 程序文件…')
 $manifest=[IO.File]::ReadAllText((Join-Path $PackageRoot 'manifest.json'))|ConvertFrom-Json;$files=@($manifest.files|ForEach-Object{$_.path})+@('manifest.json','卸载 Codex 动态背景.exe','卸载 CodexHub.exe','.codex-background-install.json')+$script:BackgroundRuntimeFiles
 $newDirectory=-not(Test-Path -LiteralPath $InstallRoot);$null=New-Item -ItemType Directory -Path $InstallRoot -Force
 if($newDirectory -and [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
  $acl=Get-Acl -LiteralPath $InstallRoot;$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User;$rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,'Modify','ContainerInherit,ObjectInherit','None','Allow');$acl.AddAccessRule($rule);Set-Acl -LiteralPath $InstallRoot -AclObject $acl
 }
 # On a shared folder or volume root, grant access only to new program
 # subdirectories. Never change the ACL of the drive or an existing directory.
 if(-not $newDirectory -and [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
  foreach($name in @($manifest.files|ForEach-Object{($_.path.Replace('/','\') -split '\\')[0]}|Select-Object -Unique)){
   if(-not @($manifest.files|Where-Object{$_.path.Replace('/','\').StartsWith($name+'\',[StringComparison]::OrdinalIgnoreCase)}).Count){continue}
   $directory=Get-BackgroundSafePath -Root $InstallRoot -Relative $name
   if(-not(Test-Path -LiteralPath $directory)){
    $null=New-Item -ItemType Directory -Path $directory -Force;$acl=Get-Acl -LiteralPath $directory;$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User;$rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,'Modify','ContainerInherit,ObjectInherit','None','Allow');$acl.AddAccessRule($rule);Set-Acl -LiteralPath $directory -AclObject $acl
   }
  }
 }
 foreach($entry in $manifest.files){$source=Get-BackgroundSafePath -Root $PackageRoot -Relative $entry.path;if((Get-FileHash -LiteralPath $source).Hash -ine $entry.sha256){throw '安装源文件校验失败。'};$target=Get-BackgroundSafePath -Root $InstallRoot -Relative $entry.path;Assert-DreamBackgroundTree -Path $target -Parent $InstallRoot;$null=New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force;Copy-Item -LiteralPath $source -Destination $target -Force -ErrorAction Stop}
 Copy-Item -LiteralPath (Join-Path $PackageRoot 'manifest.json') -Destination (Join-Path $InstallRoot 'manifest.json') -Force
 $uninstaller=Join-Path $PackageRoot '卸载 CodexHub.exe';if(-not(Test-Path -LiteralPath $uninstaller)){$uninstaller=$SetupExecutable}
 Copy-Item -LiteralPath $uninstaller -Destination (Join-Path $InstallRoot '卸载 CodexHub.exe') -Force -ErrorAction Stop
 [IO.File]::WriteAllText((Join-Path $InstallRoot '.codex-background-install.json'),(@{productId=$script:NativeBackgroundId;version=$manifest.version;baseVersion='1.5.4';root=[IO.Path]::GetFullPath($InstallRoot);files=$files;pending=$true}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
 Write-BackgroundNativeRegistration -Root $InstallRoot -Version $manifest.version
 [Console]::Out.WriteLine('正在启用并验证背景服务，请稍候…');Invoke-BackgroundNativeActivate -Root $InstallRoot -ManagerPid $ManagerPid
 $marker=Join-Path $InstallRoot '.codex-background-install.json';$record=[IO.File]::ReadAllText($marker)|ConvertFrom-Json;$record.pending=$false;[IO.File]::WriteAllText($marker,($record|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
 if([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
  foreach($relative in @($files|Where-Object{$_ -notmatch '[\\/]'}|Select-Object -Unique)){
   $file=Get-BackgroundSafePath -Root $InstallRoot -Relative $relative;if(Test-Path -LiteralPath $file -PathType Leaf){$acl=Get-Acl -LiteralPath $file;$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User;$rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,'Modify','Allow');$acl.AddAccessRule($rule);Set-Acl -LiteralPath $file -AclObject $acl}
  }
 }
 Write-BackgroundFollowRegistration -Root $InstallRoot -StateRoot $StateRoot
 [Console]::Out.WriteLine('安装完成。请从桌面唯一的管理器图标打开软件。')
}
function Remove-BackgroundDesktopEntries {
 param([string[]]$Roots)
 $shell=New-Object -ComObject WScript.Shell
 try{foreach($name in @('CodexHub.lnk','Codex 动态背景.lnk','yz Codex 动态背景.lnk')){
  $path=Join-Path ([Environment]::GetFolderPath('Desktop')) $name;if(-not(Test-Path -LiteralPath $path)){continue};$link=$shell.CreateShortcut($path)
  if(@($Roots|Where-Object{Test-DreamSkinPathEqual -Left $link.TargetPath -Right (Join-Path $_ 'CodexLiveBackground.exe')}).Count){Remove-Item -LiteralPath $path -Force -ErrorAction Stop}
 }}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)}
}
