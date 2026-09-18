# -*- coding: utf-8 -*-
"""
make_head20_axis.py —— 给 drg_headaxis20 的六档头特写叠上「水平参考线 + 吻部轴」，并拼成对照图

为什么要叠线：第二十轮终于把「吻部朝向」量出来了（用蒙皮网格顶点，不靠骨命名），
但**数字只有配合图才能判断**——第十八轮就是拿一个几何量（PCA 长轴）当唯一依据走偏的。
所以这张图的用途是：**让用户指着图说"我要这个方向"，而不是在数字里挑**。

图上画的三样：
  · 白黄虚线  = 真正的世界水平方向（由探针投影 rear ± fwd 得到，**是三维真水平**，
                不是"在图上画一条横线"—— 相机有 4° 俯仰，凭感觉画横线会差 20° 以上）
  · 青色实线  = 吻部轴（颈根后 → 吻端）
  · 圆点      = 颈根(灰) 颈根后(蓝) 吻端(红) 颅顶(绿)

用法：python Tools/make_head20_axis.py
输出：Tools/screenshots/enemies/H20/h20_anno_side_*.png（单张）
      Tools/screenshots/enemies/_墨龙_头部基准_吻部轴_v1.png（对照图）
"""
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"D:\Unity Project\InkWash"
SRC = os.path.join(ROOT, "Tools", "screenshots", "enemies", "H20")
RP = os.path.join(ROOT, "Tools", "reports", "drg_headaxis20.txt")
SHEET = os.path.join(ROOT, "Tools", "screenshots", "enemies", "_墨龙_头部基准_吻部轴_v1.png")

W, H = 900, 600
REL = ["m10", "m05", "p00", "p05", "p10", "p15"]


def font(size):
    for p in (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msyhbd.ttc",
              r"C:\Windows\Fonts\simhei.ttf", r"C:\Windows\Fonts\simsun.ttc"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()


def parse():
    txt = open(RP, encoding="utf-8", errors="replace").read()
    tbl, scr = {}, {}
    for ln in txt.splitlines():
        s = ln.strip()
        if s.startswith("SCREEN "):
            tok = s.split()
            rel = tok[1].split("=")[1]
            d = {}
            i = 2
            while i + 2 < len(tok) + 1 and i + 2 <= len(tok):
                k = tok[i]
                try:
                    d[k] = (float(tok[i + 1]) * W, float(tok[i + 2]) * H)
                except (ValueError, IndexError):
                    break
                i += 3
            scr[rel] = d
            continue
        m = re.match(r"^(m\d\d|p\d\d)\s+(-?[\d.]+)\s+(-?[\d.]+)°\s+(-?[\d.]+)°\s+(-?[\d.]+)°\s+(\d+)\s+(\d+)\s+([\d.]+)$", s)
        if m:
            tbl[m.group(1)] = dict(pitch=float(m.group(2)), elev=float(m.group(3)),
                                   yaw=float(m.group(4)), skull=float(m.group(5)),
                                   nhead=int(m.group(6)), ncore=int(m.group(7)),
                                   spread=float(m.group(8)))
    return tbl, scr


def dashed(d, p0, p1, fill, width=2, dash=14, gap=10):
    x0, y0 = p0
    x1, y1 = p1
    dx, dy = x1 - x0, y1 - y0
    L = (dx * dx + dy * dy) ** 0.5
    if L < 1e-3:
        return
    ux, uy = dx / L, dy / L
    t = 0.0
    while t < L:
        a = (x0 + ux * t, y0 + uy * t)
        e = min(t + dash, L)
        b = (x0 + ux * e, y0 + uy * e)
        d.line([a, b], fill=fill, width=width)
        t = e + gap


def dot(d, p, r, fill, outline=(20, 20, 20)):
    d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=fill, outline=outline, width=2)


