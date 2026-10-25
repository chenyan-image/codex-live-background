$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot;$output=Join-Path $PSScriptRoot ('output\taskbar-startup-'+[guid]::NewGuid().ToString('N'));$null=New-Item -ItemType Directory -Path $output
$exe=Join-Path $output IdentityTest.exe;$report=Join-Path $output result.json
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /platform:x64 /codepage:65001 ("/out:$exe") /r:System.Web.Extensions.dll (Join-Path $repo 'startup\ShellIdentity.cs') (Join-Path $PSScriptRoot TaskbarStartupIdentity.cs)
if($LASTEXITCODE -ne 0){throw 'Identity test build failed'}
$p=Start-Process $exe -ArgumentList ('"'+$report+'"') -WindowStyle Hidden -PassThru
if(-not $p.WaitForExit(15000)){throw 'Identity test timed out'}
$data=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;if($p.ExitCode -ne 0 -or -not $data.pass){throw $data.error}
. (Join-Path $repo 'engine\scripts\common-windows.ps1')
# Only substitute target validation for this synthetic executable. The actual
# production direct-launch implementation, argument encoding and helper run.
function Assert-DreamSkinCodexDirectLaunchTarget {param($Codex)}
$child=Join-Path $output direct-child.json;$profile=Join-Path $output 'direct profile'
$target=[pscustomobject]@{Executable=$exe;AppUserModelId='OpenAI.Codex_2p2nqsd0c76g0!App'}
$childId=Start-DreamSkinCodexDirect -Codex $target -Arguments @('--child',$child,'中文 argument',('--user-data-dir='+$profile))
$deadline=[DateTime]::UtcNow.AddSeconds(10);while(-not(Test-Path -LiteralPath $child) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 20}
$direct=[IO.File]::ReadAllText($child)|ConvertFrom-Json
if($direct.title -cne $target.AppUserModelId -or $direct.profile -cne $profile -or $direct.literal -cne '中文 argument'){throw 'Manual connection launch identity mismatch'}
@{pass=$true;canonicalStartupIdentity=$data.canonicalStartupIdentity;unicodeArgumentsAndEnvironment=$true;manualConnectionIdentityMatches=$true;explicitId=$data.explicitId;realCodexStarted=$false}|ConvertTo-Json -Compress
