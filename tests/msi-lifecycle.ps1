param([Parameter(Mandatory=$true)][string]$MsiPath,[string]$PreviousMsiPath)
$ErrorActionPreference='Stop'
$msi=[IO.Path]::GetFullPath($MsiPath)
$installer=New-Object -ComObject WindowsInstaller.Installer
$invoke=[Reflection.BindingFlags]::InvokeMethod;$get=[Reflection.BindingFlags]::GetProperty
function Call($object,[string]$name,[object[]]$arguments){$unwrapped=@($arguments|ForEach-Object{$_.PSObject.BaseObject});$object.GetType().InvokeMember($name,$invoke,$null,$object,$unwrapped)}
function Cell($database,[string]$sql){$view=Call $database 'OpenView' @($sql);try{$null=Call $view 'Execute' @();$row=Call $view 'Fetch' @();if(-not $row){throw 'MSI metadata missing'};try{$row.GetType().InvokeMember('StringData',$get,$null,$row,@(1))}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($row)}}finally{$null=Call $view 'Close' @();[void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)}}
$database=Call $installer 'OpenDatabase' @($msi,0)
try{
 if((Cell $database 'SELECT `Value` FROM `Property` WHERE `Property`=''ProductName''') -cne 'Codex Background isolated package test'){throw 'Refusing to install a real product'}
 if((Cell $database 'SELECT `Value` FROM `Property` WHERE `Property`=''SKIPBACKGROUND''') -ne '1' -or (Cell $database 'SELECT `Value` FROM `Property` WHERE `Property`=''DREAM_MSI_PROBE''') -ne '1'){throw 'Test must skip all real background operations'}
 if((Cell $database 'SELECT `DefaultDir` FROM `Directory` WHERE `Directory`=''INSTALLDIR''') -notlike 'CodexBackgroundIsolated-*'){throw 'Test package uses a real installation directory'}
 if((Cell $database 'SELECT `Condition` FROM `InstallExecuteSequence` WHERE `Action`=''SetInstallDirectory''') -cne 'NOT INSTALLDIR AND CURRENTINSTALLDIR'){throw 'Custom directory is overwritten'}
 if((Cell $database 'SELECT `Type` FROM `Control` WHERE `Dialog_`=''InstallDirDlg'' AND `Control`=''InstallPath''') -cne 'PathEdit'){throw 'Installer has no editable path'}
 if((Cell $database 'SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''RemoveExistingProducts''') -ne '1450'){throw 'Old version is not removed before new install'}
 foreach($action in @('InstallBackground','RemoveBackground')){if((Cell $database ('SELECT `Target` FROM `CustomAction` WHERE `Action`='''+$action+'''')) -cne '"[INSTALLDIR]CodexLiveBackground.exe" --msi-action-probe'){throw 'Non-probe command found'}}
 $product=Cell $database 'SELECT `Value` FROM `Property` WHERE `Property`=''ProductCode'''
}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($database);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)}
$sandbox=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('output\msi-'+[guid]::NewGuid().ToString('N'))));$target=Join-Path $sandbox '自定义目录 with spaces';$null=New-Item -ItemType Directory -Path $sandbox
$boundary=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'output')).TrimEnd('\')+'\'
if(-not $target.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Test target outside workspace'}
$msiexec=Join-Path $env:WINDIR 'System32\msiexec.exe'
function Run([string[]]$arguments,[string]$logName){$log=Join-Path $sandbox $logName;$process=Start-Process -FilePath $msiexec -ArgumentList (@($arguments)+@('/qn','/norestart','/L*V',('"'+$log+'"'))) -WindowStyle Hidden -Wait -PassThru;if($process.ExitCode -notin @(0,3010)){throw ('MSI '+$logName+' failed with '+$process.ExitCode+'; '+$log)};return [IO.File]::ReadAllText($log)}
$installed=$false
try{
 if($PreviousMsiPath){
  $previous=[IO.Path]::GetFullPath($PreviousMsiPath);$testInstaller=New-Object -ComObject WindowsInstaller.Installer;$db=Call $testInstaller 'OpenDatabase' @($previous,0)
  try{if((Cell $db 'SELECT `Value` FROM `Property` WHERE `Property`=''ProductName''') -cne 'Codex Background isolated package test' -or (Cell $db 'SELECT `Value` FROM `Property` WHERE `Property`=''SKIPBACKGROUND''') -ne '1'){throw 'Previous product is not isolated'};$previousProduct=Cell $db 'SELECT `Value` FROM `Property` WHERE `Property`=''ProductCode'''}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($db);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($testInstaller)}
  $oldTarget=Join-Path $sandbox '旧版目录';$null=Run @('/i',('"'+$previous+'"'),('INSTALLDIR="'+$oldTarget+'"'),'LAUNCHMANAGER=0') 'previous-install.log'
 }
 $installLog=Run @('/i',('"'+$msi+'"'),('INSTALLDIR="'+$target+'"'),'LAUNCHMANAGER=0') 'install.log';$installed=$true
 if(-not(Test-Path -LiteralPath (Join-Path $target 'CodexLiveBackground.exe'))){throw 'Manager not installed in isolated target'}
 if($PreviousMsiPath -and (Test-Path -LiteralPath (Join-Path $oldTarget 'CodexLiveBackground.exe'))){throw 'Old version files survived upgrade'}
 if(-not(Test-Path -LiteralPath (Join-Path $target '卸载 Codex 动态背景.exe'))){throw 'Standalone uninstaller not installed'}
 $repairLog=Run @('/fvomus',('"'+$msi+'"'),'LAUNCHMANAGER=0') 'repair.log'
 [IO.File]::WriteAllText((Join-Path $target 'installed.json'),'{}');[IO.File]::WriteAllText((Join-Path $target 'startup\guard-state.json'),'{}')
 $removeLog=Run @('/x',$product,'LAUNCHMANAGER=0') 'uninstall.log';$installed=$false
 foreach($record in @(@{text=$installLog;action='InstallBackground'},@{text=$repairLog;action='InstallBackground'},@{text=$removeLog;action='RemoveBackground'})){
  if($record.text -notmatch ('CustomActionSchedule\(Action='+$record.action+',')){throw ('Probe action did not actually execute: '+$record.action)}
  if($record.text -match 'Error 2753|错误 2753'){throw '2753 reproduced'}
 }
 if(Test-Path -LiteralPath (Join-Path $target 'CodexLiveBackground.exe')){throw 'Isolated manager survived uninstall'}
 if((Test-Path -LiteralPath (Join-Path $target 'installed.json')) -or (Test-Path -LiteralPath (Join-Path $target 'startup\guard-state.json'))){throw 'Generated runtime files survived uninstall'}
 @{pass=$true;install=$true;repair=$true;uninstall=$true;customPathPersisted=$true;previousVersionRemoved=[bool]$PreviousMsiPath;serviceActionsExercised=$true;realServicesModified=$false;realInstallationTouched=$false;sandbox=$sandbox}|ConvertTo-Json -Compress
}finally{if($installed){$null=Run @('/x',$product,'LAUNCHMANAGER=0') 'cleanup.log'};if($previousProduct -and (Test-Path -LiteralPath (Join-Path $oldTarget 'CodexLiveBackground.exe'))){$null=Run @('/x',$previousProduct,'LAUNCHMANAGER=0') 'previous-cleanup.log'}}
