param([Parameter(Mandatory=$true)][string]$TestMsi)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\service-removal.ps1')
. (Join-Path $PSScriptRoot '..\source\native-lifecycle.ps1')
function Test-DreamSkinPathEqual {param($Left,$Right) [IO.Path]::GetFullPath($Left).TrimEnd('\') -ieq [IO.Path]::GetFullPath($Right).TrimEnd('\')}
$msi=[IO.Path]::GetFullPath($TestMsi);$installer=New-Object -ComObject WindowsInstaller.Installer;$invoke=[Reflection.BindingFlags]::InvokeMethod;$get=[Reflection.BindingFlags]::GetProperty
$db=$installer.GetType().InvokeMember('OpenDatabase',$invoke,$null,$installer,@($msi,0))
function Cell([string]$sql){$view=$db.GetType().InvokeMember('OpenView',$invoke,$null,$db,@($sql));try{$null=$view.GetType().InvokeMember('Execute',$invoke,$null,$view,@());$r=$view.GetType().InvokeMember('Fetch',$invoke,$null,$view,@());try{return $r.GetType().InvokeMember('StringData',$get,$null,$r,@(1))}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($r)}}finally{$null=$view.GetType().InvokeMember('Close',$invoke,$null,$view,@());[void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)}}
try{if((Cell 'SELECT `Value` FROM `Property` WHERE `Property`=''ProductName''') -cne 'Codex Background isolated package test' -or (Cell 'SELECT `Value` FROM `Property` WHERE `Property`=''SKIPBACKGROUND''') -ne '1'){throw 'Refusing real MSI'};$code=Cell 'SELECT `Value` FROM `Property` WHERE `Property`=''ProductCode'''}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($db)}
if($installer.GetType().InvokeMember('ProductState',$get,$null,$installer,@([string]$code)) -eq 5){throw 'Test product already installed; refusing unrelated prior state'}
$sandbox='D:\CodexBackground-MigrationTest-'+[guid]::NewGuid().ToString('N');$root=Join-Path $sandbox '旧版 app';$backup=Join-Path $sandbox backup;$null=New-Item -ItemType Directory -Path $sandbox -Force
$script:NativeSetupExecutable=Join-Path $sandbox setup.exe
function RunMsi([string]$arguments){$p=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\msiexec.exe') -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru;if($p.ExitCode -notin @(0,3010)){throw ('Test MSI returned '+$p.ExitCode)}}
$registered=$false
try{
 RunMsi ('/i "'+$msi+'" INSTALLDIR="'+$root+'" /qn /norestart LAUNCHMANAGER=0 DREAM_MSI_PROBE=0 /L*V "'+(Join-Path $sandbox install.log)+'"');$registered=$true
 [IO.File]::WriteAllText((Join-Path $root 'user-original.jpg'),'original')
 $product=[pscustomobject]@{ProductCode=$code;Version='1.6.0';Root=$root;Cache=(Join-Path $sandbox 'missing-cache.msi');SourceName=[IO.Path]::GetFileName($msi);SourceUrl=$null;SourceSha256=(Get-FileHash $msi).Hash;LocalRecoveryFile=$msi;RecoverySource=$null}
 $files=@(Get-BackgroundMsiFiles -Product ([pscustomobject]@{Cache=$msi;Root=$root}))
 if($files.Count -lt 20 -or $files -notcontains 'CodexLiveBackground.exe'){throw 'MSI file ownership list missing'}
 Remove-BackgroundLegacyMsi -Product $product -BackupRoot $backup
 $state=$installer.GetType().InvokeMember('ProductState',$get,$null,$installer,@([string]$code));if($state -eq 5){throw 'MSI record survived migration'};$registered=$false
 if(Test-Path -LiteralPath (Join-Path $root CodexLiveBackground.exe)){throw 'Old files survived migration'}
 if([IO.File]::ReadAllText((Join-Path $root 'user-original.jpg')) -cne 'original'){throw 'Original file deleted'}
 # Inject a failure after direct file removal; verify backup restoration.
 $failedRoot=Join-Path $sandbox failed;$null=New-Item -ItemType Directory -Path $failedRoot
 [IO.File]::WriteAllText((Join-Path $failedRoot CodexLiveBackground.exe),'restore-me')
 function Invoke-BackgroundMsiRemove {param($Product,$Log) throw 'simulated unregister failure'}
 $failed=[pscustomobject]@{ProductCode=$code;Root=$failedRoot;Cache=$msi;RecoverySource=$null}
 try{Remove-BackgroundLegacyMsi -Product $failed -BackupRoot (Join-Path $sandbox failed-backup);throw 'Failure ignored'}catch{if($_.Exception.Message -ne 'simulated unregister failure'){throw}}
 if([IO.File]::ReadAllText((Join-Path $failedRoot CodexLiveBackground.exe)) -cne 'restore-me'){throw 'Failed unregister lost old files'}
 $result=@{pass=$true;cases=7;drive='D';oldMsiUnregistered=$true;verifiedRecoverySource=$true;filesRemovedBeforeMsi=$true;failureRestored=$true;originalPreserved=$true;realInstallationTouched=$false}
 $report=Join-Path $PSScriptRoot 'output\native-msi-migration-result.json';$result|ConvertTo-Json|Set-Content $report -Encoding UTF8;$result|ConvertTo-Json -Compress
}finally{
 if($registered){RunMsi ('/x '+$code+' /qn /norestart SKIPBACKGROUND=1 DREAM_MSI_PROBE=0 LAUNCHMANAGER=0');$registered=$false}
 [void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
 if(-not $registered){if($sandbox -notmatch '^D:\\CodexBackground-MigrationTest-[a-f0-9]{32}$'){throw 'Test cleanup boundary mismatch'};Assert-BackgroundInstallPath $sandbox;Remove-Item -LiteralPath $sandbox -Recurse -Force}
}
