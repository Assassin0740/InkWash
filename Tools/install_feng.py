#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""复原主角模型「武侠风格角色-锋」到 Assets/Char_Feng。

为什么需要这个脚本
------------------
该素材来自 **Unity 中国资源商店**（作者：冰橙，包 ID 20005749），价格 ¥0，
但授权是 **Unity 商店授权 + 作者另注「禁止二次贩卖」** —— 与 Quaternius/Kevin Iglesias
那批一样：可以用在工程里，**不能把素材本体再分发**。仓库一旦公开就构成再分发，
所以 `.gitignore` 把 `Assets/Char_Feng/` 整目录屏蔽（连同 .meta）。

代价是：**全新克隆 / 换机器会缺这个角色**，需要重跑本脚本。

取材方式
--------
从本机 Unity/团结引擎的 **Asset Store 下载缓存**里找原始 .unitypackage，解包落位。
本机缓存实测路径：
    %APPDATA%/Unity/Asset Store-5.x/冰橙/3D角色人形角色人类/assetstore_package_20005749.unitypackage

换个账号 / 换机器的话，先在编辑器里把该包「添加至我的资源 → 下载」，缓存到位后本脚本
就能自动找到（脚本会递归扫 Asset Store-5.x 下的 *20005749*.unitypackage，不写死目录名）。

解包用 Tools/extract_unitypackage.py —— 它按 <guid>/asset + pathname 还原，
**保住 .meta 里的 GUID**，所以引用不会断。

用法
----
    python Tools/install_feng.py --check     # 只体检现状
    python Tools/install_feng.py             # 安装（已存在且同尺寸则跳过）
    python Tools/install_feng.py --force     # 强制覆盖
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

PKG_ID = "20005749"
DST_DIR = os.path.join(PROJECT, "Assets", "Char_Feng")

# 解包后必须存在的关键文件（体检判据）
REQUIRED = [
    os.path.join(DST_DIR, "Fbx", "Feng.fbx"),
    os.path.join(DST_DIR, "Fbx", "Material_Pbr_Diffuse.jpg"),
    os.path.join(DST_DIR, "Animation", "Idle.anim"),
    os.path.join(DST_DIR, "Animation", "Walk.anim"),
    os.path.join(DST_DIR, "Animation", "Feng.controller"),
]


def find_package():
    """在 Asset Store 缓存里递归找 *20005749*.unitypackage。不写死发布者目录名。"""
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
            # 同名多个时取体积最大的（最可能是完整包）
            hits.sort(key=os.path.getsize, reverse=True)
            return hits[0]
    return None


def check(verbose=True):
    ok = True
    for p in REQUIRED:
        exists = os.path.exists(p)
        if verbose:
            print("  %s %s" % ("[OK]" if exists else "[X ]", os.path.relpath(p, PROJECT)))
        if not exists:
            ok = False
    if not ok and verbose:
        pkg = find_package()
        if pkg:
            print("\n  找到原始包：%s" % pkg)
            print("  跑 `python Tools/install_feng.py` 即可复原。")
        else:
            print("\n  [!] 本机 Asset Store 缓存里没找到 *%s*.unitypackage。" % PKG_ID)
            print("      请先在编辑器商店里把「武侠风格角色-锋」添加至我的资源并下载，再重跑。")
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

    # 只解包 Assets/Char_Feng 下的条目，落到工程根，pathname 自带的 Assets/ 前缀会自然对位
    planned, written, skipped = extract(pkg, PROJECT, only="Assets/Char_Feng")
    print("解包条目：计划 %d / 写出 %d / 跳过（已存在）%d" % (planned, written, skipped))
    print("完成。提醒：Assets/Char_Feng/ 不入库，新克隆请重跑本脚本。")
    return 0


def main():
    ap = argparse.ArgumentParser(description="复原主角模型「武侠风格角色-锋」（Unity 中国资源商店）")
    ap.add_argument("--check", action="store_true", help="只体检，不安装")
    ap.add_argument("--force", action="store_true", help="覆盖已存在的文件")
    args = ap.parse_args()

    if args.check:
        print("=== Char_Feng 体检 ===")
        sys.exit(0 if check() else 1)

    print("=== 安装「武侠风格角色-锋」 -> Assets/Char_Feng ===")
    rc = install(force=args.force)
    print()
    check()
    sys.exit(rc)


if __name__ == "__main__":
    main()
