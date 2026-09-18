# -*- coding: utf-8 -*-
"""
make_head_scan22.py —— 把 drg_headscan22 的十档侧视图裁到头部并放大，叠上
「真正的世界水平线（黄虚线）」与「吻部轴（青线）」，做成能一眼判「抬头/低头」的挑选图。

为什么要放大：900x600 全景里龙头只有 ~180 px 宽、还是深墨剪影 + 浅灰天。
第二十/二十一轮连着三轮尺子翻车，教训就是——**数字只给候选区间，裁决在眼睛**；
那就先把眼睛能用的图做出来。

读：Tools/reports/drg_headscan22.txt（Pass B 的表格 + SCREEN 行）
读：Tools/screenshots/enemies/H22/h22_side_<tag>.png
写：Tools/screenshots/enemies/H22/h22_anno_side_<tag>.png（单张，已叠线）
写：Tools/screenshots/enemies/_墨龙_头部_定档扫描_v4.png（10 档挑选图）

用法（必须用带 PIL 的 venv python）：
  "C:/Users/廖中钰/.workbuddy/binaries/python/envs/default/Scripts/python.exe" Tools/make_head_scan22.py
"""
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"D:\Unity Project\InkWash"
SRC = os.path.join(ROOT, "Tools", "screenshots", "enemies", "H22")
RP = os.path.join(ROOT, "Tools", "reports", "drg_headscan22.txt")
SHEET = os.path.join(ROOT, "Tools", "screenshots", "enemies", "_墨龙_头部_定档扫描_v4.png")

W, H = 900, 600
BOX = (250, 170, 620, 400)        # 裁切窗口（原图坐标）：含一段身体 + 整颗头
ZOOM = 1.7
COLS = 5

# 期望档位（顺序即"从抬到低"）；缺的会跳过并提示
ORDER = ["p03", "p00", "m03", "m06", "m09", "m12", "m15", "m18", "m21", "m24"]


