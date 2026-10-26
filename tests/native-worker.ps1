param([Parameter(Mandatory=$true)][string]$IconFile)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$sandbox=Join-Path $PSScriptRoot ('output\worker-'+[guid]::NewGuid().ToString('N'))
$package=Join-Path $sandbox package;$root=Join-Path $sandbox 'custom app';$state=Join-Path $sandbox state
$null=New-Item -ItemType Directory -Path $package,$state -Force
$namespace='Software\CodexBackgroundNativeTests\'+[guid]::NewGuid().ToString('N')
$utf8=[Text.UTF8Encoding]::new($true)
foreach($name in @('native-actions.ps1','native-lifecycle.ps1','service-removal.ps1','service-compatibility.ps1','shortcuts.ps1','uninstall-data.ps1','upgrade.ps1','legacy-msi-products.json','legacy-managers.json')){Copy-Item -LiteralPath (Join-Path $repo ('source\'+$name)) -Destination $package}
$common=Join-Path $package 'engine\scripts';$null=New-Item -ItemType Directory -Path $common -Force
foreach($name in @('common-windows.ps1','config-utf8.ps1')){Copy-Item -LiteralPath (Join-Path $repo ('engine\scripts\'+$name)) -Destination $common}
# Keep the production file, process and registry operations. Isolate only discovery,
# service activation and registry names so no installed app or live service is used.
$path=Join-Path $package native-lifecycle.ps1;$text=[IO.File]::ReadAllText($path).Replace('Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexLiveBackground',($namespace+'\Uninstall')).Replace('Software\CodexLiveBackground',($namespace+'\App')).Replace('CodexBackgroundSetup-','CodexBackgroundTestSetup-');[IO.File]::WriteAllText($path,$text,$utf8)
$path=Join-Path $package native-actions.ps1;$text=[IO.File]::ReadAllText($path).Replace('$state=Join-Path $env:LOCALAPPDATA CodexDreamSkin',('$state='''+$state.Replace("'","''")+''''))
$facade=@'
function Get-BackgroundPlan {param($InstallRoot,$PackageRoot) [pscustomobject]@{roots=$(if(Test-Path -LiteralPath $InstallRoot){@($InstallRoot)}else{@()});products=@();requiresAuthorization=$false}}
function Stop-BackgroundIntegrations {param($Roots,$StateRoot,$PackageRoot,$Uninstall,$ExcludeIds) Stop-BackgroundOwnedProcesses -Roots $Roots -StateRoot $StateRoot -ExcludeIds $ExcludeIds}
function Invoke-BackgroundNativeActivate {param($Root,$ManagerPid) [IO.File]::WriteAllText((Join-Path $Root 'activated.txt'),'isolated service facade')}
'@
$text=$text.Replace("try{if(`$Action -eq 'Plan')",($facade+"`r`ntry{if(`$Action -eq 'Plan')"));[IO.File]::WriteAllText($path,$text,$utf8)
$workerCode=Join-Path $sandbox Worker.cs;[IO.File]::WriteAllText($workerCode,'using System.Threading;class Worker{static void Main(){Thread.Sleep(300000);}}')
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe ('/out:'+(Join-Path $package CodexLiveBackground.exe)) $workerCode
if($LASTEXITCODE -ne 0){throw 'Fixture compile failed'}
$files=@(Get-ChildItem -LiteralPath $package -Recurse -File|ForEach-Object{@{path=$_.FullName.Substring($package.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})
[IO.File]::WriteAllText((Join-Path $package manifest.json),(@{version='1.7.0.2';files=$files}|ConvertTo-Json -Depth 5))
$zip=Join-Path $sandbox fixture.zip;Add-Type -AssemblyName System.IO.Compression.FileSystem;[IO.Compression.ZipFile]::CreateFromDirectory($package,$zip)
$setup=Join-Path $sandbox FixtureSetup.exe
& (Join-Path $repo 'source\build-setup.ps1') -PackageZip $zip -OutputFile $setup -IconFile $IconFile
function Run-Worker([string]$Action){
 $operation=Join-Path ([IO.Path]::GetTempPath()) ('CodexBackgroundOperation-'+[guid]::NewGuid().ToString('N'));$null=New-Item -ItemType Directory -Path $operation
 [IO.File]::WriteAllText((Join-Path $operation request.json),(@{action=$Action;root=$root;managerPid=$PID;requiresAuthorization=$false}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
 $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
 $p=Start-Process $setup -ArgumentList ('--worker "'+(Join-Path $operation request.json)+'" "'+$sid+'"') -WindowStyle Hidden -PassThru
 if(-not $p.WaitForExit(60000)){throw 'Fixture worker timed out'}
 $result=Get-Content (Join-Path $operation result.json) -Raw|ConvertFrom-Json
 if(-not $result.pass){$workerLog=Join-Path $operation worker.log;$details=if(Test-Path -LiteralPath $workerLog){Get-Content -LiteralPath $workerLog -Raw}else{''};throw ($result.error+"`n"+$details)}
 Copy-Item -LiteralPath (Join-Path $operation result.json) -Destination (Join-Path $sandbox ($Action+'-result.json'))
 if(-not $operation.StartsWith(([IO.Path]::GetTempPath()+'CodexBackgroundOperation-'),[StringComparison]::OrdinalIgnoreCase)){throw 'Operation boundary mismatch'}
 Remove-Item -LiteralPath $operation -Recurse -Force
}
$manager=$null
try{
 Run-Worker Install
 $key='HKCU:\'+$namespace+'\Uninstall';$registration=Get-ItemProperty -LiteralPath $key
 if($registration.DisplayVersion -ne '1.7.0.2' -or $registration.InstallLocation -ne $root){throw 'Real registry registration mismatch'}
 if((Get-FileHash -LiteralPath (Join-Path $root '卸载 CodexHub.exe')).Hash -ne (Get-FileHash $setup).Hash){throw 'Standalone uninstaller copy mismatch'}
 $null=New-Item -ItemType Directory -Path (Join-Path $state cdp-profile),(Join-Path $state runtime) -Force
 [IO.File]::WriteAllText((Join-Path $state 'cdp-profile\Login Data'),'keep');[IO.File]::WriteAllText((Join-Path $state 'runtime\cache.txt'),'generated')
 [IO.File]::WriteAllText((Join-Path $root 'original.jpg'),'keep')
 $manager=Start-Process (Join-Path $root CodexLiveBackground.exe) -WindowStyle Hidden -PassThru
 Run-Worker Uninstall
 if(-not $manager.WaitForExit(1000)){throw 'Manager survived uninstall'}
 if((Test-Path -LiteralPath $key) -or (Test-Path -LiteralPath (Join-Path $root CodexLiveBackground.exe)) -or (Test-Path -LiteralPath (Join-Path $state runtime))){throw 'Uninstall incomplete'}
 if(-not(Test-Path -LiteralPath (Join-Path $root original.jpg)) -or -not(Test-Path -LiteralPath (Join-Path $state 'cdp-profile\Login Data'))){throw 'Original or login deleted'}
 @{pass=$true;cases=6;exeWorkerInstallAndUninstall=$true;actualRegistryAndFileOperations=$true;ownedManagerStopped=$true;realInstallationModified=$false}|ConvertTo-Json -Compress
}finally{
 if($manager -and -not $manager.HasExited){$manager.Kill();$manager.WaitForExit()}
 $key='HKCU:\'+$namespace;if(Test-Path -LiteralPath $key){Remove-Item -LiteralPath $key -Recurse -Force}
}
