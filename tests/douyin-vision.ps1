param([Parameter(Mandatory=$true)][string]$FixtureDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory);if(Test-Path -LiteralPath $out){throw 'OutputDirectory must be new'};$null=New-Item -ItemType Directory -Path $out
$repo=Split-Path -Parent $PSScriptRoot;$bridge=Join-Path $out 'CodexHub.WindowsOcr.dll'
& (Join-Path $repo 'source\build-windows-ocr.ps1') -OutputPath $bridge
$exe=Join-Path $out 'DouyinVisionTests.exe';$icon=Join-Path $repo 'source\assets\douyin\emoji-button.png'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 ('/out:'+$exe) /r:System.Drawing.dll /r:System.Web.Extensions.dll ('/resource:'+$bridge+',CodexHub.WindowsOcr') ('/resource:'+$icon+',CodexHub.DouyinEmoji') (Join-Path $repo 'source\DouyinSpark.cs') (Join-Path $repo 'source\DouyinVision.cs') (Join-Path $PSScriptRoot 'DouyinVisionTests.cs')
if($LASTEXITCODE -ne 0){throw 'Visual fixture build failed'}
& $exe ([IO.Path]::GetFullPath($FixtureDirectory)) $out $icon
if($LASTEXITCODE -ne 0){throw 'Visual fixture failed'}
