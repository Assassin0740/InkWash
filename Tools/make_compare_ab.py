# -*- coding: utf-8 -*-
"""同取景 A/B：MoShan 的 SkinnedMeshRenderer(空) vs 同网格同材质挂 MeshRenderer(有)。

两张图的相机 center 与视野（9.43 m）完全相同，唯一变量是渲染路径。
"""
import os
from PIL import Image, ImageDraw, ImageFont

BASE = r"D:\Unity Project\InkWash\Tools\screenshots\enemies"
OUT = os.path.join(BASE, "_对比_同网格_SMR空白_vs_MeshRenderer画出.png")

FONT_CANDIDATES = [
    r"C:\Windows\Fonts\msyh.ttc",
    r"C:\Windows\Fonts\msyhbd.ttc",
    r"C:\Windows\Fonts\simhei.ttf",
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

bad = load("AB_MoShan_A_原样SMR.png")
ok = load("AB_MoShan_B_同网格挂MeshRenderer.png")
W_IMG, H_IMG = ok.width, ok.height

PAD = 24
TOPH = 132
BOTH = 116
W = PAD * 3 + W_IMG * 2
H = TOPH + H_IMG + BOTH

canvas = Image.new("RGB", (W, H), (250, 250, 252))
d = ImageDraw.Draw(canvas)

d.text((PAD, 14), "同一个网格，为什么左边什么都没有？", font=font(33, True), fill=(24, 24, 28))
d.text((PAD, 62),
       "同一实例、同一台相机、同一视野 9.43 m —— 两张图唯一的区别是「走哪条渲染路径」。",
       font=font(19), fill=(96, 96, 106))
d.text((PAD, 88),
       "网格和材质完全一样（都是 Object_49 + M_Ink_Enemy_MoShan），所以问题不在网格、不在材质、不在取景。",
       font=font(19), fill=(96, 96, 106))

def paste(im, x, label, sub, ok_flag):
    y = TOPH
    box = (0, 190, 100) if ok_flag else (230, 55, 55)
    ov = Image.new("RGBA", im.size, (0, 0, 0, 0))
    od = ImageDraw.Draw(ov)
    od.rectangle([0, 0, im.width, 56], fill=(0, 0, 0, 175))
    od.rectangle([16, 17, 38, 39], fill=box)
    od.text((50, 11), label, font=font(25, True), fill=(255, 255, 255))
    od.text((im.width - 14, 15), sub, font=font(21), fill=(255, 255, 255), anchor="ra")
    od.rectangle([0, 0, im.width - 1, im.height - 1], outline=box, width=4)
    out = Image.alpha_composite(im.convert("RGBA"), ov).convert("RGB")
    canvas.paste(out, (x, y))

paste(bad, PAD, "SkinnedMeshRenderer —— 全空", "非背景 0.00%", False)
paste(ok, PAD * 2 + W_IMG, "同一网格挂 MeshRenderer", "非背景 1.56%", True)

yb = TOPH + H_IMG + 16
d.text((PAD, yb), "走蒙皮路径 = 一个像素都没有", font=font(22, True), fill=(180, 30, 30))
d.text((PAD, yb + 32),
       "骨骼引用数组里 26 个元素全是 null，rootBone 也是 null。",
       font=font(19), fill=(70, 70, 80))
d.text((PAD, yb + 58),
       "蒙皮退化，三角形没被光栅化：整个画面只有 2 种颜色。",
       font=font(19), fill=(70, 70, 80))

d.text((PAD * 2 + W_IMG, yb), "换条渲染路径 = 完整山怪", font=font(22, True), fill=(20, 110, 40))
d.text((PAD * 2 + W_IMG, yb + 32),
       "同一份网格、同一个材质，只换成普通的 MeshRenderer，",
       font=font(19), fill=(70, 70, 80))
d.text((PAD * 2 + W_IMG, yb + 58),
       "山怪就完整出现了（含黑墨外轮廓）。",
       font=font(19), fill=(70, 70, 80))

canvas.save(OUT)
print("saved:", OUT, canvas.size)
