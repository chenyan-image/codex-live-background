param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference='Stop'
$runtime=Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime.WindowsRuntime') -Recurse -Filter System.Runtime.WindowsRuntime.dll | Select-Object -First 1
$facade=Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime') -Recurse -Filter System.Runtime.dll | Select-Object -First 1
$references=@('Windows.Foundation.winmd','Windows.Globalization.winmd','Windows.Graphics.winmd','Windows.Media.winmd','Windows.Storage.winmd') | ForEach-Object { '/r:'+(Join-Path (Join-Path $env:WINDIR 'System32\WinMetadata') $_) }
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:library /platform:x64 /codepage:65001 /optimize+ /r:System.Web.Extensions.dll ('/r:'+$runtime.FullName) ('/r:'+$facade.FullName) @references ('/out:'+[IO.Path]::GetFullPath($OutputPath)) (Join-Path $PSScriptRoot 'WindowsOcr.cs')
if($LASTEXITCODE -ne 0){throw 'Windows OCR bridge build failed'}
