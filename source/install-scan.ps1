# Installer diagnostics are read-only. Cleanup revalidates every owned file;
# neither a folder name nor the scan report grants permission to delete a tree.
function Get-BackgroundScanItem {
 param([string]$Root,[string]$PackageRoot,[string]$Kind='program')
 $path=[IO.Path]::GetFullPath($Root)
 $present=@('CodexLiveBackground.exe','CodexDreamSkinManager.exe','.codex-background-install.json')|Where-Object{[IO.File]::Exists((Join-Path $path $_))}
 if(-not $present.Count){return}
 try{
  Assert-BackgroundInstallPath $path
  if([IO.Directory]::Exists((Join-Path $path '.git')) -or [IO.File]::Exists((Join-Path $path '.git'))){throw '这是源代码仓库，作为报告项保留，不清理。'}
  $files=@(Get-BackgroundOwnedFiles -Root $path)
  if(-not $files.Count){throw '没有匹配到本项目已核验的程序文件清单。'}
  $known=@(Get-BackgroundKnownManagerFiles -Root $path)
  if(-not $known.Count){
   # Disk discovery must not elevate a self-authored product marker into
   # deletion authority. A known executable or pinned manifest is required.
   $manifest=Join-Path $path 'manifest.json';$trustedManifest=Join-Path $PackageRoot 'manifest.json';$valid=$false
   if(Test-Path -LiteralPath $manifest -PathType Leaf){
    $digest=(Get-FileHash -LiteralPath $manifest).Hash
    if((Test-Path -LiteralPath $trustedManifest) -and $digest -ieq (Get-FileHash -LiteralPath $trustedManifest).Hash){$valid=$true}
    $history=Join-Path $PSScriptRoot 'historical-install-files.json'
    if(Test-Path -LiteralPath $history){$valid=$valid -or @((([IO.File]::ReadAllText($history)|ConvertFrom-Json).releases)|Where-Object{$_.manifestSha256 -ieq $digest}).Count -gt 0}
   }
   if(-not $valid){throw '仅发现安装标记，程序或清单的 SHA-256 无法核验；作为报告项保留。'}
  }
  $bytes=0L;$identity=@();$existing=@()
  foreach($relative in @($files|Sort-Object -Unique)){
   $file=Get-BackgroundSafePath -Root $path -Relative $relative
   if(-not(Test-Path -LiteralPath $file)){continue}
   Assert-DreamBackgroundTree -Path $file -Parent $path
   $item=Get-Item -LiteralPath $file -Force
   if($item.PSIsContainer){throw ('清单中的程序文件变成了文件夹：'+$relative)}
   $bytes+=$item.Length;$existing+=@($relative)
   # Detect changes between the scan and cleanup, including a replaced marker.
   $identity+=@($relative+':'+$item.Length+':'+$item.LastWriteTimeUtc.Ticks)
   if($relative -in @('CodexLiveBackground.exe','CodexDreamSkinManager.exe','.codex-background-install.json')){$identity+=@((Get-FileHash -LiteralPath $file).Hash)}
  }
  $sha=[Security.Cryptography.SHA256]::Create();try{$stamp=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($identity -join "`n")))).Replace('-','')}finally{$sha.Dispose()}
  [pscustomobject]@{path=$path;kind=$Kind;verified=$true;bytes=$bytes;files=@($existing);identity=$stamp;detail='归属已核验；清理仅移除清单中的程序文件。'}
 }catch{[pscustomobject]@{path=$path;kind=$Kind;verified=$false;bytes=0;files=@();identity='';detail=$_.Exception.Message}}
}
function Get-BackgroundScanDrives {
 foreach($drive in [IO.DriveInfo]::GetDrives()){try{if($drive.DriveType -eq 'Fixed' -and $drive.IsReady){$drive.RootDirectory.FullName}}catch{}}
}
function Invoke-BackgroundDeepScan {
 param([string]$InstallRoot,[string]$PackageRoot,[string]$OperationRoot,[string[]]$ScanRoots,[ValidateRange(1,600)][int]$Seconds=180,[ValidateRange(1,1000000)][int]$DirectoryLimit=250000)
 $script:NativePackageRoot=$PackageRoot
 $issues=[Collections.Generic.List[object]]::new();$items=[Collections.Generic.List[object]]::new();$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 $codex=@();try{$codex=@(Get-DreamSkinRegisteredCodexInstalls)}catch{$issues.Add([pscustomobject]@{level='warning';check='Codex';message=$_.Exception.Message})}
 [Console]::Out.WriteLine('Codex 检测：'+$(if($codex.Count){'已安装'}else{'未安装；这不是安装 CodexHub 的阻止条件。'}))
 $plan=$null;try{$plan=Get-BackgroundPlan -InstallRoot $InstallRoot -PackageRoot $PackageRoot;Assert-BackgroundDestination -Root $InstallRoot -PackageRoot $PackageRoot;[Console]::Out.WriteLine('安装预检：目标目录、旧记录和可用空间检查通过。');foreach($product in $plan.products){[Console]::Out.WriteLine('发现旧 MSI 安装记录：'+$product.Version+'；'+$product.Root)}}catch{$issues.Add([pscustomobject]@{level='error';check='安装预检';message=$_.Exception.Message});[Console]::Out.WriteLine('发现安装阻止原因：'+$_.Exception.Message)}
 $registered=@();try{$registered=@(Get-DreamBackgroundRegisteredInstallRoots)}catch{$issues.Add([pscustomobject]@{level='warning';check='安装记录';message=$_.Exception.Message})}
 $state=Join-Path $env:LOCALAPPDATA 'CodexDreamSkin'
 $seeds=@($InstallRoot)+$registered+@((Join-Path $env:LOCALAPPDATA 'Programs\CodexLiveBackground'),(Join-Path $env:LOCALAPPDATA 'Programs\CodexDreamSkinManager'))
 foreach($root in $seeds){try{
  $path=[IO.Path]::GetFullPath($root);if(-not $seen.Add($path.TrimEnd('\'))){continue}
  $item=Get-BackgroundScanItem -Root $path -PackageRoot $PackageRoot;if($item){$items.Add($item)}
 }catch{$issues.Add([pscustomobject]@{level='warning';check='已知安装位置';message=$_.Exception.Message})}}
 $skip=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 foreach($name in @('Windows','WindowsApps','$Recycle.Bin','System Volume Information','.git','.svn','node_modules','.codex','WinSxS','Installer','CodexDreamSkin')){$null=$skip.Add($name)}
 $pending=[Collections.Generic.Queue[string]]::new();$drives=if($ScanRoots){@($ScanRoots)}else{@(Get-BackgroundScanDrives)}
 foreach($root in $drives){$pending.Enqueue([IO.Path]::GetFullPath($root))}
 $visited=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$watch=[Diagnostics.Stopwatch]::StartNew();$count=0;$denied=0;$partial=$false
 [Console]::Out.WriteLine('正在扫描本地磁盘中的旧版安装；只检查本项目标记，不读取个人文件内容…')
 while($pending.Count){
  if($watch.Elapsed.TotalSeconds -ge $Seconds -or $count -ge $DirectoryLimit -or [IO.File]::Exists((Join-Path $OperationRoot 'cancel-scan'))){$partial=$true;break}
  $path=$pending.Dequeue();if(-not $visited.Add($path.TrimEnd('\'))){continue};$count++
  try{
   if(([IO.File]::GetAttributes($path) -band [IO.FileAttributes]::ReparsePoint) -or (Test-DreamSkinPathEqual -Left $path -Right $PackageRoot)){continue}
   # Development repositories are not installed applications.
   if([IO.Directory]::Exists((Join-Path $path '.git')) -or [IO.File]::Exists((Join-Path $path '.git'))){continue}
   if($seen.Add($path.TrimEnd('\'))){$item=Get-BackgroundScanItem -Root $path -PackageRoot $PackageRoot;if($item){$items.Add($item);[Console]::Out.WriteLine('发现'+$(if($item.verified){'已核验程序：'}else{'待核验残留：'})+$path);continue}}
   foreach($child in [IO.Directory]::EnumerateDirectories($path)){
    $name=[IO.Path]::GetFileName($child)
    if($skip.Contains($name) -or $name -match '^CodexBackground(?:Setup|Operation|Uninstall)-[a-f0-9]{32}$'){continue}
    $pending.Enqueue($child)
   }
  }catch{$denied++}
  if($count%1500 -eq 0){[Console]::Out.WriteLine('已检查 '+$count+' 个目录；发现 '+$items.Count+' 处程序或残留…')}
 }
 # Only stale, identity-verified extraction caches are offered for cleanup.
 foreach($cache in @(Get-ChildItem -LiteralPath ([IO.Path]::GetTempPath()) -Directory -Force -ErrorAction SilentlyContinue|Where-Object{$_.Name -cmatch '^CodexBackgroundSetup-[a-f0-9]{32}$' -and $_.LastWriteTimeUtc -lt [DateTime]::UtcNow.AddDays(-1)})){
  if(Test-DreamSkinPathEqual -Left $cache.FullName -Right $PackageRoot){continue}
  $item=Get-BackgroundScanItem -Root $cache.FullName -PackageRoot $PackageRoot -Kind temporary;if($item){$items.Add($item)}
 }
 if($partial){$issues.Add([pscustomobject]@{level='warning';check='扫描范围';message='达到扫描时间或目录数量限制，报告为部分扫描。'})}
 if($denied){$issues.Add([pscustomobject]@{level='warning';check='扫描权限';message=('有 '+$denied+' 个目录不可读或在扫描时变化，已跳过。')})}
 $processes=@();try{$processes=@(Get-BackgroundOwnedProcesses -Roots @($items|Where-Object{$_.verified -and $_.kind -eq 'program'}|ForEach-Object{$_.path}) -StateRoot $state -ExcludeIds @($PID))|ForEach-Object{[pscustomobject]@{pid=$_.ProcessId;path=$_.Executable}}}catch{$issues.Add([pscustomobject]@{level='warning';check='后台进程';message=$_.Exception.Message})}
 foreach($process in $processes){[Console]::Out.WriteLine('发现本项目后台进程：PID '+$process.pid+'；'+$process.path)}
 $report=[ordered]@{schema=1;productId='codexhub-install-scan';owner=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;createdUtc=[DateTime]::UtcNow.ToString('o');target=$InstallRoot;codexInstalled=($codex.Count -gt 0);environment=@{windows=[Environment]::OSVersion.VersionString;is64Bit=[Environment]::Is64BitOperatingSystem;powershell=$PSVersionTable.PSVersion.ToString();temporaryDirectory=[IO.Path]::GetTempPath()};directories=$count;partial=$partial;skipped=$denied;items=@($items.ToArray());issues=@($issues.ToArray());processes=@($processes)}
 $reportPath=Join-Path $OperationRoot 'scan.json';[IO.File]::WriteAllText($reportPath,($report|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
 $lines=@('CodexHub 安装诊断',('时间：'+$report.createdUtc),('目标：'+$InstallRoot),('未安装 Codex 是否阻止安装：否'),('扫描目录：'+$count+'；部分扫描：'+$partial),'')
 foreach($issue in $issues){$lines+=@(('['+$issue.level+'] '+$issue.check+'：'+$issue.message))}
 foreach($item in $items){$lines+=@(('['+$(if($item.verified){'可清理'}else{'仅报告'})+'] '+$item.path+' — '+[math]::Round($item.bytes/1MB,2)+' MiB — '+$item.detail))}
 [IO.File]::WriteAllLines((Join-Path $OperationRoot '安装诊断.txt'),[string[]]$lines,[Text.UTF8Encoding]::new($true))
 [Console]::Out.WriteLine('扫描完成：'+$items.Count+' 处程序或残留；'+@($issues|Where-Object level -eq error).Count+' 项安装阻止原因。')
 [Console]::Out.WriteLine('诊断报告：'+(Join-Path $OperationRoot '安装诊断.txt'))
}
function Invoke-BackgroundResidualCleanup {
 param([string]$ScanReport,[string]$PackageRoot,[string]$OperationRoot,[int]$ManagerPid)
 if(-not [IO.File]::Exists($ScanReport) -or (Get-Item -LiteralPath $ScanReport).Length -gt 8MB){throw '请先完成深度扫描，再清理已确认的残留。'}
 $report=[IO.File]::ReadAllText($ScanReport)|ConvertFrom-Json
 if($report.schema -ne 1 -or $report.productId -cne 'codexhub-install-scan' -or $report.owner -cne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value){throw '扫描报告归属不匹配。'}
 if([DateTime]::Parse($report.createdUtc).ToUniversalTime() -lt [DateTime]::UtcNow.AddHours(-24)){throw '扫描报告已过期，请重新扫描。'}
 $script:NativePackageRoot=$PackageRoot;$verified=@()
 foreach($saved in @($report.items|Where-Object verified)){
  if(Test-DreamSkinPathEqual -Left $saved.path -Right $PackageRoot){continue}
  $current=Get-BackgroundScanItem -Root $saved.path -PackageRoot $PackageRoot -Kind $saved.kind
  if(-not $current -or -not $current.verified -or $current.identity -cne $saved.identity){throw ('扫描后文件已变化，已停止；请重新扫描：'+$saved.path)}
  if($current.kind -eq 'temporary'){
   if([IO.Path]::GetDirectoryName($current.path.TrimEnd('\')) -ine [IO.Path]::GetTempPath().TrimEnd('\') -or [IO.Path]::GetFileName($current.path) -cnotmatch '^CodexBackgroundSetup-[a-f0-9]{32}$' -or (Get-Item -LiteralPath $current.path).LastWriteTimeUtc -ge [DateTime]::UtcNow.AddDays(-1)){throw '缓存位置或时间不匹配，已保留。'}
  }elseif($current.kind -ne 'program'){throw '扫描项目类型无效。'}
  $verified+=@($current)
 }
 if(-not $verified.Count){[Console]::Out.WriteLine('没有可安全清理的已核验残留；无需清理。');return}
 $roots=@($verified|Where-Object kind -eq program|ForEach-Object{$_.path});$state=Join-Path $env:LOCALAPPDATA 'CodexDreamSkin'
 if($roots.Count){Stop-BackgroundIntegrations -Roots $roots -StateRoot $state -PackageRoot $PackageRoot -ExcludeIds @($PID,$ManagerPid);Remove-BackgroundDesktopEntries -Roots $roots}
 foreach($item in $verified){
  # Keep a small diagnostic backup of the old marker; personal material and
  # settings in CodexDreamSkin are never passed to a removal routine here.
  $marker=Join-Path $item.path '.codex-background-install.json';if(Test-Path -LiteralPath $marker){Copy-Item -LiteralPath $marker -Destination (Join-Path $OperationRoot ('marker-'+[guid]::NewGuid().ToString('N')+'.json'))}
  [Console]::Out.WriteLine('正在清理已核验'+$(if($item.kind -eq 'temporary'){'解压缓存：'}else{'程序残留：'})+$item.path)
  Remove-BackgroundOwnedFiles -Root $item.path -Files $item.files
  if($item.kind -eq 'program'){Remove-BackgroundNativeRegistration -Root $item.path}
 }
 [Console]::Out.WriteLine('残留清理完成。壁纸、宠物素材、设置和 Codex 登录资料均保留；可继续安装。')
}
