[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$EngineSource,
  [Parameter(Mandatory=$true)][string]$NodeExecutable,
  [Parameter(Mandatory=$true)][string]$NodeLicense,
  [Parameter(Mandatory=$true)][string]$PortSource,
  [Parameter(Mandatory=$true)][string]$ThirdPartySource,
  [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'OutputDirectory must be new; build does not delete existing files.' }
New-Item -ItemType Directory -Path $out | Out-Null
$engine = Join-Path $out 'engine'
New-Item -ItemType Directory -Path $engine | Out-Null
foreach ($folder in @('scripts','assets')) {
  $destination = Join-Path $engine $folder
  New-Item -ItemType Directory -Path $destination | Out-Null
  Get-ChildItem -LiteralPath (Join-Path $EngineSource $folder) -File | Where-Object { $_.Name -ne 'play-startup-animation.mjs' } | Copy-Item -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $EngineSource 'VERSION') -Destination $engine
function New-NeutralImage([string]$Path) {
  $bitmap = New-Object Drawing.Bitmap 1280,720
  $graphics = [Drawing.Graphics]::FromImage($bitmap)
  $brush = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.Point]::new(0,0)),([Drawing.Point]::new(1280,720)),([Drawing.Color]::FromArgb(20,34,60)),([Drawing.Color]::FromArgb(83,110,132))
  try { $graphics.FillRectangle($brush,0,0,1280,720); $bitmap.Save($Path,[Drawing.Imaging.ImageFormat]::Jpeg) }
  finally { $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
New-NeutralImage (Join-Path $engine 'assets\dream-reference.jpg')
$preset = Join-Path $engine 'presets\preset-gothic-void-crusade'
New-Item -ItemType Directory -Path $preset -Force | Out-Null
$theme = Get-Content -LiteralPath (Join-Path $EngineSource 'presets\preset-gothic-void-crusade\theme.json') -Raw | ConvertFrom-Json
$theme.name = 'Neutral Gradient'
$theme.tagline = 'A locally generated neutral gradient.'
$theme.statusText = 'READY'
$theme.promoSub = 'Neutral Gradient'
[IO.File]::WriteAllText((Join-Path $preset 'theme.json'), ($theme | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
New-NeutralImage (Join-Path $preset 'background.jpg')
$nodeRoot = Join-Path $engine 'runtime\node'
New-Item -ItemType Directory -Path $nodeRoot -Force | Out-Null
Copy-Item -LiteralPath $NodeExecutable -Destination (Join-Path $nodeRoot 'node.exe')
Copy-Item -LiteralPath $NodeLicense -Destination (Join-Path $nodeRoot 'LICENSE')
$signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $nodeRoot 'node.exe')
if ($signature.Status -ne 'Valid') { throw ('Bundled Node.js signature is invalid: ' + $signature.Status) }
foreach ($file in @('control.ps1','使用说明.txt')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $out }
$sourceOut = Join-Path $out 'source'
New-Item -ItemType Directory -Path $sourceOut | Out-Null
foreach ($file in @('Manager.cs','control.ps1','使用说明.txt','build.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $sourceOut }
$port = Join-Path $out 'startup'
New-Item -ItemType Directory -Path $port | Out-Null
Copy-Item -LiteralPath (Join-Path $PortSource 'DreamSkinPortHook.cs'),(Join-Path $PortSource 'register-port.ps1') -Destination $port
$monitor = [IO.File]::ReadAllText((Join-Path $PortSource 'monitor-skin.ps1'))
$monitor = [regex]::Replace($monitor, '(?m)^\$install = .*$', '$install = Join-Path $env:LOCALAPPDATA ''Programs\CodexLiveBackground''')
$monitor = $monitor.Replace("'windows\scripts'", "'engine\scripts'")
[IO.File]::WriteAllText((Join-Path $port 'monitor-skin.ps1'), $monitor, [Text.UTF8Encoding]::new($true))
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /platform:x64 /codepage:65001 /optimize+ "/out:$out\CodexLiveBackground.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $PSScriptRoot 'Manager.cs')
if ($LASTEXITCODE -ne 0) { throw 'Manager compilation failed.' }
& $csc /nologo /target:winexe /platform:x64 /codepage:65001 /optimize+ "/out:$port\DreamSkinPortHook.exe" (Join-Path $port 'DreamSkinPortHook.cs')
if ($LASTEXITCODE -ne 0) { throw 'Startup guard compilation failed.' }
$repoRoot = Split-Path -Parent $EngineSource
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE'),(Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $out
$thirdParty = Join-Path $out 'THIRD_PARTY'
New-Item -ItemType Directory -Path $thirdParty | Out-Null
Copy-Item -LiteralPath (Join-Path $ThirdPartySource 'Codex-Dream-Skin') -Destination $thirdParty -Recurse
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $thirdParty 'Dream-Skin-Manager-LICENSE')
Copy-Item -LiteralPath $NodeLicense -Destination (Join-Path $thirdParty 'Node-LICENSE')
@'
This distribution adds a Windows launcher, startup-port guard, no-close behavior,
visible-thread selector repairs, and theme recovery to the MIT-licensed engine.
Source: https://github.com/xxloocee/Codex-Dream-Skin-Manager
Upstream: https://github.com/Fei-Away/Codex-Dream-Skin (MIT)
Original user wallpapers and original bundled preset media are excluded.
Both shipped JPEGs are locally generated neutral gradients.
The OpenAI application, trademarks and accounts are not included.
'@ | Set-Content -LiteralPath (Join-Path $thirdParty 'MODIFICATIONS.txt') -Encoding UTF8
# Windows PowerShell 5.1 requires BOM for scripts containing Chinese strings.
Get-ChildItem -LiteralPath $out -Recurse -File -Filter '*.ps1' | ForEach-Object {
  $content = [IO.File]::ReadAllText($_.FullName)
  [IO.File]::WriteAllText($_.FullName, $content, [Text.UTF8Encoding]::new($true))
}
$entries = @(Get-ChildItem -LiteralPath $out -Recurse -File | ForEach-Object {
  [ordered]@{path=$_.FullName.Substring($out.Length+1);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
[ordered]@{version='1.0.0';platform='windows-x64';files=$entries} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'manifest.json') -Encoding UTF8
Write-Output $out
