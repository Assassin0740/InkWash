# -*- coding: utf-8 -*-
"""把 MoGu(正常) 与 MoShan(空白) 的品红哨兵底图并排拼成一张可读的对照图。"""
import os
from PIL import Image, ImageDraw, ImageFont

BASE = r"D:\Unity Project\InkWash\Tools\screenshots\enemies"
OUT = os.path.join(BASE, "_对比_MoGu正常_vs_MoShan全空.png")

FONT_CANDIDATES = [
    r"C:\Windows\Fonts\msyh.ttc",
    r"C:\Windows\Fonts\msyhbd.ttc",
    r"C:\Windows\Fonts\simhei.ttf",
    r"C:\Windows\Fonts\simsun.ttc",
]

def font(size, bold=False):
    cands = ([r"C:\Windows\Fonts\msyhbd.ttc"] if bold else []) + FONT_CANDIDATES
    for p in cands:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()

def load(name, w=660):
    im = Image.open(os.path.join(BASE, name)).convert("RGB")
    return im.resize((w, int(im.height * w / im.width)), Image.LANCZOS)

ok = load("hist_MoGu_magenta.png")
bad = load("hist_MoShan_magenta.png")
W_IMG, H_IMG = ok.width, ok.height

PAD = 24
TOPH = 108
BOTH = 68
W = PAD * 3 + W_IMG * 2
H = TOPH + H_IMG + BOTH

canvas = Image.new("RGB", (W, H), (250, 250, 252))
d = ImageDraw.Draw(canvas)

# ---- 顶部标题 ----
d.text((PAD, 16), "为何说 MoShan「一个像素都没画」—— 同一套判据的对照",
       font=font(33, True), fill=(24, 24, 28))
d.text((PAD, 68),
       "品红哨兵底（8-bit RT 抖动 ≤19 < 阈值 24，判据干净）｜各自按包围盒取景；另用固定 40 m 取景复核，同样全空",
       font=font(20), fill=(96, 96, 106))

# ---- 两图 + 叠标 ----
def paste(im, x, label, sub, ok_flag):
    y = TOPH
    box = (0, 210, 110) if ok_flag else (240, 60, 60)
    ov = Image.new("RGBA", im.size, (0, 0, 0, 0))
    od = ImageDraw.Draw(ov)
    od.rectangle([0, 0, im.width, 56], fill=(0, 0, 0, 175))
    # 状态色块（雅黑没有 ✅/❌ 字形，会渲染成空心方块）
    od.rectangle([16, 17, 38, 39], fill=box)
    od.text((50, 11), label, font=font(26, True), fill=(255, 255, 255))
    od.text((im.width - 14, 15), sub, font=font(21), fill=(255, 255, 255), anchor="ra")
    od.rectangle([0, 0, im.width - 1, im.height - 1], outline=box, width=4)
    out = Image.alpha_composite(im.convert("RGBA"), ov).convert("RGB")
    canvas.paste(out, (x, y))

paste(ok, PAD, "MoGu —— 正常渲染", "非背景 0.49%", True)
paste(bad, PAD * 2 + W_IMG, "MoShan —— 全空", "非背景 0.00%", False)

# ---- 底部注 ----
yb = TOPH + H_IMG + 18
d.text((PAD, yb), "黑色那一坨 = 模型真的被画出来了", font=font(22, True), fill=(20, 110, 40))
d.text((PAD * 2 + W_IMG, yb), "整块纯品红 = 一个像素都没有（这就是结论本身）",
       font=font(22, True), fill=(180, 30, 30))

canvas.save(OUT)
print("saved:", OUT, canvas.size)
