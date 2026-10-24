$ErrorActionPreference='Stop'
$env:PSModulePath=Join-Path $PSHOME 'Modules'
$package=Get-AppxPackage -Name OpenAI.Codex
if(-not $package -or $package.SignatureKind -ne 'Store' -or $package.Status -ne 'Ok' -or $package.IsDevelopmentMode -or $package.Publisher -cne 'CN=50BDFD77-8903-4850-9FFE-6E8522F64D5B' -or $package.PackageFullName -cnotmatch '^OpenAI\.Codex_\d+\.\d+\.\d+\.\d+_x64__2p2nqsd0c76g0$'){throw 'Verified official Codex registration unavailable.'}
[Console]::Write($package.PackageFullName)
