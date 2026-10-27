param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\service-removal.ps1')
. (Join-Path $PSScriptRoot '..\source\native-lifecycle.ps1')
. (Join-Path $PSScriptRoot '..\source\install-scan.ps1')
function Test-DreamSkinPathEqual {param($Left,$Right);[IO.Path]::GetFullPath($Left).TrimEnd('\') -ieq [IO.Path]::GetFullPath($Right).TrimEnd('\')}
function WriteText($File,$Text){$null=New-Item -ItemType Directory -Path (Split-Path -Parent $File) -Force;[IO.File]::WriteAllText($File,$Text,[Text.UTF8Encoding]::new($false))}
$base=[IO.Path]::GetFullPath($OutputDirectory);$package=Join-Path $base 'package';$program=Join-Path $base 'running-install';$operation=Join-Path $base 'cleanup';$checks=0
function Need($Condition,$Message){if(-not $Condition){throw $Message};$script:checks++}
$null=New-Item -ItemType Directory -Path $package,$program,$operation
WriteText (Join-Path $package 'CodexLiveBackground.exe') 'known program'
WriteText (Join-Path $package 'control.ps1') '# static program'
$files=@('CodexLiveBackground.exe','control.ps1')
$manifest=@{version='1.7.0.17';files=@($files|ForEach-Object{[ordered]@{path=$_;bytes=(Get-Item -LiteralPath (Join-Path $package $_)).Length;sha256=(Get-FileHash -LiteralPath (Join-Path $package $_)).Hash}})}
WriteText (Join-Path $package 'manifest.json') ($manifest|ConvertTo-Json -Depth 5)
foreach($relative in $files){Copy-Item -LiteralPath (Join-Path $package $relative) -Destination (Join-Path $program $relative)}
$marker=Join-Path $program '.codex-background-install.json'
WriteText $marker (@{productId='codex-live-background/exe';root=$program;files=$files+@('manifest.json','.codex-background-install.json')+$script:BackgroundRuntimeFiles}|ConvertTo-Json -Depth 5)
Copy-Item -LiteralPath (Join-Path $package 'manifest.json') -Destination $program
WriteText (Join-Path $program 'startup\guard-state.json') '{"pid":123,"heartbeat":1}'
WriteText (Join-Path $program 'startup\monitor-state.json') '{"time":"before"}'
WriteText (Join-Path $program 'startup\startup.log') 'before'
WriteText (Join-Path $program 'installed.json') '{}'
WriteText (Join-Path $program 'personal.txt') 'preserve personal file'
$script:NativePackageRoot=$package
$before=Get-BackgroundScanItem -Root $program -PackageRoot $package
Need $before.verified 'Fixture ownership failed'
WriteText (Join-Path $program 'startup\guard-state.json') '{"pid":123,"heartbeat":999999}'
WriteText (Join-Path $program 'startup\monitor-state.json') '{"time":"after","phase":"running"}'
[IO.File]::AppendAllText((Join-Path $program 'startup\startup.log'),"`nnormal background log")
WriteText (Join-Path $program 'startup\skin-startup.log') 'created after scan'
Remove-Item -LiteralPath (Join-Path $program 'installed.json')
$after=Get-BackgroundScanItem -Root $program -PackageRoot $package
Need ($after.verified -and $after.identity -eq $before.identity) 'Normal heartbeat/log writes still invalidate the scan'
Need ($after.files -contains 'startup\skin-startup.log') 'New runtime file was not retained in the owned cleanup list'
$static=Join-Path $program 'control.ps1';$originalStatic=[IO.File]::ReadAllText($static);WriteText $static '# actual program modification'
$changed=Get-BackgroundScanItem -Root $program -PackageRoot $package
Need ($changed.identity -ne $after.identity) 'A modified static program was not detected'
WriteText $static $originalStatic
$originalMarker=[IO.File]::ReadAllText($marker);WriteText $marker ($originalMarker+"`n")
$changed=Get-BackgroundScanItem -Root $program -PackageRoot $package
Need ($changed.identity -ne $after.identity) 'A changed installation marker was ignored'
WriteText $marker $originalMarker
$snapshot=Get-BackgroundScanItem -Root $program -PackageRoot $package
$reportFile=Join-Path $base 'scan.json'
WriteText $reportFile (@{schema=2;productId='codexhub-install-scan';owner=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;createdUtc=[DateTime]::UtcNow.ToString('o');items=@($snapshot)}|ConvertTo-Json -Depth 7)
$script:stops=0;$script:registrations=@()
function Stop-BackgroundIntegrations {
 $script:stops++
 WriteText (Join-Path $program 'startup\stop-guard') 'created while stopping'
 [IO.File]::AppendAllText((Join-Path $program 'startup\startup.log'),"`nexiting")
}
function Remove-BackgroundDesktopEntries {}
function Remove-BackgroundNativeRegistration {param($Root);$script:registrations+=@($Root)}
$originalReport=[IO.File]::ReadAllText($reportFile);$oldReport=$originalReport|ConvertFrom-Json;$oldReport.schema=1;WriteText $reportFile ($oldReport|ConvertTo-Json -Depth 7)
$blocked=$false;try{Invoke-BackgroundResidualCleanup -ScanReport $reportFile -PackageRoot $package -OperationRoot $operation -ManagerPid $PID}catch{$blocked=$_.Exception.Message -match '旧版'}
Need ($blocked -and $script:stops -eq 0) 'Legacy scan format did not request a fresh scan before stopping anything'
WriteText $reportFile $originalReport
Invoke-BackgroundResidualCleanup -ScanReport $reportFile -PackageRoot $package -OperationRoot $operation -ManagerPid $PID
Need ($script:stops -eq 1) 'Cleanup did not stop the owned background components'
Need (-not [IO.File]::Exists((Join-Path $program 'CodexLiveBackground.exe'))) 'Verified old program was not removed'
Need (-not [IO.File]::Exists((Join-Path $program 'startup\stop-guard'))) 'Runtime file created during shutdown survived cleanup'
Need ([IO.File]::ReadAllText((Join-Path $program 'personal.txt')) -eq 'preserve personal file') 'Cleanup deleted unrelated data'
Need ($script:registrations.Count -eq 1) 'Cleanup did not retire the matching registration'
# Runtime files may change contents, but may never turn into directories.
foreach($relative in $files){Copy-Item -LiteralPath (Join-Path $package $relative) -Destination (Join-Path $program $relative)}
WriteText $marker $originalMarker
$runtimeDirectory=Join-Path $program 'startup\monitor-state.json';$null=New-Item -ItemType Directory -Path $runtimeDirectory -Force
$unsafe=Get-BackgroundScanItem -Root $program -PackageRoot $package
Need (-not $unsafe.verified) 'Transient-file exclusion bypassed the file-type check'
Remove-Item -LiteralPath $runtimeDirectory
# All static files must be revalidated after shutdown and before any deletion.
$snapshot=Get-BackgroundScanItem -Root $program -PackageRoot $package
WriteText $reportFile (@{schema=2;productId='codexhub-install-scan';owner=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;createdUtc=[DateTime]::UtcNow.ToString('o');items=@($snapshot)}|ConvertTo-Json -Depth 7)
function Stop-BackgroundIntegrations {WriteText (Join-Path $program 'control.ps1') '# replaced during shutdown'}
$blocked=$false;try{Invoke-BackgroundResidualCleanup -ScanReport $reportFile -PackageRoot $package -OperationRoot $operation -ManagerPid $PID}catch{$blocked=$_.Exception.Message -match '停止后台组件后'}
Need $blocked 'A program replacement during shutdown was not detected'
Need ([IO.File]::Exists((Join-Path $program 'CodexLiveBackground.exe'))) 'Cleanup deleted program files before post-shutdown validation finished'
@{pass=$true;checks=$checks;liveStateAllowed=$true;staticChangesDetected=$true;shutdownStateRemoved=$true;realInstallChanged=$false}|ConvertTo-Json -Compress
