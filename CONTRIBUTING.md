# 开发与发布约定

4K 与卸载验证：在 Windows 上运行 `node tests/video-preparation.mjs` 和 Windows PowerShell 5.1 的 `tests/skin-removal.ps1`。前者默认验证原生 4K 和失败分类；将 `DREAM_TEST_FFMPEG` 指向开发测试用的已验证 FFmpeg 可额外执行真实 4K、1440p、1080p 转换、分片 MP4、完整时长和缓存回归。该变量仅用于测试，不影响产品运行时选择。

编译后的管理器支持 `--ui-test <report.json>` 验证窗口按钮、缩放和不透明度反馈；`--smoke-test <image.png> [width height]` 生成无个人素材的界面截图。这些模式不会装载或卸载真实背景服务。

v1.3.0 新增 `tests/service-removal.ps1`、`tests/upgrade.ps1`、`tests/dark-appearance.ps1`，均在独立测试目录中执行，Windows 注册与进程操作使用测试替身。`node tests/media-preview.mjs` 使用 `DREAM_TEST_FFMPEG` 生成测试图案，验证循环预览、缓存修复、帧数限制与源文件保留。

管理器 `--ui-test` 还验证 80 次尺寸变化与 250 次滑杆拖动；`--lifecycle-test <report.json>` 验证操作中关闭、状态取消和超时；`--preview-test <gif> <report.json>` 验证实际动画帧变化。安装器 `--verify-only <report.json>` 只验证内置包，`--smoke-test <image.png>` 生成截图。**`--install-test <report.json>` 会实际升级本机服务和程序**，仅在明确允许升级的测试电脑上运行，结果写入报告。

## 分支

- `main` 保存稳定版本。
- `develop` 集成下一版本的开发内容。
- 从 `develop` 创建 `feat/<短名>`、`fix/<短名>` 或 `docs/<短名>` 分支，每个分支聚焦一个明确改动。
- 紧急修复可从 `main` 创建 `hotfix/<短名>`，发布后将同一修复合回 `develop`。

## 每次修改的名称和标记

每个完成的改动都要有独立提交，提交名说明实际结果。推荐格式：

```text
fix(video): convert unsupported MP4 to verified VP9 WebM
fix(startup): recover the monitor after a compatible upgrade
docs: explain offline FFmpeg runtime installation
```

为完成的改动创建 **带说明的 Git 标签**，名称使用递增序号和短名：

```text
change/0001-video-compatibility
change/0002-offline-runtime-docs
```

标签说明记录修改目的和验证结果，避免仅使用 `update`、`fix` 或 `修改` 作为名称。标签一旦公开，不重用、不移动。不要为尚未完成或未验证的工作创建“已发布”标记。

正式发布使用语义化版本标签 `vX.Y.Z`，例如 `v1.1.2`。历史 `v1.0.0`、`v1.0.1`、`v1.0.2`、`v1.1.0`、`v1.1.1` 标签只能指向真实保留快照恢复出的对应内容；不得根据猜测补造版本或原始开发日期。

## 修改约束

- 不强制关闭、重启或结束 Codex 进程来完成背景操作。
- 保留「装载服务」「换壁纸」「卸载皮肤」「卸载服务」四个入口，产品名为「Codex 动态背景」。`yz` 只在管理器右下角以小字显示。
- 端点仅限同用户官方 Codex 的本机回环地址；不能通过放宽身份验证来绕过连接问题。
- 素材必须经过结构和尺寸检查。视频还必须在实际 Codex renderer 中成功解码，不能只检查扩展名或 `canPlayType`。
- 编码不支持与播放策略、窗口隐藏、播放中断分开处理。只有确认编码不兼容时才转换，转换不截短或改写原文件。
- 不修改用户原素材；验证、下载或转换失败时保留当前壁纸。
- 不打包个人媒体、聊天内容、凭据、独立登录资料、运行状态或机器专用路径。
- 第三方许可证、下载校验和及来源说明随相关组件保留。可选 FFmpeg 使用固定 LGPL shared 发行，不在项目 ZIP 中重新分发二进制。

## 构建与验证

按 [README](README.md) 使用 Windows PowerShell 5.1 构建到不存在的新目录。包含中文字符串的 `.ps1` 文件保存为 **UTF-8 BOM**，避免 PowerShell 5.1 误读。JavaScript 模块使用 UTF-8。

修改后执行与变更相关的验证：

1. PowerShell 语法解析、JavaScript 语法检查和 C# 编译。
2. 媒体解析及播放分类的自动回归，覆盖正常和损坏输入。
3. 如涉及解码，在当前 Codex 验证真实解码帧；报告测试使用的 Codex 版本和视频编码，不公开用户素材。
4. 如涉及安装，核对两个按钮、登录入口、真实监控心跳和旧组件升级；保留已有聊天及壁纸。
5. 重新生成发布文件清单和 SHA-256，检查源码、许可、下载配置齐全，且不包含本机状态与个人文件。

不将测试生成的视频、运行时下载、安装状态、日志、屏幕截图或诊断文件提交到仓库。增加测试时使用临时目录和生成素材，不依赖维护者个人路径或已登录账号。

## 发布

在 `develop` 完成验证和版本更新，将确定的版本合入 `main`，创建带说明的 `vX.Y.Z` 标签。发布说明描述问题、最终行为、验证结果和现有限制，并附对应 ZIP 与 SHA-256。

为每次正式发布更新 [CHANGELOG](CHANGELOG.md)。发布资源中的版本号、GUI、源码、构建标记和服务兼容清单必须一致。不要将兼容层版本误写为上游引擎版本，也不要将本项目称为 OpenAI 官方插件。

1.4.0：tests/legacy-install.ps1 -ReleaseDirectory <历史ZIP目录>验证真实1.0.x包的识别和迁移、全新安装路径。tests/shortcuts.ps1验证快捷方式备份、修复和用户修改保留；tests/script-request.ps1验证Unicode/JSON参数不作为代码执行。build-msi.ps1 -TestPackage创建独立产品和组件标识、默认跳过真实后台操作；可在独立INSTALLDIR测试MSI文件部署与卸载。正式包不用-TestPackage。

1.5.0：`WallpaperLibraryTests.cs` 验证记录保存、原文件保留、Steam 多库与项目读取；管理器 `--gallery-test <报告路径>` 验证新增画廊选择、过滤、分页、记录与连接状态展示。`MediaPreviewJobTests.cs` 验证取消时预览进程及其子进程退出；`tests/codex-icons.ps1` 验证图标缓存、旧快捷方式修复与恢复。构建后仅重跑与本次变更相关的检查；窗口缩放、滑杆拖影和关闭等已解决功能没有新改动时不重复测试。
