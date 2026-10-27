param(
 [Parameter(Mandatory=$true)][string]$Repo,
 [Parameter(Mandatory=$true)][string]$HistoricalManager,
 [string]$Report
)
$ErrorActionPreference='Stop'
. (Join-Path $Repo 'source\native-lifecycle.ps1')
function Test-DreamSkinPathEqual {param($Left,$Right) [IO.Path]::GetFullPath($Left).TrimEnd('\') -ieq [IO.Path]::GetFullPath($Right).TrimEnd('\')}
$sandbox=Join-Path ([IO.Path]::GetTempPath()) ('HubPreflight-'+[guid]::NewGuid().ToString('N').Substring(0,10))
$package=Join-Path $sandbox package;$installed=Join-Path $sandbox 'old install';$state=Join-Path $sandbox state
$null=New-Item -ItemType Directory -Path $package,$installed,$state -Force
$original=Join-Path $installed 'CodexLiveBackground.exe';$material=Join-Path $installed 'personal-wallpaper.jpg'
[IO.File]::WriteAllText($original,'existing installed executable');[IO.File]::WriteAllText($material,'original personal material')
$originalHash=(Get-FileHash -LiteralPath $original).Hash;$materialHash=(Get-FileHash -LiteralPath $material).Hash
$program=Join-Path $package 'CodexLiveBackground.exe';[IO.File]::WriteAllText($program,'known current program fixture')
$outside=Join-Path $sandbox outside.bin;[IO.File]::WriteAllText($outside,'outside')
$programEntry=[ordered]@{path='CodexLiveBackground.exe';bytes=(Get-Item -LiteralPath $program).Length;sha256=(Get-FileHash -LiteralPath $program).Hash}
$script:checks=0;$script:planCalled=$false;$script:stopCalled=$false
function Need([bool]$Condition,[string]$Message){$script:checks++;if(-not $Condition){throw $Message}}
function Manifest($Entries){[IO.File]::WriteAllText((Join-Path $package manifest.json),(@{version='1.7.0.9';platform='windows-x64';files=@($Entries)}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))}
# These boundaries must never reach the real registry, service or process code.
function Get-BackgroundPlan {param($InstallRoot,$PackageRoot) $script:planCalled=$true;throw 'fixture-preflight-passed'}
function Stop-BackgroundIntegrations {param($Roots,$StateRoot,$PackageRoot,$Uninstall,$ExcludeIds) $script:stopCalled=$true;throw 'fixture-service-write-reached'}
$call=@{Action='Install';InstallRoot=$installed;StateRoot=$state;PackageRoot=$package;SetupExecutable=(Join-Path $sandbox Setup.exe);OperationRoot=(Join-Path $sandbox operation);ManagerPid=$PID}
$cases=@(
 @{name='missing';entry=@{path='missing.dll';bytes=9;sha256=$programEntry.sha256}},
 @{name='hash';entry=@{path='CodexLiveBackground.exe';bytes=$programEntry.bytes;sha256=('F'*64)}},
 @{name='length';entry=@{path='CodexLiveBackground.exe';bytes=($programEntry.bytes+1);sha256=$programEntry.sha256}},
 @{name='traversal';entry=@{path='..\outside.bin';bytes=7;sha256=(Get-FileHash -LiteralPath $outside).Hash}},
 @{name='absolute';entry=@{path=$outside;bytes=7;sha256=(Get-FileHash -LiteralPath $outside).Hash}}
)
foreach($case in $cases){
 $script:planCalled=$false;$script:stopCalled=$false
 # A valid earlier entry proves the implementation checks the whole package
 # before invoking shutdown instead of interleaving copy/delete with validation.
 Manifest @($programEntry,$case.entry)
 $message=$null;try{Invoke-BackgroundNativeAction @call}catch{$message=$_.Exception.Message}
 Need ($message -and $message -ne 'fixture-preflight-passed' -and -not $script:planCalled -and -not $script:stopCalled) ('Invalid source reached installation side effects: '+$case.name)
 Need ((Get-FileHash -LiteralPath $original).Hash -eq $originalHash -and (Get-FileHash -LiteralPath $material).Hash -eq $materialHash) ('Invalid source modified existing files: '+$case.name)
}
Manifest @($programEntry);$script:planCalled=$false;$message=$null
try{Invoke-BackgroundNativeAction @call}catch{$message=$_.Exception.Message}
Need ($script:planCalled -and $message -eq 'fixture-preflight-passed' -and -not $script:stopCalled) 'Valid package did not finish preflight before the isolated plan boundary'
$script:NativePackageRoot=$package
$current=Join-Path $sandbox 'partial current';$unknown=Join-Path $sandbox 'unknown';$historical=Join-Path $sandbox 'partial historical'
$null=New-Item -ItemType Directory -Path $current,$unknown,$historical
Copy-Item -LiteralPath $program -Destination (Join-Path $current 'CodexLiveBackground.exe')
$owned=@(Get-BackgroundOwnedFiles -Root $current)
Need ($owned -contains 'CodexLiveBackground.exe' -and -not(Test-Path -LiteralPath (Join-Path $current '.codex-background-install.json'))) 'Exact current executable without a marker was not recognized for retry'
[IO.File]::AppendAllText((Join-Path $current 'CodexLiveBackground.exe'),' tampered')
Need (@(Get-BackgroundOwnedFiles -Root $current).Count -eq 0) 'Changed current executable was treated as owned'
[IO.File]::WriteAllText((Join-Path $unknown 'CodexLiveBackground.exe'),'unrelated executable')
Need (@(Get-BackgroundOwnedFiles -Root $unknown).Count -eq 0) 'Unknown executable was treated as owned'
$history=[IO.File]::ReadAllText((Join-Path $Repo 'source\historical-install-files.json'))|ConvertFrom-Json
$historicalHash=(Get-FileHash -LiteralPath $HistoricalManager).Hash
$release=@($history.releases|Where-Object {$_.managerSha256 -ieq $historicalHash})|Select-Object -First 1
Need ($null -ne $release) 'Historical test executable does not match a frozen release fingerprint'
Copy-Item -LiteralPath $HistoricalManager -Destination (Join-Path $historical 'CodexLiveBackground.exe')
$owned=@(Get-BackgroundOwnedFiles -Root $historical)
Need ($owned.Count -ge $release.files.Count -and $owned -contains 'CodexLiveBackground.exe' -and -not(Test-Path -LiteralPath (Join-Path $historical '.codex-background-install.json'))) 'Exact historical executable without a marker was not recognized'
Need ((Get-FileHash -LiteralPath $original).Hash -eq $originalHash -and (Get-FileHash -LiteralPath $material).Hash -eq $materialHash) 'Fingerprint discovery modified the original installation'
$result=[ordered]@{pass=$true;checks=$script:checks;invalidSourceCases=$cases.Count;allSourceVerifiedBeforeShutdown=$true;validCurrentAndHistoricalPartialInstallsRecognized=$true;unknownAndChangedExecutablesRejected=$true;existingProgramAndMaterialPreserved=$true;realServicesModified=$false;realRegistrationModified=$false;realProcessesInspectedOrStopped=$false;fixture=$sandbox}
$json=$result|ConvertTo-Json -Depth 4 -Compress
if($Report){[IO.File]::WriteAllText([IO.Path]::GetFullPath($Report),$json,[Text.UTF8Encoding]::new($false))}
Write-Output $json
