# -*- coding: utf-8 -*-
"""六敌人 × _HueKeep 三档 对比图（同机位、同 tick，唯一变量 = _HueKeep）"""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
D = os.path.join(ROOT, "Tools/screenshots/enemies")

OBJS = ["墨徒", "墨偶", "墨魇", "墨山", "墨骨", "墨龙"]
KEEPS = [("keep0.0", "_HueKeep = 0（旧行为·纯墨）"),
         ("keep0.5", "_HueKeep = 0.5（淡墨透色）"),
         ("keep1.0", "_HueKeep = 1.0（本色清楚）")]

CW = 400
GAP = 12
PAD = 26
ROWH = int(CW * 9 / 16)
HEAD = 158
ROWLAB = 30
FOOT = 104


def font(sz, bold=False):
    for p in ("C:/Windows/Fonts/msyhbd.ttc" if bold else "C:/Windows/Fonts/msyh.ttc",
              "C:/Windows/Fonts/simhei.ttf"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, sz)
            except Exception:
                pass
    return ImageFont.load_default()


def find(name, tag):
    for f in os.listdir(D):
        if f.startswith("EH_" + name + "_" + tag) and f.endswith(".png"):
            return os.path.join(D, f)
    return None


def main():
    W = PAD * 2 + len(KEEPS) * CW + (len(KEEPS) - 1) * GAP
    H = HEAD + len(OBJS) * (ROWLAB + ROWH) + (len(OBJS) - 1) * GAP + FOOT
    cv = Image.new("RGB", (W, H), (28, 30, 34))
    d = ImageDraw.Draw(cv)

    d.text((PAD, 30), "敌人「固有色保留」定标  ——  _HueKeep 三档", font=font(29, True), fill=(238, 240, 244))
    d.text((PAD, 72), "同机位 / 同光照 / 同 tick，唯一变量 = _HueKeep。shader 新参数，默认 0 ⇒ 主角·龙·武器零影响",
           font=font(15), fill=(150, 158, 170))
    d.text((PAD, 96), "判据看「身上有几种颜色」：色相熵 墨偶 0.70→1.65（有效色相 2→4），而不只是「更艳」",
           font=font(15), fill=(196, 152, 96))

    for j, (tag, lab) in enumerate(KEEPS):
        x = PAD + j * (CW + GAP)
        d.text((x + 4, HEAD - 26), lab, font=font(17, True), fill=(214, 220, 230))

    y = HEAD
    for ob in OBJS:
        d.text((PAD + 2, y + 4), ob, font=font(19, True), fill=(232, 236, 242))
        y += ROWLAB
        for j, (tag, _) in enumerate(KEEPS):
            p = find(ob, tag)
            x = PAD + j * (CW + GAP)
            if not p:
                ImageDraw.Draw(cv).rectangle([x, y, x + CW, y + ROWH], fill=(52, 44, 44))
                continue
            im = Image.open(p).convert("RGB")
            im = im.resize((CW, ROWH), Image.LANCZOS)
            cv.paste(im, (x, y))
            d.rectangle([x, y, x + CW - 1, y + ROWH - 1], outline=(74, 80, 90))
        y += ROWH + GAP

    d.line([PAD, H - FOOT + 10, W - PAD, H - FOOT + 10], fill=(62, 68, 78), width=1)
    d.text((PAD, H - FOOT + 26),
           "读图：0 档整只怪一族色（腿/身/武器分不出）；1 档各部位本色全显（墨偶蓝袍+橙帽带+青杖、墨骨紫红甲+灰白盔+暗红剑）",
           font=font(15), fill=(162, 170, 182))
    d.text((PAD, H - FOOT + 50),
           "墨龙全程无变化：它的 _BaseMap 已清空（颜色画在 glTF emissiveTexture 上），_HueKeep 对它无效 —— 需另行处理",
           font=font(15), fill=(190, 132, 104))

    out = os.path.join(D, "_定标_固有色保留_HueKeep三档.png")
    cv.save(out)
    print("已生成:", out, cv.size)


if __name__ == "__main__":
    main()
