# -*- coding: utf-8 -*-
"""
跨机素材推送 —— 在**源机器**上运行，把搬运集写到目标机器的工程根。

    # 精确模式（默认）：按 Tools/reports/transfer_manifest.txt 逐文件复制 ≈168 MB / 78 文件
    python Tools/push_transfer.py --target "\\\\192.168.16.102\\e$\\Programs\\Project\\InkWash"

    # 整目录模式：Assets/ThirdParty 全量（≈640 MB），排除两边字节一致的龙 fbx
    python Tools/push_transfer.py --target "E:\\" --full

    # 预演：只打印要做什么，不落盘
    python Tools/push_transfer.py --target "E:\\" --dry-run

为什么要有这个脚本
------------------
`robocopy` 的 `/IF` 只吃通配符、**不吃清单文件**，而"精确搬运"必须按 guid 闭包
逐文件来（清单由 `Tools/transfer_closure.py` 产出）。整目录那种粗活才交给 robocopy。

纪律
----
被引用的**目录自身 .meta** 也在清单里（`ThirdParty.meta` / `KayKit.meta` …）。
漏掉任何一个，Unity 都会重新生成 GUID，prefab 里所有 `{fileID:…, guid:…}` 当场静默断裂。
"""

import argparse
import os
import shutil
import sys

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
MANIFEST = os.path.join(TOOLS, "reports", "transfer_manifest.txt")

# 两边已确认字节一致（md5 fd6907f2…）→ 整目录模式下没必要再传 13 MB
FULL_SKIP = {os.path.join("Assets", "ThirdParty", "Ziyuan", "_fbx", "chinese_dragon.fbx"),
             os.path.join("Assets", "ThirdParty", "Ziyuan", "_fbx", "chinese_dragon.fbx.meta")}


def human(n):
    return "%.1f MB" % (n / 1048576.0)


def push_precise(target, dry):
    with open(MANIFEST, encoding="utf-8") as f:
        files = [l.strip() for l in f if l.strip() and not l.startswith("#")]

    done = miss = 0
    total = 0
    for rel in files:
        src = os.path.join(ROOT, rel.replace("/", os.sep))
        if not os.path.isfile(src):
            print("  [缺·源] %s" % rel)
            miss += 1
            continue
        dst = os.path.join(target, rel.replace("/", os.sep))
        if not dry:
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(src, dst)
        total += os.path.getsize(src)
        done += 1
        if done % 20 == 0:
            print("  已复制 %d/%d" % (done, len(files)))
    print("精确模式: 复制 %d / 源缺失 %d / 共 %s" % (done, miss, human(total)))
    return 0 if miss == 0 else 1


def push_full(target, dry):
    src_root = os.path.join(ROOT, "Assets", "ThirdParty")
    dst_root = os.path.join(target, "Assets", "ThirdParty")
    done = 0
    total = 0
    skipped = 0
    for dp, _dn, fns in os.walk(src_root):
        for fn in fns:
            src = os.path.join(dp, fn)
            rel = os.path.relpath(src, src_root)
            if rel in FULL_SKIP:
                skipped += 1
                continue
            dst = os.path.join(dst_root, rel)
            if not dry:
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                shutil.copy2(src, dst)
            total += os.path.getsize(src)
            done += 1
            if done % 200 == 0:
                print("  已复制 %d（%s）" % (done, human(total)))
    print("整目录模式: 复制 %d 个文件 / %s（跳过 %d 个已一致的龙 fbx）"
          % (done, human(total), skipped))
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--target", required=True,
                    help="目标机器上的工程根，如 \\\\192.168.16.102\\e$\\Programs\\Project\\InkWash")
    ap.add_argument("--full", action="store_true", help="整目录复制 Assets/ThirdParty")
    ap.add_argument("--dry-run", action="store_true", help="只预演，不落盘")
    a = ap.parse_args()

    target = a.target.rstrip("\\/")
    print("源 : %s" % ROOT)
    print("目标: %s" % target)
    print("模式: %s%s" % ("整目录 ThirdParty" if a.full else "精确闭包",
                          "（预演）" if a.dry_run else ""))

    # 可达性自检：目标父目录必须存在（UNC/盘符都要）
    probe = target if os.path.isdir(target) else os.path.dirname(target)
    if not a.dry_run and not os.path.isdir(probe):
        print("\n[!] 目标不可达: %s" % probe)
        print("    两机不在同一网络时，先接入同一 Wi-Fi/热点，或改用 U 盘。")
        return 2

    print()
    rc = push_full(target, a.dry_run) if a.full else push_precise(target, a.dry_run)
    print()
    print("下一步（目标机器）: python Tools/check_clone_deps.py")
    print("  判据 = 工程级悬空引用降到 0；**不要**看 ThirdParty 的文件数（Unity 自己会补 .meta）")
    return rc


if __name__ == "__main__":
    sys.exit(main())
