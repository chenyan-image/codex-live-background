param([Parameter(Mandatory=$true)][string]$PackageRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\service-compatibility.ps1')
$target=Join-Path $PSScriptRoot ('output\gallery-deployment-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $target -Force
Copy-DreamBackgroundPackage -SourceRoot ([IO.Path]::GetFullPath($PackageRoot)) -DestinationRoot $target
foreach($relative in @('codex-icons.ps1','wallpapers\catalog.json','wallpapers\flower-peach.jpg','wallpapers\ayaka.jpg','wallpapers\dresden-moonlight.gif','wallpapers\rain-sakura-preview.jpg')){
 $file=Join-Path $target $relative
 if(-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash -cne (Get-FileHash -LiteralPath (Join-Path $PackageRoot $relative)).Hash){throw ('Manual deployment omitted '+$relative)}
}
if(Test-Path -LiteralPath (Join-Path $target 'wallpapers\bundled')){throw 'Retired bundled media was copied'}
@{pass=$true;wallpapers=4;bundledVideos=0;remoteVideos=1;iconModuleIncluded=$true;target='isolated-fixture';noLiveInstall=$true}|ConvertTo-Json -Compress
