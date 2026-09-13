#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""安装 Quaternius Universal Animation Library（UAL1，CC0）到 Assets/ThirdParty。

为什么需要它
------------
工程原先只用了 UAL2（Universal Animation Library 2）。UAL2 的动作偏 RPG：
农场 / 盾牌 / 忍者 / 僵尸，**没有正经的走路循环，也没有任何跑步片段** ——
里面唯一的循环行走是 `Walk_Carry_Loop`（抱物负重走，躯干后仰）和
`Zombie_Walk_Fwd_Loop`（僵尸拖步）。这就是"走路仰着身子""没有跑步动作"的根源。

UAL1（2025-04 版，OpenGameArt 的 Standard 免费版，45 个片段）自带：
    Walk（前/后/左/右等 8 方向）、Jog、Sprint、Roll、Dodge、Idle(Normal/Formal/Sword)、
    Sword Attack、Hit、Death 等 —— 正好补齐 UAL1 缺的那几样。
两套库都是同一作者、同一套"通用人形骨架"（universal humanoid rig），
Unity 走 Humanoid 重定向即可混用。

授权
----
CC0 1.0（公有领域）。可自由用于个人/教育/商业项目，无需署名。
原始地址：
    https://opengameart.org/node/174563
    https://quaternius.com/packs/universalanimationlibrary.html

用法
----
    python Tools/install_ual1.py            # 安装（已装则跳过）
    python Tools/install_ual1.py --check    # 只体检
    python Tools/install_ual1.py --force    # 覆盖重装
"""
import argparse
import os
import shutil
import sys

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_ROOT = os.path.join(PROJECT, "_RawDownloads", "Quaternius", "UAL1", "extracted")
SRC_DIR = os.path.join(SRC_ROOT, "Animation Library[Standard]")
DST_DIR = os.path.join(PROJECT, "Assets", "ThirdParty", "Quaternius", "UniversalAnimationLibrary")

FBX_NAME = "AnimationLibrary_Unity_Standard.fbx"


def plan():
    """返回 [(源绝对路径, 目标绝对路径)]。"""
    return [
        (os.path.join(SRC_DIR, "Unity", FBX_NAME), os.path.join(DST_DIR, "Unity", FBX_NAME)),
        (os.path.join(SRC_DIR, "License.txt"), os.path.join(DST_DIR, "License.txt")),
        (os.path.join(SRC_DIR, "Preview.png"), os.path.join(DST_DIR, "Preview.png")),
        (os.path.join(SRC_DIR, "Unity_Setup.png"), os.path.join(DST_DIR, "Unity_Setup.png")),
    ]


def check(verbose=True):
    ok = True
    if not os.path.isdir(SRC_DIR):
        print("[X] 源目录不存在：%s" % SRC_DIR)
        print("    先把 universal_animation_librarystandard.zip 解压到 "
              "_RawDownloads/Quaternius/UAL1/extracted/")
        print("    下载地址：https://opengameart.org/sites/default/files/"
              "universal_animation_librarystandard.zip")
        return False
    for src, dst in plan():
        s = os.path.exists(src)
        d = os.path.exists(dst)
        if verbose:
            print("  %s 源=%s 目标=%s" % ("[OK]" if (s and d) else "[--]", s, d))
        if not (s and d):
            ok = False
    return ok


def install(force=False):
    if not os.path.isdir(SRC_DIR):
        print("[X] 源目录不存在：%s" % SRC_DIR)
        return 1
    copied = skipped = 0
    for src, dst in plan():
        if not os.path.exists(src):
            print("[!] 缺源文件，跳过：%s" % src)
            continue
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        if os.path.exists(dst) and not force:
            if os.path.getsize(src) == os.path.getsize(dst):
                skipped += 1
                continue
        shutil.copy2(src, dst)
        copied += 1
        print("  + %s" % os.path.relpath(dst, PROJECT))
    print("完成：复制 %d 个，跳过 %d 个。" % (copied, skipped))
    print("提醒：Assets/ThirdParty/ 不入库，新克隆请重跑本脚本。")
    return 0


def main():
    ap = argparse.ArgumentParser(description="安装 Quaternius UAL1（CC0）")
    ap.add_argument("--check", action="store_true", help="只体检，不安装")
    ap.add_argument("--force", action="store_true", help="覆盖已存在的文件")
    args = ap.parse_args()

    if args.check:
        print("=== UAL1 体检 ===")
        sys.exit(0 if check() else 1)

    print("=== 安装 UAL1 -> Assets/ThirdParty/Quaternius/UniversalAnimationLibrary ===")
    rc = install(force=args.force)
    print()
    check()
    sys.exit(rc)


if __name__ == "__main__":
    main()
