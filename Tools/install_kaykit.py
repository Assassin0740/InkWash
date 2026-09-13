#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""安装 KayKit 风格化角色 + 动画库（CC0）到 Assets/ThirdParty。

为什么是 KayKit
---------------
工程此前的主角是 Quaternius 的 UniversalBaseCharacters 模型 + UAL1/UAL2 动画
+ Kevin Iglesias 跑步片段 —— 三套来源拼在一起，靠 Unity Humanoid 重定向黏合。
反馈里反复出现的"走路仰身""跑步扭腰""像两条腿来回蹬"，根因就在这个拼装上：
动作是给另一副骨架做的，重定向只能保证姿势合法，保不住重心与节奏。

KayKit 的模型和动画是 **同一套 Rig_Medium 骨架**：把角色 FBX 和动画 FBX 一起丢进
工程、rig 都设成 Humanoid，动作直接对得上，不存在跨源重定向的偏差。

内容
----
    Adventurers/   9 个风格化角色（Knight / Barbarian / Rogue / Ranger / Mage /
                   Druid / Engineer …）+ 全套武器（剑/盾/斧/弓/弩/杖/药水）
    Skeletons/     6 个骷髅角色（Warrior / Rogue / Mage / Minion / Golem / Necromancer）
                   + 镰刀/巨锤/狼牙棒/骨盾等
    Animations/    Character Animations 1.1 全套 133 个片段，按类分文件：
                   General（待机/受击/死亡）、MovementBasic（走跑跳）、
                   MovementAdvanced（翻滚/潜行/攀爬）、CombatMelee（近战连招/格挡）、
                   CombatRanged（射击/瞄准/施法）、Simulation（社交动作）、
                   Special（骷髅专用）、Tools（工具动作）
                   —— Rig_Medium / Rig_Large 两套，对应常规体型与大体型角色。

授权
----
CC0 1.0（公有领域）。可自由用于个人/教育/商业项目，无需署名。
原始地址：
    https://kaylousberg.com/game-assets/character-animations
    https://kaylousberg.com/game-assets/character-pack-adventurers
本工程经 GitHub 镜像仓库获取（itch.io 在部分网络下不可达）：
    https://github.com/GeorgeQLe/assets-kaykit-3d-characters

用法
----
    python Tools/install_kaykit.py            # 安装（已装则跳过）
    python Tools/install_kaykit.py --check    # 只体检
    python Tools/install_kaykit.py --force    # 覆盖重装
