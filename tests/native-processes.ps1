$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\engine\scripts\common-windows.ps1')
. (Join-Path $PSScriptRoot '..\source\service-removal.ps1')
. (Join-Path $PSScriptRoot '..\source\native-lifecycle.ps1')
$sandbox=Join-Path $PSScriptRoot ('output\process-'+[guid]::NewGuid().ToString('N'));$root=Join-Path $sandbox app;$other=Join-Path $sandbox unrelated;$state=Join-Path $sandbox state
$null=New-Item -ItemType Directory -Path $root,$other,$state,(Join-Path $root 'engine\runtime\node'),(Join-Path $root startup) -Force
$code=Join-Path $sandbox Worker.cs;[IO.File]::WriteAllText($code,'using System.Threading;class Worker{static void Main(){Thread.Sleep(300000);}}')
$worker=Join-Path $root CodexLiveBackground.exe;& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe "/out:$worker" $code
if($LASTEXITCODE -ne 0){throw 'Worker build failed'}
$node=Join-Path $root 'engine\runtime\node\node.exe';Copy-Item $worker $node
$outside=Join-Path $other node.exe;Copy-Item $worker $outside
$scriptPath=Join-Path $root 'startup\monitor-skin.ps1';[IO.File]::WriteAllText($scriptPath,'Start-Sleep -Seconds 300')
$p1=Start-Process $worker -WindowStyle Hidden -PassThru;$p2=Start-Process $node -WindowStyle Hidden -PassThru;$foreign=Start-Process $outside -WindowStyle Hidden -PassThru
$ps=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe';$p3=Start-Process $ps -ArgumentList ('-NoProfile -NonInteractive -File "'+$scriptPath+'"') -WindowStyle Hidden -PassThru
$tempRoot=Join-Path ([IO.Path]::GetTempPath()) ('CodexBackgroundSetup-'+[guid]::NewGuid().ToString('N'));$null=New-Item -ItemType Directory -Path $tempRoot;$tempScript=Join-Path $tempRoot control.ps1;[IO.File]::WriteAllText($tempScript,'Start-Sleep -Seconds 300');$p4=Start-Process $ps -ArgumentList ('-NoProfile -NonInteractive -File "'+$tempScript+'"') -WindowStyle Hidden -PassThru
$engine=Join-Path $root 'engine\scripts';$null=New-Item -ItemType Directory -Path $engine -Force;$engineScript=Join-Path $engine manager-actions.ps1;[IO.File]::WriteAllText($engineScript,'Start-Sleep -Seconds 300');$p5=Start-Process $ps -ArgumentList ('-NoProfile -NonInteractive -File "'+$engineScript+'"') -WindowStyle Hidden -PassThru
$probe=Join-Path $root ffprobe.exe;Copy-Item $worker $probe;$p6=Start-Process $probe -WindowStyle Hidden -PassThru
try{
 Start-Sleep -Milliseconds 300
 $owned=@(Get-BackgroundOwnedProcesses -Roots @($root) -StateRoot $state -ExcludeIds @($PID))
 foreach($id in @($p1.Id,$p2.Id,$p3.Id,$p4.Id,$p5.Id,$p6.Id)){if($owned.ProcessId -notcontains $id){throw ('Owned process missed: '+$id)}}
 if($owned.ProcessId -contains $foreign.Id){throw 'Unrelated Node selected'}
 Stop-BackgroundOwnedProcesses -Roots @($root) -StateRoot $state -ExcludeIds @($PID)
 foreach($p in @($p1,$p2,$p3,$p4,$p5,$p6)){if(-not $p.WaitForExit(1000)){throw 'Owned process survived'}}
 if($foreign.HasExited){throw 'Unrelated process stopped'}
 @{pass=$true;cases=8;ownedManagerNodeAndControllerStopped=$true;temporaryControllerStopped=$true;engineScriptAndFfprobeStopped=$true;foreignNodePreserved=$true;realCodexTouched=$false}|ConvertTo-Json -Compress
}finally{foreach($p in @($p1,$p2,$p3,$p4,$p5,$p6,$foreign)){if(-not $p.HasExited){$p.Kill();$p.WaitForExit()}};if(-not $tempRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath())+'CodexBackgroundSetup-')){throw 'Temporary boundary mismatch'};Assert-BackgroundInstallPath $tempRoot;Remove-Item -LiteralPath $tempRoot -Recurse -Force}
