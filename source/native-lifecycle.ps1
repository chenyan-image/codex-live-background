$script:NativeBackgroundId='codex-live-background/exe'
$script:BackgroundRuntimeFiles=@('installed.json','startup\startup.log','startup\guard-state.json','startup\monitor-state.json','startup\stop-guard','startup\registration.json','startup\skin-startup.log')
function Get-BackgroundSafePath {
 param([string]$Root,[string]$Relative)
 if(-not $Relative -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':')){throw '程序文件清单路径无效。'}
 $base=[IO.Path]::GetFullPath($Root).TrimEnd('\');$path=[IO.Path]::GetFullPath((Join-Path $base $Relative))
 if(-not $path.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){throw '程序文件路径超出安装目录。'}
 return $path
}
function Assert-BackgroundInstallPath {
 param([string]$Root)
 $path=[IO.Path]::GetFullPath($Root).TrimEnd('\')
 $forbidden=@([IO.Path]::GetPathRoot($path).TrimEnd('\'),$env:USERPROFILE,$env:LOCALAPPDATA,$env:APPDATA,$env:WINDIR,[Environment]::GetFolderPath('ProgramFiles'),[Environment]::GetFolderPath('Desktop'))
 if($path -in $forbidden -or $path.StartsWith($env:WINDIR+'\',[StringComparison]::OrdinalIgnoreCase)){throw '请选择专用的软件文件夹，不能直接安装到磁盘根目录或系统目录。'}
 $cursor=$path;while($cursor){if(Test-Path -LiteralPath $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw '安装路径包含链接或联接，已停止。'}};$parent=Split-Path -Parent $cursor;if($parent -eq $cursor){break};$cursor=$parent}
 if(Test-Path -LiteralPath $path){$pending=[Collections.Generic.Stack[string]]::new();$pending.Push($path);while($pending.Count){foreach($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force){if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw '程序目录包含链接或联接，已保留。'};if($entry.PSIsContainer){$pending.Push($entry.FullName)}}}}
}
function Get-BackgroundOwnedFiles {
 param([string]$Root)
 $manifest=Join-Path $Root 'manifest.json';$marker=Join-Path $Root '.codex-background-install.json'
 if(Test-Path -LiteralPath $marker){$data=[IO.File]::ReadAllText($marker)|ConvertFrom-Json;if($data.productId -cne $script:NativeBackgroundId -or -not(Test-DreamSkinPathEqual -Left $data.root -Right $Root)){throw '安装标记归属不匹配。'};$trusted=[IO.File]::ReadAllText((Join-Path $script:NativePackageRoot 'manifest.json'))|ConvertFrom-Json;$allowed=@($trusted.files|ForEach-Object{$_.path})+@('manifest.json','卸载 Codex 动态背景.exe','卸载 CodexHub.exe','.codex-background-install.json')+$script:BackgroundRuntimeFiles;foreach($file in $data.files){if($file -notin $allowed){throw '安装标记包含未经核验的文件，已停止。'}};return @($data.files)}
 $exe=Join-Path $Root 'CodexLiveBackground.exe';$catalog=Join-Path $PSScriptRoot 'legacy-managers.json'
 if((Test-Path -LiteralPath $exe) -and (Test-Path -LiteralPath $catalog)){$hash=(Get-FileHash -LiteralPath $exe).Hash;$known=([IO.File]::ReadAllText($catalog)|ConvertFrom-Json).releases|Where-Object{$_.sha256 -ceq $hash}|Select-Object -First 1;if($known){return @($known.files)+@('CodexLiveBackground.exe','manifest.json')+$script:BackgroundRuntimeFiles}}
 return @()
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
 if(-not $found){$uri=[uri]$Product.SourceUrl;if($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com' -or -not $uri.AbsolutePath.StartsWith('/chenyan-image/codex-live-background/releases/download/')){throw '旧缓存下载来源无效。'};[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12;$client=[Net.WebClient]::new();try{$client.DownloadFile($uri,$target)}finally{$client.Dispose()}}
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
 if(Test-Path -LiteralPath $Root){$directories=@(Get-ChildItem -LiteralPath $Root -Directory -Recurse -Force|Sort-Object {$_.FullName.Length} -Descending);foreach($dir in @($directories)+@(Get-Item -LiteralPath $Root)){if(-not @(Get-ChildItem -LiteralPath $dir.FullName -Force).Count){Remove-Item -LiteralPath $dir.FullName -Force -ErrorAction Stop}}}
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
function Get-BackgroundOwnedProcesses {
 param([string[]]$Roots,[string]$StateRoot,[int[]]$ExcludeIds)
 Initialize-DreamSkinNativeInspection
 foreach($item in @(Get-CimInstance Win32_Process -Filter "Name='CodexLiveBackground.exe' OR Name='CodexDreamSkinManager.exe' OR Name='DreamSkinPortHook.exe' OR Name='node.exe' OR Name='ffmpeg.exe' OR Name='ffprobe.exe' OR Name='powershell.exe'")){
  $id=[int]$item.ProcessId;if($id -in $ExcludeIds){continue};$owned=$false
  try{if(-not [CodexDreamSkin.NativeInspection]::CurrentUser($id)){continue};$path=[CodexDreamSkin.NativeInspection]::ProcessPath($id)
   foreach($root in @($Roots)+@((Join-Path $StateRoot 'startup'),(Join-Path $StateRoot 'runtime'))){$prefix=[IO.Path]::GetFullPath($root).TrimEnd('\')+'\';if($path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){$owned=$true;break};if($item.Name -ieq 'powershell.exe'){foreach($relative in @('control.ps1','native-actions.ps1','startup\monitor-skin.ps1','monitor-skin.ps1','register-port.ps1')){if(Test-DreamSkinCommandLineToken -CommandLine "$($item.CommandLine)" -Token (Join-Path $root $relative)){$owned=$true;break}}}}
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
 $registered=(Get-ItemProperty -LiteralPath 'HKCU:\Software\CodexLiveBackground' -Name InstallRoot -ErrorAction SilentlyContinue).InstallRoot
 $fallback=if($registered){$registered}else{Join-Path $env:LOCALAPPDATA 'Programs\CodexLiveBackground'}
 $products=@(Get-BackgroundLegacyMsi -CatalogPath (Join-Path $PackageRoot 'legacy-msi-products.json') -FallbackRoot $fallback)
 $roots=@($InstallRoot,$fallback,(Join-Path $env:LOCALAPPDATA 'Programs\CodexLiveBackground'),(Join-Path $env:LOCALAPPDATA 'Programs\CodexDreamSkinManager'))+@($products|ForEach-Object{$_.Root})
 $owned=@();foreach($root in @($roots|Select-Object -Unique)){if(-not(Test-Path -LiteralPath $root)){continue};if(@(Get-BackgroundOwnedFiles -Root $root).Count -or @($products|Where-Object{$_.Root -ieq $root}).Count){$owned+=@($root)}elseif((Test-DreamSkinPathEqual -Left $root -Right $InstallRoot) -and @(Get-ChildItem -LiteralPath $root -Force).Count){throw '所选文件夹不是已识别的软件目录，请选择新的专用文件夹。'}}
 $authorization=$products.Count -gt 0
 $parent=[IO.Path]::GetFullPath($InstallRoot);while(-not(Test-Path -LiteralPath $parent)){$parent=Split-Path -Parent $parent;if(-not $parent){throw '安装磁盘不可用。'}}
 $probe=Join-Path $parent ('.codex-write-'+[guid]::NewGuid().ToString('N')+'.tmp')
 try{$stream=[IO.FileStream]::new($probe,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::DeleteOnClose);$stream.Dispose()}catch [UnauthorizedAccessException]{$authorization=$true}
 [pscustomobject]@{root=[IO.Path]::GetFullPath($InstallRoot);roots=@($owned);products=@($products);requiresAuthorization=$authorization;baseVersion='1.5.4'}
}
function Stop-BackgroundIntegrations {
 param([string[]]$Roots,[string]$StateRoot,[string]$PackageRoot,[switch]$Uninstall,[int[]]$ExcludeIds)
 $task=Get-ScheduledTask -TaskName CodexDreamSkinPort -ErrorAction SilentlyContinue
 $legacy=Join-Path $StateRoot 'startup';$startup=if($task){Split-Path -Parent "$($task.Actions[0].Execute)"}elseif(Test-Path -LiteralPath $legacy){$legacy}else{$null}
 $valid=@($legacy)+@($Roots|ForEach-Object{Join-Path $_ 'startup'})
 if($startup -and -not @($valid|Where-Object{Test-DreamSkinPathEqual -Left $_ -Right $startup}).Count){throw '同名启动服务不在已识别路径内，未移除。'}
 $candidates=@();if($startup){$candidates+=@($startup)};$candidates+=@($valid|Where-Object{Test-Path -LiteralPath $_})
 $first=$true;foreach($serviceRoot in @($candidates|Select-Object -Unique)){$install=if(Test-DreamSkinPathEqual -Left $serviceRoot -Right $legacy){$PackageRoot}else{Split-Path -Parent $serviceRoot};Remove-DreamBackgroundService -InstallRoot $install -StateRoot $StateRoot -PackageRoot $PackageRoot -StartupRoot $serviceRoot -ForUpgrade:((-not $Uninstall) -or (-not $first)) -AllowDisconnected -GraceSeconds 2;$first=$false}
 Restore-DreamBackgroundShortcuts -StateRoot $StateRoot
 Stop-BackgroundOwnedProcesses -Roots $Roots -StateRoot $StateRoot -ExcludeIds $ExcludeIds
}
function Write-BackgroundNativeRegistration {
 param([string]$Root,[string]$Version)
 $key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLiveBackground'
 $null=New-Item -Path $key -Force;$values=@{DisplayName='CodexHub';DisplayVersion=$Version;Publisher='codex-live-background contributors';InstallLocation=$Root;UninstallString=('"'+(Join-Path $Root '卸载 CodexHub.exe')+'" --uninstall "'+$Root+'"');DisplayIcon=(Join-Path $Root 'CodexBackground.ico');NoModify=1;NoRepair=1;URLInfoAbout='https://github.com/chenyan-image/codex-live-background'}
 foreach($name in $values.Keys){$type=if($values[$name] -is [int]){'DWord'}else{'String'};$null=New-ItemProperty -LiteralPath $key -Name $name -Value $values[$name] -PropertyType $type -Force}
 $key='HKCU:\Software\CodexLiveBackground';$null=New-Item -Path $key -Force;$null=New-ItemProperty -LiteralPath $key -Name InstallRoot -Value $Root -PropertyType String -Force;$null=New-ItemProperty -LiteralPath $key -Name InstallKind -Value EXE -PropertyType String -Force
}
function Remove-BackgroundNativeRegistration {
 param([string]$Root)
 foreach($key in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLiveBackground','HKCU:\Software\CodexLiveBackground')){if(Test-Path -LiteralPath $key){$data=Get-ItemProperty -LiteralPath $key;$path=if($data.InstallLocation){$data.InstallLocation}else{$data.InstallRoot};if($path -and (Test-DreamSkinPathEqual -Left $path -Right $Root)){Remove-Item -LiteralPath $key -Recurse -Force -ErrorAction Stop}}}
}
function Invoke-BackgroundNativeAction {
 param([ValidateSet('Install','Uninstall')][string]$Action,[string]$InstallRoot,[string]$StateRoot,[string]$PackageRoot,[string]$SetupExecutable,[string]$OperationRoot,[int]$ManagerPid)
 $script:NativePackageRoot=$PackageRoot; $plan=Get-BackgroundPlan -InstallRoot $InstallRoot -PackageRoot $PackageRoot
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
 $null=New-Item -ItemType Directory -Path $InstallRoot -Force
 if([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
  $acl=Get-Acl -LiteralPath $InstallRoot;$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User;$rule=[Security.AccessControl.FileSystemAccessRule]::new($sid,'Modify','ContainerInherit,ObjectInherit','None','Allow');$acl.AddAccessRule($rule);Set-Acl -LiteralPath $InstallRoot -AclObject $acl
 }
 foreach($entry in $manifest.files){$source=Get-BackgroundSafePath -Root $PackageRoot -Relative $entry.path;if((Get-FileHash -LiteralPath $source).Hash -cne $entry.sha256){throw '安装源文件校验失败。'};$target=Get-BackgroundSafePath -Root $InstallRoot -Relative $entry.path;Assert-DreamBackgroundTree -Path $target -Parent $InstallRoot;$null=New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force;Copy-Item -LiteralPath $source -Destination $target -Force -ErrorAction Stop}
 Copy-Item -LiteralPath (Join-Path $PackageRoot 'manifest.json') -Destination (Join-Path $InstallRoot 'manifest.json') -Force
 Copy-Item -LiteralPath $SetupExecutable -Destination (Join-Path $InstallRoot '卸载 CodexHub.exe') -Force -ErrorAction Stop
 [IO.File]::WriteAllText((Join-Path $InstallRoot '.codex-background-install.json'),(@{productId=$script:NativeBackgroundId;version=$manifest.version;baseVersion='1.5.4';root=[IO.Path]::GetFullPath($InstallRoot);files=$files;pending=$true}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
 Write-BackgroundNativeRegistration -Root $InstallRoot -Version $manifest.version
 [Console]::Out.WriteLine('正在启用并验证背景服务，请稍候…');Invoke-BackgroundNativeActivate -Root $InstallRoot -ManagerPid $ManagerPid
 $marker=Join-Path $InstallRoot '.codex-background-install.json';$record=[IO.File]::ReadAllText($marker)|ConvertFrom-Json;$record.pending=$false;[IO.File]::WriteAllText($marker,($record|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
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