"""
import argparse
import os
import shutil
import sys

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.join(PROJECT, "_RawDownloads", "KayKit", "assets-kaykit-3d-characters")
KK = os.path.join(REPO, "assets", "kaykit")
DST = os.path.join(PROJECT, "Assets", "ThirdParty", "KayKit")

ADV = os.path.join(KK, "adventurers-2.0")
SKE = os.path.join(KK, "skeletons-1.1")
ANIM = os.path.join(KK, "character-animations-1.1", "Animations", "fbx")


def _dir_files(src_dir, exts):
    """列出目录下（不递归）指定扩展名的文件。"""
    if not os.path.isdir(src_dir):
        return []
    out = []
    for name in sorted(os.listdir(src_dir)):
        p = os.path.join(src_dir, name)
        if os.path.isfile(p) and os.path.splitext(name)[1].lower() in exts:
            out.append(p)
    return out


def plan():
    """返回 [(源绝对路径, 目标绝对路径)]。"""
    pairs = []

    # --- 冒险者：角色 + 武器 ---
    for src in _dir_files(os.path.join(ADV, "Characters", "fbx"), {".fbx", ".png"}):
        pairs.append((src, os.path.join(DST, "Adventurers", "Characters", os.path.basename(src))))
    for src in _dir_files(os.path.join(ADV, "Assets", "fbx(unity)"), {".fbx", ".png"}):
        pairs.append((src, os.path.join(DST, "Adventurers", "Weapons", os.path.basename(src))))

    # --- 骷髅：角色 + 武器 ---
    for src in _dir_files(os.path.join(SKE, "characters", "fbx"), {".fbx", ".png"}):
        pairs.append((src, os.path.join(DST, "Skeletons", "Characters", os.path.basename(src))))
    for src in _dir_files(os.path.join(SKE, "assets", "fbx(unity)"), {".fbx", ".png"}):
        pairs.append((src, os.path.join(DST, "Skeletons", "Weapons", os.path.basename(src))))

    # --- 动画库：Rig_Medium / Rig_Large ---
    for rig in ("Rig_Medium", "Rig_Large"):
        for src in _dir_files(os.path.join(ANIM, rig), {".fbx"}):
            pairs.append((src, os.path.join(DST, "Animations", rig, os.path.basename(src))))

    # --- 授权 ---
    lic = os.path.join(REPO, "LICENSES", "KayKit-CC0-License.txt")
    pairs.append((lic, os.path.join(DST, "LICENSE.txt")))

    return pairs


# 体检时必须有产物的关键文件（角色 / 近战动画 / 授权）
REQUIRED = [
    os.path.join(DST, "Adventurers", "Characters", "Knight.fbx"),
    os.path.join(DST, "Adventurers", "Characters", "Rogue_Hooded.fbx"),
    os.path.join(DST, "Adventurers", "Weapons", "sword_1handed.fbx"),
    os.path.join(DST, "Skeletons", "Characters", "Skeleton_Warrior.fbx"),
    os.path.join(DST, "Animations", "Rig_Medium", "Rig_Medium_CombatMelee.fbx"),
    os.path.join(DST, "Animations", "Rig_Medium", "Rig_Medium_General.fbx"),
    os.path.join(DST, "Animations", "Rig_Medium", "Rig_Medium_MovementBasic.fbx"),
    os.path.join(DST, "LICENSE.txt"),
]


def check(verbose=True):
    ok = True
    if not os.path.isdir(REPO):
        print("[X] 源仓库不存在：%s" % REPO)
        print("    重新拉取（稀疏检出，只取需要的目录）：")
        print("      git clone --depth 1 --filter=blob:none --sparse "
              "https://github.com/GeorgeQLe/assets-kaykit-3d-characters.git")
        print("      git sparse-checkout set assets/kaykit LICENSES")
        return False
    for p in REQUIRED:
        exists = os.path.exists(p)
        if verbose:
            print("  %s %s" % ("[OK]" if exists else "[X ]", os.path.relpath(p, PROJECT)))
        if not exists:
            ok = False
    return ok


def install(force=False):
    if not os.path.isdir(REPO):
        print("[X] 源仓库不存在：%s" % REPO)
        return 1
    copied = skipped = missing = 0
    for src, dst in plan():
        if not os.path.exists(src):
            print("[!] 缺源文件，跳过：%s" % os.path.relpath(src, PROJECT))
            missing += 1
            continue
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        if os.path.exists(dst) and not force:
            if os.path.getsize(src) == os.path.getsize(dst):
                skipped += 1
                continue
        shutil.copy2(src, dst)
        copied += 1
    print("完成：复制 %d 个，跳过 %d 个，缺源 %d 个。" % (copied, skipped, missing))
    print("提醒：Assets/ThirdParty/ 不入库，新克隆请重跑本脚本。")
    return 0


def main():
    ap = argparse.ArgumentParser(description="安装 KayKit 角色 + 动画库（CC0）")
    ap.add_argument("--check", action="store_true", help="只体检，不安装")
    ap.add_argument("--force", action="store_true", help="覆盖已存在的文件")
    args = ap.parse_args()

    if args.check:
        print("=== KayKit 体检 ===")
        sys.exit(0 if check() else 1)

    print("=== 安装 KayKit -> Assets/ThirdParty/KayKit ===")
    rc = install(force=args.force)
    print()
    check()
    sys.exit(rc)


if __name__ == "__main__":
    main()
