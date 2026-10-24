# Codex 动态背景

为 Windows 版 Codex 换上喜欢的图片、GIF 或视频背景。

基于原作者 [xxloocee / Codex-Dream-Skin-Manager](https://github.com/xxloocee/Codex-Dream-Skin-Manager) 和 [Fei-Away / Codex-Dream-Skin](https://github.com/Fei-Away/Codex-Dream-Skin) 修改，主要修复 Bug、精简源码并改善 Windows 使用体验。

**[下载安装包](https://github.com/chenyan-image/codex-live-background/releases/latest) · [更新记录](CHANGELOG.md) · [详细说明](docs/guide.md)**

## 特点

- **4K 视频背景**：优先使用原画质；电脑无法解码时自动生成兼容副本，并按需降低分辨率。
- **图片铺满窗口**：横图、方图、竖图都按比例裁切铺满 Codex，覆盖侧栏和对话区域；聊天框透明度可调。
- **Wallpaper Engine 壁纸库**：自动查找 Steam 库中的视频、GIF 和图片，支持搜索、类型筛选、连续向下滚动和预览。
- **壁纸记录与内置背景**：保存最近使用的壁纸，附带六张原创背景。
- **自动恢复**：服务随 Windows 登录运行，Codex 打开后恢复背景，无需一直开着管理器。
- **便捷管理**：支持旧版升级、卸载皮肤和卸载服务，保留个人素材与壁纸记录。

支持 MP4、WebM、GIF、PNG、JPG、WebP；视频源文件最大 **2 GiB**，最长 **10 分钟**。视频静音循环，原素材不被修改。

## 界面

本地壁纸与预览：

![本地壁纸管理界面](docs/manager.png)

Wallpaper Engine 壁纸库（本机实际扫描），向下连续滚动浏览：

![Wallpaper Engine 连续滚动壁纸库](docs/wallpaper-engine.png)

本地页展示内置原创背景，Wallpaper Engine 页为本机真实壁纸库截图。实际壁纸由用户本机提供。场景项目 `scene.pkg`、网页及应用壁纸只显示可用预览图，不能直接播放原场景。

## 使用

1. 在 [Releases](https://github.com/chenyan-image/codex-live-background/releases/latest) 下载 `.msi`，双击安装；升级前先退出旧管理器。
2. 导入本地素材，或在 Wallpaper Engine 页面选择壁纸。
3. 右侧预览后点击「应用这张壁纸」。关闭管理器，背景仍继续运行。

适用 **Windows 10 / 11 x64 + 官方 Codex 稳定版**。首次开启背景连接时，若提示普通启动实例，请保存输入后手动完全退出 Codex 一次，再应用壁纸。

本工具为非官方项目，与 OpenAI 无隶属关系。当前安装包尚未正式代码签名，Windows 可能提示未知发布者；详见 [发布说明](docs/windows-trust.md)。

[MIT](LICENSE) · [第三方声明](THIRD_PARTY_NOTICES.md) · [构建与开发](docs/guide.md#从源码构建)
