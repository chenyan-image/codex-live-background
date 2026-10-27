param([Parameter(Mandatory=$true)][string]$LegacyPayloadRoot,[Parameter(Mandatory=$true)][string]$PackageRoot,[Parameter(Mandatory=$true)][string]$OutputDirectory,[string]$LifecycleDirectory)
$ErrorActionPreference='Stop'
if(-not $LifecycleDirectory){$LifecycleDirectory=Join-Path $PSScriptRoot '..\source'}
. (Join-Path $LifecycleDirectory 'service-removal.ps1')
. (Join-Path $LifecycleDirectory 'native-lifecycle.ps1')
. (Join-Path $LifecycleDirectory 'install-scan.ps1')
function Test-DreamSkinPathEqual {param($Left,$Right);[IO.Path]::GetFullPath($Left).TrimEnd('\') -ieq [IO.Path]::GetFullPath($Right).TrimEnd('\')}
function WriteText($File,$Text){$null=New-Item -ItemType Directory -Path (Split-Path -Parent $File) -Force;[IO.File]::WriteAllText($File,$Text,[Text.UTF8Encoding]::new($false))}
$checks=0;function Need($Value,$Message){if(-not $Value){throw $Message};$script:checks++}
$base=[IO.Path]::GetFullPath($OutputDirectory);$root=Join-Path $base '模拟 E 盘\CloudMusic';$operation=Join-Path $base 'cleanup';$null=New-Item -ItemType Directory -Path $root,$operation -Force
$manifest=[IO.File]::ReadAllText((Join-Path $LegacyPayloadRoot 'manifest.json'))|ConvertFrom-Json
$note='docs\release-1.7.0-preview.11.md'
Need (@($manifest.files|Where-Object{$_.path.Replace('/','\') -eq $note}).Count -eq 1) 'Published preview11 fixture does not contain the reported file'
foreach($name in @('manifest.json','CodexLiveBackground.exe',$note)){$target=Join-Path $root $name;$null=New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force;Copy-Item -LiteralPath (Join-Path $LegacyPayloadRoot $name) -Destination $target}
$owned=@($manifest.files|ForEach-Object{$_.path})+@('manifest.json','.codex-background-install.json')+$script:BackgroundRuntimeFiles
$marker=Join-Path $root '.codex-background-install.json';WriteText $marker (@{productId='codex-live-background/exe';root=$root;version=$manifest.version;pending=$true;files=$owned}|ConvertTo-Json -Depth 6)
WriteText (Join-Path $root 'music-library.txt') 'preserve unrelated files';$script:NativePackageRoot=$PackageRoot
# This is the exact failing preflight path in the supplied diagnostic report.
$recognized=@(Get-BackgroundOwnedFiles -Root $root)
Need ($recognized -contains $note) 'The legitimate preview11 release note is still rejected'
Assert-BackgroundDestination -Root $root -PackageRoot $PackageRoot;$checks++
$item=Get-BackgroundScanItem -Root $root -PackageRoot $PackageRoot
Need $item.verified 'Published preview11 manager failed scan identity validation'
# Failed/uninstalled copies can be missing the manager; the pinned published
# manifest must still prove the remaining program files belong to this app.
Remove-Item -LiteralPath (Join-Path $root 'CodexLiveBackground.exe')
$partial=Get-BackgroundScanItem -Root $root -PackageRoot $PackageRoot
Need $partial.verified 'Pinned preview11 manifest did not recognize a partial install'
$originalManifest=[IO.File]::ReadAllBytes((Join-Path $root 'manifest.json'));[IO.File]::AppendAllText((Join-Path $root 'manifest.json'),"`n")
$unknown=Get-BackgroundScanItem -Root $root -PackageRoot $PackageRoot
Need (-not $unknown.verified) 'An unrecognized manifest with no known manager gained cleanup authority'
[IO.File]::WriteAllBytes((Join-Path $root 'manifest.json'),$originalManifest)
$originalMarker=[IO.File]::ReadAllText($marker)
$bad=$originalMarker|ConvertFrom-Json;$bad.files=@($bad.files)+@('..\personal.txt');WriteText $marker ($bad|ConvertTo-Json -Depth 6)
$blocked=$false;try{$null=Get-BackgroundOwnedFiles -Root $root}catch{$blocked=$true}
Need $blocked 'Historical compatibility allowed a traversal entry'
$bad=$originalMarker|ConvertFrom-Json;$bad.files=@($bad.files)+@('docs\not-a-published-release.md');WriteText $marker ($bad|ConvertTo-Json -Depth 6)
$blocked=$false;try{$null=Get-BackgroundOwnedFiles -Root $root}catch{$blocked=$true}
Need $blocked 'Historical compatibility accepted an arbitrary Markdown filename'
WriteText $marker $originalMarker;$item=Get-BackgroundScanItem -Root $root -PackageRoot $PackageRoot
$reportFile=Join-Path $base 'scan.json';WriteText $reportFile (@{schema=2;productId='codexhub-install-scan';owner=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;createdUtc=[DateTime]::UtcNow.ToString('o');items=@($item)}|ConvertTo-Json -Depth 7)
$script:stopped=0;function Stop-BackgroundIntegrations {$script:stopped++};function Remove-BackgroundDesktopEntries {};function Remove-BackgroundNativeRegistration {}
Invoke-BackgroundResidualCleanup -ScanReport $reportFile -PackageRoot $PackageRoot -OperationRoot $operation -ManagerPid $PID
Need ($script:stopped -eq 1) 'Verified residual cleanup did not stop its integrations'
Need (-not [IO.File]::Exists((Join-Path $root $note))) 'Verified old release note was not removed'
Need ([IO.File]::ReadAllText((Join-Path $root 'music-library.txt')) -eq 'preserve unrelated files') 'Cleanup removed unrelated files in the shared folder'
$current=[IO.File]::ReadAllText((Join-Path $PackageRoot 'manifest.json'))|ConvertFrom-Json
foreach($doc in @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\docs') -Filter 'release-*.md' -File)){
 $relative='docs\'+$doc.Name;Need (@($current.files|Where-Object{$_.path.Replace('/','\') -eq $relative}).Count -eq 1) ('New payload dropped an existing release note: '+$relative)
}
@{pass=$true;checks=$checks;publishedPreview11Recognized=$true;partialInstallRecognized=$true;unknownFilesRejected=$true;sharedDataPreserved=$true;realInstallChanged=$false}|ConvertTo-Json -Compress
