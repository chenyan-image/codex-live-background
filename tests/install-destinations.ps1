param([string]$OutputDirectory=(Join-Path $PSScriptRoot ('output\install-destinations-'+[guid]::NewGuid().ToString('N'))))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\service-removal.ps1')
. (Join-Path $PSScriptRoot '..\source\native-lifecycle.ps1')
function Test-DreamSkinPathEqual {param($Left,$Right);[IO.Path]::GetFullPath($Left).TrimEnd('\') -ieq [IO.Path]::GetFullPath($Right).TrimEnd('\')}
$checks=0
function Need($Condition,$Message){if(-not $Condition){throw $Message};$script:checks++}
$null=New-Item -ItemType Directory -Path $OutputDirectory -Force
foreach($letter in [char[]]'CDEXYZ'){
 $root="$($letter):\";Assert-BackgroundInstallPath $root
 Need ((Get-BackgroundSafePath -Root $root -Relative 'engine\test.txt') -ieq ($root+'engine\test.txt')) ('Drive root became relative: '+$root)
}
$root=Join-Path $OutputDirectory '客户 [方括号] 软件 & 任意名字 # % !';$package=Join-Path $OutputDirectory 'package';$null=New-Item -ItemType Directory -Path $root,$package -Force
$script:NativePackageRoot=$package
[IO.File]::WriteAllText((Join-Path $root 'personal.txt'),'preserve')
[IO.File]::WriteAllText((Join-Path $package 'CodexLiveBackground.exe'),'fixture')
$manifest=@{version='1.7.0.15';files=@(@{path='CodexLiveBackground.exe';bytes=7;sha256=(Get-FileHash -LiteralPath (Join-Path $package 'CodexLiveBackground.exe')).Hash})}
[IO.File]::WriteAllText((Join-Path $package 'manifest.json'),($manifest|ConvertTo-Json -Depth 5))
Assert-BackgroundInstallPath $root;Assert-BackgroundDestination -Root $root -PackageRoot $package;$checks++
[IO.File]::WriteAllText((Join-Path $root 'CodexLiveBackground.exe'),'unrelated collision')
$blocked=$false;try{Assert-BackgroundDestination -Root $root -PackageRoot $package}catch{$blocked=$_.Exception.Message -match '同名文件'}
Need $blocked 'An unrelated file collision was overwritten'
Need ([IO.File]::ReadAllText((Join-Path $root 'CodexLiveBackground.exe')) -eq 'unrelated collision') 'Collision changed the unrelated file'
$null=New-Item -ItemType Directory -Path (Join-Path $root 'owned'),(Join-Path $root 'unrelated-empty') -Force
[IO.File]::WriteAllText((Join-Path $root 'owned\program.txt'),'owned fixture')
Remove-BackgroundOwnedFiles -Root $root -Files @('owned\program.txt')
Need (-not(Test-Path -LiteralPath (Join-Path $root 'owned'))) 'Owned empty folder survived'
Need (Test-Path -LiteralPath (Join-Path $root 'unrelated-empty')) 'Uninstall pruned an unrelated empty folder'
Need ([IO.File]::ReadAllText((Join-Path $root 'personal.txt')) -eq 'preserve') 'Uninstall modified personal data'
$history=Join-Path $OutputDirectory 'fixed-history'
Need (Test-BackgroundOwnedExecutable -Executable 'E:\engine\runtime\node\node.exe' -Roots @('E:\') -StateRoot $history) 'Owned runtime was missed on a drive root'
Need (-not(Test-BackgroundOwnedExecutable -Executable 'E:\another-app\node.exe' -Roots @('E:\') -StateRoot $history)) 'Another app on the same drive was claimed as owned'
Need (-not(Test-BackgroundOwnedExecutable -Executable 'E:\other\powershell.exe' -Roots @('E:\') -StateRoot $history)) 'An unrelated PowerShell was claimed as owned'
Need (Test-BackgroundOwnedExecutable -Executable 'E:\CodexLiveBackground.exe' -Roots @('E:\') -StateRoot $history) 'The root manager was missed'
Write-BackgroundInstallHistory -Root 'E:\' -Version '1.7.0.15' -Status installed -HistoryDirectory $history
Write-BackgroundInstallHistory -Root 'C:\客户 软件 & 自定义' -Version '1.7.0.15' -Status installed -HistoryDirectory $history
Write-BackgroundInstallHistory -Root 'E:\' -Version '' -Status removed -HistoryDirectory $history
$record=[IO.File]::ReadAllText((Join-Path $history 'installations.json'))|ConvertFrom-Json
Need ($record.installations.Count -eq 2) 'Install history was not deduplicated'
Need (@($record.installations|Where-Object {$_.root -eq 'E:\' -and $_.status -eq 'removed'}).Count -eq 1) 'History lost the drive-root slash or removal status'
Need (@($record.installations|Where-Object {$_.root -eq 'E:\' -and $_.version -eq '1.7.0.15'}).Count -eq 1) 'Uninstall lost the prior installed version'
Need (@($record.installations|Where-Object {$_.root -eq 'C:\客户 软件 & 自定义' -and $_.status -eq 'installed'}).Count -eq 1) 'History lost the Unicode installation'
@{pass=$true;checks=$checks;driveRoots=@('C:\','D:\','E:\','X:\','Y:\','Z:\');sharedFolderPreserved=$true;historyPersisted=$true;realInstallChanged=$false}|ConvertTo-Json -Compress
