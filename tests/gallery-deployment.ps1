param([Parameter(Mandatory=$true)][string]$PackageRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\service-compatibility.ps1')
$target=Join-Path $PSScriptRoot ('output\gallery-deployment-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $target -Force
Copy-DreamBackgroundPackage -SourceRoot ([IO.Path]::GetFullPath($PackageRoot)) -DestinationRoot $target
foreach($relative in @('codex-icons.ps1','wallpapers\midnight-ridge.jpg','wallpapers\quiet-moon.jpg','wallpapers\forest-mist.jpg','wallpapers\blue-tide.jpg','wallpapers\amber-dunes.jpg','wallpapers\star-atlas.jpg','wallpapers\bundled\catalog.json','wallpapers\bundled\water-eyes.mp4','wallpapers\bundled\water-eyes-preview.gif','wallpapers\bundled\talking-eyes-2.mp4','wallpapers\bundled\talking-eyes-2-preview.jpg','wallpapers\bundled\zero-two.jpg')){
 $file=Join-Path $target $relative
 if(-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash -cne (Get-FileHash -LiteralPath (Join-Path $PackageRoot $relative)).Hash){throw ('Manual deployment omitted '+$relative)}
}
@{pass=$true;wallpapers=9;bundledVideos=2;iconModuleIncluded=$true;target='isolated-fixture';noLiveInstall=$true}|ConvertTo-Json -Compress
