# 附带壁纸与素材来源

安装包提供六张由 `BuiltinWallpapers.cs` 生成的原创背景，以及用户指定的以下三项素材。两段视频保留 3840×2160、60 FPS 和完整时长，均为 H.264 MP4。静态图来自原场景的预览图。

| 安装后名称 | 类型 | 原项目 |
| --- | --- | --- |
| 水中会说话的眼睛 | 4K / 60 FPS 视频，约 9.71 秒 | [水中会说话的眼睛（过渡稍微好点版）](https://steamcommunity.com/sharedfiles/filedetails/?id=3582218429) |
| 会说话的眼睛2 | 4K / 60 FPS 视频，10 秒 | [会说话的眼睛2](https://steamcommunity.com/sharedfiles/filedetails/?id=3488045446) |
| 零二 02 FRANXX | 静态 JPG 预览图 | [零二 02 FRANXX](https://steamcommunity.com/sharedfiles/filedetails/?id=3295448069) |

这些第三方素材的权利归各自作者、角色和相关权利人所有，不作为本仓库原创作品，也不纳入软件代码的 MIT 授权。仓库中的截图用于展示软件界面。第三项不包含或运行原 `scene.pkg` 场景。

素材随安装包存放于 `wallpapers/bundled`，运行时使用安装目录中的副本，无需原作者本机路径或 Steam。六张原创背景与这三项附带素材都显示在本地壁纸页；安装不会替换用户已选背景。

Git 仓库保留 `source/bundled-wallpapers.json` 中的名称、来源、字节大小及 SHA-256；大体积视频随 Release 安装包/ZIP 分发。源码构建时，将已安装版本或解压后 ZIP 中的 `wallpapers/bundled` 目录传给 `build.ps1 -BundledMediaSource`。构建会校验每个素材，缺失或哈希不匹配时停止。
