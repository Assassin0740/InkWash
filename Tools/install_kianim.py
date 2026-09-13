#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""安装 Kevin Iglesias《Human Soldier Animations FREE》的跑步/走路/待机片段到 Assets/ThirdParty。

为什么需要它
------------
Quaternius（UAL1 / UAL2）的跑步片段骨盆甩幅过大，观感就是「扭腰」。
实测（Tools/cs/s2_probe_run.py 同款方法，量 Hips 骨骼偏航峰峰值）：

    片段                          骨盆偏航 p-p    真人跑步参考
    Quaternius Sprint_Loop          42.7°        ≤20°   ← 明显过头
    Quaternius Jog_Fwd_Loop         55.3°        ≤20°   ← 更过头
    Kevin Iglesias Run01_Forward    50.5°        ——     （见下方"已知取舍"）

KI 这套是 Unity 商店里评价很高的「军人动作」包，跑姿更收、步幅与地面速度匹配
（实测原生 4.49 m/s、单步 1.35m、200 步/分，落在真人范围），是目前本工程能拿到的
最干净的成品跑步片段，所以 Run 状态改用它。

已知取舍
--------
骨盆偏航 50.5° 这个数**比 Quaternius 还大**，但肉眼观感明显更好（连拍对比见
Tools/screenshots/ab/1_Rig_Sprint_Loop_back.png vs 3_Run01_Forward_back.png）。
原因是这个指标把"髋线偏航 + 肩线偏航"直接相加，对**摆臂幅度**也很敏感，
不能单独当质量判据。真正决定观感的是：步频是否落在人类区间、脚是否打滑、
四肢是否同相位。换片段时建议以**连拍图**为准，指标只做辅助。

授权
----
Unity Asset Store 免费资源（Kevin Iglesias / Human Soldier Animations FREE）。
按 Unity Asset Store 标准 EULA 使用：可随本项目（毕设/非商用）使用，不可再分发素材本体。
因为 Assets/ThirdParty/ 不入库，新克隆请重跑本脚本。

用法
----
    python Tools/install_kianim.py            # 安装（已装则跳过）
    python Tools/install_kianim.py --check    # 只体检
    python Tools/install_kianim.py --dry      # 只看会解出哪些文件
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.stdout.reconfigure(encoding="utf-8")

from extract_unitypackage import extract  # noqa: E402

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 源：优先工程的 _RawDownloads（可入库/可搬运），否则退回本机 Asset Store 缓存
SRC_CANDIDATES = [
    os.path.join(PROJECT, "_RawDownloads", "KevinIglesias",
                 "Human Soldier Animations FREE.unitypackage"),
    os.path.join(os.environ.get("APPDATA", ""), "Unity", "Asset Store-5.x",
                 "Kevin Iglesias", "Animation", "Human Soldier Animations FREE.unitypackage"),
]

DST_DIR = os.path.join(PROJECT, "Assets", "ThirdParty", "KevinIglesias")

# 只装 Run 状态真正用到的那几个（体积 71MB 的包，其余 200+ 片段不落盘）。
# 用「文件名去扩展名后精确匹配」，否则 only 的子串匹配会把
# HumanM@Walk01_ForwardLeft / ForwardRight、HumanM@Idle01-MilitaryIdle01 一起捞进来。
WANTED_STEMS = [
    "HumanM@Run01_Forward",
    "HumanM@Walk01_Forward",
    "HumanM@Idle01",
    "Human Soldier Animations 2.0 FREE",
]

# 包里片段所在的公共前缀（去掉它，落到更短的 ThirdParty 路径下）
STRIP_PREFIX = "Assets/Kevin Iglesias/Human Animations"

# 验收：这些文件存在才算装好
REQUIRED = [
    "Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx",
    "Animations/Male/Movement/Walk/HumanM@Walk01_Forward.fbx",
    "Animations/Male/Idles/HumanM@Idle01.fbx",
]


def find_source():
    for p in SRC_CANDIDATES:
        if p and os.path.exists(p):
            return p
    return None


def check(verbose=True):
    ok = True
    missing = []
    for rel in REQUIRED:
        p = os.path.join(DST_DIR, rel.replace("/", os.sep))
        e = os.path.exists(p)
        if not e:
            missing.append(rel)
            ok = False
        if verbose:
            print("  %s %s" % ("[OK]" if e else "[--]", rel))
    if not ok and verbose:
        print("     缺失 %d 个，运行 python Tools/install_kianim.py 安装" % len(missing))
    return ok


def install(dry=False):
    pkg = find_source()
    if pkg is None:
        print("[X] 找不到 Human Soldier Animations FREE.unitypackage，已找过：")
        for p in SRC_CANDIDATES:
            print("      " + (p or "(空)"))
        print("    请从 Unity 商店装一次（Window > Package Manager > My Assets），")
        print("    或把它放到 _RawDownloads/KevinIglesias/ 下。")
        return 1

    print("源包: %s" % pkg)
    print("目标: %s" % os.path.relpath(DST_DIR, PROJECT))
    total = written = 0
    for stem in WANTED_STEMS:
        n, w, _ = extract(pkg, DST_DIR, exact=[stem], strip_prefix=STRIP_PREFIX,
                          dry=dry, verbose=False)
        total += n
        written += w
        print("  %-58s 命中 %d 写出 %d" % (stem, n, w))

    if dry:
        print("（--dry，未落盘）")
        return 0

    print("完成：命中 %d 条、写出 %d 个文件（跳过已存在）。" % (total, written))
    print()
    if not check():
        return 1
    print()
    print("提醒：Assets/ThirdParty/ 不入库，新克隆请重跑本脚本。")
    print("      装完回 Unity 等导入，然后重跑 Tools/cs/s2_setup_kianim.cs 确认导入设置。")
    return 0


def main():
    ap = argparse.ArgumentParser(description="安装 Kevin Iglesias Human Soldier Animations FREE")
    ap.add_argument("--check", action="store_true", help="只体检，不安装")
    ap.add_argument("--dry", action="store_true", help="只列出会解出的文件")
    args = ap.parse_args()

    if args.check:
        print("=== Kevin Iglesias 动画 体检 ===")
        sys.exit(0 if check() else 1)

    print("=== 安装 Kevin Iglesias -> Assets/ThirdParty/KevinIglesias ===")
    sys.exit(install(dry=args.dry))


if __name__ == "__main__":
    main()
