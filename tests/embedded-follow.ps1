param([Parameter(Mandatory=$true)][string]$PackageRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot;$out=Join-Path $PSScriptRoot ('output\embedded-'+[guid]::NewGuid().ToString('N'));$root=Join-Path $out 'app with spaces';$state=Join-Path $out state;$startup=Join-Path $root startup;$null=New-Item -ItemType Directory -Path $startup,$state -Force
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe';$worker=Join-Path $startup DreamSkinPortHook.exe;$exe=Join-Path $out EmbeddedFollowTests.exe;$report=Join-Path $out result.json
& $csc /nologo /target:winexe /main:GuardWorkerFixture ("/out:$worker") (Join-Path $PSScriptRoot GuardWorkerFixture.cs)
if($LASTEXITCODE -ne 0){throw 'Guard fixture compile failed'}
& $csc /nologo /target:exe /main:EmbeddedFollowTests /platform:x64 /codepage:65001 ("/out:$exe") /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $repo 'source\SoftwareUpdate.cs') (Join-Path $repo 'source\CodexFollow.cs') (Join-Path $PSScriptRoot EmbeddedFollowTests.cs)
if($LASTEXITCODE -ne 0){throw 'Embedded follow tests failed to compile'}
& $exe $root $state ([IO.Path]::GetFullPath($PackageRoot)) $report
if($LASTEXITCODE -ne 0){throw (Get-Content -LiteralPath $report -Raw)}
Get-Content -LiteralPath $report -Raw
