# Third-party notices

## Codex Dream Skin Manager and Codex Dream Skin

This Windows customization is derived from the MIT-licensed projects:

- https://github.com/xxloocee/Codex-Dream-Skin-Manager
- https://github.com/Fei-Away/Codex-Dream-Skin

Original copyright and license notices are retained in LICENSE and THIRD_PARTY/Codex-Dream-Skin. Windows service integration, the native manager, transparency repairs and portable video compatibility are modifications in this project.

Personal wallpapers and upstream media that are excluded from the software license are not included. Both JPEG files shipped here are locally generated neutral gradients.

Codex, ChatGPT and OpenAI names belong to their respective owners. This is an unofficial customization with no affiliation, endorsement or sponsorship by OpenAI.

## Node.js

Release ZIPs bundle official signed Node.js 22.22.2 for Windows x64. The source repository does not include its executable. Node.js and dependency licenses are retained in THIRD_PARTY/Node-LICENSE and engine/runtime/node/LICENSE in release ZIPs.

## FFmpeg runtime

The project release ZIP contains the compatibility conversion code and a pinned runtime download configuration, not FFmpeg executable binaries. When a source video needs normalization or verified compatibility conversion, the target computer downloads the fixed BtbN LGPL shared build directly from its publisher, verifies SHA-256, and installs the required runtime files locally. A user may instead supply the unchanged official archive using the documented offline path.

Publisher, exact version, checksum, download URL, license texts and upstream source locations are documented in THIRD_PARTY/FFmpeg-Runtime. FFmpeg runs locally and does not receive or upload videos to a service. Its own licensing remains separate from this project's MIT license.
## Phosphor Icons

Application icon geometry is adapted from Mountains (duotone) in https://github.com/phosphor-icons/core, MIT licensed. The original SVG and license are in source/assets; release ZIPs also include THIRD_PARTY/Phosphor-LICENSE. Colors and the landscape tile treatment are modifications in this project.
# 附带壁纸素材

从 v1.5.3 起，安装包另附用户指定的两段 4K / 60 FPS 视频与一张静态预览图。它们来自 Wallpaper Engine Workshop，不属于本仓库原创素材，亦不纳入代码的 MIT 授权。项目链接、类型和安装目录见 [附带壁纸来源](docs/bundled-wallpapers.md)；文件校验值见 `source/bundled-wallpapers.json`。其余六张内置背景由本仓库代码生成。
