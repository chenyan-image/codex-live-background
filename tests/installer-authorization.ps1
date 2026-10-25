param([Parameter(Mandatory=$true)][string]$MsiPath,[Parameter(Mandatory=$true)][string]$PackageRoot,[string]$LauncherPath)
$ErrorActionPreference='Stop'
$msi=[IO.Path]::GetFullPath($MsiPath);$installer=New-Object -ComObject WindowsInstaller.Installer
$invoke=[Reflection.BindingFlags]::InvokeMethod;$get=[Reflection.BindingFlags]::GetProperty
$db=$installer.GetType().InvokeMember('OpenDatabase',$invoke,$null,$installer,@($msi,0))
function Cell([string]$sql){$view=$db.GetType().InvokeMember('OpenView',$invoke,$null,$db,@($sql));try{$null=$view.GetType().InvokeMember('Execute',$invoke,$null,$view,@());$row=$view.GetType().InvokeMember('Fetch',$invoke,$null,$view,@());if(-not $row){return ''};try{return $row.GetType().InvokeMember('StringData',$get,$null,$row,@(1))}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($row)}}finally{$null=$view.GetType().InvokeMember('Close',$invoke,$null,$view,@());[void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)}}
try{
 $summary=$db.GetType().InvokeMember('SummaryInformation',$get,$null,$db,@(0))
 try{$bits=[int]$summary.GetType().InvokeMember('Property',$get,$null,$summary,@(15))}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($summary)}
 if(($bits -band 8) -or -not($bits -band 2)){throw 'Real MSI still suppresses required privileges'}
 if((Cell 'SELECT `Value` FROM `Property` WHERE `Property`=''MSIINSTALLPERUSER''') -ne '1'){throw 'User registration context changed'}
 if((Cell 'SELECT `Value` FROM `Property` WHERE `Property`=''SecureCustomProperties''') -notmatch 'LAUNCHMANAGER'){throw 'Authorized install cannot keep manager unelevated'}
 if(Cell 'SELECT `Value` FROM `Property` WHERE `Property`=''DISABLEROLLBACK'''){throw 'Rollback was disabled'}
 foreach($action in @('InstallBackground','RemoveBackground')){if([int](Cell ('SELECT `Type` FROM `CustomAction` WHERE `Action`='''+$action+'''')) -band 2048){throw 'User background operation was moved to SYSTEM'}}
}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($db);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)}
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path ([IO.Path]::GetFullPath($PackageRoot)) '卸载 Codex 动态背景.exe'))
$method=$assembly.GetType('Uninstall').GetMethod('NeedsAuthorization',[Reflection.BindingFlags]'Static,NonPublic')
$local='C:\Users\example\AppData\Local';$system='C:\Windows\System32'
foreach($case in @(@(($local+'\Programs\Codex'),$false),@('D:\Apps\Codex',$true),@('C:\Program Files\Codex',$true),@('C:\Users\other\Codex',$true),@(($local+'Evil\Codex'),$true))){$result=$method.Invoke($null,@([string]$case[0],$local,$system));if($result -ne $case[1]){throw ('Wrong authorization decision: '+$case[0])}}
if($LauncherPath){
 $helper=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($LauncherPath));$verify=$helper.GetType('AdminInstall').GetMethod('Verify',[Reflection.BindingFlags]'Static,NonPublic')
 $null=$verify.Invoke($null,@([string]$msi))
 $fixture=Join-Path $PSScriptRoot ('output\wrong-msi-'+[guid]::NewGuid().ToString('N')+'.msi');[IO.File]::WriteAllText($fixture,'wrong payload')
 try{$null=$verify.Invoke($null,@([string]$fixture));throw 'Wrong MSI accepted'}catch{if($_.Exception.ToString() -notmatch '校验值不匹配'){throw}}
}
@{pass=$true;cases=13;allowsWindowsAuthorization=$true;rollbackPreserved=$true;userServiceContextPreserved=$true;pathDecisions=$true;pinnedPayloadVerified=[bool]$LauncherPath;uacRequested=$false;permissionsChanged=$false;installationPerformed=$false}|ConvertTo-Json -Compress
