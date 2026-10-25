param([Parameter(Mandatory=$true)][string]$PackageRoot,[Parameter(Mandatory=$true)][string]$MsiPath)
$ErrorActionPreference='Stop'
$exe=Join-Path ([IO.Path]::GetFullPath($PackageRoot)) '卸载 Codex 动态背景.exe'
$assembly=[Reflection.Assembly]::LoadFrom($exe);$type=$assembly.GetType('Uninstall')
$product=$type.GetMethod('Product',[Reflection.BindingFlags]'Static,NonPublic').Invoke($null,@())
$installer=New-Object -ComObject WindowsInstaller.Installer
$invoke=[Reflection.BindingFlags]::InvokeMethod;$get=[Reflection.BindingFlags]::GetProperty
$database=$installer.GetType().InvokeMember('OpenDatabase',$invoke,$null,$installer,@([IO.Path]::GetFullPath($MsiPath),0))
function Cell([string]$sql){$view=$database.GetType().InvokeMember('OpenView',$invoke,$null,$database,@($sql));try{$null=$view.GetType().InvokeMember('Execute',$invoke,$null,$view,@());$row=$view.GetType().InvokeMember('Fetch',$invoke,$null,$view,@());try{return $row.GetType().InvokeMember('StringData',$get,$null,$row,@(1))}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($row)}}finally{$null=$view.GetType().InvokeMember('Close',$invoke,$null,$view,@());[void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)}}
try{
 if($product -cne (Cell 'SELECT `Value` FROM `Property` WHERE `Property`=''ProductCode''')){throw 'Uninstaller targets another product'}
 if((Cell 'SELECT `Target` FROM `CustomAction` WHERE `Action`=''RemoveBackground''') -cne '"[INSTALLDIR]CodexLiveBackground.exe" --package-remove'){throw 'MSI does not call full package cleanup'}
 if([int](Cell 'SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''RemoveBackground''') -ge [int](Cell 'SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''RemoveFiles''')){throw 'Services are removed after files'}
 @{pass=$true;cases=3;uninstallerProductMatches=$true;fullCleanupBeforeFileRemoval=$true;uninstallLaunched=$false}|ConvertTo-Json -Compress
}finally{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($database);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)}
