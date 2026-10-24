# Windows 安装、签名和拦截

v1.5.1 主安装文件采用 Windows 原生 MSI，单文件下载后双击安装。当前用户安装，不要求管理员权限；Windows 设置的已安装应用中可查看、修复或卸载。1.0.x 无完整版本字段的历史程序用原始发布 SHA-256 识别；无旧版则全新安装。用户壁纸和登录资料不作为安装文件覆盖。

新版不使用自制 EXE 解压器作为主安装器，不启动编码的 PowerShell 命令，子脚本使用 RemoteSigned，不重置 Windows 的 StartupApproved 禁用记录。仅使用一个当前用户登录计划任务，保留官方 Codex 包激活入口的恢复能力。用户原有防病毒、应用控制、证书信任及组织策略不变。

## 两种不同提示

- “Windows 已保护你的电脑”“未知发布者”：涉及 SmartScreen、应用信誉、发布者签名。自签名或单纯重新打包不能建立受信任发布者身份。新版本即使签名有效仍可能需要积累信誉。
- 具体病毒/PUA 名称或应用控制拦截：需要记录检测名称、被拦截文件及发行版本。未经调查不能直接宣称误报；微软及其他安全软件的最终判定由其维护。请勿为安装而关闭防护或添加整盘排除。

本机未找到可用的 CA 代码签名证书，Defender 检测提供程序无法读取。因此当前公开包必须明确视为**项目程序未签名**，没有“微软审核通过”“零误报”保证。自带 Node.js 的签名不代表整个产品已签名。

微软说明：[SmartScreen 与发布信誉](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)、[开发者检测复核](https://learn.microsoft.com/en-us/defender-xdr/developer-faq)、[提交可疑或误检文件](https://www.microsoft.com/en-us/wdsi/filesubmission)。提交时仅使用公开发行包，不附带壁纸、聊天或账号目录；本项目没有代用户提交、也没有收到复核结论。

## 正式代码签名

发布者先取得有效 CA 代码签名证书或完成微软 Artifact Signing 的身份验证。不得用自签名根证书替代发行信任；私钥无需提交到仓库。

在本机证书存储中已有证书及私钥时，按顺序操作：

```powershell
# 先构建目录，然后签署内部 EXE 和 PowerShell 脚本，刷新校验表。
.\source\sign-release.ps1 -PackageRoot .\dist\package `
  -CertificateThumbprint <证书指纹> -TimestampServer <证书服务商时间戳地址>
# 只接受有效签名的内部组件来构建 MSI。
.\source\build-msi.ps1 -PackageRoot .\dist\package `
  -OutputFile .\dist\Codex-Background-win-x64.msi -RequireTrustedSignature
# 最后签署 MSI，之后不再修改安装包字节。
.\source\sign-release.ps1 -InstallerPath .\dist\Codex-Background-win-x64.msi `
  -CertificateThumbprint <证书指纹> -TimestampServer <证书服务商时间戳地址>
```

脚本会拒绝过期、自签名、缺少私钥或链不可信的证书，逐文件验证签名；不会导入证书或关闭安全功能。该流程已提供代码，当前没有真实发布者证书，尚未执行实际签名验证。

## Windows 任务栏

装载服务会备份并修复桌面、开始菜单、Quick Launch 和传统任务栏目录内能够识别的官方 Codex 直接启动快捷方式，同时创建 `Codex（动态背景）` 快捷方式。外观仍打开官方 Codex，后台管理器不会随之弹出。只改写可确认目标、没有额外深链接语义的入口；其他应用和用户后来修改的快捷方式保留。

商店应用固定图标也由现有包激活守护处理。若某个旧固定项没有可修改的快捷方式且仍未恢复背景，可把桌面 `Codex（动态背景）` 固定到任务栏。程序不修改未公开的 Taskband 二进制数据。卸载服务时恢复未被用户再次修改的原快捷方式。
