# -*- coding: utf-8 -*-
"""
跨机克隆依赖自检 —— 在**目标机器**的工程根目录运行：

    python Tools/check_clone_deps.py

背景
----
`/Assets/ThirdParty/`、`/Assets/Char_Feng/`、`/Assets/W_Sword/`、`/Assets/QFrameworkData/`
以及 BGM 都被 `.gitignore` 排除（授权 + 体积），所以全新克隆里它们一律缺失。
这些目录必须**含 `.meta` 整目录拷贝**过来 —— 漏拷 .meta 会让 Unity 重新生成 GUID，
所有 `{fileID:…, guid:…}` 引用当场静默断裂（不报错，只是模型/贴图/动作没了）。

本脚本不依赖 git，判据是「被引用的 guid 在磁盘上找不找得到定义」：
  · 找得到  → 已补齐
  · 找不到  → **漏拷**（或引用了未安装的包）
"""

import os
import re
import sys
import collections

TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS_DIR)
ASSETS = os.path.join(ROOT, "Assets")

# 这几个目录本身就是「不入库素材」，它们内部的互相引用不算工程级断链
ASSET_DIRS = ("Assets\\ThirdParty", "Assets\\QFrameworkData",
              "Assets\\Char_Feng", "Assets\\W_Sword")

# 需要整目录拷贝过来的东西（含目录级 .meta）
REQUIRED = [
    ("Assets/ThirdParty",     "第三方素材（Quaternius/KayKit/Ziyuan/Kenney/KevinIglesias/QFramework）"),
    ("Assets/ThirdParty.meta", "↑ 目录级 .meta"),
    ("Assets/Char_Feng",      "主角模型「武侠风格角色-锋」"),
    ("Assets/Char_Feng.meta", "↑ 目录级 .meta"),
    ("Assets/W_Sword",        "主角兵器「古代宝剑」"),
    ("Assets/W_Sword.meta",   "↑ 目录级 .meta"),
]

SCAN_EXT = (".prefab", ".unity", ".asset", ".mat", ".controller", ".anim",
            ".overrideController", ".playable", ".mixer", ".preset")

META_GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)
GUID_REF_RE = re.compile(r"guid:\s*([0-9a-f]{32})")

# Unity 内建资源的 guid 形态（全 0 前后缀），不必报警
BUILTIN_RE = re.compile(r"^0{16}[0-9a-f]{16}$")


def build_guid_map(bases, max_bytes=4096):
    """guid -> 定义它的文件。只读 meta 开头（guid 在头部，省时）。"""
    m = {}
    for base in bases:
        if not os.path.isdir(base):
            continue
        for dp, _dn, fns in os.walk(base):
            for fn in fns:
                if not fn.endswith(".meta"):
                    continue
                p = os.path.join(dp, fn)
                try:
                    with open(p, encoding="utf-8", errors="ignore") as f:
                        t = f.read(max_bytes)
                except OSError:
                    continue
                mm = META_GUID_RE.search(t)
                if mm:
                    m.setdefault(mm.group(1), p[:-5])
    return m


def norm(p):
    return os.path.relpath(p, ROOT).replace("/", "\\")


def in_asset_dir(relpath):
    return relpath.startswith(ASSET_DIRS)


def main():
    print("=" * 72)
    print("InkWash 跨机克隆依赖自检")
    print("工程根: %s" % ROOT)
    print("=" * 72)

    # ---- A. 必需目录是否到位 ----
    print()
    print("[A] 不入库素材目录")
    missing_dir = False
    for rel, desc in REQUIRED:
        p = os.path.join(ROOT, rel.replace("/", os.sep))
        if os.path.isdir(p):
            n = sum(len(f) for _d, _s, f in os.walk(p))
            print("    [OK ] %-28s %5d 个文件   %s" % (rel, n, desc))
        elif os.path.isfile(p):
            print("    [OK ] %-28s               %s" % (rel, desc))
        else:
            print("    [!! ] %-28s 缺失 —— %s" % (rel, desc))
            missing_dir = True

    # ---- B. BGM ----
    print()
    print("[B] BGM（也被 .gitignore 排除，克隆下来会没声音）")
    bgm_dir = os.path.join(ASSETS, "_Project", "Audio")
    bgm = []
    if os.path.isdir(bgm_dir):
        for dp, _dn, fns in os.walk(bgm_dir):
            if "BGM" not in dp:
                continue
            bgm += [f for f in fns if f.lower().endswith((".mp3", ".wav"))]
    if bgm:
        print("    [OK ] 找到 %d 个 BGM 文件" % len(bgm))
    else:
        print("    [!! ] 一个都没有 —— 需按 Docs/素材来源与授权清单.md 重新下载")

    # ---- C. guid 引用完整性 ----
    print()
    print("[C] 扫描 guid 引用（Assets + Library/PackageCache）")
    gmap = build_guid_map([ASSETS,
                           os.path.join(ROOT, "Library", "PackageCache"),
                           os.path.join(ROOT, "Packages")])
    print("    可解析的 guid 定义: %d 个" % len(gmap))

    refs_by_file = {}
    for dp, _dn, fns in os.walk(ASSETS):
        for fn in fns:
            if not fn.endswith(SCAN_EXT):
                continue
            p = os.path.join(dp, fn)
            try:
                with open(p, encoding="utf-8", errors="ignore") as f:
                    t = f.read()
            except OSError:
                continue
            gs = set(GUID_REF_RE.findall(t))
            if gs:
                refs_by_file[p] = gs

    project_missing = collections.defaultdict(set)   # 引用方 -> 缺失 guid
    asset_missing = 0
    builtin_skipped = 0
    for p, gs in refs_by_file.items():
        rel = norm(p)
        is_asset = in_asset_dir(rel)
        for g in gs:
            if g in gmap:
                continue
            if BUILTIN_RE.match(g):
                builtin_skipped += 1
                continue
            if is_asset:
                asset_missing += 1
            else:
                project_missing[rel].add(g)

    print("    Unity 内建 guid（已忽略）: %d 处" % builtin_skipped)
    print("    素材目录内部未解析引用:  %d 处（不影响工程）" % asset_missing)

    print()
    print("[D] 结论")
    bad = sum(len(v) for v in project_missing.values())
    if not project_missing and not missing_dir:
        print("    通过 —— 工程资产的全部外部引用都能在磁盘上找到定义。")
    else:
        if missing_dir:
            print("    !! 上表 [A] 有缺项，先补齐目录再复检。")
        if project_missing:
            print("    !! 有 %d 处工程引用找不到定义，分布在 %d 个文件："
                  % (bad, len(project_missing)))
            for rel in sorted(project_missing, key=lambda k: -len(project_missing[k]))[:20]:
                print("       %-62s %d 处" % (rel, len(project_missing[rel])))
            print()
            print("    常见原因：")
            print("      1) 漏拷 .meta（最常见）—— 整目录拷贝必须连 .meta 一起")
            print("      2) 拷了 .fbx 但没拷它引用的外部贴图")
            print("      3) Unity 尚未导入完（等进度条走完再跑本脚本）")
            print("      4) 引用了某个未安装的 UPM 包（查 Packages/manifest.json）")

    print()
    print("=" * 72)
    return 0


if __name__ == "__main__":
    sys.exit(main())
