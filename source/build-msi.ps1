param([Parameter(Mandatory=$true)][string]$PackageRoot,[Parameter(Mandatory=$true)][string]$OutputFile,[switch]$RequireTrustedSignature,[switch]$TestPackage)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($PackageRoot);$out=[IO.Path]::GetFullPath($OutputFile)
if(Test-Path -LiteralPath $out){throw 'OutputFile must be new.'}
$manifest=[IO.File]::ReadAllText((Join-Path $root 'manifest.json'))|ConvertFrom-Json
foreach($entry in $manifest.files){$path=Join-Path $root $entry.path;if((Get-FileHash -LiteralPath $path).Hash -cne $entry.sha256){throw ('Package changed: '+$entry.path)}}
if($RequireTrustedSignature){foreach($file in @(Get-ChildItem -LiteralPath $root -Recurse -File|Where-Object{$_.Extension -in @('.exe','.ps1')})){if((Get-AuthenticodeSignature -LiteralPath $file.FullName).Status -ne 'Valid'){throw ('Trusted signature required: '+$file.Name)}}}
$work=Join-Path ([IO.Path]::GetDirectoryName($out)) ('msi-build-'+[guid]::NewGuid().ToString('N'));$null=New-Item -ItemType Directory -Path $work
$installer=New-Object -ComObject WindowsInstaller.Installer
$invoke=[Reflection.BindingFlags]::InvokeMethod;$set=[Reflection.BindingFlags]::SetProperty;$get=[Reflection.BindingFlags]::GetProperty
function Call($Object,[string]$Name,[object[]]$Arguments){$unwrapped=@($Arguments|ForEach-Object{$_.PSObject.BaseObject});$Object.GetType().InvokeMember($Name,$invoke,$null,$Object,$unwrapped)}
function SetProperty($Object,[string]$Name,[object[]]$Arguments){$null=$Object.GetType().InvokeMember($Name,$set,$null,$Object,$Arguments)}
function Query([string]$Sql){$view=Call $database 'OpenView' @($Sql);$null=Call $view 'Execute' @();$null=Call $view 'Close' @();[void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)}
function Table([string]$Name,[string]$Definition){Query ('CREATE TABLE `'+$Name+'` ('+$Definition+')')}
function Row([string]$Name,[string[]]$Columns,[object[]]$Values){
 $sql='INSERT INTO `'+$Name+'` (`'+($Columns -join '`,`')+'`) VALUES ('+((@('?')*$Values.Count)-join ',')+')';$view=Call $database 'OpenView' @($sql);$record=Call $installer 'CreateRecord' @($Values.Count)
 try{for($i=0;$i -lt $Values.Count;$i++){if($null -eq $Values[$i]){continue};if($Values[$i] -is [int]){SetProperty $record 'IntegerData' @(($i+1),$Values[$i])}else{SetProperty $record 'StringData' @(($i+1),[string]$Values[$i])}};$null=Call $view 'Execute' @($record)}finally{$null=Call $view 'Close' @();[void][Runtime.InteropServices.Marshal]::ReleaseComObject($record);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)}
}
$namespace=if($TestPackage){'test-'+[guid]::NewGuid().ToString('N')}else{'codex-live-background'}
function StableGuid([string]$Value){$md5=[Security.Cryptography.MD5]::Create();try{return ([guid]::new($md5.ComputeHash([Text.Encoding]::UTF8.GetBytes($namespace+'|'+$Value.ToLowerInvariant())))).ToString('B').ToUpperInvariant()}finally{$md5.Dispose()}}
try{
 $database=Call $installer 'OpenDatabase' @($out,3)
 # Windows Installer database strings use a declared ANSI code page; Chinese names use 936.
 [IO.File]::WriteAllText((Join-Path $work 'codepage.idt'),"`r`n`r`n936`t_ForceCodepage`r`n",[Text.Encoding]::ASCII);$null=Call $database 'Import' @($work,'codepage.idt')
 Table 'Property' '`Property` CHAR(72) NOT NULL, `Value` LONGCHAR LOCALIZABLE PRIMARY KEY `Property`'
 Table 'Directory' '`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`'
 Table 'Component' '`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`'
 Table 'Feature' '`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` SHORT, `Level` SHORT NOT NULL, `Directory_` CHAR(72), `Attributes` SHORT NOT NULL PRIMARY KEY `Feature`'
 Table 'FeatureComponents' '`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Feature_`,`Component_`'
 Table 'File' '`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` SHORT, `Sequence` LONG NOT NULL PRIMARY KEY `File`'
 Table 'Media' '`DiskId` SHORT NOT NULL, `LastSequence` LONG NOT NULL, `DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72) PRIMARY KEY `DiskId`'
 Table 'CustomAction' '`Action` CHAR(72) NOT NULL, `Type` SHORT NOT NULL, `Source` CHAR(72), `Target` LONGCHAR LOCALIZABLE PRIMARY KEY `Action`'
 Table 'InstallExecuteSequence' '`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`'
 Table 'InstallUISequence' '`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`'
 Table 'Upgrade' '`UpgradeCode` CHAR(38) NOT NULL, `VersionMin` CHAR(20), `VersionMax` CHAR(20), `Language` CHAR(255), `Attributes` LONG NOT NULL, `Remove` CHAR(255), `ActionProperty` CHAR(72) NOT NULL PRIMARY KEY `UpgradeCode`,`VersionMin`,`VersionMax`,`Language`,`Attributes`'
 $product=StableGuid ('product-'+$manifest.version);$upgrade=StableGuid 'upgrade'
 $properties=@{ProductCode=$product;UpgradeCode=$upgrade;ProductVersion=$manifest.version;ProductLanguage='2052';ProductName='Codex 动态背景';Manufacturer='codex-live-background contributors';INSTALLLEVEL='1';REBOOT='ReallySuppress';MSIINSTALLPERUSER='1';ALLUSERS='2';SecureCustomProperties='OLDPRODUCTS';ARPNOMODIFY='1';ARPURLINFOABOUT='https://github.com/chenyan-image/codex-live-background';LAUNCHMANAGER='1'}
 if($TestPackage){$properties.ProductName='Codex Background isolated package test';$properties.SKIPBACKGROUND='1';$properties.LAUNCHMANAGER='0'}
 foreach($name in $properties.Keys){Row 'Property' @('Property','Value') @($name,$properties[$name])}
 Row 'Directory' @('Directory','Directory_Parent','DefaultDir') @('TARGETDIR',$null,'SourceDir')
 Row 'Directory' @('Directory','Directory_Parent','DefaultDir') @('LocalAppDataFolder','TARGETDIR','.')
 Row 'Directory' @('Directory','Directory_Parent','DefaultDir') @('UserPrograms','LocalAppDataFolder','Programs')
 Row 'Directory' @('Directory','Directory_Parent','DefaultDir') @('INSTALLDIR','UserPrograms','CodexLiveBackground')
 Row 'Feature' @('Feature','Title','Display','Level','Directory_','Attributes') @('Main','Codex 动态背景',1,1,'INSTALLDIR',0)
 $directories=@{''='INSTALLDIR'};$sequence=0;$managerId=$null
 $ddf=[Collections.Generic.List[string]]::new();$ddf.Add('.OPTION EXPLICIT');$ddf.Add('.Set Cabinet=on');$ddf.Add('.Set Compress=on');$ddf.Add('.Set CompressionType=LZX');$ddf.Add('.Set CompressionMemory=21');$ddf.Add('.Set CabinetNameTemplate=payload.cab');$ddf.Add('.Set MaxDiskSize=0');$ddf.Add('.Set DiskDirectoryTemplate="'+$work+'"');$ddf.Add('.Set InfFileName="'+(Join-Path $work 'payload.inf')+'"');$ddf.Add('.Set RptFileName="'+(Join-Path $work 'payload.rpt')+'"')
 foreach($file in Get-ChildItem -LiteralPath $root -Recurse -File|Sort-Object FullName){
  $relative=$file.FullName.Substring($root.Length+1);$folder=[IO.Path]::GetDirectoryName($relative);$parent='INSTALLDIR';$current=''
  foreach($part in @($folder.Split('\')|Where-Object{$_})){$current=if($current){$current+'\'+$part}else{$part};if(-not $directories.ContainsKey($current)){$id='D'+$directories.Count;$directories[$current]=$id;Row 'Directory' @('Directory','Directory_Parent','DefaultDir') @($id,$parent,$part)};$parent=$directories[$current]}
  $sequence++;$id='F'+$sequence.ToString('D4');$component='C'+$sequence.ToString('D4');$version=$file.VersionInfo.FileVersion
  if($version -notmatch '^\d+\.\d+\.\d+\.\d+$'){$version=$null}
  Row 'Component' @('Component','ComponentId','Directory_','Attributes','KeyPath') @($component,(StableGuid $relative),$parent,256,$id)
  Row 'FeatureComponents' @('Feature_','Component_') @('Main',$component)
  Row 'File' @('File','Component_','FileName','FileSize','Version','Attributes','Sequence') @($id,$component,$file.Name,[int]$file.Length,$version,16384,$sequence)
  $ddf.Add('"'+$file.FullName+'" '+$id);if($relative -eq 'CodexLiveBackground.exe'){$managerId=$id}
 }
 [IO.File]::WriteAllLines((Join-Path $work 'payload.ddf'),$ddf,[Text.Encoding]::Default)
 $lines=& (Join-Path $env:WINDIR 'System32\makecab.exe') /F (Join-Path $work 'payload.ddf') 2>&1;if($LASTEXITCODE -ne 0){throw ($lines -join "`n")}
 $view=Call $database 'OpenView' @('INSERT INTO `_Streams` (`Name`,`Data`) VALUES (?,?)');$record=Call $installer 'CreateRecord' @(2)
 try{SetProperty $record 'StringData' @(1,'payload.cab');$null=Call $record 'SetStream' @(2,(Join-Path $work 'payload.cab'));$null=Call $view 'Execute' @($record)}finally{$null=Call $view 'Close' @();[void][Runtime.InteropServices.Marshal]::ReleaseComObject($record);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($view)}
 Row 'Media' @('DiskId','LastSequence','Cabinet') @(1,$sequence,'#payload.cab')
 Row 'CustomAction' @('Action','Type','Source','Target') @('InstallBackground',1042,$managerId,'--service-install')
 Row 'CustomAction' @('Action','Type','Source','Target') @('RemoveBackground',1042,$managerId,'--service-remove')
 Row 'CustomAction' @('Action','Type','Source','Target') @('LaunchManager',210,$managerId,'')
 Row 'CustomAction' @('Action','Type','Source','Target') @('SetInstallDirectory',51,'INSTALLDIR','[LocalAppDataFolder]Programs\CodexLiveBackground')
 Row 'Upgrade' @('UpgradeCode','VersionMin','VersionMax','Attributes','ActionProperty') @($upgrade,'0.0.0',$manifest.version,0,'OLDPRODUCTS')
 $actions=@{FindRelatedProducts=25;CostInitialize=800;FileCost=900;CostFinalize=1000;MigrateFeatureStates=1200;InstallValidate=1400;InstallInitialize=1500;ProcessComponents=1600;UnpublishFeatures=1800;RemoveFiles=3500;InstallFiles=4000;RegisterUser=6000;RegisterProduct=6100;PublishFeatures=6300;PublishProduct=6400;InstallExecute=6500;RemoveExistingProducts=6550;InstallFinalize=6600}
 foreach($name in $actions.Keys){Row 'InstallExecuteSequence' @('Action','Sequence') @($name,[int]$actions[$name])}
 Row 'InstallExecuteSequence' @('Action','Condition','Sequence') @('SetInstallDirectory','NOT SKIPBACKGROUND',950)
 Row 'InstallExecuteSequence' @('Action','Condition','Sequence') @('RemoveBackground','REMOVE~="ALL" AND NOT UPGRADINGPRODUCTCODE AND NOT SKIPBACKGROUND',3450)
 Row 'InstallExecuteSequence' @('Action','Condition','Sequence') @('InstallBackground','NOT REMOVE~="ALL" AND NOT SKIPBACKGROUND',6450)
 Row 'InstallExecuteSequence' @('Action','Condition','Sequence') @('LaunchManager','NOT REMOVE~="ALL" AND UILevel>=3 AND LAUNCHMANAGER="1"',6700)
 foreach($pair in @(@('FindRelatedProducts',25),@('CostInitialize',800),@('FileCost',900),@('CostFinalize',1000),@('ExecuteAction',1300))){Row 'InstallUISequence' @('Action','Sequence') @($pair[0],[int]$pair[1])}
 Row 'InstallUISequence' @('Action','Condition','Sequence') @('SetInstallDirectory','NOT SKIPBACKGROUND',950)
 $summary=$database.GetType().InvokeMember('SummaryInformation',$get,$null,$database,@(20))
 foreach($pair in @(@(1,936),@(2,'Installation Database'),@(3,'Codex Live Background '+$manifest.version),@(4,'codex-live-background contributors'),@(7,'x64;2052'),@(9,([guid]::NewGuid().ToString('B').ToUpperInvariant())),@(14,200),@(15,10),@(18,'Windows Installer database builder'))){SetProperty $summary 'Property' @($pair[0],$pair[1])}
 $null=Call $summary 'Persist' @();$null=Call $database 'Commit' @();[void][Runtime.InteropServices.Marshal]::ReleaseComObject($summary)
 Write-Output $out
}finally{if($database){[void][Runtime.InteropServices.Marshal]::ReleaseComObject($database)};[void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)}
