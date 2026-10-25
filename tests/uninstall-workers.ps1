$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\source\uninstall-data.ps1')
Add-Type 'namespace CodexDreamSkin { public static class NativeInspection { public static bool CurrentUser(int id){return id!=4;} public static string ProcessPath(int id){return Paths[id];} public static System.Collections.Generic.Dictionary<int,string> Paths=new System.Collections.Generic.Dictionary<int,string>(); } }'
$install=Join-Path $PSScriptRoot 'output\custom app';$state=Join-Path $PSScriptRoot 'output\fake data'
$paths=@{1=(Join-Path $install 'engine\runtime\node\node.exe');2=(Join-Path $state 'runtime\ffmpeg\ffmpeg.exe');3='C:\unrelated\node.exe';4=(Join-Path $state 'runtime\ffmpeg\ffmpeg.exe');5=(Join-Path $install 'engine\runtime\node\node.exe')}
foreach($id in $paths.Keys){[CodexDreamSkin.NativeInspection]::Paths[$id]=$paths[$id]}
function Initialize-DreamSkinNativeInspection {}
function Test-DreamSkinPathEqual {param($Left,$Right) $Left -ieq $Right}
function Test-DreamSkinCommandLineToken {param($CommandLine,$Token) $CommandLine -ceq $Token}
function Get-CimInstance {param($Class,$Filter)foreach($id in 1..5){[pscustomobject]@{ProcessId=$id;CommandLine=$(if($id -eq 1){Join-Path $install 'engine\scripts\prepare-video.mjs'}else{'unrelated script'})}}}
function Get-Process {param($Id,$ErrorAction) [pscustomobject]@{StartTime=[DateTime]::Now}}
$script:stopped=@()
function Stop-DreamBackgroundWorker {param($Identity) $script:stopped+=@($Identity.ProcessId)}
Stop-DreamBackgroundMediaWorkers -InstallRoot $install -StateRoot $state
if(($script:stopped -join ',') -cne '1,2'){throw 'Owned workers not stopped or unrelated process selected'}
@{pass=$true;cases=5;ownedTranscodersSelected=$true;foreignProcessesPreserved=$true;realProcessesStopped=$false}|ConvertTo-Json -Compress
