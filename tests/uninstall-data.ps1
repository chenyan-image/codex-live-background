$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\service-removal.ps1')
. (Join-Path $PSScriptRoot '..\source\uninstall-data.ps1')
$sandbox=Join-Path $PSScriptRoot ('output\uninstall-data-'+[guid]::NewGuid().ToString('N'));$state=Join-Path $sandbox 'state';$outside=Join-Path $sandbox 'originals'
$null=New-Item -ItemType Directory -Path $state,$outside,(Join-Path $state 'runtime'),(Join-Path $state 'runtime-downloads'),(Join-Path $state 'compatible-video-v2'),(Join-Path $state 'previews'),(Join-Path $state 'cdp-profile') -Force
[IO.File]::WriteAllText((Join-Path $outside 'wallpaper.mp4'),'user original')
[IO.File]::WriteAllText((Join-Path $state 'cdp-profile\Login Data'),'login')
[IO.File]::WriteAllText((Join-Path $state 'runtime\download.exe'),'generated')
[IO.File]::WriteAllText((Join-Path $state 'previews\preview.jpg'),'generated')
[IO.File]::WriteAllText((Join-Path $state 'wallpaper-library.json'),'{"external":"originals/wallpaper.mp4"}')
[IO.File]::WriteAllText((Join-Path $state 'unknown-user-file.txt'),'preserve')
Remove-DreamBackgroundGeneratedData -StateRoot $state
foreach($name in @('runtime','runtime-downloads','compatible-video-v2','previews','wallpaper-library.json')){if(Test-Path -LiteralPath (Join-Path $state $name)){throw 'Generated file survived'}}
foreach($path in @((Join-Path $state 'cdp-profile\Login Data'),(Join-Path $outside 'wallpaper.mp4'),(Join-Path $state 'unknown-user-file.txt'))){if(-not(Test-Path -LiteralPath $path)){throw 'User file deleted'}}
Remove-DreamBackgroundGeneratedData -StateRoot $state
$junction=Join-Path $state 'runtime';$null=New-Item -ItemType Junction -Path $junction -Target $outside
try{Remove-DreamBackgroundGeneratedData -StateRoot $state;throw 'Junction accepted'}catch{if($_.Exception.Message -notmatch '链接或联接'){throw}}
[IO.Directory]::Delete($junction)
@{pass=$true;cases=10;cachesRemoved=$true;originalsPreserved=$true;loginPreserved=$true;unknownFilesPreserved=$true;junctionRejected=$true;realDataModified=$false}|ConvertTo-Json -Compress
