#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Unity 中国资源商店 · 免费包安装器（保 GUID，落位到 Assets/ThirdParty/）

为什么要"安装器"而不是直接 Import
----------------------------------
Unity 商店的那批素材（冰橙、Quaternius、Kevin Iglesias …）授权都是
「可用在工程里，但**不可再分发素材本体**」。仓库一旦公开就构成再分发，
所以这些目录必须 `.gitignore` 屏蔽 —— 代价是**全新克隆会缺素材**。

本脚本就是补这个缺口：从本机商店下载缓存里找到原始 .unitypackage，解包复原。

和 Tools/install_feng.py 的区别
-------------------------------
install_feng.py 是「主角模型」的专用脚本（目标目录写死在 Assets/Char_Feng）。
本脚本是**通用**的：给一个包 ID 就能装，包内路径自动探测，落位到
    Assets/ThirdParty/<发布者>/<素材名>/
所以换素材不用再写新脚本。用 --dry 可以只看包里有什么，不写盘。

用法
----
    # 看包里有什么（不写盘）—— 拿到新包先跑这个
    python Tools/install_store_pkg.py --id 20003228 --dry

    # 真装
    python Tools/install_store_pkg.py --id 20003228 --publisher 冰橙 --name 古代宝剑

    # 体检（装完 / 新克隆后自检）
    python Tools/install_store_pkg.py --id 20003228 --publisher 冰橙 --name 古代宝剑 --check

包不在缓存里怎么办
------------------
去 https://assetstore.u3d.cn/ 对应商品页点「添加至我的资源」，
再在编辑器 Window > Package Manager > My Assets 里 Download。
下载完成后包会落到：
    %APPDATA%/Unity/Asset Store-5.x/<发布者>/<分类>/assetstore_package_<id>.unitypackage
