#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""复原主角兵器「古代宝剑」到 Assets/W_Sword。

为什么需要这个脚本
------------------
该素材来自 **Unity 中国资源商店**（发布者：冰凛橙夏，包 ID 20003228），价格 ¥0，
授权是 **Unity 商店标准 EULA（不可再分发素材本体）** —— 与 Char_Feng / Kevin Iglesias
那批一样：可以用在工程里，**不能把素材本体再分发**。仓库一旦公开就构成再分发，
所以 `.gitignore` 把 `Assets/W_Sword/` 整目录屏蔽（连同 .meta）。

代价是：**全新克隆 / 换机器会缺这把剑**，需要重跑本脚本。
（本项目对这把剑做了二次加工：材质 `Assets/_Project/Art/Materials/M_W_Sword.mat`
 与预制体 `Assets/_Project/Prefabs/Weapons/W_Sword.prefab` 都入库，
 它们只引用贴图与网格的 GUID —— 所以只要本脚本把 GUID 原样恢复，引用就不会断。）

取材方式
--------
从本机 Unity/团结引擎的 **Asset Store 下载缓存**里找原始 .unitypackage，解包落位。
本机缓存实测路径（注意发布者目录名是「冰凛橙夏」）：
    %APPDATA%/Unity/Asset Store-5.x/冰凛橙夏/3D道具武器/assetstore_package_20003228.unitypackage

包内**只有 4 个文件**（没有 metallic 贴图 —— 别从别处补，会引入不同批次、GUID 对不上）：
    W_Sword.FBX                              约 30.6 MB
    texture_pbr_20250901.png                 4096²，base color
    texture_pbr_20250901_normal.png          4096²，法线
    texture_pbr_20250901_roughness.png       4096²，粗糙度

用法
----
    python Tools/install_w_sword.py --check     # 只体检现状
    python Tools/install_w_sword.py             # 安装（已存在且同尺寸则跳过）
    python Tools/install_w_sword.py --force     # 强制覆盖
"""
import argparse
import glob
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
sys.path.insert(0, HERE)

from extract_unitypackage import extract  # noqa: E402

PKG_ID = "20003228"
DST_DIR = os.path.join(PROJECT, "Assets", "W_Sword")

# 解包后必须存在的关键文件（体检判据）
REQUIRED = [
    os.path.join(DST_DIR, "W_Sword.FBX"),
    os.path.join(DST_DIR, "texture_pbr_20250901.png"),
    os.path.join(DST_DIR, "texture_pbr_20250901_normal.png"),
    os.path.join(DST_DIR, "texture_pbr_20250901_roughness.png"),
]


def find_package():
    """在 Asset Store 缓存里递归找 *20003228*.unitypackage。不写死发布者目录名。"""
    roots = []
    for env in ("APPDATA", "LOCALAPPDATA"):
        base = os.environ.get(env)
        if not base:
            continue
        roots.append(os.path.join(base, "Unity", "Asset Store-5.x"))
        roots.append(os.path.join(base, "Tuanjie", "Asset Store-5.x"))
        roots.append(os.path.join(base, "Unity", "Asset Store"))
    for r in roots:
        if not os.path.isdir(r):
            continue
        hits = glob.glob(os.path.join(r, "**", "*%s*.unitypackage" % PKG_ID), recursive=True)
        if hits:
            hits.sort(key=os.path.getsize, reverse=True)
            return hits[0]
    return None


def check(verbose=True):
    ok = True
    for p in REQUIRED:
        exists = os.path.exists(p)
        if verbose:
            size = ("%.1f MB" % (os.path.getsize(p) / 1048576.0)) if exists else "-"
            print("  %s %-46s %s" % ("[OK]" if exists else "[X ]", os.path.relpath(p, PROJECT), size))
    if not ok and verbose:
        pkg = find_package()
        if pkg:
            print("\n  找到原始包：%s" % pkg)
            print("  跑 `python Tools/install_w_sword.py` 即可复原。")
        else:
            print("\n  [!] 本机 Asset Store 缓存里没找到 *%s*.unitypackage。" % PKG_ID)
            print("      请先在编辑器商店里把「古代宝剑」添加至我的资源并下载，再重跑。")
    return ok


def install(force=False):
    pkg = find_package()
    if not pkg:
        print("[X] 找不到原始 .unitypackage（*%s*）。" % PKG_ID)
        print("    请先在编辑器商店里下载该免费素材，再重跑本脚本。")
        return 1
    print("源包：%s" % pkg)
    print("      （%.1f MB）" % (os.path.getsize(pkg) / 1048576.0))
    print("目标：%s" % os.path.relpath(DST_DIR, PROJECT))

    planned, written, skipped = extract(pkg, PROJECT, only="Assets/W_Sword")
    print("解包条目：计划 %d / 写出 %d / 跳过（已存在）%d" % (planned, written, skipped))
    print("完成。提醒：Assets/W_Sword/ 不入库，新克隆请重跑本脚本。")
    return 0


def main():
    ap = argparse.ArgumentParser(description="复原主角兵器「古代宝剑」（Unity 中国资源商店）")
    ap.add_argument("--check", action="store_true", help="只体检，不安装")
    ap.add_argument("--force", action="store_true", help="覆盖已存在的文件")
    args = ap.parse_args()

    if args.check:
        print("=== W_Sword 体检 ===")
        sys.exit(0 if check() else 1)

    print("=== 安装「古代宝剑」 -> Assets/W_Sword ===")
    rc = install(force=args.force)
    print()
    check()
    sys.exit(rc)


if __name__ == "__main__":
    main()
