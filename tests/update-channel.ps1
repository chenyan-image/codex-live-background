param([string]$Repo=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
$out=Join-Path ([IO.Path]::GetTempPath()) ('HubNet-'+[guid]::NewGuid().ToString('N').Substring(0,10));$null=New-Item -ItemType Directory -Path $out
$exe=Join-Path $out UpdateChannelTests.exe
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /main:UpdateChannelTests /platform:x64 /codepage:65001 ('/out:'+$exe) /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $Repo 'source\SoftwareUpdate.cs') (Join-Path $Repo 'source\CodexFollow.cs') (Join-Path $PSScriptRoot UpdateChannelTests.cs)
if($LASTEXITCODE -ne 0){throw 'Update channel tests failed to compile'}
& $exe (Join-Path $out fixture)
if($LASTEXITCODE -ne 0){throw 'Update channel tests failed'}
Write-Output ('Report: '+(Join-Path $out 'fixture\result.json'))
