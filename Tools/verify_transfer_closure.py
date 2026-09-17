# -*- coding: utf-8 -*-
"""
验证 transfer_closure.py 产出的搬运集是否**足够**：
把「入库资产 + 搬运集」当作目标机器磁盘的全部内容，看还有几处 guid 引用悬空。
0 处 = 闭包完整；>0 = 闭包漏了东西。

    python Tools/verify_transfer_closure.py
"""
import os
import re
import sys
import collections

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
ASSETS = os.path.join(ROOT, "Assets")
MANIFEST = os.path.join(TOOLS, "reports", "transfer_manifest.txt")

META_GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)
GUID_REF_RE = re.compile(r"guid:\s*([0-9a-f]{32})")
BUILTIN_RE = re.compile(r"^0{16}[0-9a-f]{16}$")
SCAN_EXT = (".prefab", ".unity", ".asset", ".mat", ".controller", ".anim",
            ".overrideController", ".playable", ".mixer", ".preset")


def rel(p):
    return os.path.relpath(p, ROOT).replace("\\", "/")


def main():
    # 搬运集（清单里的相对路径）
    keep = set()
    with open(MANIFEST, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith("#"):
                keep.add(line.replace("/", os.sep))

    # 目标机器上会存在的文件 = 入库资产（非 ignore）+ 搬运集
    # 入库资产 = Assets 下 .meta 中，其对应文件不在 ignore 目录里的
    IGN = ("Assets/ThirdParty", "Assets/Char_Feng", "Assets/W_Sword", "Assets/QFrameworkData")

    def is_ignored(r):
        if any(r == d or r.startswith(d + "/") for d in IGN):
            return True
        return r.lower().endswith((".mp3", ".wav")) and "BGM" in r

    gmap = {}
    for dp, _dn, fns in os.walk(ASSETS):
        for fn in fns:
            if not fn.endswith(".meta"):
                continue
            p = os.path.join(dp, fn)
            asset = p[:-5]
            r = rel(asset)
            # 只保留「目标机器上会有」的
            if is_ignored(r):
                if os.path.relpath(p, ROOT).replace("\\", os.sep) not in keep:
                    continue
            try:
                with open(p, encoding="utf-8", errors="ignore") as f:
                    t = f.read(4096)
            except OSError:
                continue
            mm = META_GUID_RE.search(t)
            if mm:
                gmap.setdefault(mm.group(1), asset)

    print("模拟目标机器可解析 guid: %d 个" % len(gmap))

    dangling = collections.defaultdict(set)
    for dp, _dn, fns in os.walk(ASSETS):
        for fn in fns:
            if not fn.lower().endswith(SCAN_EXT):
                continue
            p = os.path.join(dp, fn)
            r = rel(p)
            # 只检查「目标机器上会有」的引用方
            if is_ignored(r) and os.path.relpath(p, ROOT).replace("\\", os.sep) not in keep:
                continue
            try:
                with open(p, encoding="utf-8", errors="ignore") as f:
                    t = f.read()
            except OSError:
                continue
            for g in GUID_REF_RE.findall(t):
                if g in gmap or BUILTIN_RE.match(g):
                    continue
                dangling[r].add(g)

    total = sum(len(v) for v in dangling.values())
    print("\n悬空引用: %d 处，分布在 %d 个文件" % (total, len(dangling)))
    for r, gs in sorted(dangling.items(), key=lambda kv: -len(kv[1]))[:25]:
        print("   %-64s %d 处" % (r, len(gs)))
    print()
    print("（URP 的 URP-*-Renderer.asset / M_W_Sword.mat 等引用的是 UPM 包与 W_Sword，"
          "本脚本不含 PackageCache 与已单独复原的目录，属预期噪声。）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
