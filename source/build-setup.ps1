param([Parameter(Mandatory=$true)][string]$PackageZip,[Parameter(Mandatory=$true)][string]$OutputFile,[Parameter(Mandatory=$true)][string]$IconFile)
$ErrorActionPreference='Stop'
$zip=[IO.Path]::GetFullPath($PackageZip);$out=[IO.Path]::GetFullPath($OutputFile)
if(Test-Path -LiteralPath $out){throw 'OutputFile must be new.'}
$generated=Join-Path ([IO.Path]::GetDirectoryName($out)) ('SetupPayload-'+[guid]::NewGuid().ToString('N')+'.cs')
$hash=(Get-FileHash -LiteralPath $zip).Hash
[IO.File]::WriteAllText($generated,('internal static class SetupPayload { public const string Sha256="'+$hash+'"; public const string Version="1.7.0.2"; }'),[Text.UTF8Encoding]::new($false))
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
try{
 & $csc /nologo /target:winexe /main:Setup /platform:x64 /codepage:65001 /optimize+ "/out:$out" ("/win32manifest:"+(Join-Path $PSScriptRoot 'app.manifest')) "/win32icon:$IconFile" "/resource:$zip,Codex.Payload.zip" "/resource:$IconFile,Codex.Setup.ico" ("/resource:"+(Join-Path $PSScriptRoot 'assets\brand\codexhub.png')+",CodexHub.Brand") ("/resource:"+(Join-Path $PSScriptRoot 'assets\sounds\click.wav')+",CodexHub.ClickSound") /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Accessibility.dll (Join-Path $PSScriptRoot 'Setup.cs') (Join-Path $PSScriptRoot 'Manager.cs') (Join-Path $PSScriptRoot 'IconAssets.cs') (Join-Path $PSScriptRoot 'WallpaperLibrary.cs') (Join-Path $PSScriptRoot 'GalleryControls.cs') (Join-Path $PSScriptRoot 'GalleryManager.cs') (Join-Path $PSScriptRoot 'MediaPreviewJob.cs') (Join-Path $PSScriptRoot 'CompanionCore.cs') (Join-Path $PSScriptRoot 'WindowResize.cs') (Join-Path $PSScriptRoot 'CompanionBehavior.cs') (Join-Path $PSScriptRoot 'CompanionVideo.cs') (Join-Path $PSScriptRoot 'CompanionOverlay.cs') (Join-Path $PSScriptRoot 'CompanionSettings.cs') (Join-Path $PSScriptRoot 'CompanionQuota.cs') (Join-Path $PSScriptRoot 'CompanionWindow.cs') (Join-Path $PSScriptRoot 'CompanionPage.cs') (Join-Path $PSScriptRoot 'CompanionActivity.cs') (Join-Path $PSScriptRoot 'CompanionBailong.cs') (Join-Path $PSScriptRoot 'CompanionTokens.cs') (Join-Path $PSScriptRoot 'HubPopupMenu.cs') $generated
 if($LASTEXITCODE -ne 0){throw 'Single EXE installer compilation failed.'}
}finally{Remove-Item -LiteralPath $generated -Force -ErrorAction SilentlyContinue}
Write-Output $out
