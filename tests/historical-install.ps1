param([string]$PackageRoot,[string]$InstallMarker)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\native-lifecycle.ps1')
function Test-DreamSkinPathEqual {param($Left,$Right) [IO.Path]::GetFullPath($Left).TrimEnd('\') -ieq [IO.Path]::GetFullPath($Right).TrimEnd('\')}
$sandbox=Join-Path $PSScriptRoot ('output\history-'+[guid]::NewGuid().ToString('N'));$null=New-Item -ItemType Directory -Path $sandbox
$script:NativePackageRoot=$PackageRoot;$catalog=Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\source\historical-install-files.json') -Raw|ConvertFrom-Json
$checks=0
foreach($release in $catalog.releases){$marker=@{productId=$script:NativeBackgroundId;root=$sandbox;version=$release.version;files=@($release.files)+@('manifest.json','.codex-background-install.json','installed.json')};[IO.File]::WriteAllText((Join-Path $sandbox '.codex-background-install.json'),($marker|ConvertTo-Json -Depth 4));$owned=@(Get-BackgroundOwnedFiles -Root $sandbox);if($owned.Count -lt $release.files.Count){throw ('Historical release rejected: '+$release.version)};$checks++}
if(-not $InstallMarker){$InstallMarker='D:\codex\.codex-background-install.json'};$actual=Get-Content -LiteralPath $InstallMarker -Raw|ConvertFrom-Json;$actual.root=$sandbox;[IO.File]::WriteAllText((Join-Path $sandbox '.codex-background-install.json'),($actual|ConvertTo-Json -Depth 4));$null=Get-BackgroundOwnedFiles -Root $sandbox;$checks++
foreach($bad in @('..\outside.txt','C:\outside.txt','personal-tax-document.pdf','source\..\personal.txt')){$actual.files+=@($bad);[IO.File]::WriteAllText((Join-Path $sandbox '.codex-background-install.json'),($actual|ConvertTo-Json -Depth 4));$rejected=$false;try{$null=Get-BackgroundOwnedFiles -Root $sandbox}catch{$rejected=$true};if(-not $rejected){throw ('Unknown marker entry accepted: '+$bad)};$actual.files=@($actual.files|Where-Object{$_ -ne $bad});$checks++}
@{pass=$true;checks=$checks;preview3Accepted=$true;preview4Accepted=$true;preview5Accepted=$true;customerMarkerAccepted=$true;unknownFilesRejected=$true;realInstallationModified=$false}|ConvertTo-Json -Compress
