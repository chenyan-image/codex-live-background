# Codex 动态背景

> **原项目的 Bug 修复与源码精简版。** 原始软件由 [xxloocee / Codex-Dream-Skin-Manager](https://github.com/xxloocee/Codex-Dream-Skin-Manager) 提供；底层皮肤引擎来自 [Fei-Away / Codex-Dream-Skin](https://github.com/Fei-Away/Codex-Dream-Skin)。本仓库主要修复使用中遇到的 Bug、简化管理器与源文件并补充 Windows 兼容处理，不将原项目及其主要功能标为我们原创。原始版权和许可证均予以保留。

`codex-live-background` 为 Windows 上的官方 Codex 稳定版添加本地图片、GIF 和视频背景，支持静音循环与登录后自动恢复。

这是基于开源 Dream Skin 引擎的**非官方定制工具**，与 OpenAI 不存在隶属、认可或赞助关系。它不会修改 Codex 的应用安装文件。OpenAI、Codex 和 ChatGPT 的名称、商标及相关权利不属于本项目的 MIT 授权范围。

![管理器界面](docs/manager.png)

## 适用环境

- Windows 10 / 11，x64。
- 当前 Windows 用户已安装官方 Codex 稳定版商店包 `OpenAI.Codex`。
- 不支持未验证的 Beta 包、ChatGPT Classic、网页端、macOS、Linux 或 ARM64。
- 发布包自带经过签名验证的 Node.js，无需单独安装 Node.js 或 Python。

Codex 后续更新可能改变界面或启动行为；出现不兼容时请使用本仓库较新的发布版本。

## 使用

从本仓库 **[Releases](https://github.com/chenyan-image/codex-live-background/releases/latest)** 下载 Windows x64 ZIP，完整解压到可写目录，打开 `CodexLiveBackground.exe`。

界面只有两个按钮：

| 按钮 | 作用 |
| --- | --- |
| 装载服务 | 安装或修复后台服务，设置当前用户登录 Windows 后自动恢复背景，并创建桌面入口。 |
| 换壁纸 | 选择本地素材，自动连接 Codex、验证兼容性并应用背景。 |

首次使用先点一次「装载服务」，之后直接点「换壁纸」。关闭管理器不会停止后台服务，登录后也不会自动弹出管理器窗口。产品名称为「Codex 动态背景」。

已有背景连接的 Codex 窗口可以直接更换壁纸。若首次启用时 Codex 是普通启动实例，请先保存未发送的输入，手动完全退出 Codex 一次，再点「换壁纸」。工具不会强制关闭或重启聊天。独立背景配置首次使用时可能需要重新登录。

## 素材与兼容转换

| 素材 | 支持范围 |
| --- | --- |
| MP4 | 标准非分片 H.264 / AVC 或 H.265 / HEVC，能否直接播放由当前电脑上的 Codex 实际解码验证决定。 |
| WebM | 单视频轨、无音轨的 VP8 / VP9；兼容副本自动使用 VP9。 |
| 图片和动画 | GIF、PNG、JPG / JPEG、WebP。 |
| 大小 | 每个素材最大 512 MiB。 |
| 视频时长与帧率 | 最长 10 分钟，最高 120 FPS。 |
| 像素尺寸 | 单边不超过 16384 像素，总像素不超过 50 MP。 |
| 图片动画 | 最多 300 帧；该限制不用于计算视频帧数。 |

动态素材静音循环播放。素材会保存副本，移动原文件不影响已保存的背景。

当文件结构合规、但当前 Codex 确实不能解码 MP4 时，程序自动生成 **VP9 WebM 兼容副本**。转换保留完整时长，关闭音频，长边最高 1920 像素，最高 30 FPS，以降低不同电脑的解码负担；能够直接播放的视频保留原画质。副本必须再次通过文件结构检查和当前 Codex 的真实解码帧验证后才应用。兼容副本会缓存，重复选择相同素材无需重新转换。

播放权限被拒绝、播放中断或窗口隐藏会分别提示，不会误当作编码问题转换视频。导入失败时保留现有壁纸，原文件始终不变。

### 首次转换需要的下载

首次需要兼容转换时，程序从 **BtbN/FFmpeg-Builds** 的固定发行下载约 **74 MiB** 的 LGPL shared Windows x64 运行时 ZIP，核对固定 SHA-256 后仅解压白名单组件。之后复用本机运行时。项目发布 ZIP 不包含 FFmpeg 二进制，也不会上传视频。

离线电脑可在另一台电脑下载同一个原始 ZIP，将它原封不动放在目标电脑的：

```text
%LOCALAPPDATA%\CodexDreamSkin\runtime-downloads\ffmpeg-n9.0.2-17-g2a571b6068-win64-lgpl-shared-9.0.zip
```

不需要手动解压或改名。软件仍会验证同一 SHA-256。精确下载地址、校验值、许可及对应源码见 [FFmpeg 运行时说明](THIRD_PARTY/FFmpeg-Runtime/README.md) 和 `runtime-download.json`。长视频首次转换可能需要几分钟，进度显示在管理器中。

## 自启动、文件位置与诊断

| 内容 | 当前用户目录或入口 |
| --- | --- |
| 程序安装位置 | `%LOCALAPPDATA%\Programs\CodexLiveBackground` |
| 背景、兼容副本和运行时 | `%LOCALAPPDATA%\CodexDreamSkin` |
| 后台日志 | `%LOCALAPPDATA%\CodexDreamSkin\startup` |
| 最近的视频诊断 | `%LOCALAPPDATA%\CodexDreamSkin\last-video-diagnostic.json` |
| 登录启动入口 | `CodexDreamSkinPort` 计划任务及同名 HKCU Run 后备项 |

两个登录入口共用互斥锁，只运行一份后台服务。「装载服务」会检查真实监控心跳，并备份、升级可识别的旧组件。连接仅使用本机 `127.0.0.1:9335`，无需开放公网防火墙规则。

视频诊断用于区分播放策略、格式与解码问题，仅保存在本机，不读取聊天内容，也不会自动发送。提交 Issue 前请检查并删除不愿公开的文件名、路径和其他个人信息。

## 升级与卸载

升级时关闭旧管理器，把新版 ZIP 解压到新文件夹，打开新版并点一次「装载服务」。兼容升级会保留已有壁纸和背景配置。

需要卸载时，在 Windows PowerShell 中运行：

```powershell
& "$env:LOCALAPPDATA\Programs\CodexLiveBackground\control.ps1" -Action Remove
```

卸载会停用背景并移除后台启动与桌面入口。关闭管理器后可手动删除程序目录。用户素材、日志和独立背景登录资料仍保留在 `CodexDreamSkin` 用户目录；需要删除时请先自行备份。

## 从源码构建

源码布局：

```text
source/       Windows 管理器、控制脚本和构建脚本
engine/       Dream Skin 引擎及本项目的兼容性修改
startup/      本地端口启动钩子与监控源码
THIRD_PARTY/  第三方许可证、来源和运行时下载配置
```

构建需要 Windows x64、Windows PowerShell 5.1、系统 .NET Framework C# 编译器，以及来自官方发行且签名有效的 Node.js x64 22 或更高版本及其 LICENSE。以下变量填写本地 Node.js 文件位置：

```powershell
$nodeExe = Read-Host 'Node.js node.exe 的完整路径'
$nodeLicense = Read-Host 'Node.js LICENSE 的完整路径'
.\source\build.ps1 `
  -EngineSource .\engine `
  -NodeExecutable $nodeExe `
  -NodeLicense $nodeLicense `
  -PortSource .\startup `
  -ThirdPartySource .\THIRD_PARTY `
  -OutputDirectory .\dist\Codex-Background-local-win-x64
```

输出目录必须是尚不存在的新目录。构建会生成中性渐变示例图，不打包个人壁纸、账号凭据、聊天记录或机器配置。

## 验证源码

使用 Node.js 22 或更高版本，在 Windows 下运行：

~~~powershell
node tests/video-playback-regression.mjs
node tests/webm-validation.mjs
~~~

这些回归不连接 Codex，也不更改当前壁纸；测试媒体是 FFmpeg 生成的小尺寸图案。

## 分支和版本

- `main`：稳定主线，正式版本由 `vX.Y.Z` 标签标记。
- `develop`：下一版本的集成开发分支。
- `feat/<名称>`、`fix/<名称>`：独立功能和修复分支。
- 每个完成的修改都使用独立、明确命名的提交和带说明的 `change/<序号>-<短名>` 标签，方便回溯。

历史功能见 [CHANGELOG](CHANGELOG.md)，参与开发前请读 [CONTRIBUTING](CONTRIBUTING.md)。从旧发布包恢复的历史标签对应实际快照，不代表凭空补造旧提交日期。

## 来源和许可

本项目的软件代码按 [MIT License](LICENSE) 分发，并保留以下项目的版权和许可声明：

- [Codex Dream Skin Manager](https://github.com/xxloocee/Codex-Dream-Skin-Manager)：Windows 管理器与引擎整合的来源。
- [Codex Dream Skin](https://github.com/Fei-Away/Codex-Dream-Skin)：背景注入引擎，MIT。
- [Node.js](https://nodejs.org/)：发布包中的脚本运行时，许可随包保留。
- [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds)：可选下载的 LGPL shared 运行时，用于生成 VP9 兼容副本。

详细说明见 [第三方声明](THIRD_PARTY_NOTICES.md) 和 `THIRD_PARTY`。软件许可证不授予用户壁纸、第三方图片、人物肖像、角色、商标或 OpenAI 应用本身的使用和再分发权；请仅使用自己有权使用的素材。
