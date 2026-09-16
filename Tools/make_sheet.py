# 拼一张对比图板 v2：图片按宽度撑满，文字自动换行
import os
from PIL import Image, ImageDraw, ImageFont

D = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "Tools", "screenshots", "ziyuan"))
OUT = os.path.join(D, "_对比图板.png")

BG = (247, 245, 240)
CARD = (255, 255, 255)
TITLE = (32, 32, 38)
SUB = (105, 100, 96)
LINE = (208, 203, 196)
ACCENT = (150, 60, 45)

FONT_CANDS = [r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msyhbd.ttc", r"C:\Windows\Fonts\simhei.ttf"]
def load_font(size):
    for p in FONT_CANDS:
        if os.path.isfile(p):
            try: return ImageFont.truetype(p, size)
            except Exception: pass
    return ImageFont.load_default()

F_TITLE = load_font(38)
F_SUB   = load_font(19)
F_ROW   = load_font(23)
F_TAG   = load_font(17)

def load(name):
    p = os.path.join(D, name)
    return Image.open(p).convert("RGB") if os.path.isfile(p) else None

def wrap(draw, text, font, max_w):
    """按像素宽度折行（中文按字折）"""
    lines, cur = [], ""
    for ch in text:
        t = cur + ch
        if draw.textlength(t, font=font) > max_w and cur:
            lines.append(cur); cur = ch
        else:
            cur = t
    if cur: lines.append(cur)
    return lines

PAD = 32
COL_W = 560
IMG_H = 300
LINE_H = 26
CAP_LINES = 2
CAP_H = 30 + CAP_LINES * LINE_H

rows = [
    ("① 全素材阵容 · 水墨化后", "F1_全阵容_最终.png",
     "山怪 / 兽人 / 不死兵 / 静态龙 / Boss龙 —— 五个新素材同框，比例已归一"),
    ("② 水墨化效果（本项目 shader）", "E1_Orge_水墨.png",
     "InkWash/InkCharacter：墨阶量化 + 墨线轮廓 + 贴图色相回填"),
    ("③ 原 PBR 效果（对照组）", "E2_Orge_原PBR.png",
     "同一模型原素材 URP/Lit：亮肤色、镜面高光、鲜艳毛皮"),
    ("④ Boss 中国龙（水墨化）", "B5_Z_Dragon.png",
     "273 骨 + 57.5s 飞行动画；低模 50k 面；蓝鳞层次分明"),
    ("⑤ 斜视阵容（看体积感）", "A2_阵容_斜45.png",
     "从 3/4 角看前后关系与厚度"),
    ("⑥ 山怪特写", "B1_Z_Orge.png",
     "素材自带底座平面已清理；赭墨色符合「敌人用重墨」约定"),
]

cols = 2
rows_n = (len(rows) + 1) // 2
CARD_W = COL_W
CARD_H = 12 + IMG_H + CAP_H + 14
HEADER = 158
W = PAD * 2 + CARD_W * cols + PAD
H = HEADER + PAD + rows_n * (CARD_H + PAD)

canvas = Image.new("RGB", (W, H), BG)
dr = ImageDraw.Draw(canvas)

dr.text((PAD, 32), "InkWash · 新素材（Sketchfab）导入 + 水墨化对比", font=F_TITLE, fill=TITLE)
sub1 = "5 个素材：mountain_orge / low-poly_orc / cursed_undead_soldier_rig / chinese_dragon / lowpoly_textured_chinese_dragon"
sub2 = "流程：Blender 转 FBX → Unity Humanoid（3 个可重定向）→ 单位归一 → 水墨材质（B 路线：保留贴图色相）"
dr.text((PAD, 86), sub1, font=F_SUB, fill=SUB)
dr.text((PAD, 112), sub2, font=F_SUB, fill=SUB)

y0 = HEADER + PAD
for i, (title, fname, desc) in enumerate(rows):
    col, row = i % cols, i // cols
    x = PAD + col * (CARD_W + PAD)
    y = y0 + row * (CARD_H + PAD)

    dr.rounded_rectangle([x, y, x + CARD_W, y + CARD_H], radius=10,
                         fill=CARD, outline=LINE, width=1)

    avail_w = CARD_W - 20
    im = load(fname)
    if im is not None:
        # 按宽度撑满，高度不超 IMG_H
        r = min(avail_w / im.width, (IMG_H - 8) / im.height)
        if r > 0:
            nw, nh = max(1, int(im.width * r)), max(1, int(im.height * r))
            im2 = im.resize((nw, nh), Image.LANCZOS)
            canvas.paste(im2, (x + (CARD_W - nw) // 2, y + 10 + max(0, (IMG_H - nh) // 2)))
    else:
        dr.text((x + 20, y + 20), f"(缺图 {fname})", font=F_ROW, fill=ACCENT)

    # 分隔线
    ly = y + 12 + IMG_H
    dr.line([x + 12, ly, x + CARD_W - 12, ly], fill=LINE, width=1)

    dr.text((x + 16, ly + 8), title, font=F_ROW, fill=TITLE)
    lines = wrap(dr, desc, F_TAG, avail_w - 8)[:CAP_LINES]
    for j, ln in enumerate(lines):
        dr.text((x + 16, ly + 36 + j * LINE_H), ln, font=F_TAG, fill=SUB)

canvas.save(OUT, "PNG", optimize=True)
print("saved", OUT, canvas.size)
