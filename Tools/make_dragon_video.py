#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
make_dragon_video.py —— 把 drg_verify.cs 录下的 PNG 序列合成 mp4

为什么用固定帧号序列而不是屏幕录制：
  drg_verify.cs 在 Play 里设了 Time.captureFramerate = 30，
  每帧步长被钉死成 1/30 s ⇒ 产物与编辑器实际帧率完全无关，
  不会出现"掉帧导致龙看起来一顿一顿"的假象。

★ ffmpeg 的参数必须传 Windows 路径（本项目踩过：Git Bash 路径 ffmpeg 不认）。

用法：
  python Tools/make_dragon_video.py <帧目录> <输出mp4> [fps] [帧名前缀]
  （前缀默认 drv；drg_dive.cs 录的是 drd）
"""
import os
import subprocess
import sys

try:
    import imageio_ffmpeg
except ImportError:
    print("缺少 imageio-ffmpeg，请先安装")
    sys.exit(1)


def win(p):
    """转成 ffmpeg 能认的 Windows 路径"""
    return os.path.abspath(p).replace("/", "\\")


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "Tools/screenshots/enemies/DRV"
    out = sys.argv[2] if len(sys.argv) > 2 else "Tools/screenshots/enemies/_墨龙运动核改造.mp4"
    fps = sys.argv[3] if len(sys.argv) > 3 else "30"

    if not os.path.isdir(src):
        print("帧目录不存在:", src)
        sys.exit(1)

    frames = sorted(f for f in os.listdir(src) if f.endswith(".png"))
    if not frames:
        print("帧目录里没有 PNG:", src)
        sys.exit(1)

    # 只为诊断打印，不当断言用
    print("帧数 =", len(frames), " 首帧 =", frames[0], " 末帧 =", frames[-1])

    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    os.makedirs(os.path.dirname(os.path.abspath(out)) or ".", exist_ok=True)

    # ★ 帧名前缀：不同探针用不同前缀（drg_verify → drv，drg_dive → drd）
    pat = sys.argv[4] if len(sys.argv) > 4 else "drv"

    # yuv420p + faststart：保证 Windows 自带播放器 / 浏览器 / 微信都能直接放
    cmd = [
        ffmpeg, "-y",
        "-framerate", str(fps),
        "-start_number", "0",
        "-i", win(os.path.join(src, pat + "_%04d.png")),
        "-c:v", "libx264",
        "-preset", "slow",
        "-crf", "20",
        "-pix_fmt", "yuv420p",
        "-movflags", "+faststart",
        win(out),
    ]
    print("编码中……")
    r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode != 0:
        print("ffmpeg 失败：")
        print((r.stderr or "")[-2000:])
        sys.exit(r.returncode)

    size = os.path.getsize(out)
    print("完成:", out, f"({size/1024/1024:.2f} MB, {len(frames)/float(fps):.1f} s)")


if __name__ == "__main__":
    main()
