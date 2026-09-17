# -*- coding: utf-8 -*-
"""导入新鲜度检查：Library 有没有跟上工作区的资产？

为什么需要它
------------
Unity 的导入产物（`Library/ArtifactDB`、`Library/Artifacts/`）落后于工作区时，
**编辑模式读到的 asset 仍可能是旧版**，甚至出现「引用解析不出来 ⇒ mesh / 材质 全 null」
这种看起来像"资产坏了"的假象。

本工程的真实案例：`Z_Dragon.prefab`、`Z_Enemy_MoLong.prefab` 的文件 mtime 是
09-17 14:44（那边重建的新版），而 `Library/ArtifactDB` 停在 09-17 01:02
⇒ 相差 13.7 小时 ⇒ 编辑模式读 `Z_Enemy_MoLong.prefab` 的 SMR 得到
`sharedMesh=null / sharedMaterials=null`。**进 Play 前导入完成后一切正常。**

判据
----
`资产 mtime > Library/ArtifactDB mtime`  ⇒  该资产尚未重新导入，编辑模式读数不可信。
注意这是**充分不必要**的近似判据（看的是库文件整体时间戳），但它能一眼捞出
"这批新改的文件还没被 Unity 吃进去"。

用法
----
    python Tools/check_import_freshness.py                 # 扫 Assets 下所有资产
    python Tools/check_import_freshness.py --top 30
    python Tools/check_import_freshness.py --paths a.prefab b.mat
"""
import argparse
import datetime
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
os.chdir(ROOT)

EXT = (".prefab", ".unity", ".asset", ".mat", ".controller", ".anim",
       ".overrideController", ".playbook", ".mixer", ".preset",
       ".fbx", ".shader", ".cs")
SKIP_DIR = ("Library", "Temp", "Logs", "obj", "Build", "Builds", ".git",
            "PackageCache", "node_modules", "_RawDownloads")


def ts(t):
    return datetime.datetime.fromtimestamp(t).strftime("%m-%d %H:%M:%S")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--top", type=int, default=25, help="最多列出多少个未导入资产")
    ap.add_argument("--paths", nargs="*", help="只查这些路径（相对工程根）")
    ap.add_argument("--all", action="store_true", help="列出全部，不只 top N")
    args = ap.parse_args()

    libs = [os.path.join("Library", n) for n in ("ArtifactDB", "SourceAssetDB")]
    lib_m = 0.0
    print("[A] Library 基准时间")
    for lb in libs:
        if os.path.exists(lb):
            m = os.path.getmtime(lb)
            lib_m = max(lib_m, m)
            print("    %-28s %s" % (lb, ts(m)))
        else:
            print("    %-28s (不存在)" % lb)
    if lib_m == 0.0:
        print("\n[!] 找不到 Library 基准文件 —— 工程从未打开过，本检查无意义。")
        return 0

    print("\n[B] 资产扫描")
    stale = []
    total = 0
    if args.paths:
        cands = [p for p in args.paths if os.path.isfile(p)]
    else:
        cands = []
        for dp, dns, fns in os.walk("Assets"):
            dns[:] = [d for d in dns if d not in SKIP_DIR]
            for fn in fns:
                if fn.endswith(EXT) and not fn.endswith(".meta"):
                    cands.append(os.path.join(dp, fn))
    for p in cands:
        total += 1
        try:
            m = os.path.getmtime(p)
        except OSError:
            continue
        if m > lib_m:
            stale.append((m, p))

    stale.sort(reverse=True)
    print("    扫描资产 %d 个，晚于 Library 的 %d 个" % (total, len(stale)))

    if stale:
        print("\n[C] 尚未重新导入的资产（编辑模式读数不可信）")
        n = len(stale) if args.all else min(args.top, len(stale))
        for m, p in stale[:n]:
            print("    %s  %s" % (ts(m), p.replace(os.sep, "/")))
        if n < len(stale):
            print("    ... 另有 %d 个（--all 看全部）" % (len(stale) - n))
        print("\n[D] 结论")
        print("    有资产比 Library 新 ⇒ **Unity 还没吃进这批改动**。")
        print("    处置：在 Unity 里 Assets → Refresh（Ctrl+R）或重开工程，等导入完成后再读数。")
        print("    ★ 只改资产的 mtime 不代表生效 —— 判据是「读数是否变化」，不是「refresh 有没有返回值」。")
    else:
        print("\n[D] 结论")
        print("    通过 —— 没有资产晚于 Library，导入产物是最新的。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
