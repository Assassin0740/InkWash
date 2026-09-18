# -*- coding: utf-8 -*-
"""
make_head_final_sheet.py —— 墨龙头部「改前(30) / 改后(3)」对照图

为什么要这张：用户连着两轮说「头抬得这么高要干嘛」「头还是有问题」，
所以交付不能只有数字 —— 得把**线上原来的 30** 和**改完的 3** 摆同一行同一机位对比，
再叠上真正的世界水平参考线，让"抬没抬"一眼可见。

数据来源（两张报告都带 SCREEN 归一化投影，直接复用，不重新量）：
  Tools/reports/drg_head21.txt       → p30 侧视 / 俯视 3/4
  Tools/reports/drg_headfinal21.txt  → p03 侧视 / 俯视 3/4 / 前侧 3/4

用法：python Tools/make_head_final_sheet.py
产物：Tools/screenshots/enemies/_墨龙_头部_改前改后_v1.png
"""
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"D:\Unity Project\InkWash"
ENEM = os.path.join(ROOT, "Tools", "screenshots", "enemies")
OUT = os.path.join(ENEM, "_墨龙_头部_改前改后_v1.png")
W, H = 900, 600

# (行标题, 报告, 图目录, 前缀, tag, 视图后缀, 说明)
ROWS = [
    ("改前  headAlignPitchDeg = 30", os.path.join(ROOT, "Tools", "reports", "drg_head21.txt"),
     os.path.join(ENEM, "H21"), "h21_", ["side_p30", "q_p30"],
     ["侧视", "俯视 3/4"]),
    ("改后  headAlignPitchDeg = 3", os.path.join(ROOT, "Tools", "reports", "drg_headfinal21.txt"),
     os.path.join(ENEM, "H21F"), "h21f_", ["side_p03", "q_p03", "front_p03"],
     ["侧视", "俯视 3/4", "前侧 3/4"]),
]


