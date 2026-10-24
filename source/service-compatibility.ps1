function Get-DreamBackgroundServicePlan {
  param($Task, [string]$StartupRoot, [string]$ProfilesPath)
  if ($null -eq $Task) { return [pscustomobject]@{ Mode='new'; Profile='new' } }
  $expected = [IO.Path]::GetFullPath((Join-Path $StartupRoot 'DreamSkinPortHook.exe'))
  $actions = @($Task.Actions)
  if ($actions.Count -ne 1 -or
      [IO.Path]::GetFullPath("$($actions[0].Execute)") -ine $expected -or
      "$($actions[0].Arguments)".Trim() -cne '--guard') {
    throw '同名计划任务不属于兼容的 Dream Skin 启动入口，已保留原任务。请检查任务计划程序中的 CodexDreamSkinPort。'
  }
  $owner = "$($Task.Principal.UserId)"
  $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
  try {
    if ($owner -notmatch '^S-1-') { $owner = ([Security.Principal.NTAccount]::new($owner)).Translate([Security.Principal.SecurityIdentifier]).Value }
  } catch { throw '无法确认旧启动任务属于当前用户，已保留原设置。' }
  if ($owner -ine $sid -or "$($Task.Principal.LogonType)" -ne 'Interactive') {
    throw '旧启动任务不属于当前用户的交互会话，已保留原设置。'
  }
  $hashes = @{}
  foreach ($name in @('DreamSkinPortHook.exe','monitor-skin.ps1','register-port.ps1')) {
    $path = Join-Path $StartupRoot $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('旧启动组件不完整：' + $name) }
    $hashes[$name] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
  }
  $profiles = [IO.File]::ReadAllText($ProfilesPath) | ConvertFrom-Json
  foreach ($profile in $profiles.profiles) {
    $matches = $true
    foreach ($name in $hashes.Keys) { if ($hashes[$name] -cne "$($profile.hashes.$name)") { $matches=$false; break } }
    if ($matches) { return [pscustomobject]@{Mode='reuse'; Profile="$($profile.name)"} }
  }
  throw '旧启动组件与已验证版本不一致，暂未自动接管；背景和原服务已保留。请保留诊断信息以便适配该版本。'
}

function Wait-DreamBackgroundService {
  param([string]$StartupRoot, [int]$Seconds=20)
  Initialize-DreamSkinNativeInspection
  $expected = Join-Path $StartupRoot 'DreamSkinPortHook.exe'
  $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
  do {
    try {
      $guard = [IO.File]::ReadAllText((Join-Path $StartupRoot 'guard-state.json')) | ConvertFrom-Json
      if ($guard.registered -and $guard.pid -and
          [CodexDreamSkin.NativeInspection]::CurrentUser([int]$guard.pid) -and
          (Test-DreamSkinPathEqual -Left ([CodexDreamSkin.NativeInspection]::ProcessPath([int]$guard.pid)) -Right $expected)) { return $guard }
    } catch { }
    Start-Sleep -Milliseconds 250
  } while ([DateTime]::UtcNow -lt $deadline)
  throw '未能确认背景启动服务正在运行。请查看 CodexDreamSkin\startup\startup.log，然后重试启用。'
}

function Copy-DreamBackgroundPackage {
  param([string]$SourceRoot,[string]$DestinationRoot)
  foreach ($name in @('CodexLiveBackground.exe','CodexBackground.ico','control.ps1','autostart.ps1','skin-removal.ps1','service-removal.ps1','upgrade.ps1','service-upgrade.ps1','service-compatibility.ps1','service-compatibility.json','engine','startup','LICENSE','THIRD_PARTY_NOTICES.md','THIRD_PARTY','使用说明.txt')) {
    $source = Join-Path $SourceRoot $name
    $files = if (Test-Path -LiteralPath $source -PathType Container) { @(Get-ChildItem -LiteralPath $source -Recurse -File) } else { @(Get-Item -LiteralPath $source) }
    foreach ($file in $files) {
      $relative = $file.FullName.Substring($SourceRoot.TrimEnd('\').Length+1)
      $destination = Join-Path $DestinationRoot $relative
      if ((Test-Path -LiteralPath $destination -PathType Leaf) -and
          (Get-FileHash -LiteralPath $destination).Hash -eq (Get-FileHash -LiteralPath $file.FullName).Hash) { continue }
      New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
      Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
  }
}
