# Codex 动态背景

为 Windows 版 Codex 换上喜欢的图片、GIF 或视频背景。

基于原作者 [xxloocee / Codex-Dream-Skin-Manager](https://github.com/xxloocee/Codex-Dream-Skin-Manager) 和 [Fei-Away / Codex-Dream-Skin](https://github.com/Fei-Away/Codex-Dream-Skin) 修改，主要修复 Bug、精简源码并改善 Windows 使用体验。

**[下载安装包](https://github.com/chenyan-image/codex-live-background/releases/latest) · [更新记录](CHANGELOG.md) · [详细说明](docs/guide.md)**

## 特点

- **4K 视频背景**：优先使用原画质；电脑无法解码时自动生成兼容副本，并按需降低分辨率。
- **图片铺满窗口**：横图、方图、竖图都按比例裁切铺满 Codex，覆盖侧栏和对话区域；聊天框透明度可调。
- **Wallpaper Engine 壁纸库**：自动查找 Steam 库中的视频、GIF 和图片，支持搜索、类型筛选、连续向下滚动和预览。
- **壁纸记录与内置背景**：保存最近使用的壁纸，内置六张轻量原创背景，可导入自己的图片和视频。
- **自动恢复**：服务随 Windows 登录运行，Codex 打开后恢复背景，无需一直开着管理器。左下角可点击主动连接，管理器打开后也会自动重试。
- **便捷管理**：支持旧版升级、卸载皮肤和卸载服务，保留个人素材与壁纸记录。

## 支持格式

| 类型 | 文件格式 | 支持与处理方式 |
| --- | --- | --- |
| 视频 | **MP4** | 支持 H.264 / AVC、H.265 / HEVC；AV1、分片 MP4 等需要兼容处理的输入会先转换，再验证实际解码。 |
| 视频 | **WebM** | 支持 VP8 / VP9 输入，需要时生成 VP9 兼容副本。 |
| 动图 | **GIF** | 循环播放，可直接作为动态背景。 |
| 图片 | **PNG** | 按比例裁切铺满窗口，保留聊天框透明度设置。 |
| 图片 | **JPG / JPEG** | 支持横图、方图和竖图，自动适配窗口。 |
| 图片 | **WebP** | 可导入、预览并应用为图片背景。 |
| Wallpaper Engine | **视频 / GIF / 图片项目** | 自动扫描 Steam 库，也可手动定位目录；支持搜索、筛选和预览。 |
| Wallpaper Engine | **scene.pkg / 网页 / 应用项目** | 有预览图时可使用预览图，原场景动画、网页和程序不能直接播放。 |

| 项目 | 范围与限制 |
| --- | --- |
| **4K 视频** | 原画质优先；需要兼容转换时，先保留源尺寸（长边最高 4096），再按需降至长边 2560、1920 像素。 |
| 文件大小 | 视频源文件最大 **2 GiB**，图片最大 **512 MiB**；最终播放副本最大 **512 MiB**。 |
| 视频时长与帧率 | 最长 **10 分钟**，输入最高 **120 FPS**；兼容副本最高 **30 FPS**。 |
| 分辨率与动画 | 单边最高 **16384 像素**，总像素最高 **50 MP**；图片动画最多 **300 帧**。 |
| 播放方式 | 视频静音循环；转换保留完整时长，原素材不被修改。 |

首次生成视频预览或兼容副本时，需要下载并校验 FFmpeg 运行时，之后复用本机缓存。[兼容与离线使用说明](docs/guide.md#首次转换需要的下载)

## 界面

本地壁纸与预览：

![本地壁纸管理界面](docs/manager.png)

Wallpaper Engine 壁纸库（本机实际扫描），向下连续滚动浏览：

![Wallpaper Engine 连续滚动壁纸库](docs/wallpaper-engine.png)

安装包仅附六张轻量原创背景。Wallpaper Engine 页来自本机真实壁纸库，截图中的第三方素材需从自己的电脑导入或扫描；场景项目只能使用预览图。

## 使用

1. 在 [Releases](https://github.com/chenyan-image/codex-live-background/releases/latest) 下载 `.msi`，双击安装；升级前先退出旧管理器。
2. 导入本地素材，或在 Wallpaper Engine 页面选择壁纸。
3. 右侧预览后点击「应用这张壁纸」。关闭管理器，背景仍继续运行。

适用 **Windows 10 / 11 x64 + 官方 Codex 稳定版**。首次开启背景连接时，若提示普通启动实例，请保存输入后手动完全退出 Codex 一次，再应用壁纸。

本工具为非官方项目，与 OpenAI 无隶属关系。当前安装包尚未正式代码签名，Windows 可能提示未知发布者；详见 [发布说明](docs/windows-trust.md)。

[MIT](LICENSE) · [第三方声明](THIRD_PARTY_NOTICES.md) · [构建与开发](docs/guide.md#从源码构建)
