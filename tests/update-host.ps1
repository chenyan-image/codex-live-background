param([Parameter(Mandatory=$true)][string]$PackageRoot,[Parameter(Mandatory=$true)][string]$OldManager,[Parameter(Mandatory=$true)][string]$Archive)
$ErrorActionPreference='Stop'
$sandbox=Join-Path ([IO.Path]::GetTempPath()) ('HubHost-'+[guid]::NewGuid().ToString('N').Substring(0,12));$app=Join-Path $sandbox 'app with spaces';$state=Join-Path $sandbox state
$null=New-Item -ItemType Directory -Path $app,$state
Copy-Item -LiteralPath $OldManager -Destination (Join-Path $app 'CodexLiveBackground.exe')
[IO.File]::WriteAllText((Join-Path $app 'manifest.json'),'{"version":"1.7.0.2","files":[]}')
$settings=Join-Path $state 'pet-settings.json';[IO.File]::WriteAllText($settings,'{"Scale":112,"SelectedId":"personal"}');$settingsHash=(Get-FileHash -LiteralPath $settings).Hash
$old=Start-Process -FilePath (Join-Path $app 'CodexLiveBackground.exe') -ArgumentList '--wait-test' -WindowStyle Hidden -PassThru
$request=[ordered]@{Root=$app;StateRoot=$state;Archive=[IO.Path]::GetFullPath($Archive);Sha256=(Get-FileHash -LiteralPath $Archive).Hash;ManagerPid=$old.Id;ManagerStarted=$old.StartTime.ToUniversalTime().Ticks;ResumeArguments=@('--wait-test')}
$path=Join-Path $sandbox 'request.json';[IO.File]::WriteAllText($path,($request|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
$hostProcess=Start-Process -FilePath (Join-Path $PackageRoot 'CodexHubUpdater.exe') -ArgumentList ('--apply "'+$path+'"') -WindowStyle Hidden -PassThru
if(-not $hostProcess.WaitForExit(60000)){throw ('Update host did not exit; own fixture retained: '+$sandbox)}
if((Get-Item -LiteralPath (Join-Path $app 'CodexLiveBackground.exe')).VersionInfo.FileVersion -ne '1.7.0.4'){throw 'Real update host did not install the new program'}
$started=@(Get-CimInstance Win32_Process -Filter "Name='CodexLiveBackground.exe'"|Where-Object{$_.ExecutablePath -eq (Join-Path $app 'CodexLiveBackground.exe') -and $_.ProcessId -ne $old.Id})
if($started.Count -ne 1 -or $started[0].CommandLine -notlike '*--wait-test*'){throw 'Update host did not restore original launch arguments'}
if((Get-FileHash -LiteralPath $settings).Hash -cne $settingsHash){throw 'Native update host changed preferences'}
$result=[ordered]@{pass=$true;nativeHostApplied=$true;parentExitedNormally=$old.HasExited;argumentsRestored=$true;profileBytesPreserved=$true;newVersion='1.7.0.4';realInstallationModified=$false;fixture=$sandbox}
[IO.File]::WriteAllText((Join-Path $sandbox 'verified.json'),($result|ConvertTo-Json),[Text.UTF8Encoding]::new($false));$result|ConvertTo-Json -Compress
$new=Get-Process -Id $started[0].ProcessId -ErrorAction SilentlyContinue;if($new){$null=$new.WaitForExit(35000)}
