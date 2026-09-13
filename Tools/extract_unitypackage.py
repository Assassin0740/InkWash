# -*- coding: utf-8 -*-
"""把 .unitypackage 解包到指定目录（保持 GUID，即 .meta 原样落位）。

.unitypackage 内部结构：
    <guid>/asset        —— 文件内容
    <guid>/asset.meta   —— 对应的 .meta（GUID 就在这里，保住它引用才不断）
    <guid>/pathname     —— 目标相对路径，例如 Assets/QFramework/xxx.cs

用法（命令行）:
    python extract_unitypackage.py <包路径> <输出根目录> [--strip-prefix Assets/QFramework] [--only 子串]
    python extract_unitypackage.py <包> <out> --dry     # 只看计划不写盘

用法（作为库）:
    from extract_unitypackage import extract
    extract("包.unitypackage", "输出根", only="Movement/Run", strip_prefix="Assets/A/B")
"""
import argparse
import os
import sys
import tarfile

sys.stdout.reconfigure(encoding="utf-8")


def norm(name):
    """tar 条目名可能带 './' 前缀，统一掉。"""
    return name.replace("\\", "/").lstrip("./")


def read_package(package, only=None, strip_prefix=None, exact=None):
    """读出 [(guid, pathname, asset_bytes, meta_bytes)]，不落盘。

    exact: 只保留「文件名去掉扩展名后恰好等于其中一项」的条目。
           解决 only 是子串匹配的连带问题——比如 only="HumanM@Walk01_Forward"
           会把 ForwardLeft / ForwardRight 一起捞进来。
    """
    plan = []
    exact_set = set(exact) if exact else None
    with tarfile.open(package, "r:gz") as tar:
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
            # 坑：Unity 导出的 .unitypackage 里，部分 pathname 的内容是
            #   "<路径>\n00"
            # （本例："Human Soldier Animations FREE.unitypackage" 全都是这样，
            #   而 Quaternius 的包没有）。只 strip() 去不掉中间那个换行，
            # 结果会把文件名拼成 "xxx.fbx\n00" → OSError: Invalid argument。
            # 统一按第一个换行切掉。
            raw_pn = tar.extractfile(pm).read().decode("utf-8", errors="replace")
            pathname = raw_pn.split("\n")[0].strip()
            if exact_set is not None:
                stem = os.path.splitext(os.path.basename(pathname))[0]
                if stem not in exact_set:
                    continue
            if only and only.lower() not in pathname.lower():
                continue
            if strip_prefix:
                sp = strip_prefix.replace("\\", "/").strip("/")
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
    return plan


def extract(package, outroot, only=None, strip_prefix=None, exact=None, dry=False, verbose=True):
    """解包并落盘，返回 (计划条数, 写出数, 跳过数)。"""
    if not os.path.exists(package):
        raise FileNotFoundError("找不到包: " + package)

    plan = read_package(package, only=only, strip_prefix=strip_prefix, exact=exact)
    if verbose:
        print("待解包条目: %d" % len(plan))
    if dry:
        for _, p, a, _ in plan[:60]:
            print("  %-78s %8d" % (p, len(a)))
        if len(plan) > 60:
            print("  ... 其余 %d 条省略" % (len(plan) - 60))
        return len(plan), 0, 0

    written = skipped = 0
    for guid, pathname, data, meta in plan:
        dst = os.path.join(outroot, pathname.replace("/", os.sep))
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

    if verbose:
        print("已写出: %d  跳过(已存在): %d" % (written, skipped))
        print("输出根: " + outroot)
    return len(plan), written, skipped


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("package")
    ap.add_argument("outroot", help="输出根目录（绝对路径）")
    ap.add_argument("--strip-prefix", default=None,
                    help="从 pathname 中去掉的前缀，例如 Assets/QFramework")
    ap.add_argument("--only", default=None, help="只解包 pathname 含该子串的条目")
    ap.add_argument("--dry", action="store_true")
    args = ap.parse_args()

    try:
        extract(args.package, args.outroot, only=args.only,
                strip_prefix=args.strip_prefix, dry=args.dry)
    except FileNotFoundError as e:
        print(e)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
