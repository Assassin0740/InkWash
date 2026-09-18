# -*- coding: utf-8 -*-
"""
make_head_gen3.py —— 墨龙头部俯仰「三代同框」：改前 30 → 上一版 3 → 本轮 -12

三张图来自三次不同的探针运行（H21 / H21F / H22），但**取景公式完全一样**
（都在 pitch=0 时锁索引、按「吻端–颈根中点」定视心），所以可以直接并排比。

叠线口径统一由本脚本自己画（不依赖各次探针自带的标注），保证三代可比：
  黄虚线 = 探针报告里 refA→refB 的投影方向 = **真正的世界水平**（也是巡游时的身体轴线）
  青线   = 吻部轴（颈根后 → 吻端）

用法：python Tools/make_head_gen3.py
产物：Tools/screenshots/enemies/_墨龙_头部_三代对照_v5.png
"""
import os
import re
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.stdout.reconfigure(encoding="utf-8")

import make_head_scan22 as M          # 复用 dashed / dot / font / BOX

ROOT = r"D:\Unity Project\InkWash"
OUT = os.path.join(ROOT, "Tools", "screenshots", "enemies", "_墨龙_头部_三代对照_v5.png")
ZOOM = 1.85

# (标签, 图目录, 报告, 档位 tag, 副标题)
GEN = [
    ("改前", os.path.join(ROOT, "Tools", "screenshots", "enemies", "H21"),
     os.path.join(ROOT, "Tools", "reports", "drg_head21.txt"), "p30",
     "headAlignPitchDeg = +30    吻部仰角 +25.8°", "用户两次说「头抬得这么高」的元凶"),
    ("上一版", os.path.join(ROOT, "Tools", "screenshots", "enemies", "H21F"),
     os.path.join(ROOT, "Tools", "reports", "drg_headfinal21.txt"), "p03",
     "headAlignPitchDeg = +3    吻部仰角 +0.3°", "尺子说「水平」，眼睛仍读作抬头"),
    ("本轮", os.path.join(ROOT, "Tools", "screenshots", "enemies", "H22"),
     os.path.join(ROOT, "Tools", "reports", "drg_headscan22.txt"), "m12",
     "headAlignPitchDeg = -12    吻部仰角 -14.8°", "颅骨压回身体线（本轮定档）"),
]


def parse_screen(path):
    out = {}
    txt = open(path, encoding="utf-8", errors="replace").read()
    for ln in txt.splitlines():
        s = ln.strip()
        if not s.startswith("SCREEN "):
            continue
        tok = s.split()
        tag = tok[1].split("=")[1]
        d, i = {}, 2
        while i + 2 < len(tok):
            try:
                d[tok[i]] = (float(tok[i + 1]) * M.W, float(tok[i + 2]) * M.H)
            except (ValueError, IndexError):
                break
            i += 3
        out[tag] = d
    return out


def draw_lines(im, sc):
    d = ImageDraw.Draw(im)
    if "refA" in sc and "refB" in sc:
        ax, ay = sc["refA"]
        bx, by = sc["refB"]
        vx, vy = bx - ax, by - ay
        n = (vx * vx + vy * vy) ** 0.5
        if n > 1e-3:
            vx, vy = vx / n, vy / n
            M.dashed(d, (ax, ay), (ax + vx * 1600, ay + vy * 1600), (255, 232, 120))
            M.dashed(d, (ax, ay), (ax - vx * 1600, ay - vy * 1600), (255, 232, 120))
    if "rear" in sc and "tip" in sc:
        rx, ry = sc["rear"]
        tx, ty = sc["tip"]
        vx, vy = tx - rx, ty - ry
        n = (vx * vx + vy * vy) ** 0.5
        if n > 1e-3:
            d.line([(rx, ry), (tx + vx / n * 110, ty + vy / n * 110)], fill=(40, 220, 235), width=4)
    for k, col, r in (("neck", (150, 150, 160), 5), ("rear", (80, 140, 255), 6),
                      ("tip", (240, 60, 60), 7), ("top", (90, 220, 120), 5)):
        if k in sc:
            M.dot(d, sc[k], r, col)
    return im


def main():
    tiles = []
    for name, sdir, rp, tag, sub, sub2 in GEN:
        if not os.path.exists(rp):
            print("× 缺报告", rp)
            return 1
        scr = parse_screen(rp).get(tag)
        if scr is None:
            print("× %s 里没有 SCREEN %s" % (rp, tag))
            return 1
        # 文件名前缀三种（三次探针各用各的）：h21_ / h21f_ / h22_
        fp = ""
        for pref in ("h21_", "h21f_", "h22_"):
            cand = os.path.join(sdir, pref + "side_%s.png" % tag)
            if os.path.exists(cand):
                fp = cand
                break
        if not os.path.exists(fp):
            print("× 缺图", fp)
            return 1
        im = Image.open(fp).convert("RGB")
        im = draw_lines(im, scr).crop(M.BOX)
        tw, th = int(im.width * ZOOM), int(im.height * ZOOM)
        tiles.append((name, (sub, sub2), im.resize((tw, th), Image.LANCZOS)))
        print("  合成 %s（%s）" % (name, os.path.basename(fp)))

    PAD, LBL_H, HEAD = 12, 78, 74
    tw, th = tiles[0][2].size
    cw = PAD + 3 * (tw + PAD)
    ch = PAD + HEAD + th + LBL_H + PAD
    cv = Image.new("RGB", (cw, ch), (238, 240, 244))
    d = ImageDraw.Draw(cv)

    d.rectangle([0, 0, cw, HEAD - 8], fill=(24, 26, 30))
    d.text((14, 8), "墨龙头部俯仰 · 三代同框（同一行波相位 0.50 / 同一取景公式 / 蒙皮网格实测）",
           font=M.font(24, True), fill=(255, 235, 200))
    d.text((14, 38), "黄虚线 = 真正的世界水平（三维投影方向，也是巡游时的身体轴线）｜青线 = 吻部轴｜"
                     "看：龙头整体相对黄虚线在上方还是下方", font=M.font(18), fill=(152, 158, 172))

    for i, (name, sub, im) in enumerate(tiles):
        x = PAD + i * (tw + PAD)
        y = PAD + HEAD
        cv.paste(im, (x, y))
        d.rectangle([x - 1, y - 1, x + tw, y + th], outline=(160, 166, 176))
        col = (176, 34, 34) if i < 2 else (24, 120, 60)
        d.text((x + 4, y + th + 4), name, font=M.font(23, True), fill=col)
        d.text((x + 4, y + th + 32), sub[0], font=M.font(17), fill=(50, 54, 64))
        d.text((x + 4, y + th + 54), sub[1], font=M.font(16), fill=(120, 126, 138))

    cv.save(OUT)
    print("完成：%s（%dx%d）" % (OUT, cw, ch))
    return 0


if __name__ == "__main__":
    sys.exit(main())
