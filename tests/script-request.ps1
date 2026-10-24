$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$sandbox=Join-Path $PSScriptRoot ('output\request-'+[guid]::NewGuid().ToString('N'));$null=New-Item -ItemType Directory -Path $sandbox
foreach($file in @('common-windows.ps1','config-utf8.ps1','invoke-script-request.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('..\engine\scripts\'+$file)) -Destination $sandbox}
$echo=Join-Path $sandbox 'echo.ps1'
@'
param([string]$Text,[string]$Json,[switch]$Flag)
@{text=$Text;json=$Json;flag=$Flag.IsPresent}|ConvertTo-Json -Compress
'@|Set-Content -LiteralPath $echo -Encoding UTF8
. (Join-Path $sandbox 'common-windows.ps1')
$text='中文 "quotes" $() `ticks` & literal';$json='{"name":"测试","path":"C:\\a b"}'
$result=Invoke-DreamSkinPowerShellScript -ScriptPath $echo -ArgumentList @('-Text',$text,'-Json',$json,'-Flag') -SwitchParameters @('-Flag')
if($result.ExitCode -ne 0){throw ($result.Output -join "`n")}
$parsed=($result.Output -join "`n")|ConvertFrom-Json
if($parsed.text -cne $text -or $parsed.json -cne $json -or -not $parsed.flag){throw 'JSON data was altered or evaluated as code'}
$request=Join-Path $sandbox 'outside.json';[IO.File]::WriteAllText($request,(@{ScriptPath=(Join-Path $PSScriptRoot 'other.ps1');Parameters=@{}}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$bad=Invoke-DreamSkinNative -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoProfile','-NonInteractive','-ExecutionPolicy','RemoteSigned','-File',(Join-Path $sandbox 'invoke-script-request.ps1'),'-RequestPath',$request)
if($bad.ExitCode -eq 0){throw 'Request escaped the engine script directory'}
@{pass=$true;cases=2;unicodeAndQuotesPreserved=$true;outsideRejected=$true}|ConvertTo-Json -Compress
