$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\shortcuts.ps1')
function Test-DreamSkinPathEqual {param($Left,$Right) if(-not $Left -or -not $Right){return $false};[IO.Path]::GetFullPath($Left) -ieq [IO.Path]::GetFullPath($Right)}
$sandbox=Join-Path $PSScriptRoot ('output\shortcuts-'+[guid]::NewGuid().ToString('N'));$links=Join-Path $sandbox 'links';$state=Join-Path $sandbox 'state';$null=New-Item -ItemType Directory -Path $links,$state -Force
$codex=[pscustomobject]@{Executable=(Join-Path $sandbox 'Official\ChatGPT.exe');AppUserModelId='OpenAI.Codex_2p2nqsd0c76g0!App'}
$shell=New-Object -ComObject WScript.Shell
try{
 $path=Join-Path $links 'Codex.lnk';$link=$shell.CreateShortcut($path);$link.TargetPath=$codex.Executable;$link.IconLocation=$codex.Executable+',0';$link.Save();$before=(Get-FileHash -LiteralPath $path).Hash
 $other=Join-Path $links 'Other.lnk';$link=$shell.CreateShortcut($other);$link.TargetPath='C:\Windows\notepad.exe';$link.Save();$unrelated=(Get-FileHash -LiteralPath $other).Hash
 $oldEntry=Join-Path $links 'Codex（动态背景）.lnk';$link=$shell.CreateShortcut($oldEntry);$link.TargetPath=Join-Path $state 'startup\DreamSkinPortHook.exe';$link.Arguments='--launch';$link.Save()
 $result=Repair-DreamBackgroundShortcuts -Codex $codex -StateRoot $state -Directories @($links) -DesktopDirectory $links
 if(Test-Path -LiteralPath $oldEntry){throw 'Extra generated desktop icon survived'}
 if($result.repaired -ne 1 -or $shell.CreateShortcut($path).Arguments -ne '--launch'){throw 'Taskbar shortcut not repaired'}
 if((Get-FileHash -LiteralPath $other).Hash -cne $unrelated){throw 'Unrelated shortcut changed'}
 if((Repair-DreamBackgroundShortcuts -Codex $codex -StateRoot $state -Directories @($links) -DesktopDirectory $links).repaired -ne 0){throw 'Shortcut migration is not idempotent'}
 $link=$shell.CreateShortcut($oldEntry);$link.TargetPath=$codex.Executable;$link.Description='启动官方 Codex 并恢复动态背景';$link.Save()
 $null=Repair-DreamBackgroundShortcuts -Codex $codex -StateRoot $state -Directories @($links) -DesktopDirectory $links
 if(Test-Path -LiteralPath $oldEntry){throw 'Old restored generated icon survived'}
 Restore-DreamBackgroundShortcuts -StateRoot $state
 if((Get-FileHash -LiteralPath $path).Hash -cne $before){throw 'Shortcut bytes not restored'}
 $null=Repair-DreamBackgroundShortcuts -Codex $codex -StateRoot $state -Directories @($links);$link=$shell.CreateShortcut($path);$link.TargetPath='C:\Windows\write.exe';$link.Save();$changed=(Get-FileHash -LiteralPath $path).Hash
 Restore-DreamBackgroundShortcuts -StateRoot $state;if((Get-FileHash -LiteralPath $path).Hash -cne $changed){throw 'User shortcut edits were overwritten'}
}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)}
@{pass=$true;cases=7;noExtraDesktopIcon=$true;directLaunch=$true;unrelatedPreserved=$true;idempotent=$true;restored=$true;userChangesPreserved=$true}|ConvertTo-Json -Compress
