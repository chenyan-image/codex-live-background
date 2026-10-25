$ErrorActionPreference='Stop';$env:PSModulePath=Join-Path $PSHOME Modules
. (Join-Path $PSScriptRoot '..\source\shortcuts.ps1')
function Test-DreamSkinPathEqual {param($Left,$Right) if(-not $Left -or -not $Right){return $false};[IO.Path]::GetFullPath($Left) -ieq [IO.Path]::GetFullPath($Right)}
$sandbox=Join-Path $PSScriptRoot ('output\taskbar-'+[guid]::NewGuid().ToString('N'));$links=Join-Path $sandbox links;$state=Join-Path $sandbox state;$startup=Join-Path $sandbox startup;$null=New-Item -ItemType Directory -Path $links,$state,$startup -Force
$stable=[pscustomobject]@{Executable=(Join-Path $sandbox 'WindowsApps\OpenAI.Codex_26.1021.1.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe');AppUserModelId='OpenAI.Codex_2p2nqsd0c76g0!App'}
$beta=[pscustomobject]@{Executable=(Join-Path $sandbox 'WindowsApps\OpenAI.CodexBeta_26.727.1.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe');AppUserModelId='OpenAI.CodexBeta_2p2nqsd0c76g0!App'}
$shell=New-Object -ComObject WScript.Shell
function Link([string]$Name,[string]$Target,[string]$Arguments=''){$p=Join-Path $links ($Name+'.lnk');$l=$shell.CreateShortcut($p);$l.TargetPath=$Target;$l.Arguments=$Arguments;$l.Save();return $p}
try{
 Initialize-DreamBackgroundShellIdentity
 $normal=Link normal $stable.Executable;[CodexBackgroundShell.Identity]::SetShortcut($normal,$beta.AppUserModelId)
 $stale=Link stale (Join-Path $sandbox 'WindowsApps\OpenAI.Codex_1.0.0.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe')
 $betaPin=Link beta (Join-Path $sandbox 'WindowsApps\OpenAI.CodexBeta_1.0.0.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe')
 $hook=Link old-service (Join-Path $startup DreamSkinPortHook.exe) '--launch'
 $package=Link package (Join-Path $env:WINDIR explorer.exe) ('shell:AppsFolder\'+$stable.AppUserModelId)
 $deep=Link deep $stable.Executable 'codex://threads/fixture';$deepHash=(Get-FileHash $deep).Hash
 $foreign=Link ChatGPT (Join-Path $sandbox 'OtherPublisher\ChatGPT.exe');$foreignHash=(Get-FileHash $foreign).Hash
 $call=@{Codex=$stable;StateRoot=$state;Directories=@($links);StartupRoot=$startup;DesktopDirectory=$links;LaunchChannels=@($stable,$beta)}
 $result=Repair-DreamBackgroundShortcuts @call;if($result.repaired -ne 5){throw ('Unexpected repair count: '+$result.repaired)}
 foreach($p in @($normal,$stale,$hook,$package)){$l=$shell.CreateShortcut($p);if($l.TargetPath -ine (Join-Path $env:WINDIR explorer.exe) -or $l.Arguments -cne ('shell:AppsFolder\'+$stable.AppUserModelId) -or [CodexBackgroundShell.Identity]::ReadShortcut($p) -cne $stable.AppUserModelId){throw 'Stable entry identity mismatch'}}
 if($shell.CreateShortcut($betaPin).Arguments -cne ('shell:AppsFolder\'+$beta.AppUserModelId) -or [CodexBackgroundShell.Identity]::ReadShortcut($betaPin) -cne $beta.AppUserModelId){throw 'Beta entry routed into stable'}
 if((Get-FileHash $deep).Hash -cne $deepHash -or (Get-FileHash $foreign).Hash -cne $foreignHash){throw 'Deep link or unrelated app changed'}
 if((Repair-DreamBackgroundShortcuts @call).repaired -ne 0){throw 'Canonical migration was not idempotent'}
 Restore-DreamBackgroundShortcuts -StateRoot $state -DesktopDirectory $links;if($shell.CreateShortcut($stale).Arguments -cne ('shell:AppsFolder\'+$stable.AppUserModelId)){throw 'Uninstall recreated broken version pin'}
 $l=$shell.CreateShortcut($normal);$l.TargetPath=Join-Path $env:WINDIR notepad.exe;$l.Arguments='';$l.Save();$changed=(Get-FileHash $normal).Hash
 Restore-DreamBackgroundShortcuts -StateRoot $state -DesktopDirectory $links;if((Get-FileHash $normal).Hash -cne $changed){throw 'User shortcut edit replaced'}
 @{pass=$true;cases=10;staleVersionLinksRepaired=$true;stableAndBetaSeparated=$true;hookAndPackageEntriesUnified=$true;appUserModelIdMatches=$true;deepAndForeignLinksPreserved=$true;uninstallKeepsOfficialEntry=$true;realPinnedItemsModified=$false}|ConvertTo-Json -Compress
}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)}
