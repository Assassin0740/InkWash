# -*- coding: utf-8 -*-
"""列出 .unitypackage 的内容清单（不安装，只看结构）。

.unitypackage 实质是 gzip 压缩的 tar，内部按 GUID 分目录：
    <guid>/asset        —— 文件内容
    <guid>/asset.meta   —— 对应的 .meta
    <guid>/pathname     —— 目标路径，例如 Assets/QFramework/Framework/Core/xxx.cs

用法:
    python peek_unitypackage.py <包路径> [--filter 关键字] [--top N]
"""
import argparse
import io
import os
import sys
import tarfile
from collections import Counter

sys.stdout.reconfigure(encoding="utf-8")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("package", help=".unitypackage 路径")
    ap.add_argument("--filter", default=None, help="只看路径含该关键字的条目")
    ap.add_argument("--top", type=int, default=0, help="只打印前 N 条")
    ap.add_argument("--tree", action="store_true", help="按目录聚合统计")
    args = ap.parse_args()

    if not os.path.exists(args.package):
        print("找不到包: " + args.package)
        return 1

    entries = {}
    total_bytes = 0
    with tarfile.open(args.package, "r:gz") as tar:
        for m in tar:
            if not m.isfile():
                continue
            parts = m.name.split("/")
            if len(parts) != 2:
                continue
            guid, fname = parts
            entries.setdefault(guid, {})[fname] = m.size
            if fname == "asset":
                total_bytes += m.size

    print("GUID 条目数: %d" % len(entries))
    print("asset 总体积: %.2f MB" % (total_bytes / 1024.0 / 1024.0))
    print()

    # 需要重新打开一次来读 pathname 内容
    paths = []
    with tarfile.open(args.package, "r:gz") as tar:
        for m in tar:
            if not m.isfile() or not m.name.endswith("/pathname"):
                continue
            guid = m.name.split("/")[0]
            # 部分 Unity 导出的包里 pathname 内容是 "<路径>\n00"（见
            # extract_unitypackage.py 里的同款注释）。按第一个换行切掉，
            # 否则清单里每条路径后面都会多出一行 "00"。
            raw = tar.extractfile(m).read().decode("utf-8", errors="replace")
            data = raw.split("\n")[0].strip()
            paths.append((guid, data))

    paths.sort(key=lambda x: x[1])
    print("实际落地文件数: %d" % len(paths))

    if args.filter:
        paths = [p for p in paths if args.filter.lower() in p[1].lower()]
        print("匹配 %r 的条目: %d" % (args.filter, len(paths)))
    print()

    if args.tree:
        # 按「前 3 层目录」聚合
        buckets = Counter()
        for _, p in paths:
            seg = p.replace("\\", "/").split("/")
            key = "/".join(seg[:4]) if len(seg) >= 4 else "/".join(seg[:3])
            buckets[key] += 1
        for k, v in sorted(buckets.items()):
            print("  %-70s %4d" % (k, v))
        print()
        exts = Counter(os.path.splitext(p)[1].lower() or "<无扩展名>" for _, p in paths)
        print("按扩展名:")
        for k, v in exts.most_common():
            print("  %-14s %4d" % (k, v))
        print()
        # 顶层目录
        roots = Counter()
        for _, p in paths:
            seg = p.replace("\\", "/").split("/")
            roots["/".join(seg[:2])] += 1
        print("按顶层目录:")
        for k, v in sorted(roots.items()):
            print("  %-50s %4d" % (k, v))
        print()
    else:
        shown = paths[: args.top] if args.top else paths
        for _, p in shown:
            print(p)
        if args.top and len(paths) > args.top:
            print("... 其余 %d 条省略" % (len(paths) - args.top))
    return 0


if __name__ == "__main__":
    sys.exit(main())
