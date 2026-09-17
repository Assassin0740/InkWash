# -*- coding: utf-8 -*-
"""统计敌人 _BaseMap 贴图本身的颜色（sRGB + 线性两套），回答"它自己的颜色是什么"。

判据：
  · 平均 RGB / 平均彩度（max-min）
  · 按色相分 12 桶，取占比 top-3 的色相族与其代表色（只统计不透明且非极暗像素）
  · 同时给出 sRGB 与 Linear 值（shader 里 albedo 走 linear）
"""
import os, colorsys
from collections import defaultdict
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

TARGETS = [
    ("墨山 MoShan",  "Assets/ThirdParty/Ziyuan/mountain_orge/textures/Orge_LP_baseColor.png"),
    ("墨怪 MoGuai",  "Assets/ThirdParty/Ziyuan/low-poly_orc/textures/07_-_Default_diffuse.png"),
    ("墨骨 MoGu",    "Assets/ThirdParty/Ziyuan/cursed_undead_soldier_rig/textures/Undead_Material_baseColor.png"),
    ("墨偶/墨徒/墨魇(KayKit 共用)",
                     "Assets/ThirdParty/KayKit/Skeletons/Characters/skeleton_texture_A.png"),
]


def s2l(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def analyze(path):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    step = max(1, int((w * h) ** 0.5 / 180))     # 大图抽稀，够统计用

    n = 0
    sr = sg = sb = 0.0
    lr = lg = lb = 0.0
    sat_sum = 0.0
    hue_bins = defaultdict(lambda: [0, 0.0, 0.0, 0.0])
    for y in range(0, h, step):
        for x in range(0, w, step):
            r, g, b, a = px[x, y]
            if a < 16:
                continue
            rf, gf, bf = r / 255.0, g / 255.0, b / 255.0
            luma = 0.2126 * rf + 0.7152 * gf + 0.0722 * bf
            if luma < 0.02:          # 纯黑底/阴影，不代表材质色
                continue
            n += 1
            sr += r; sg += g; sb += b
            lr += s2l(rf); lg += s2l(gf); lb += s2l(bf)
            mx, mn = max(rf, gf, bf), min(rf, gf, bf)
            sat_sum += (mx - mn)
            hh = colorsys.rgb_to_hsv(rf, gf, bf)[0]
            bkt = int(hh * 12) % 12
            e = hue_bins[bkt]
            e[0] += 1; e[1] += r; e[2] += g; e[3] += b

    if n == 0:
        return None
    avg_srgb = (sr / n, sg / n, sb / n)
    avg_lin = (lr / n, lg / n, lb / n)
    top = sorted(hue_bins.items(), key=lambda kv: -kv[1][0])[:3]
    hues = []
    for bkt, e in top:
        c = e[0]
        hues.append((e[0] / n, (e[1] / c, e[2] / c, e[3] / c),
                     (bkt * 30, (bkt + 1) * 30)))
    return dict(size=(w, h), n=n, avg_srgb=avg_srgb, avg_lin=avg_lin,
                sat=sat_sum / n, hues=hues)


def hue_name(a, b):
    names = [(0, 15, "红"), (15, 45, "橙"), (45, 70, "黄"), (70, 160, "绿"),
             (160, 200, "青"), (200, 255, "蓝"), (255, 290, "紫"),
             (290, 330, "品红"), (330, 360, "红")]
    for lo, hi, nm in names:
        if a < hi and b > lo:
            return nm
    return "?"


def main():
    print("=" * 92)
    print("敌人 _BaseMap 贴图本色统计（抽稀采样，去除透明与极暗像素）")
    print("=" * 92)
    for label, rel in TARGETS:
        p = os.path.join(ROOT, rel)
        print()
        print(f"── {label}")
        print(f"   文件：{rel}")
        if not os.path.exists(p):
            print("   ✗ 文件不存在")
            continue
        r = analyze(p)
        if not r:
            print("   ✗ 无有效像素")
            continue
        s = r["avg_srgb"]; l = r["avg_lin"]
        print(f"   尺寸 {r['size'][0]}×{r['size'][1]}   有效像素样本 {r['n']}")
        print(f"   平均色 sRGB  ({s[0]:5.1f},{s[1]:5.1f},{s[2]:5.1f})   平均彩度 {r['sat']*255:5.1f}/255")
        print(f"   平均色 Linear({l[0]:.4f},{l[1]:.4f},{l[2]:.4f})"
              f"  ← shader 里 albedo 用的就是这一套")
        print("   主要色相族（按像素占比）：")
        for frac, c, (a, b) in r["hues"]:
            nm = hue_name(a, b)
            mx, mn = max(c), min(c)
            print(f"     {frac*100:5.1f}%   {nm:2s} {a:3d}-{b:3d}°   "
                  f"sRGB({c[0]:5.1f},{c[1]:5.1f},{c[2]:5.1f})  彩度 {mx-mn:5.1f}")
    print()
    print("=" * 92)


if __name__ == "__main__":
    main()
