param([Parameter(Mandatory=$true)][string]$MsiPath,[Parameter(Mandatory=$true)][string]$OutputFile)
$ErrorActionPreference='Stop'
$msi=[IO.Path]::GetFullPath($MsiPath);$out=[IO.Path]::GetFullPath($OutputFile)
if(Test-Path -LiteralPath $out){throw 'OutputFile must be new'}
if([IO.Path]::GetFileName($msi) -cne 'Codex-Background-1.6.2-win-x64.msi'){throw 'Unexpected MSI version'}
$hash=(Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash
$generated=Join-Path ([IO.Path]::GetDirectoryName($out)) ('AdminInstallPayload-'+[guid]::NewGuid().ToString('N')+'.cs')
$source='internal static class AdminInstallPayload { public const string FileName="'+[IO.Path]::GetFileName($msi)+'"; public const string Sha256="'+$hash+'"; }'
[IO.File]::WriteAllText($generated,$source,[Text.UTF8Encoding]::new($false))
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /platform:x64 /codepage:65001 /optimize+ ("/win32manifest:"+(Join-Path $PSScriptRoot 'app.manifest')) ("/win32icon:"+(Join-Path $PSScriptRoot 'assets\app.ico')) "/out:$out" /r:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'AdminInstall.cs') $generated
if($LASTEXITCODE -ne 0){throw 'Authorized install launcher compilation failed'}
Write-Output $out
