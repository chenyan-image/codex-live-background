param([Parameter(Mandatory=$true)][string]$MsiPath)
$ErrorActionPreference='Stop'
$installer=New-Object -ComObject WindowsInstaller.Installer
$invoke=[Reflection.BindingFlags]::InvokeMethod;$get=[Reflection.BindingFlags]::GetProperty
$database=$installer.GetType().InvokeMember('OpenDatabase',$invoke,$null,$installer,@([IO.Path]::GetFullPath($MsiPath),0))
function Cell([string]$sql){$view=$database.GetType().InvokeMember('OpenView',$invoke,$null,$database,@($sql));try{$null=$view.GetType().InvokeMember('Execute',$invoke,$null,$view,@());$row=$view.GetType().InvokeMember('Fetch',$invoke,$null,$view,@());if(-not $row){throw ('Missing authoring: '+$sql)};try{return $row.GetType().InvokeMember('StringData',$get,$null,$row,@(1))}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($row)}}finally{$null=$view.GetType().InvokeMember('Close',$invoke,$null,$view,@());[void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)}}
try{
 $progress=[int](Cell 'SELECT `Sequence` FROM `InstallUISequence` WHERE `Action`=''ProgressDlg''')
 $execute=[int](Cell 'SELECT `Sequence` FROM `InstallUISequence` WHERE `Action`=''ExecuteAction''')
 if($progress -ge $execute -or ([int](Cell 'SELECT `Attributes` FROM `Dialog` WHERE `Dialog`=''ProgressDlg''') -band 2)){throw 'Progress is blocked or starts after execution'}
 foreach($mapping in @(@('Progress','SetProgress','Progress'),@('Stage','ActionText','Text'),@('Details','ActionData','Text'))){if((Cell ('SELECT `Attribute` FROM `EventMapping` WHERE `Dialog_`=''ProgressDlg'' AND `Control_`='''+$mapping[0]+''' AND `Event`='''+$mapping[1]+'''')) -cne $mapping[2]){throw 'Native progress event not wired'}}
 foreach($ending in @(@('SuccessDlg',-1),@('FailureDlg',-3),@('UserExitDlg',-2))){if([int](Cell ('SELECT `Sequence` FROM `InstallUISequence` WHERE `Action`='''+$ending[0]+'''')) -ne $ending[1]){throw 'Missing exit result page'}}
 foreach($action in @('InstallBackground','RemoveBackground')){if([int](Cell ('SELECT `Type` FROM `CustomAction` WHERE `Action`='''+$action+'''')) -band 192){throw 'Service operation detaches from installer'};if((Cell ('SELECT `Description` FROM `ActionText` WHERE `Action`='''+$action+'''')) -notmatch '请稍候'){throw 'Long operation has no visible status'}}
 if((Cell 'SELECT `Condition` FROM `InstallExecuteSequence` WHERE `Action`=''LaunchManager''') -notmatch 'UILevel<5'){throw 'Full installer opens manager before finish page'}
 if((Cell 'SELECT `Condition` FROM `ControlEvent` WHERE `Dialog_`=''SuccessDlg'' AND `Control_`=''Finish'' AND `Event`=''DoAction''') -notmatch 'LAUNCHMANAGER'){throw 'Finish page does not launch manager'}
 if(-not (Cell 'SELECT `Value` FROM `Property` WHERE `Property`=''MsiLogging''')){throw 'Failure log is not enabled'}
 @{pass=$true;cases=11;modelessBeforeExecute=$true;nativeProgressEvents=$true;synchronousServiceActions=$true;allResultPages=$true;launchAfterFinish=$true;readOnly=$true}|ConvertTo-Json -Compress
}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($database);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)}
