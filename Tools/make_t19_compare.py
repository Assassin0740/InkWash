# -*- coding: utf-8 -*-
"""
make_t19_compare.py —— 把 drg_tail19.cs 录的两组俯视帧做成「修前 / 修后」并排对照图

两遍录制是分开跑的，起始相位不同 ⇒ 直接按帧号并排会错相位。这里自动对齐：
  ① 用缩略灰度图暴力搜整体帧号偏移 m，使 A_i 与 B_{(i+m)%N} 的平均绝对差最小；
  ② 按「暗像素联合包围盒」裁到龙身（相机跟着链心走 ⇒ 龙稳定居中，裁剪框固定不抖）；
  ③ 左右并排 + 顶部标注 → T19CMP/cmp_%04d.png，交 make_dragon_video.py 合 mp4。

用法：python Tools/make_t19_compare.py
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"D:\Unity Project\InkWash"
SRC = os.path.join(ROOT, "Tools", "screenshots", "enemies", "T19")
OUT = os.path.join(ROOT, "Tools", "screenshots", "enemies", "T19CMP")

N = 100              # 每模式帧数
LUM_DARK = 150       # 「暗像素」阈值：龙是墨色，地面近白
MARGIN = 26          # 裁剪外扩
BAR = 92             # 顶部标注条高度

LBL_L1 = "修前 · 旧版独立行波"
LBL_L2 = "接点折角 20.7°  尾尖摆幅 0.686 m"
LBL_R1 = "修后 · 身体 sin 延续"
LBL_R2 = "接点折角 6.1°  尾尖摆幅 1.982 m"


def list_frames(prefix):
    fs = [os.path.join(SRC, "%s_%04d.png" % (prefix, i)) for i in range(N)]
    miss = [f for f in fs if not os.path.exists(f)]
    if miss:
        print("缺帧 %d 个，例如 %s" % (len(miss), miss[0]))
        sys.exit(1)
    return fs


def gray_small(path, size=(560, 315)):
    return np.asarray(Image.open(path).convert("L").resize(size, Image.BILINEAR), dtype=np.int16)


def best_offset(fa, fb):
    """
    整体帧号偏移：A_i 对应 B[(i+m)%N]。
    ★ 判据只用**暗像素**（龙身）：整幅平均绝对差会被大片白地稀释 ——
      实测整幅残差最优 4.429 / 次优 4.430，几乎不可分（等于没对齐）。
    """
    A = np.stack([gray_small(p) for p in fa])
    B = np.stack([gray_small(p) for p in fb])
    idx = np.arange(0, N, 4)
    vals = []
    for m in range(N):
        tot, cnt = 0.0, 0
        for i in idx:
            a = A[i]
            b = B[(i + m) % N]
            mask = (a < LUM_DARK) | (b < LUM_DARK)
            c = int(mask.sum())
            if c == 0:
                continue
            tot += float(np.abs(a[mask] - b[mask]).sum())
            cnt += c
        vals.append(tot / max(1, cnt))
    order = np.argsort(vals)
    m = int(order[0])
    print("相位对齐：m = %d" % m)
    print("  残差曲线前 5 名：" + ", ".join("m=%d:%.3f" % (k, vals[k]) for k in order[:5]))
    print("  最差 m=%d:%.3f   中位 %.3f" % (int(order[-1]), vals[order[-1]], float(np.median(vals))))
    return m


def dark_bbox(paths):
    """所有帧暗像素的联合包围盒（裁剪框固定 ⇒ 不抖）"""
    x0 = y0 = 10 ** 9
    x1 = y1 = -1
    for p in paths[::4]:
        a = np.asarray(Image.open(p).convert("L"), dtype=np.uint8)
        ys, xs = np.nonzero(a < LUM_DARK)
        if xs.size == 0:
            continue
        x0 = min(x0, int(xs.min())); x1 = max(x1, int(xs.max()))
        y0 = min(y0, int(ys.min())); y1 = max(y1, int(ys.max()))
    W, H = Image.open(paths[0]).size
    if x1 < 0:
        print("× 没找到暗像素，退回整幅")
        return (0, 0, W, H)
    box = (max(0, x0 - MARGIN), max(0, y0 - MARGIN),
           min(W, x1 + MARGIN), min(H, y1 + MARGIN))
    print("裁剪框 = %s（原图 %dx%d）" % (str(box), W, H))
    return box


def font(size):
    for p in (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msyhbd.ttc",
              r"C:\Windows\Fonts\simhei.ttf", r"C:\Windows\Fonts\simsun.ttc"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    print("⚠ 没找到中文字体，标注会退化成方框")
    return ImageFont.load_default()


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".png"):
            os.remove(os.path.join(OUT, f))

    fa = list_frames("t19a")     # 修后
    fb = list_frames("t19b")     # 修前
    m = best_offset(fa, fb)
    box = dark_bbox(fa + fb)

    fnt = font(24)
    fnt2 = font(19)
    pane_w, pane_h = Image.open(fa[0]).convert("RGB").crop(box).size
    canvas_w = pane_w * 2 + 12
    canvas_h = pane_h + BAR
    # libx264 要求宽高均为偶数，否则 ffmpeg 直接拒绝编码
    if canvas_h % 2:
        canvas_h += 1

    for i in range(N):
        left = Image.open(fb[(i + m) % N]).convert("RGB").crop(box)    # 修前
        right = Image.open(fa[i]).convert("RGB").crop(box)             # 修后
        cv = Image.new("RGB", (canvas_w, canvas_h), (232, 234, 238))
        cv.paste(left, (0, BAR))
        cv.paste(right, (pane_w + 12, BAR))
        d = ImageDraw.Draw(cv)
        d.rectangle([0, 0, canvas_w, BAR - 1], fill=(24, 26, 30))
        d.line([(pane_w + 5, 0), (pane_w + 5, canvas_h)], fill=(24, 26, 30), width=3)
        d.text((10, 8), LBL_L1, font=fnt, fill=(255, 200, 200))
        d.text((10, 54), LBL_L2, font=fnt2, fill=(255, 160, 160))
        d.text((pane_w + 22, 8), LBL_R1, font=fnt, fill=(190, 236, 255))
        d.text((pane_w + 22, 54), LBL_R2, font=fnt2, fill=(140, 210, 250))
        cv.save(os.path.join(OUT, "cmp_%04d.png" % i))

    print("完成 %d 帧 → %s（每格 %dx%d，成图 %dx%d）" % (N, OUT, pane_w, pane_h, canvas_w, canvas_h))


if __name__ == "__main__":
    main()
