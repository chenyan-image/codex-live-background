param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$base=[IO.Path]::GetFullPath($OutputDirectory);$null=New-Item -ItemType Directory -Path $base
. (Join-Path $PSScriptRoot '..\source\service-removal.ps1')
. (Join-Path $PSScriptRoot '..\source\upgrade.ps1')
. (Join-Path $PSScriptRoot '..\source\native-lifecycle.ps1')
. (Join-Path $PSScriptRoot '..\source\install-scan.ps1')
function Test-DreamSkinPathEqual {param($Left,$Right);[IO.Path]::GetFullPath($Left).TrimEnd('\') -ieq [IO.Path]::GetFullPath($Right).TrimEnd('\')}
$checks=0;function Need($Value,$Message){if(-not $Value){throw $Message};$script:checks++}
function WriteText($Path,$Text){$null=New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force;[IO.File]::WriteAllText($Path,$Text,[Text.UTF8Encoding]::new($false))}
$previousLocal=$env:LOCALAPPDATA
$previousTemp=$env:TEMP;$previousTmp=$env:TMP
try{
 $env:LOCALAPPDATA=Join-Path $base 'local';$null=New-Item -ItemType Directory -Path $env:LOCALAPPDATA
 $env:TEMP=Join-Path $base 'temporary';$env:TMP=$env:TEMP;$null=New-Item -ItemType Directory -Path $env:TEMP
 $package=Join-Path $base 'package';$scanRoot=Join-Path $base 'disk';$old=Join-Path $scanRoot '中文 & 旧安装 [x]';$unknown=Join-Path $scanRoot 'unknown';$repository=Join-Path $scanRoot 'repository';$operation=Join-Path $base 'scan';$destination=Join-Path $base 'fresh-install'
 foreach($path in @($package,$old,$unknown,$repository,$operation)){$null=New-Item -ItemType Directory -Path $path -Force}
 WriteText (Join-Path $package 'CodexLiveBackground.exe') 'known-manager'
 $manifest=@{version='1.7.0.16';files=@(@{path='CodexLiveBackground.exe';bytes=13;sha256=(Get-FileHash -LiteralPath (Join-Path $package 'CodexLiveBackground.exe')).Hash})}
 WriteText (Join-Path $package 'manifest.json') ($manifest|ConvertTo-Json -Depth 6)
 $script:NativePackageRoot=$package
 Copy-Item -LiteralPath (Join-Path $package 'CodexLiveBackground.exe') -Destination $old
 WriteText (Join-Path $old '.codex-background-install.json') (@{productId='codex-live-background/exe';root=$old;files=@('CodexLiveBackground.exe','..\personal.txt')}|ConvertTo-Json -Depth 5)
 WriteText (Join-Path $old 'personal.txt') 'keep user data'
 WriteText (Join-Path $scanRoot 'personal.txt') 'keep outside'
 WriteText (Join-Path $unknown 'CodexLiveBackground.exe') 'unknown-manager'
 WriteText (Join-Path $unknown '.codex-background-install.json') '{broken'
 $spoof=Join-Path $scanRoot 'marker-only';WriteText (Join-Path $spoof '.codex-background-install.json') (@{productId='codex-live-background/exe';root=$spoof;files=@('manifest.json','CodexLiveBackground.exe')}|ConvertTo-Json -Depth 5)
 WriteText (Join-Path $spoof 'CodexLiveBackground.exe') 'unknown executable';WriteText (Join-Path $spoof 'manifest.json') '{}'
 $cache=Join-Path $env:TEMP 'CodexBackgroundSetup-00000000000000000000000000000000';$null=New-Item -ItemType Directory -Path $cache
 Copy-Item -LiteralPath (Join-Path $package 'CodexLiveBackground.exe') -Destination $cache;WriteText (Join-Path $cache 'keep-me.txt') 'keep unrelated cache file';(Get-Item -LiteralPath $cache).LastWriteTimeUtc=[DateTime]::UtcNow.AddDays(-2)
 $hidden=Join-Path $scanRoot '未登记安装\任意名称';$null=New-Item -ItemType Directory -Path $hidden
 Copy-Item -LiteralPath (Join-Path $package 'CodexLiveBackground.exe') -Destination $hidden
 $null=New-Item -ItemType Directory -Path (Join-Path $repository '.git')
 Copy-Item -LiteralPath (Join-Path $package 'CodexLiveBackground.exe') -Destination $repository
 $state=Join-Path $env:LOCALAPPDATA 'CodexDreamSkin';WriteText (Join-Path $state 'pet-settings.json') 'settings';WriteText (Join-Path $state 'pets\custom.png') 'material'
 function Get-DreamSkinRegisteredCodexInstalls {return @()}
 function Get-DreamBackgroundRegisteredInstallRoots {return @($old,$unknown)}
 function Get-BackgroundLegacyMsi {return @()}
 function Get-ScheduledTask {return $null}
 function Get-BackgroundOwnedProcesses {return @()}
 $script:stopped=0;$script:removed=@()
 function Stop-BackgroundIntegrations {$script:stopped++}
 function Remove-BackgroundDesktopEntries {}
 function Remove-BackgroundNativeRegistration {param($Root);$script:removed+=@($Root)}
 $files=@(Get-BackgroundOwnedFiles -Root $old)
 Need ($files -contains 'CodexLiveBackground.exe') 'Known manager did not recover a damaged marker'
 Need ($files -notcontains '..\personal.txt') 'Damaged marker traversal was trusted'
 $plan=Get-BackgroundPlan -InstallRoot $destination -PackageRoot $package
 Need (@($plan.roots).Count -eq 1) 'Unrelated damaged install still blocked a fresh target'
 Invoke-BackgroundDeepScan -InstallRoot $destination -PackageRoot $package -OperationRoot $operation -ScanRoots @($scanRoot) -Seconds 20
 $reportFile=Join-Path $operation 'scan.json';$report=[IO.File]::ReadAllText($reportFile)|ConvertFrom-Json
 Need (-not $report.codexInstalled) 'No-Codex machine was misreported'
 Need (@($report.issues|Where-Object level -eq error).Count -eq 0) 'No-Codex scan blocked installation'
 Need (@($report.items|Where-Object {$_.path -eq $old -and $_.verified}).Count -eq 1) 'Scan missed the unregistered Unicode folder'
 Need (@($report.items|Where-Object {$_.path -eq $unknown -and -not $_.verified}).Count -eq 1) 'Unknown executable was granted cleanup ownership'
 Need (@($report.items|Where-Object {$_.path -eq $hidden -and $_.verified}).Count -eq 1) 'Disk walk missed an unregistered installation'
 Need (@($report.items|Where-Object {$_.path -eq $spoof -and -not $_.verified}).Count -eq 1) 'A self-authored marker granted cleanup authority without a known hash'
 Need (@($report.items|Where-Object {$_.path -eq $cache -and $_.kind -eq 'temporary' -and $_.verified}).Count -eq 1) 'Verified stale extraction cache was missed'
 Need (@($report.items|Where-Object path -eq $repository).Count -eq 0) 'Scanner traversed a source repository'
 Need ($script:stopped -eq 0) 'Read-only scan stopped services'
 Need ([IO.File]::Exists((Join-Path $old 'CodexLiveBackground.exe'))) 'Read-only scan deleted the old program'
 Need ([IO.File]::Exists((Join-Path $operation '安装诊断.txt'))) 'Human-readable diagnostic was not saved'
 $cleanup=Join-Path $base 'cleanup';$null=New-Item -ItemType Directory -Path $cleanup
 $originalMarker=[IO.File]::ReadAllText((Join-Path $old '.codex-background-install.json'));WriteText (Join-Path $old '.codex-background-install.json') 'changed'
 $blocked=$false;try{Invoke-BackgroundResidualCleanup -ScanReport $reportFile -PackageRoot $package -OperationRoot $cleanup -ManagerPid $PID}catch{$blocked=$_.Exception.Message -match '扫描后文件已变化'}
 Need $blocked 'Cleanup trusted a stale scan after files changed'
 Need ($script:stopped -eq 0) 'Cleanup stopped processes before validating all scan items'
 WriteText (Join-Path $old '.codex-background-install.json') $originalMarker
 # A fresh scan is required after any timestamp/content change.
 Invoke-BackgroundDeepScan -InstallRoot $destination -PackageRoot $package -OperationRoot $operation -ScanRoots @($scanRoot) -Seconds 20
 Invoke-BackgroundResidualCleanup -ScanReport $reportFile -PackageRoot $package -OperationRoot $cleanup -ManagerPid $PID
 Need (-not [IO.File]::Exists((Join-Path $old 'CodexLiveBackground.exe'))) 'Verified program survived cleanup'
 Need ([IO.File]::ReadAllText((Join-Path $old 'personal.txt')) -eq 'keep user data') 'Cleanup removed a user file inside the old directory'
 Need ([IO.File]::ReadAllText((Join-Path $scanRoot 'personal.txt')) -eq 'keep outside') 'Cleanup escaped the old directory'
 Need ([IO.File]::Exists((Join-Path $unknown 'CodexLiveBackground.exe'))) 'Cleanup touched an unverified executable'
 Need ([IO.File]::Exists((Join-Path $spoof 'CodexLiveBackground.exe'))) 'Cleanup trusted a spoofed product marker'
 Need (-not [IO.File]::Exists((Join-Path $cache 'CodexLiveBackground.exe'))) 'Verified stale cache program survived cleanup'
 Need ([IO.File]::ReadAllText((Join-Path $cache 'keep-me.txt')) -eq 'keep unrelated cache file') 'Cache cleanup deleted an unrelated file'
 Need ([IO.File]::ReadAllText((Join-Path $state 'pet-settings.json')) -eq 'settings') 'Cleanup changed user settings'
 Need ([IO.File]::ReadAllText((Join-Path $state 'pets\custom.png')) -eq 'material') 'Cleanup removed imported pet material'
 Need ($script:removed.Count -eq 2 -and $script:removed -contains $old -and $script:removed -contains $hidden) 'Cleanup removed the wrong installation registration'
 # Extract only activation functions; no action entrypoint, COM or desktop writes.
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot '..\source\native-actions.ps1'),[ref]$tokens,[ref]$errors)
 $activate=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-BackgroundNativeActivate'},$true)
 . ([scriptblock]::Create($activate.Extent.Text))
 $script:shortcut=0;function Write-BackgroundManagerShortcut {$script:shortcut++}
 Invoke-BackgroundNativeActivate -Root $destination -ManagerPid $PID
 Need ($script:shortcut -eq 1) 'No-Codex activation failed to create a manager entry'
 function Get-DreamSkinRegisteredCodexInstalls {throw 'Appx API unavailable'}
 Invoke-BackgroundNativeActivate -Root $destination -ManagerPid $PID
 Need ($script:shortcut -eq 2) 'An unavailable Codex detection API aborted program installation'
 $taskRoot=Join-Path $destination 'startup';$task=[pscustomobject]@{Actions=@([pscustomobject]@{Execute=(Join-Path $taskRoot 'DreamSkinPortHook.exe');Arguments='--guard'});Principal=[pscustomobject]@{UserId=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;LogonType='Interactive'}}
 $resolved=Resolve-DreamBackgroundUpgradeStartup -Task $task -Installs @([pscustomobject]@{Root=$destination}) -StateRoot $state -PackageRoot $package
 Need ($resolved -eq $taskRoot) 'An owned dangling startup task still blocks installation'
 $task.Actions[0].Arguments='--something-else';$blocked=$false;try{$null=Resolve-DreamBackgroundUpgradeStartup -Task $task -Installs @([pscustomobject]@{Root=$destination}) -StateRoot $state -PackageRoot $package}catch{$blocked=$true}
 Need $blocked 'An unrelated dangling task was accepted for removal'
 # Bound the scanner and mark incomplete scans explicitly.
 WriteText (Join-Path $operation 'cancel-scan') ''
 Invoke-BackgroundDeepScan -InstallRoot $destination -PackageRoot $package -OperationRoot $operation -ScanRoots @($scanRoot) -Seconds 20
 $partial=[IO.File]::ReadAllText($reportFile)|ConvertFrom-Json
 Need $partial.partial 'Canceled scan was reported as a complete disk scan'
 @{pass=$true;checks=$checks;noCodexSupported=$true;damagedMarkerRecovered=$true;personalDataPreserved=$true;realInstallChanged=$false}|ConvertTo-Json -Compress
}finally{$env:LOCALAPPDATA=$previousLocal;$env:TEMP=$previousTemp;$env:TMP=$previousTmp}
