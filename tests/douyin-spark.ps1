param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'OutputDirectory must be new'}
$null=New-Item -ItemType Directory -Path $out
$repo=Split-Path -Parent $PSScriptRoot
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf=Join-Path (Split-Path -Parent $csc) 'WPF'
$exe=Join-Path $out 'DouyinSparkTests.exe'
& $csc /nologo /target:exe /platform:x64 /codepage:65001 /optimize+ "/out:$exe" "/lib:$wpf" /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:WindowsBase.dll "/r:$repo\source\assets\windows\Interop.UIAutomationClient.dll" "/resource:$repo\source\assets\windows\Interop.UIAutomationClient.dll,CodexHub.UIAutomationInterop" /r:UIAutomationClient.dll /r:UIAutomationTypes.dll (Join-Path $repo 'source\DouyinSpark.cs') (Join-Path $repo 'source\DouyinAccessibility.cs') (Join-Path $repo 'source\DouyinWorker.cs') (Join-Path $repo 'source\DouyinLauncher.cs') (Join-Path $repo 'source\DouyinNativeUia.cs') (Join-Path $PSScriptRoot 'DouyinSparkTests.cs')
if($LASTEXITCODE -ne 0){throw 'Douyin fixture compilation failed'}
& $exe $out
if($LASTEXITCODE -ne 0){throw 'Douyin fixture failed'}
Get-Content -LiteralPath (Join-Path $out 'result.json') -Raw
