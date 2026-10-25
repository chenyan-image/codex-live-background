param([Parameter(Mandatory=$true)][string]$ManagerPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ManagerPath));$type=$assembly.GetType('Manager')
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$form=$type.GetConstructor($flags,$null,@([bool]),$null).Invoke(@($false))
try{
 $status=$type.GetField('status',$flags).GetValue($form)
 $detail=$type.GetField('connectionDetail',$flags).GetValue($form)
 $hint=$type.GetField('previewHint',$flags).GetValue($form)
 $update=$type.GetMethod('UpdateConnection',$flags)
 $message='请保存输入并手动完全退出 Codex，再点击连接'
 $json=@{installed=$true;connected=$false;serviceRunning=$true;connectionError=$true;message=$message}|ConvertTo-Json -Compress
 $null=$update.Invoke($form,@([string]$json))
 if($status.Text -notmatch '重试' -or $hint.Text -cne $message -or $detail.Text -notmatch '手动完全退出'){throw 'Disconnected reason is not visible'}
 $json=@{installed=$true;connected=$true;serviceRunning=$true;background='playing'}|ConvertTo-Json -Compress
 $null=$update.Invoke($form,@([string]$json))
 if($status.Text -notmatch '连接就绪' -or $detail.Text -notmatch '视频播放中'){throw 'Verified connection state is not visible'}
 $type.GetField('checkingStatus',$flags).SetValue($form,$true)
 $click=[Windows.Forms.Control].GetMethod('OnClick',$flags)
 foreach($control in @($status,$detail,$status.Parent)){
  $type.GetField('pendingConnect',$flags).SetValue($form,$false)
  $null=$click.Invoke($control,@([EventArgs]::Empty))
  if(-not $type.GetField('pendingConnect',$flags).GetValue($form)){throw 'Connection click was lost during status query'}
 }
 @{pass=$true;cases=5;queuedClick=$true;actionableFailure=$true;realConnectionAttempted=$false}|ConvertTo-Json -Compress
}finally{$form.Dispose()}
