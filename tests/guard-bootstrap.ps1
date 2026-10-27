param([Parameter(Mandatory=$true)][string]$PackageRoot)
$ErrorActionPreference='Stop';$repo=Split-Path -Parent $PSScriptRoot;$out=Join-Path $PSScriptRoot ('output\guard-bootstrap-'+[guid]::NewGuid().ToString('N'));$sandbox=Join-Path ([IO.Path]::GetTempPath()) ('HubGB-'+[guid]::NewGuid().ToString('N').Substring(0,10));$root=Join-Path $sandbox app;$state=Join-Path $sandbox state;$startup=Join-Path $root startup;$null=New-Item -ItemType Directory -Path $startup,$state,$out -Force
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe';$exe=Join-Path $sandbox GuardBootstrapTests.exe;$report=Join-Path $out result.json
& $csc /nologo /target:winexe /main:GuardWorkerFixture ('/out:'+(Join-Path $startup DreamSkinPortHook.exe)) (Join-Path $PSScriptRoot GuardWorkerFixture.cs)
if($LASTEXITCODE -ne 0){throw 'Guard fixture compile failed'}
& $csc /nologo /target:exe /main:GuardBootstrapTests /platform:x64 /codepage:65001 ("/out:$exe") /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $repo 'source\SoftwareUpdate.cs') (Join-Path $repo 'source\CodexFollow.cs') (Join-Path $PSScriptRoot GuardBootstrapTests.cs)
if($LASTEXITCODE -ne 0){throw 'Guard bootstrap tests failed to compile'}
& $exe $root $state ([IO.Path]::GetFullPath($PackageRoot)) $report
if($LASTEXITCODE -ne 0){throw (Get-Content -LiteralPath $report -Raw)}
Get-Content -LiteralPath $report -Raw
