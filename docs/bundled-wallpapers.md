# 内置精选背景

原先六张程序生成背景已从安装包和默认库中移除，升级同时清理它们的内置记录，保留用户自己的导入和原文件。

| 默认名称 | 使用的素材 | 原项目 |
| --- | --- | --- |
| 花桃全屏 | 原项目 JPG 预览图 | [3552423078](https://steamcommunity.com/sharedfiles/filedetails/?id=3552423078) |
| Kamisato Ayaka 神里绫华 | 原项目 JPG 预览图 | [3301717199](https://steamcommunity.com/sharedfiles/filedetails/?id=3301717199) |
| 月光下的德累斯顿 | 原项目动态预览 GIF | [3200802205](https://steamcommunity.com/sharedfiles/filedetails/?id=3200802205) |
| 雨樱【Rain Sakura】- 4K | 完整 H.264 原片，3840×2160、30 FPS、86.7 秒、101871488 字节 | [2970331102](https://steamcommunity.com/sharedfiles/filedetails/?id=2970331102) |

安装包提供四项预览，其他电脑无需原电脑的 Steam 路径。《雨樱》仅附静态封面，应用时从 GitHub 发行页下载完整 4K 原片并校验；已经存在的有效原片直接复用。宠物动画仍随包提供，不受壁纸按需下载影响。三个场景项目使用已提供的图片或 GIF 预览，不执行 scene.pkg。

这些是用户指定的第三方素材，原始来源和 SHA256 记录于 source/assets/wallpapers/catalog.json 及对应 NOTICE；不纳入软件代码的 MIT 授权。

以下为历史记录：

# v1.5.3 历史附带素材

**v1.5.4 起，以下三项已从安装包和默认壁纸库中移除，以减小体积。** 当时的 v1.5.4 版本只保留六张程序生成背景；当前版本采用上表四项。用户自己的 Steam 原文件和本地导入功能保留。

以下记录仅说明 v1.5.3 曾附带的素材来源，不代表当前安装包仍包含这些视频或图片。

| 安装后名称 | 类型 | 原项目 |
| --- | --- | --- |
| 水中会说话的眼睛 | 4K / 60 FPS 视频，约 9.71 秒 | [水中会说话的眼睛（过渡稍微好点版）](https://steamcommunity.com/sharedfiles/filedetails/?id=3582218429) |
| 会说话的眼睛2 | 4K / 60 FPS 视频，10 秒 | [会说话的眼睛2](https://steamcommunity.com/sharedfiles/filedetails/?id=3488045446) |
| 零二 02 FRANXX | 静态 JPG 预览图 | [零二 02 FRANXX](https://steamcommunity.com/sharedfiles/filedetails/?id=3295448069) |

这些第三方素材的权利归各自作者、角色和相关权利人所有，不作为本仓库原创作品，也不纳入软件代码的 MIT 授权。仓库中的截图用于展示软件界面。第三项不包含或运行原 `scene.pkg` 场景。

历史文件名和校验值见 [v1.5.3 素材清单](https://github.com/chenyan-image/codex-live-background/blob/v1.5.3/source/bundled-wallpapers.json)。当前源码构建不再需要素材目录参数。
