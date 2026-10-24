$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\engine\scripts\config-utf8.ps1')
$sandbox=Join-Path $PSScriptRoot ('output\dark-'+[guid]::NewGuid().ToString('N'));$null=New-Item -ItemType Directory -Path $sandbox
$config=Join-Path $sandbox 'config.toml';$backup=Join-Path $sandbox 'before.toml'
$original="model = `"example`"`r`n[desktop]`r`nappearanceTheme = `"light`"`r`notherSetting = `"keep`"`r`n"
[IO.File]::WriteAllText($config,$original,[Text.UTF8Encoding]::new($false))
$null=Install-DreamSkinBaseTheme -ConfigPath $config -BackupPath $backup -AppearanceTheme dark
$updated=[IO.File]::ReadAllText($config)
if($updated -notmatch 'appearanceTheme = "dark"' -or $updated -notmatch 'otherSetting = "keep"' -or $updated -notmatch 'model = "example"'){throw 'Dark preference changed unrelated settings'}
if([IO.File]::ReadAllText($backup) -cne $original){throw 'Original config backup lost'}
$null=Install-DreamSkinBaseTheme -ConfigPath $config -BackupPath $backup -AppearanceTheme dark
if([IO.File]::ReadAllText($backup) -cne $original){throw 'Repeated apply overwrote original backup'}
@{pass=$true;cases=3;dark=$true;unrelatedSettingsPreserved=$true;backupPreserved=$true}|ConvertTo-Json -Compress
