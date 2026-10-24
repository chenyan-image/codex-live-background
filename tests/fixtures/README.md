# Generated video fixtures

These small files contain only FFmpeg-generated solid colors or test patterns. No user wallpaper or copyrighted third-party footage is included.

`4k.webm` is a 3840×2160 solid teal VP9 video, 30 FPS, 0.5 seconds, generated with:

```text
ffmpeg -f lavfi -i color=c=0x235956:s=3840x2160:r=30:d=0.5 -an -c:v libvpx-vp9 -deadline realtime -cpu-used 8 -threads 4 -b:v 0 -crf 40 4k.webm
```