def font(size, bold=False):
    cands = ([r"C:\Windows\Fonts\msyhbd.ttc", r"C:\Windows\Fonts\msyh.ttc"] if bold
             else [r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msyhbd.ttc"])
    for p in cands + [r"C:\Windows\Fonts\simhei.ttf", r"C:\Windows\Fonts\simsun.ttc"]:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()


def parse():
    txt = open(RP, encoding="utf-8", errors="replace").read()
    tbl, scr = {}, {}
    started = False
    for ln in txt.splitlines():
        s = ln.strip()
        if s.startswith("SCREEN "):
            tok = s.split()
            tag = tok[1].split("=")[1]
            d, i = {}, 2
            while i + 2 < len(tok):
                k = tok[i]
                try:
                    d[k] = (float(tok[i + 1]) * W, float(tok[i + 2]) * H)
                except (ValueError, IndexError):
                    break
                i += 3
            scr[tag] = d
            continue
        if s.startswith("── Pass B"):
            started = True
            continue
        if not started:
            continue
        m = re.match(r"^(-?[\d.]+)\s+(-?[\d.]+)°\s+(-?[\d.]+)°\s+(-?[\d.]+)°\s+([\d.]+)\s+m$", s)
        if m:
            p = float(m.group(1))
            tag = ("p" if p >= 0 else "m") + "%02d" % abs(int(round(p)))
            tbl[tag] = dict(pitch=p, elev=float(m.group(2)), yaw=float(m.group(3)),
                            skull=float(m.group(4)), fwdspan=float(m.group(5)))
    return tbl, scr


def dashed(d, p0, p1, fill, width=3, dash=15, gap=10):
    x0, y0 = p0
    x1, y1 = p1
    dx, dy = x1 - x0, y1 - y0
    L = (dx * dx + dy * dy) ** 0.5
    if L < 1e-3:
        return
    ux, uy = dx / L, dy / L
    t = 0.0
    while t < L:
        e = min(t + dash, L)
        d.line([(x0 + ux * t, y0 + uy * t), (x0 + ux * e, y0 + uy * e)], fill=fill, width=width)
        t = e + gap


def dot(d, p, r, fill, outline=(20, 20, 20)):
    d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=fill, outline=outline, width=2)


def annotate(tag, sc):
    fp = os.path.join(SRC, "h22_side_%s.png" % tag)
    if not os.path.exists(fp):
        return None
    im = Image.open(fp).convert("RGB")
    d = ImageDraw.Draw(im)

    # 黄虚线 = 真实世界水平（三维投影出来的方向，不是图上手画的横线）
    if "refA" in sc and "refB" in sc:
        ax, ay = sc["refA"]
        bx, by = sc["refB"]
        vx, vy = bx - ax, by - ay
        n = (vx * vx + vy * vy) ** 0.5
        if n > 1e-3:
            vx, vy = vx / n, vy / n
            dashed(d, (ax, ay), (ax + vx * 1600, ay + vy * 1600), (255, 232, 120))
            dashed(d, (ax, ay), (ax - vx * 1600, ay - vy * 1600), (255, 232, 120))

    # 青线 = 吻部轴（颈根后 → 吻端）
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
            dot(d, sc[k], r, col)

    out = os.path.join(SRC, "h22_anno_side_%s.png" % tag)
    im.save(out)
    return im.crop(BOX)


def main():
    if not os.path.exists(RP):
        print("× 报告不存在：", RP)
        return 1
    tbl, scr = parse()
    print("报告解析：表格 %d 档 [%s]，SCREEN %d 组"
          % (len(tbl), ",".join(sorted(tbl)), len(scr)))
    if not tbl:
        print("× Pass B 表格为空（探针可能没跑完）")
        return 1

    keys = [k for k in ORDER if k in tbl and k in scr]
    miss = [k for k in ORDER if k not in keys]
    if miss:
        print("  缺档（已跳过）：", ",".join(miss))

    tiles = []
    for tag in keys:
        im = annotate(tag, scr[tag])
        if im is None:
            print("  缺图 ", tag)
            continue
        tw, th = int(im.width * ZOOM), int(im.height * ZOOM)
        tiles.append((tag, im.resize((tw, th), Image.LANCZOS)))
        print("  标注 ", tag)
    if not tiles:
        return 1

    tw, th = tiles[0][1].size
    rows = (len(tiles) + COLS - 1) // COLS
    PAD, LBL_H, HEAD = 10, 40, 76
    cw = PAD + COLS * (tw + PAD)
    ch = PAD + HEAD + rows * (th + LBL_H + PAD)
    cv = Image.new("RGB", (cw, ch), (238, 240, 244))
    d = ImageDraw.Draw(cv)

    d.rectangle([0, 0, cw, HEAD - 6], fill=(24, 26, 30))
    d.text((12, 8), "墨龙头部俯仰 · 定档扫描（绝对值）—— 同一行波相位 0.50 / 同机位 / 蒙皮网格实测（顶点索引已锁死）",
           font=font(23, True), fill=(255, 235, 200))
    d.text((12, 44), "黄虚线 = 真正的世界水平（也是巡游时的身体轴线）｜青线 = 吻部轴｜看点：龙头整体落在黄虚线上方 = 抬头，压在线下 = 低头",
           font=font(19), fill=(150, 156, 170))

    y = HEAD + PAD
    for i, (tag, im) in enumerate(tiles):
        r, c = divmod(i, COLS)
        x = PAD + c * (tw + PAD)
        yy = y + r * (th + LBL_H + PAD)
        cv.paste(im, (x, yy))
        d.rectangle([x - 1, yy - 1, x + tw, yy + th], outline=(160, 166, 176))
        t = tbl[tag]
        hi = t["elev"] > 6.0
        col = (176, 34, 34) if hi else ((26, 116, 60) if t["elev"] < -4 else (40, 44, 52))
        d.text((x + 4, yy + th + 4), "headAlignPitchDeg = %+.0f" % t["pitch"],
               font=font(21, True), fill=col)
        d.text((x + 4, yy + th + 22), "吻部仰角 %+.1f°   颅顶仰角 %+.1f°" % (t["elev"], t["skull"]),
               font=font(17), fill=(80, 86, 98))

    cv.save(SHEET)
    print("完成：%s（%dx%d）" % (SHEET, cw, ch))
    return 0


if __name__ == "__main__":
    sys.exit(main())
