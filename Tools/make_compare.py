# -*- coding: utf-8 -*-
"""把 MoGu(正常) 与 MoShan(全空) 的【纯蓝底】哨兵图并排拼成一张可读的对照图。

哨兵色纪律：不用洋红 —— 那是 Unity「材质丢失」的渲染色，看图的任何人在视觉上
都无法区分「我的画布」与「真的缺材质」；不用绿 —— 8-bit RT 抖动最大 62 > 阈值 24。
纯蓝/纯红单通道抖动 ≤21，判据干净。
"""
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

ok = load("hist_MoGu_blue.png")
bad = load("hist_MoShan_blue.png")
W_IMG, H_IMG = ok.width, ok.height

PAD = 24
TOPH = 132
BOTH = 106
W = PAD * 3 + W_IMG * 2
H = TOPH + H_IMG + BOTH

canvas = Image.new("RGB", (W, H), (250, 250, 252))
d = ImageDraw.Draw(canvas)

# ---- 顶部标题 ----
d.text((PAD, 14), "为何说 MoShan「一个像素都没画」—— 纯蓝底哨兵对照",
       font=font(33, True), fill=(24, 24, 28))
d.text((PAD, 62),
       "哨兵蓝 = 我为这次测量临时铺的画布底色，不是材质。不用洋红：那是 Unity「材质丢失」的渲染色；",
       font=font(19), fill=(96, 96, 106))
d.text((PAD, 88),
       "也不用绿：8-bit RT 抖动最大 62 > 阈值 24，会凭空多出 25% 假像素。取景：各自按包围盒（探针 dg_pixel_hist）。",
       font=font(19), fill=(96, 96, 106))

# ---- 两图 + 叠标 ----
def paste(im, x, label, sub, ok_flag):
    y = TOPH
    box = (0, 190, 100) if ok_flag else (230, 55, 55)
    ov = Image.new("RGBA", im.size, (0, 0, 0, 0))
    od = ImageDraw.Draw(ov)
    od.rectangle([0, 0, im.width, 56], fill=(0, 0, 0, 175))
    od.rectangle([16, 17, 38, 39], fill=box)
    od.text((50, 11), label, font=font(26, True), fill=(255, 255, 255))
    od.text((im.width - 14, 15), sub, font=font(21), fill=(255, 255, 255), anchor="ra")
    od.rectangle([0, 0, im.width - 1, im.height - 1], outline=box, width=4)
    out = Image.alpha_composite(im.convert("RGBA"), ov).convert("RGB")
    canvas.paste(out, (x, y))

paste(ok, PAD, "MoGu —— 正常渲染", "非背景 11.27%", True)
paste(bad, PAD * 2 + W_IMG, "MoShan —— 全空", "非背景 0.00%", False)

# ---- 底部注（每栏第二行必须按栏宽拆行，否则左右两栏文字会串在一起）----
yb = TOPH + H_IMG + 14
d.text((PAD, yb), "黑色那坨 = 真画出来的墨色", font=font(22, True), fill=(20, 110, 40))
d.text((PAD, yb + 32),
       "最硬的证据：它由 RGB(12,9,8) 共 37671 个像素构成，",
       font=font(19), fill=(70, 70, 80))
d.text((PAD, yb + 58),
       "把画布换成纯红后仍是 37671 个 —— 一个不差。",
       font=font(19), fill=(70, 70, 80))

d.text((PAD * 2 + W_IMG, yb), "整块纯蓝 = 一个像素都没有", font=font(22, True), fill=(180, 30, 30))
d.text((PAD * 2 + W_IMG, yb + 32),
       "整个画面只有 2 种颜色，且都是蓝底自身的抖动值（偏差 ≤21）",
       font=font(19), fill=(70, 70, 80))
d.text((PAD * 2 + W_IMG, yb + 58),
       "—— 连一丝模型的痕迹都没有。",
       font=font(19), fill=(70, 70, 80))

canvas.save(OUT)
print("saved:", OUT, canvas.size)
