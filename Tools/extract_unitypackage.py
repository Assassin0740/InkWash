# -*- coding: utf-8 -*-
"""把 .unitypackage 解包到指定目录（保持 GUID，即 .meta 原样落位）。

.unitypackage 内部结构：
    <guid>/asset        —— 文件内容
    <guid>/asset.meta   —— 对应的 .meta（GUID 就在这里，保住它引用才不断）
    <guid>/pathname     —— 目标相对路径，例如 Assets/QFramework/xxx.cs

用法:
    python extract_unitypackage.py <包路径> <输出根目录> [--strip-prefix Assets/QFramework] [--only 子串]
    python extract_unitypackage.py <包> <out> --dry     # 只看计划不写盘
"""
import argparse
import os
import sys
import tarfile

sys.stdout.reconfigure(encoding="utf-8")


def norm(name):
    """tar 条目名可能带 './' 前缀，统一掉。"""
    return name.replace("\\", "/").lstrip("./")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("package")
    ap.add_argument("outroot", help="输出根目录（绝对路径）")
    ap.add_argument("--strip-prefix", default=None,
                    help="从 pathname 中去掉的前缀，例如 Assets/QFramework")
    ap.add_argument("--only", default=None, help="只解包 pathname 含该子串的条目")
    ap.add_argument("--dry", action="store_true")
    args = ap.parse_args()

    if not os.path.exists(args.package):
        print("找不到包: " + args.package)
        return 1

    # 第一遍：把每个 guid 下需要的三件套读进内存（asset 可能是二进制）
    plan = []       # (guid, pathname, asset_bytes, meta_bytes)
    with tarfile.open(args.package, "r:gz") as tar:
        members = {}
        for m in tar:
            if not m.isfile():
                continue
            n = norm(m.name)
            seg = n.split("/")
            if len(seg) < 2:
                continue
            members.setdefault(seg[-2], {})[seg[-1]] = m

        for guid, files in members.items():
            pm = files.get("pathname")
            am = files.get("asset")
            if pm is None or am is None:
                continue
            pathname = tar.extractfile(pm).read().decode("utf-8", errors="replace").strip()
            if args.only and args.only.lower() not in pathname.lower():
                continue
            if args.strip_prefix:
                sp = args.strip_prefix.replace("\\", "/").strip("/")
                pn = pathname.replace("\\", "/")
                if pn == sp:
                    continue
                if pn.startswith(sp + "/"):
                    pathname = pn[len(sp) + 1:]
                else:
                    continue
            asset_bytes = tar.extractfile(am).read()
            meta_m = files.get("asset.meta")
            meta_bytes = tar.extractfile(meta_m).read() if meta_m is not None else None
            plan.append((guid, pathname, asset_bytes, meta_bytes))

    print("待解包条目: %d" % len(plan))
    if args.dry:
        for _, p, a, _ in plan[:60]:
            print("  %-78s %8d" % (p, len(a)))
        if len(plan) > 60:
            print("  ... 其余 %d 条省略" % (len(plan) - 60))
        return 0

    written = skipped = 0
    for guid, pathname, data, meta in plan:
        dst = os.path.join(args.outroot, pathname.replace("/", os.sep))
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        if os.path.exists(dst):
            skipped += 1
            continue
        with open(dst, "wb") as fh:
            fh.write(data)
        if meta is not None:
            with open(dst + ".meta", "wb") as fh:
                fh.write(meta)
        written += 1

    print("已写出: %d  跳过(已存在): %d" % (written, skipped))
    print("输出根: " + args.outroot)
    return 0


if __name__ == "__main__":
    sys.exit(main())
