function Remove-DreamBackgroundSkin {
  param([string]$InstallRoot,[string]$StateRoot,[string]$PackageRoot)
  if(-not (Test-Path -LiteralPath $StateRoot)){Write-Output '当前没有已应用的皮肤。';return}
  try{$null=Invoke-Engine 'Pause'}catch{
    . (Join-Path $PackageRoot 'engine\scripts\theme-windows.ps1')
    $null=Set-DreamSkinPaused -Paused $true -StateRoot $StateRoot
    throw ('已暂停背景恢复，但当前窗口未确认卸下皮肤，请展开 Codex 后重试。原因：'+$_.Exception.Message)
  }
  Write-Output '皮肤已卸下，聊天框恢复原界面。后台服务仍保留；点击“换壁纸”即可重新应用。'
}