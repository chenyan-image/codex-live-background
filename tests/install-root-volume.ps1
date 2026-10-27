param([Parameter(Mandatory=$true)][string]$Fixture)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\service-removal.ps1')
. (Join-Path $PSScriptRoot '..\source\native-lifecycle.ps1')
function Test-DreamSkinPathEqual {param($Left,$Right);[IO.Path]::GetFullPath($Left).TrimEnd('\') -ieq [IO.Path]::GetFullPath($Right).TrimEnd('\')}
$fixturePath=[IO.Path]::GetFullPath($Fixture);$null=New-Item -ItemType Directory -Path $fixturePath -Force
$subst=Join-Path $env:WINDIR 'System32\subst.exe';$drive=$null
foreach($letter in [char[]]'ZYXWVUTSRQPONMLKJIHGF') {if(-not [IO.Directory]::Exists("$($letter):\") -and -not @([IO.Directory]::GetLogicalDrives()|Where-Object{$_ -ieq "$($letter):\"}).Count){$drive="$($letter):";break}}
if(-not $drive){throw 'No unused fixture drive letter'}
& $subst $drive $fixturePath;if($LASTEXITCODE -ne 0){throw 'Could not map isolated fixture volume'}
try{
 $root=$drive+'\';$mapping=@(& $subst)|Where-Object{$_.StartsWith($drive+'\:',[StringComparison]::OrdinalIgnoreCase)}
 if(-not $mapping -or -not $mapping.EndsWith($fixturePath,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture volume mapping differs from the requested directory'}
 Assert-BackgroundInstallPath $root
 [IO.File]::WriteAllText((Join-Path $root 'owned-file.txt'),'owned fixture');[IO.File]::WriteAllText((Join-Path $root 'other-file.txt'),'preserve')
 $null=New-Item -ItemType Directory -Path (Join-Path $root 'unrelated-empty') -Force
 Remove-BackgroundOwnedFiles -Root $root -Files @('owned-file.txt')
 if(-not(Test-Path -LiteralPath $root) -or -not(Test-Path -LiteralPath (Join-Path $root 'unrelated-empty')) -or [IO.File]::ReadAllText((Join-Path $root 'other-file.txt')) -ne 'preserve' -or (Test-Path -LiteralPath (Join-Path $root 'owned-file.txt'))){throw 'Root cleanup removed unrelated content'}
 @{pass=$true;isolatedVolume=$drive;rootPreserved=$true;unrelatedFilesPreserved=$true}|ConvertTo-Json -Compress
}finally{
 $mapping=@(& $subst)|Where-Object{$_.StartsWith($drive+'\:',[StringComparison]::OrdinalIgnoreCase)}
 if($mapping -and $mapping.EndsWith($fixturePath,[StringComparison]::OrdinalIgnoreCase)){& $subst $drive /D}
}
