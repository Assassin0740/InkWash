#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""盘一遍新导入的 VFX 素材包：贴图清单 + 材质用到的着色器 GUID。

为什么要盘
----------
用户从团结商店导入了 4 个免费粒子包（Particle Pack / WarFX / Fog Particles / SimpleFX）。
项目的既定对接方式是「**只取贴图**，材质挂项目自己的 URP Shader」
（见 Docs/墨龙特效设计.md §八）——因为这批包的材质用的是内置管线着色器，
在 URP 下会渲染成**洋红**。所以真正要盘的是贴图，不是材质。

输出：Tools/reports/vfx_packs_scan.txt
"""
import io
import os
import re
import sys
import collections

sys.stdout.reconfigure(encoding="utf-8")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
os.chdir(ROOT)

PACKS = [
    ("Particle Pack（Unity 官方）", "Assets/UnityTechnologies"),
    ("WarFX（战争粒子）", "Assets/WarFX Assets"),
    ("Fog Particles（雾颗粒）", "Assets/Fog Particles"),
    ("SimpleFX（卡通粒子）", "Assets/SimpleFX"),
]

TEX_EXT = (".png", ".tga", ".tif", ".tiff", ".exr", ".jpg", ".psd", ".bmp")

# 与墨龙特效需求相关的关键词（设计文档 §八：青白细电弧 + 纯墨黑烟）
WANT = {
    "电弧/电光": ["lightning", "electric", "spark", "energy", "plasma", "shock", "glow"],
    "烟/雾": ["smoke", "fog", "mist", "steam", "cloud", "dust", "haze"],
    "通用闪光/拖尾": ["flash", "trail", "flare", "star", "ring", "circle", "slash", "streak"],
}


def walk_files(d):
    for root, _ds, fs in os.walk(d):
        for f in fs:
            yield os.path.join(root, f)


def main():
    sb = io.StringIO()

    def p(*a):
        s = " ".join(str(x) for x in a)
        print(s)
        sb.write(s + "\n")

    p("=" * 78)
    p("新导入 VFX 素材包盘点")
    p("=" * 78)

    # ---------- 1) 每个包的规模与扩展名分布 ----------
    p("\n── 1. 包规模 ──")
    all_tex = []
    guid2name = {}
    for label, d in PACKS:
        if not os.path.isdir(d):
            p("  [缺失] %s  (%s)" % (label, d))
            continue
        n = size = 0
        ext = collections.Counter()
        for f in walk_files(d):
            n += 1
            try:
                size += os.path.getsize(f)
            except OSError:
                pass
            ext[os.path.splitext(f)[1].lower()] += 1
            if f.endswith(".meta"):
                t = io.open(f, "r", encoding="utf-8", errors="replace").read()
                g = re.search(r"^guid:\s*([0-9a-f]+)", t, re.M)
                if g:
                    guid2name[g.group(1)] = f[:-5]
        p("  %-24s %5d 文件  %8.2f MB" % (label, n, size / 1048576.0))
        p("        %s" % dict(sorted(ext.items(), key=lambda t: -t[1])[:9]))
        for f in walk_files(d):
            if f.lower().endswith(TEX_EXT):
                all_tex.append((f, os.path.getsize(f)))

    # ---------- 2) 贴图清单（按需求分类） ----------
    p("\n── 2. 贴图清单（%d 张）──" % len(all_tex))
    for group, kws in WANT.items():
        rows = []
        for f, sz in all_tex:
            low = os.path.basename(f).lower()
            if any(k in low for k in kws):
                rows.append((f, sz))
        rows.sort(key=lambda t: (os.path.dirname(t[0]), t[0]))
        p("\n  【%s】命中 %d 张" % (group, len(rows)))
        for f, sz in rows[:40]:
            p("    %8.1f KB  %s" % (sz / 1024.0, f))
        if len(rows) > 40:
            p("    … 其余 %d 张省略" % (len(rows) - 40))

    # ---------- 3) 材质用到的着色器 ----------
    p("\n── 3. 材质 → 着色器（判 URP 是否变洋红）──")
    mats = []
    for _label, d in PACKS:
        for f in walk_files(d) if os.path.isdir(d) else []:
            if f.endswith(".mat"):
                mats.append(f)
    by_shader = collections.defaultdict(list)
    for m in mats:
        t = io.open(m, "r", encoding="utf-8", errors="replace").read()
        g = re.search(r"m_Shader:\s*\{\s*fileID:\s*\d+,\s*guid:\s*([0-9a-f]+)", t)
        guid = g.group(1) if g else "(无)"
        by_shader[guid].append(m)
    p("  材质总数 %d，用到 %d 个不同着色器" % (len(mats), len(by_shader)))
    for guid, lst in sorted(by_shader.items(), key=lambda t: -len(t[1])):
        known = guid2name.get(guid)
        p("    %4d 个材质  guid=%s  %s" % (len(lst), guid, ("→ " + known) if known else "(内建/包内，需在编辑器里解名字)"))
        p("              例: %s" % ", ".join(os.path.basename(x) for x in lst[:3]))

    out = os.path.join(ROOT, "Tools", "reports", "vfx_packs_scan.txt")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    io.open(out, "w", encoding="utf-8").write(sb.getvalue())
    print("\n→ 报告写入 Tools/reports/vfx_packs_scan.txt")


if __name__ == "__main__":
    main()
