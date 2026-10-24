# 可选视频兼容运行时来源

本项目发布 ZIP 不包含 FFmpeg 二进制。只有当前 Codex 无法解码所选视频、需要生成兼容副本时，才下载 BtbN 发布的固定版本 LGPL shared Windows x64 ZIP，并验证固定 SHA-256。

选用 2026-09-30 月末发行。上游公布月末构建保留两年；避免使用仅保留 14 天的每日构建。

来源发布页：
https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-09-30-13-08

固定下载地址：
https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-09-30-13-08/ffmpeg-n9.0.2-17-g2a571b6068-win64-lgpl-shared-9.0.zip

上游 SHA-256 清单：
https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-09-30-13-08/checksums.sha256

ZIP 文件名：`ffmpeg-n9.0.2-17-g2a571b6068-win64-lgpl-shared-9.0.zip`

ZIP 大小：76,972,461 字节（73.41 MiB）。

SHA-256：`7157177b8a6cb2174c1650ba8c71b363f2c78cba5330f88c4c02cf5b2b880646`

离线使用：在可联网电脑从上述上游地址下载这个 ZIP，将文件原封不动复制到目标电脑：

```text
%LOCALAPPDATA%\CodexDreamSkin\runtime-downloads\ffmpeg-n9.0.2-17-g2a571b6068-win64-lgpl-shared-9.0.zip
```

该路径使用目标电脑当前用户的 LOCALAPPDATA。

不需要手动解压，也不要改名。软件仍会验证相同 SHA-256；验证后只提取 runtime-download.json 列出的 ffmpeg.exe、7 个 DLL 和上游 LICENSE.txt 到自己的 runtime 目录，不安装系统编解码器、不修改 PATH。

上游 ZIP 内目录前缀是 `ffmpeg-n9.0.2-17-g2a571b6068-win64-lgpl-shared-9.0/`。运行文件位于其 bin/ 目录；无需 ffprobe.exe、ffplay.exe、开发用 include/ 或 lib/。8 个运行文件合计 156,713,984 字节（149.45 MiB）。每项原始尺寸见 runtime-download.json。

此版本启用 libvpx，可生成 WebM VP9。HEVC 解码使用 FFmpeg 自带解码器，不需要 libx265 编码器。BtbN 的 LGPL 构建不启用 GPL-only 的 libx264/libx265。

许可和源码：

- 上游 ZIP 根目录 LICENSE.txt 是 FFmpeg COPYING.LGPLv3。
- FFmpeg 版本源码：https://github.com/FFmpeg/FFmpeg/archive/2a571b6068.tar.gz
- 对应构建配方、补丁、依赖 repo/commit：https://github.com/BtbN/FFmpeg-Builds/archive/6c9aec5fc9a72ec3abedd1fa84db141fa18cf52b.tar.gz
- libvpx 对应提交：https://chromium.googlesource.com/webm/libvpx/+/5e680f30801d03c21078f8c4b772464752516211
- FFmpeg 许可说明：https://ffmpeg.org/legal.html

这些链接用于记录第三方来源。FFmpeg 二进制由目标电脑直接从上游下载；本项目的发布 ZIP 不重分发该二进制，也不将自身程序与 libav* 链接。
