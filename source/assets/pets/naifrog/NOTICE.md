# 奶蛙素材说明

来源：timerring/codex-pet-naiwa
固定提交：3c0cd7ca231f02e0d4d6956fc6ce86eb9f0cfb50
仓库：https://github.com/timerring/codex-pet-naiwa

原项目原创内容采用 MIT，原文随 LICENSE 保留。作者在 README 明确说明，Credits 中第三方素材不在其 MIT 授权范围内：Nitrogen216/awesome_pets、LynnShaw/naiwa-pet。角色与第三方素材权利归各自权利人；CodexHub 代码许可不重新授权这些素材。

本目录将作者桌面版 WebP 图集解码为透明 PNG，保留九种动作及原始帧顺序。动作速度采用 CodexHub 图集默认 10 FPS。没有专门的睡眠或碰撞片段：休息复用安静待机，碰撞使用受惊动作配合现有物理回弹。

固定版本的失败／阻塞行实际有 6 个可见帧，随后两格为透明占位；播放排除这两格，避免透明闪帧。