（本脚本递归 glob 找，不写死发布者目录名 —— 中文名可能变）
"""
import argparse
import glob
import os
import shutil
import sys

sys.stdout.reconfigure(encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
sys.path.insert(0, HERE)

from extract_unitypackage import read_package, extract  # noqa: E402

STORE_URL = "https://assetstore.u3d.cn/packages/x/assetstore-package-%s"


# --------------------------------------------------------------------------
# 找包
# --------------------------------------------------------------------------
def cache_roots():
    """可能存放商店下载缓存的根目录。不写死发布者/分类子目录。"""
    roots = []
    for env in ("APPDATA", "LOCALAPPDATA"):
        base = os.environ.get(env)
        if not base:
            continue
        for app in ("Unity", "Tuanjie"):
            roots.append(os.path.join(base, app, "Asset Store-5.x"))
            roots.append(os.path.join(base, app, "Asset Store"))
    return roots


def find_package(pkg_id):
    for r in cache_roots():
        if not os.path.isdir(r):
            continue
        hits = glob.glob(os.path.join(r, "**", "*%s*.unitypackage" % pkg_id), recursive=True)
        if hits:
            hits.sort(key=os.path.getsize, reverse=True)   # 同名取最大（最可能完整）
            return hits[0]
    return None


# --------------------------------------------------------------------------
# 探测包内结构
# --------------------------------------------------------------------------
def top_level_dirs(plan):
    """从 pathname 列表里归纳出 `Assets/<X>` 一级目录，返回 {一级目录: 条目数}。

    有的包会在 Assets/ 下再分好层（常见），也有直接 Assets/xxx.fbx 平铺的。
    平铺的情况归到一个哨兵名 "(Assets 根)" 下。
    """
    import collections
    c = collections.Counter()
    for _, pn, _, _ in plan:
        p = pn.replace("\\", "/")
        if not p.startswith("Assets/"):
            c["(Assets 之外)"] += 1
            continue
        rest = p[len("Assets/"):]
        head = rest.split("/")[0]
        c[head if "/" in rest else "(Assets 根平铺)"] += 1
    return c


def install_target(dest_root, plan, publisher, name):
    """算出每条 pathname 的新相对路径：把包内的 Assets/<X> 整体搬到
    Assets/ThirdParty/<publisher>/<name>/<X>。

    为什么要搬：包作者的一级目录名不可控（可能叫 Assets/Prefabs），直接落到工程根
    有和现有目录撞名的风险。整体搬迁**不破引用** —— Unity 引用走 GUID，不走路径，
    而 .meta（GUID 所在）是跟着文件一起搬的。
    """
    out = []
    prefix = "Assets/ThirdParty/%s/%s" % (publisher, name)
    for guid, pn, data, meta in plan:
        p = pn.replace("\\", "/")
        if not p.startswith("Assets/"):
            continue                       # Assets 外的东西（如 Packages/、ProjectSettings/）一律不碰
        rest = p[len("Assets/"):]
        out.append((guid, os.path.join(prefix.replace("/", os.sep), rest.replace("/", os.sep)),
                    data, meta))
    return out


# --------------------------------------------------------------------------
# 模式
# --------------------------------------------------------------------------
def do_check(pkg_id, publisher, name):
    dest = os.path.join(PROJECT, "Assets", "ThirdParty", publisher, name)
    print("=== 体检：%s/%s（包 %s）===" % (publisher, name, pkg_id))
    if not os.path.isdir(dest):
        print("  [X] 目标目录不存在：%s" % os.path.relpath(dest, PROJECT))
        _hint_missing_pkg(pkg_id)
        return 1
    n_files = n_meta = 0
    for root, _dirs, files in os.walk(dest):
        for f in files:
            if f.endswith(".meta"):
                n_meta += 1
            else:
                n_files += 1
    print("  [OK] %s" % os.path.relpath(dest, PROJECT))
    print("       %d 个资源文件 / %d 个 .meta" % (n_files, n_meta))
    if n_meta < n_files:
        print("  [!] .meta 比资源文件少 —— 可能丢 GUID，引用会断")
    print("  提示：该目录不入库，新克隆请重跑本脚本（不加 --check）。")
    return 0


def _hint_missing_pkg(pkg_id):
    pkg = find_package(pkg_id)
    if pkg:
        print("\n  缓存里找到原始包：%s" % pkg)
        print("  直接重跑本脚本（去掉 --check）即可安装。")
    else:
        print("\n  [!] 本机商店缓存里没有 *%s*.unitypackage。" % pkg_id)
        print("      需要人工下一个（账号门禁，脚本拿不到）：")
        print("      1) 打开 %s" % (STORE_URL % pkg_id))
        print("      2) 点「添加至我的资源」（需登录 Unity 中国账号，免费）")
        print("      3) 编辑器 Window > Package Manager > My Assets → Download")
        print("      缓存落点：%%APPDATA%%/Unity/Asset Store-5.x/<发布者>/<分类>/assetstore_package_%s.unitypackage" % pkg_id)


def do_dry(pkg_id):
    pkg = find_package(pkg_id)
    if not pkg:
        print("[X] 缓存里没有 *%s*.unitypackage" % pkg_id)
        _hint_missing_pkg(pkg_id)
        return 1
    print("源包：%s" % pkg)
    print("      %.1f MB" % (os.path.getsize(pkg) / 1048576.0))
    plan = read_package(pkg)
    print("条目：%d" % len(plan))
    print("\n=== 包内 Assets 一级目录分布 ===")
    for k, v in top_level_dirs(plan).most_common():
        print("  %-40s %5d 条" % (k, v))
    print("\n=== 前 40 条路径 ===")
    for _, pn, data, _ in sorted(plan, key=lambda t: t[1])[:40]:
        print("  %-72s %9d" % (pn, len(data)))
    if len(plan) > 40:
        print("  ... 其余 %d 条省略" % (len(plan) - 40))
    print("\n=== 扩展名分布 ===")
    import collections
    ext = collections.Counter(os.path.splitext(pn)[1].lower() for _, pn, _, _ in plan)
    for e, n in ext.most_common(20):
        print("  %-12s %4d" % (e or "(无)", n))
    return 0


def do_install(pkg_id, publisher, name, force=False):
    pkg = find_package(pkg_id)
    if not pkg:
        print("[X] 找不到原始 .unitypackage（*%s*）" % pkg_id)
        _hint_missing_pkg(pkg_id)
        return 1

    print("源包：%s" % pkg)
    print("      %.1f MB" % (os.path.getsize(pkg) / 1048576.0))

    plan = read_package(pkg)
    print("包内条目：%d" % len(plan))

    items = install_target(".", plan, publisher, name)
    dest_root = os.path.join(PROJECT, "Assets", "ThirdParty", publisher, name)
    rel = os.path.relpath(dest_root, PROJECT)
    print("目标：%s" % rel)
    if not items:
        print("[X] 包里没有 Assets/ 下的条目，没什么可装。")
        return 1

    # 先落暂存区：中途失败不会污染 Assets/
    stage = os.path.join(PROJECT, "Temp", "_storepkg_%s" % pkg_id)
    if os.path.isdir(stage):
        shutil.rmtree(stage, ignore_errors=True)
    os.makedirs(stage, exist_ok=True)

    written = skipped = 0
    for _guid, relpath, data, meta in items:
        dst = os.path.join(stage, relpath)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        final = os.path.join(PROJECT, relpath)
        if os.path.exists(final) and not force:
            skipped += 1
            continue
        with open(dst, "wb") as fh:
            fh.write(data)
        if meta is not None:
            with open(dst + ".meta", "wb") as fh:
                fh.write(meta)
        written += 1

    print("解包：写出 %d / 跳过（已存在，用 --force 覆盖）%d" % (written, skipped))
    if written == 0:
        print("没有新文件，暂存区已清理。")
        shutil.rmtree(stage, ignore_errors=True)
        return 0

    # 暂存 → 正式位置（连同 .meta，整体搬迁；Unity 引用走 GUID 不会断）
    os.makedirs(dest_root, exist_ok=True)
    for entry in os.listdir(stage):
        src = os.path.join(stage, entry)
        dst = os.path.join(dest_root, entry)
        if os.path.isdir(src):
            if os.path.isdir(dst):
                shutil.rmtree(dst, ignore_errors=True)
            shutil.move(src, dst)
        else:
            shutil.copy2(src, dst)
    shutil.rmtree(stage, ignore_errors=True)

    print("\n完成 → %s" % rel)
    print("记得确认 .gitignore 里有这一行（不可再分发）：")
    print("    /%s/" % rel.replace("\\", "/"))
    print("    /%s.meta" % rel.replace("\\", "/"))
    return 0


def main():
    ap = argparse.ArgumentParser(description="Unity 中国资源商店免费包安装器（保 GUID）")
    ap.add_argument("--id", required=True, help="assetstore-package-<数字 ID> 里的数字，如 20003228")
    ap.add_argument("--publisher", default="未分类发布者", help="发布者名（决定 Assets/ThirdParty/<这里>/）")
    ap.add_argument("--name", default=None, help="素材名（决定 Assets/ThirdParty/<发布者>/<这里>/）")
    ap.add_argument("--dry", action="store_true", help="只看包里有什么，不写盘")
    ap.add_argument("--check", action="store_true", help="只体检目标目录")
    ap.add_argument("--force", action="store_true", help="覆盖已存在的文件")
    a = ap.parse_args()

    name = a.name or ("pkg_%s" % a.id)
    if a.dry:
        return do_dry(a.id)
    if a.check:
        return do_check(a.id, a.publisher, name)
    return do_install(a.id, a.publisher, name, force=a.force)


if __name__ == "__main__":
    sys.exit(main())