def font(size, bold=False):
    c = ([r"C:\Windows\Fonts\msyhbd.ttc", r"C:\Windows\Fonts\msyh.ttc"] if bold
         else [r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msyhbd.ttc"])
    for p in c + [r"C:\Windows\Fonts\simhei.ttf", r"C:\Windows\Fonts\simsun.ttc"]:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()


def parse_screen(report):
    """{tag: {点: (px,py)}}，tag 取 SCREEN 行里的 p=xxx。"""
    out = {}
    try:
        txt = open(report, encoding="utf-8", errors="replace").read()
    except Exception:
        return out
    for ln in txt.splitlines():
        s = ln.strip()
        if not s.startswith("SCREEN "):
            continue
        tok = s.split()
        tag = tok[1].split("=")[1]
        d, i = {}, 2
        while i + 2 <= len(tok):
            try:
                d[tok[i]] = (float(tok[i + 1]) * W, float(tok[i + 2]) * H)
            except (ValueError, IndexError):
                break
            i += 3
        out[tag] = d
    return out


def parse_row(report, tag):
    """从报告的 Pass B 表里取该 tag 的读数（取表格行里第一个数 = 绝对 pitch）。"""
    pnum = tag.split("_")[-1]
    want = float(pnum[1:]) * (1 if pnum[0] == "p" else -1)
    try:
        txt = open(report, encoding="utf-8", errors="replace").read().splitlines()
    except Exception:
        return None
    started = False
    for ln in txt:
        s = ln.strip()
        if s.startswith("── Pass B"):
            started = True
            continue
        if not started:
            continue
        m = re.match(r"^(-?[\d.]+)\s+(-?[\d.]+)°\s+(-?[\d.]+)°\s+(-?[\d.]+)°\s+([\d.]+)\s+m$", s)
        if m and abs(float(m.group(1)) - want) < 0.01:
            return dict(pitch=float(m.group(1)), elev=float(m.group(2)), yaw=float(m.group(3)),
                        skull=float(m.group(4)))
    return None


def dashed(d, p0, p1, fill, width=3, dash=16, gap=11):
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


def annotate(fp, sc, line_only=False):
    im = Image.open(fp).convert("RGB")
    if sc is None:
        return im
    d = ImageDraw.Draw(im)
    if "refA" in sc and "refB" in sc and not line_only:
        ax, ay = sc["refA"]
        bx, by = sc["refB"]
        vx, vy = bx - ax, by - ay
        n = (vx * vx + vy * vy) ** 0.5
        if n > 1e-3:
            vx, vy = vx / n, vy / n
            dashed(d, (ax, ay), (ax + vx * 1400, ay + vy * 1400), (255, 236, 140))
            dashed(d, (ax, ay), (ax - vx * 1400, ay - vy * 1400), (255, 236, 140))
    if "rear" in sc and "tip" in sc and not line_only:
        rx, ry = sc["rear"]
        tx, ty = sc["tip"]
        vx, vy = tx - rx, ty - ry
        n = (vx * vx + vy * vy) ** 0.5
        if n > 1e-3:
            d.line([(rx, ry), (tx + vx / n * 90, ty + vy / n * 90)], fill=(40, 220, 235), width=4)
        d.ellipse([rx - 6, ry - 6, rx + 6, ry + 6], fill=(80, 140, 255), outline=(20, 20, 20), width=2)
        d.ellipse([tx - 7, ty - 7, tx + 7, ty + 7], fill=(240, 60, 60), outline=(20, 20, 20), width=2)
    return im


def main():
    rows = []
    for title, report, folder, prefix, tags, names in ROWS:
        scr = parse_screen(report)
        tiles = []
        for tag, nm in zip(tags, names):
            fp = os.path.join(folder, prefix + tag + ".png")
            key = tag.split("_")[-1]
            if not os.path.exists(fp):
                print("  缺图 %s" % fp)
                continue
            rec = parse_row(report, tag)
            is_side = "side" in tag
            im = annotate(fp, scr.get(key), line_only=not is_side)
            tiles.append((im, tag, nm, rec, is_side))
        rows.append((title, tiles))
        print("  %s：%d 张" % (title, len(tiles)))

    SC = 0.46
    tw, th = int(W * SC), int(H * SC)
    PAD, LBL = 10, 46
    ncol = max(len(t) for _, t in rows)
    cw = PAD + ncol * (tw + PAD)
    # 88 = 顶部两条标题；每行 = 40 行标题 + th 图 + LBL 图注 + PAD 间距
    ch = 88 + len(rows) * (th + LBL + 50) + 12
    cv = Image.new("RGB", (cw, ch), (236, 239, 244))
    d = ImageDraw.Draw(cv)
    d.rectangle([0, 0, cw, 46], fill=(22, 24, 30))
    d.text((12, 9), "墨龙头部俯仰对齐 —— 改前(30) vs 改后(3)｜同行波相位 0.50 / 同机位 / 蒙皮网格实测",
           font=font(24, True), fill=(255, 236, 200))
    d.text((12, 54), "黄虚线 = 真正的世界水平（三维投影，不是图上手画的横线）｜蓝点 = 颈根后｜红点 = 吻端｜青线 = 吻部轴",
           font=font(18), fill=(72, 78, 90))

    y = 88
    for title, tiles in rows:
        bad = "30" in title
        d.rectangle([PAD, y, cw - PAD, y + 34], fill=(250, 214, 208) if bad else (206, 236, 206))
        d.text((PAD + 10, y + 4), title + ("　（错档：吻部上抬 25.8°、颅顶 54°）" if bad else "　（出货档：吻部 +0.3°，与颈线顺接）"),
               font=font(22, True), fill=(150, 40, 30) if bad else (24, 100, 48))
        y += 40
        for k, (im, tag, nm, rec, is_side) in enumerate(tiles):
            x = PAD + k * (tw + PAD)
            cv.paste(im.resize((tw, th), Image.LANCZOS), (x, y))
            d.rectangle([x - 1, y - 1, x + tw, y + th], outline=(150, 156, 168))
            lab = nm
            if rec:
                lab = "%s    p=%.0f°  吻部仰角 %+.2f°" % (nm, rec["pitch"], rec["elev"])
            d.text((x + 4, y + th + 3), lab, font=font(19, True),
                   fill=(150, 40, 30) if (rec and rec["elev"] > 20) else (34, 38, 46))
        y += th + LBL + PAD
    cv.save(OUT)
    print("完成：%s（%dx%d）" % (OUT, cw, ch))
    return 0


if __name__ == "__main__":
    sys.exit(main())
