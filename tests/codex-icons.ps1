$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\codex-icons.ps1')
. (Join-Path $PSScriptRoot '..\source\shortcuts.ps1')
function Test-DreamSkinPathEqual {param($Left,$Right) if(-not $Left -or -not $Right){return $false};[IO.Path]::GetFullPath($Left) -ieq [IO.Path]::GetFullPath($Right)}
Add-Type -AssemblyName System.Drawing
$sandbox=Join-Path $PSScriptRoot ('output\icons-'+[guid]::NewGuid().ToString('N'));$assets=Join-Path $sandbox 'Official\assets';$links=Join-Path $sandbox 'links';$state=Join-Path $sandbox 'state';$null=New-Item -ItemType Directory -Path $assets,$links,$state -Force
$bitmap=[Drawing.Bitmap]::new(256,256);$graphics=[Drawing.Graphics]::FromImage($bitmap);try{$graphics.Clear([Drawing.Color]::CornflowerBlue);$bitmap.Save((Join-Path $assets 'Square44x44Logo.targetsize-256_altform-unplated.png'),[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$bitmap.Dispose()}
$codex=[pscustomobject]@{Executable=(Join-Path $sandbox 'Official\ChatGPT.exe');PackageRoot=(Join-Path $sandbox 'Official');PackageFullName='test-package';AppUserModelId='OpenAI.Codex_2p2nqsd0c76g0!App'}
$icon=Get-DreamBackgroundCodexIcon -Codex $codex -StateRoot $state
$loaded=[Drawing.Icon]::new($icon,32,32);try{if($loaded.Width -ne 32){throw 'Invalid icon dimensions'}}finally{$loaded.Dispose()}
$stamp=(Get-Item -LiteralPath $icon).LastWriteTimeUtc.Ticks;$again=Get-DreamBackgroundCodexIcon -Codex $codex -StateRoot $state;if($again -cne $icon -or (Get-Item -LiteralPath $icon).LastWriteTimeUtc.Ticks -ne $stamp){throw 'Icon cache not reused'}
$shell=New-Object -ComObject WScript.Shell
try{
 $path=Join-Path $links 'Codex.lnk';$link=$shell.CreateShortcut($path);$link.TargetPath=$codex.Executable;$link.IconLocation=$codex.Executable+',0';$link.Save();$original=(Get-FileHash -LiteralPath $path).Hash
 $null=Repair-DreamBackgroundShortcuts -Codex $codex -StateRoot $state -Directories @($links)
 if($shell.CreateShortcut($path).IconLocation -ine ($icon+',0')){throw 'Shortcut still references a generic EXE icon'}
 if((Repair-DreamBackgroundShortcuts -Codex $codex -StateRoot $state -Directories @($links)).repaired -ne 0){throw 'Icon repair not idempotent'}
 # Reproduce an old managed shortcut whose icon was set to the plain executable.
 $link=$shell.CreateShortcut($path);$link.IconLocation=$codex.Executable+',0';$link.Save();$record=Join-Path $state 'shortcut-backups\entries.json';$data=Get-Content -LiteralPath $record -Raw|ConvertFrom-Json;$data.entries[0].modifiedSha256=(Get-FileHash -LiteralPath $path).Hash;[IO.File]::WriteAllText($record,($data|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
 if((Repair-DreamBackgroundShortcuts -Codex $codex -StateRoot $state -Directories @($links)).repaired -ne 1){throw 'Old managed shortcut icon not upgraded'}
 Restore-DreamBackgroundShortcuts -StateRoot $state;if((Get-FileHash -LiteralPath $path).Hash -cne $original){throw 'Original shortcut bytes not restored'}
}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)}
@{pass=$true;cases=6;validMultiSizeIcon=$true;cacheReused=$true;officialIconOnShortcut=$true;oldManagedIconRepaired=$true;idempotent=$true;originalRestored=$true}|ConvertTo-Json -Compress
