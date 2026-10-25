# Windows 安装、签名和拦截

v1.5.4.1 使用单文件 Setup EXE，直接部署程序和后台组件，不内嵌 MSI。新装与新版卸载不经过 Windows Installer；已有旧 MSI 时，才通过正式卸载进行一次迁移，必要时请求原账号的 Windows UAC。可选择专用安装目录，默认位于当前用户的 LocalAppData。桌面仅一个管理器图标，完整卸载入口随程序放在安装目录中，也注册到 Windows 的已安装应用。

EXE 先验证内嵌 ZIP 的 SHA-256，再验证清单中每个文件的大小与哈希。旧 MSI 安装源缺失时，只下载本仓库已知历史版本并验证固定哈希。安装与卸载先核验归属并关闭本项目进程，不结束官方 Codex 或其他应用的同名进程。日志和失败恢复文件仅保存在本机。

脚本以明文随源码提供，使用 RemoteSigned，无编码命令或压壳；工具不关闭防病毒、不修改整个盘或 Config.Msi 权限、不禁用 Windows Installer 回滚。授权部署只给当前用户所选的专用程序目录写入权限，避免后台组件继承管理员运行要求。

## Windows 提示

- “Windows 已保护你的电脑”“未知发布者”：与发布者签名及 SmartScreen 信誉有关；更换安装格式不能保证消除提示。
- 具体病毒／PUA 名称或应用控制拦截：请记录检测名称、文件及版本，用公开发行包提交复核。未经调查不能宣称误报。

当前项目 EXE 与脚本**尚未使用正式发布者证书签名**。自带 Node.js 的签名不代表整个产品已签名，不承诺零误报或已通过微软审核。组织策略可能阻止未签名工具运行；不要为了安装关闭防护或添加整盘排除。

微软说明：[SmartScreen 与发布信誉](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)、[开发者检测复核](https://learn.microsoft.com/en-us/defender-xdr/developer-faq)、[提交可疑或误检文件](https://www.microsoft.com/en-us/wdsi/filesubmission)。只提交公开安装包，不附带壁纸、聊天或账号资料；本项目没有代用户提交或取得复核结论。

## 正式代码签名

发布者需要有效的 CA 代码签名证书与私钥，或微软 Artifact Signing 身份。不得用自签名根证书替代发行信任。证书已在本机时，先签内部组件、刷新清单，压缩负载并构建 EXE，最后签署 Setup EXE：

```powershell
.\source\sign-release.ps1 -PackageRoot .\dist\package `
  -CertificateThumbprint <证书指纹> -TimestampServer <证书服务商时间戳地址>
# 按 guide.md 压缩负载并构建 Setup EXE，然后签署最终文件。
.\source\sign-release.ps1 -InstallerPath .\dist\Codex-Background-1.5.4.1-Setup.exe `
  -CertificateThumbprint <证书指纹> -TimestampServer <证书服务商时间戳地址>
```

签署后不再修改安装包字节。脚本拒绝过期、自签名、缺少私钥或不受信任的证书，不导入证书、不关闭安全功能。当前未执行实际发行签名。

## Windows 任务栏

装载服务会备份并修复可以确认目标的官方 Codex 直接启动快捷方式，缓存本机官方图标；既有商店包固定入口通过启动守护处理。只修改已核验入口，不改 Taskband 二进制数据。卸载时恢复未被用户再次修改的快捷方式，并清理旧版额外生成的「Codex（动态背景）」桌面图标。桌面仅保留管理器入口。
