param([Parameter(Mandatory=$true)][string]$PackageRoot,[Parameter(Mandatory=$true)][string]$Tag,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$root=[IO.Path]::GetFullPath($PackageRoot);$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Update output directory must be new'}
if($Tag -notmatch '^v[0-9.]+(?:-preview\.[0-9]+)?$'){throw 'Invalid release tag'}
$package=[IO.File]::ReadAllText((Join-Path $root 'manifest.json'))|ConvertFrom-Json
$files=@($package.files|Where-Object{
 $path=$_.path.Replace('\','/')
 $path -match '^(?:CodexLiveBackground\.exe|CodexHubUpdater\.exe|卸载 CodexHub\.exe|[a-zA-Z0-9._-]+\.ps1|service-compatibility\.json|legacy-managers\.json|legacy-msi-products\.json|README\.md|CHANGELOG\.md|NOTICE\.md|LICENSE|THIRD_PARTY_NOTICES\.md|使用说明\.txt)$' -or
 $path -match '^source/[^/]+\.(?:cs|ps1)$' -or $path -eq 'source/app.manifest' -or
 $path -match '^engine/(?:scripts|assets)/[^/]+\.(?:mjs|js|ps1|css|json)$' -or $path -match '^docs/[^/]+\.md$'
}|ForEach-Object{[ordered]@{path=$_.path.Replace('\','/');bytes=$_.bytes;sha256=$_.sha256.ToLowerInvariant()}})
if(-not($files|Where-Object path -eq 'CodexHubUpdater.exe')){throw 'Updater executable is missing'}
$null=New-Item -ItemType Directory -Path $out
$name='CodexHub-'+$Tag.Substring(1)+'-Update.zip';$zipPath=Join-Path $out $name
$zip=[IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Create)
try{
 foreach($item in $files){$source=Join-Path $root $item.path;if((Get-FileHash -LiteralPath $source).Hash -ine $item.sha256){throw 'Update source hash mismatch'};$entry=$zip.CreateEntry($item.path,[IO.Compression.CompressionLevel]::Optimal);$input=[IO.File]::OpenRead($source);$stream=$entry.Open();try{$input.CopyTo($stream)}finally{$stream.Dispose();$input.Dispose()}}
 $entry=$zip.CreateEntry('update-manifest.json');$writer=[IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false));try{$writer.Write((@{product='CodexHub-program-update';version=$package.version;files=$files}|ConvertTo-Json -Depth 6))}finally{$writer.Dispose()}
}finally{$zip.Dispose()}
$hash=(Get-FileHash -LiteralPath $zipPath).Hash.ToLowerInvariant()
$release=[ordered]@{version=$package.version;tag=$Tag;url=('https://github.com/chenyan-image/codex-live-background-pet/releases/download/'+$Tag+'/'+$name);sha256=$hash;bytes=(Get-Item -LiteralPath $zipPath).Length}
[IO.File]::WriteAllText((Join-Path $out 'CodexHub-Update.json'),($release|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
[ordered]@{files=$files.Count;zip=$zipPath;bytes=$release.bytes;sha256=$hash;materialsIncluded=$false;settingsIncluded=$false}|ConvertTo-Json -Compress