def annotate(path, rel, t, sc):
    im = Image.open(path).convert("RGB")
    d = ImageDraw.Draw(im)
    f = font(19)
    fb = font(21)

    # ① 世界水平参考（真三维水平，由 rear ± fwd 投影而来）
    if "refA" in sc and "refB" in sc:
        # 沿 A→B 延长到画外，铺满整幅
        ax, ay = sc["refA"]
        bx, by = sc["refB"]
        vx, vy = bx - ax, by - ay
        n = (vx * vx + vy * vy) ** 0.5
        if n > 1e-3:
            vx, vy = vx / n, vy / n
            d.line([(ax - vx * 3000, ay - vy * 3000), (bx + vx * 3000, by + vy * 3000)],
                   fill=(120, 120, 90), width=1)
            dashed(d, (ax, ay), (ax + vx * 1400, ay + vy * 1400), (255, 236, 140), 3, 16, 11)
            dashed(d, (ax, ay), (ax - vx * 1400, ay - vy * 1400), (255, 236, 140), 3, 16, 11)

    # ② 吻部轴（rear → tip，往 tip 侧延长一点）
    if "rear" in sc and "tip" in sc:
        rx, ry = sc["rear"]
        tx, ty = sc["tip"]
        vx, vy = tx - rx, ty - ry
        n = (vx * vx + vy * vy) ** 0.5
        if n > 1e-3:
            vx, vy = vx / n, vy / n
            d.line([(rx, ry), (tx + vx * 90, ty + vy * 90)], fill=(40, 220, 235), width=4)

    # ③ 标志点
    if "neck" in sc:
        dot(d, sc["neck"], 5, (150, 150, 160))
    if "rear" in sc:
        dot(d, sc["rear"], 6, (80, 140, 255))
    if "tip" in sc:
        dot(d, sc["tip"], 7, (240, 60, 60))
    if "top" in sc:
        dot(d, sc["top"], 5, (90, 220, 120))

    # ④ 信息框
    box = [(8, 8), (360, 118)]
    d.rectangle(box, fill=(18, 20, 24), outline=(90, 96, 110))
    d.text((16, 14), "rel=%s   绝对 pitch=%.1f°" % (rel, t["pitch"]), font=fb, fill=(255, 240, 200))
    d.text((16, 42), "吻部仰角 %.2f°   偏航 %.2f°" % (t["elev"], t["yaw"]), font=f, fill=(120, 240, 250))
    d.text((16, 66), "颅顶仰角 %.2f°" % t["skull"], font=f, fill=(150, 240, 170))
    d.text((16, 90), "吻端簇展布 %.3f m   核心顶点 %d" % (t["spread"], t["ncore"]), font=f, fill=(210, 214, 224))

    out = os.path.join(SRC, "h20_anno_side_%s.png" % rel)
    im.save(out)
    return im


def main():
    tbl, scr = parse()
    if not tbl:
        print("× 报告里没解析到 Pass B 表格：", RP)
        return 1
    print("解析到 %d 档，SCREEN %d 组" % (len(tbl), len(scr)))

    tiles = {}
    for rel in REL:
        if rel not in tbl or rel not in scr:
            print("  缺 %s，跳过" % rel)
            continue
        # ★ 探针实际落盘名带 rel 前缀（h20_side_relm10.png），别按 h20_side_m10.png 找
        f = os.path.join(SRC, "h20_side_rel%s.png" % rel)
        if not os.path.exists(f):
            print("  缺图 %s" % f)
            continue
        tiles[rel] = annotate(f, rel, tbl[rel], scr[rel])
        print("  标注 %s  →  h20_anno_side_%s.png" % (rel, rel))

    keys = [r for r in REL if r in tiles]
    if not keys:
        return 1

    SCALE = 0.52
    PAD, LBL_H, ROW_H = 8, 40, 34
    tw, th = int(W * SCALE), int(H * SCALE)
    cw = ROW_H + len(keys) * (tw + PAD) + PAD
    ch = PAD + 48 + th + LBL_H + PAD
    cv = Image.new("RGB", (cw, ch), (238, 240, 244))
    d = ImageDraw.Draw(cv)
    d.rectangle([0, 0, cw, 44], fill=(24, 26, 30))
    d.text((10, 9), "墨龙头部基准标定 —— 吻部轴 vs 世界水平（同机位 / 同一行波相位 / 蒙皮网格实测）",
           font=font(23), fill=(255, 235, 200))

    y = 46 + PAD
    d.text((PAD, y + th // 2 - 16), "侧视", font=font(26), fill=(40, 44, 52))
    for k, rel in enumerate(keys):
        x = ROW_H + PAD + k * (tw + PAD)
        cv.paste(tiles[rel].resize((tw, th), Image.LANCZOS), (x, y))
        d.rectangle([x - 1, y - 1, x + tw, y + th], outline=(160, 166, 176))
        t = tbl[rel]
        col = (30, 120, 60) if abs(t["elev"]) < 3 else (40, 44, 52)
        d.text((x + 4, y + th + 4),
               "rel=%s  pitch=%.1f°  吻部仰角 %.1f°" % (rel, t["pitch"], t["elev"]),
               font=font(20), fill=col)
    cv.save(SHEET)
    print("完成：%s（%dx%d）" % (SHEET, cw, ch))
    return 0


if __name__ == "__main__":
    sys.exit(main())
