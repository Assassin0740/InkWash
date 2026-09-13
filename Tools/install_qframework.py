# -*- coding: utf-8 -*-
"""一键安装 QFramework ToolKits 到 Assets/ThirdParty/QFramework/。

为什么需要这个脚本：
    `.gitignore` 屏蔽了 `/Assets/ThirdParty/`（授权 + 体积原因），
    所以框架源码**不进版本库**。任何一次全新克隆都会缺依赖、编译不过。
    把安装动作固化成脚本，就能一条命令复原，而不是靠人肉回忆步骤。

用法：
    python install_qframework.py            # 安装（已存在则跳过）
    python install_qframework.py --check    # 只体检，不写盘
    python install_qframework.py --force    # 已存在也重装

做了什么：
    1. 取下 QFramework.Toolkits.unitypackage（优先用本地 `_RawDownloads` 里的缓存）
    2. 解包到 `Assets/ThirdParty/QFramework/`，**保留 GUID**（.meta 原样落位）
    3. 删掉包内嵌套的示例 `.unitypackage`（占体积、Unity 用不到）
    4. 修掉 `UIKit/Scripts/Resources/UIRoot.prefab` 里两个无字段的空壳组件
       （上游包自身的问题，会让 Unity 导入时报
        "Component at index 3 could not be loaded ... Removing it."）
"""
import argparse
import os
import sys
import tarfile
import urllib.request

sys.stdout.reconfigure(encoding="utf-8")

PROJ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAW = os.path.join(PROJ, "_RawDownloads", "QFramework")
PKG_URL = "https://github.com/liangxiegame/QFramework/raw/master/QFramework.Toolkits.unitypackage"
PKG_FILE = os.path.join(RAW, "qf.unitypackage")
DEST = os.path.join(PROJ, "Assets", "ThirdParty", "QFramework")
PREFIX = "Assets/QFramework"          # unitypackage 内的路径前缀
UIROOT = os.path.join(DEST, "Toolkits", "UIKit", "Scripts", "Resources", "UIRoot.prefab")


def log(msg=""):
    print(msg)


def download():
    os.makedirs(RAW, exist_ok=True)
    if os.path.exists(PKG_FILE) and os.path.getsize(PKG_FILE) > 1024 * 1024:
        log("   使用本地缓存包: %s (%.1f MB)" % (
            os.path.relpath(PKG_FILE, PROJ), os.path.getsize(PKG_FILE) / 1048576.0))
        return PKG_FILE
    log("   下载 %s" % PKG_URL)
    tmp = PKG_FILE + ".part"
    urllib.request.urlretrieve(PKG_URL, tmp)
    os.replace(tmp, PKG_FILE)
    log("   已下载 %.1f MB" % (os.path.getsize(PKG_FILE) / 1048576.0))
    return PKG_FILE


def extract(pkg):
    """把包解到 DEST，去掉 PREFIX 前缀；返回 (写出数, 跳过数)。"""
    written = skipped = 0
    with tarfile.open(pkg, "r:gz") as tar:
        members = {}
        for m in tar:
            if not m.isfile():
                continue
            seg = m.name.replace("\\", "/").lstrip("./").split("/")
            if len(seg) < 2:
                continue
            members.setdefault(seg[-2], {})[seg[-1]] = m

        for guid, files in members.items():
            pm, am = files.get("pathname"), files.get("asset")
            if pm is None or am is None:
                continue
            pn = tar.extractfile(pm).read().decode("utf-8", errors="replace").strip().replace("\\", "/")
            if not pn.startswith(PREFIX + "/"):
                continue
            rel = pn[len(PREFIX) + 1:]
            dst = os.path.join(DEST, rel.replace("/", os.sep))
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            if os.path.exists(dst):
                skipped += 1
                continue
            with open(dst, "wb") as fh:
                fh.write(tar.extractfile(am).read())
            mm = files.get("asset.meta")
            if mm is not None:
                with open(dst + ".meta", "wb") as fh:
                    fh.write(tar.extractfile(mm).read())
            written += 1
    return written, skipped


def prune_nested_packages():
    """删掉包内嵌套的示例 .unitypackage（连同 .meta）。"""
    removed = 0
    for dp, dn, fn in os.walk(DEST):
        for f in fn:
            if f.endswith(".unitypackage"):
                os.remove(os.path.join(dp, f))
                mp = os.path.join(dp, f + ".meta")
                if os.path.exists(mp):
                    os.remove(mp)
                removed += 1
    return removed


# 需要从 UIRoot.prefab 里摘掉的坏组件（no-field 空壳，Unity 无法加载）
BAD_BLOCKS = (
    "--- !u!92 &92000011727354218",
    "--- !u!124 &124000013877703386",
)


def patch_uiroot():
    """上游 UIRoot.prefab 里有两个只写了 Behaviour: 没有类型字段的组件，
    Unity 导入时报 'Component at index N could not be loaded ... Removing it.'。
    这些组件不携带任何数据，直接摘掉最干净（比重新保存预制体风险低）。
    """
    if not os.path.exists(UIROOT):
        return "未找到 UIRoot.prefab，跳过"

    with open(UIROOT, "r", encoding="utf-8", newline="") as fh:
        text = fh.read()

    # 已经修过就不重复动
    if "&92000011727354218" not in text and "&124000013877703386" not in text:
        return "已修过，无需处理"

    lines = text.split("\n")
    out, i, dropped = [], 0, []
    while i < len(lines):
        line = lines[i]
        if line.startswith("--- !u!") and any(line.startswith(b) for b in BAD_BLOCKS):
            fid = line.split("&")[-1].strip()
            dropped.append(fid)
            # 跳掉该块：直到下一个 '--- !u!' 或文件结尾
            i += 1
            while i < len(lines) and not lines[i].startswith("--- !u!"):
                i += 1
            # 同时跳掉块尾可能残留的空行
            continue
        out.append(line)
        i += 1

    fixed = "\n".join(out)
    # 再把组件引用行摘掉，否则 GameObject 会指向不存在的 fileID
    for fid in dropped:
        fixed = fixed.replace("  - component: {fileID: %s}\n" % fid, "")

    with open(UIROOT, "w", encoding="utf-8", newline="") as fh:
        fh.write(fixed)
    return "已摘掉 %d 个坏组件: %s" % (len(dropped), ", ".join(dropped))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只体检不写盘")
    ap.add_argument("--force", action="store_true", help="已存在也重装")
    args = ap.parse_args()

    asmdef = os.path.join(DEST, "Framework", "Scripts", "QFramework.asmdef")
    audiokit = os.path.join(DEST, "Toolkits", "AudioKit", "Scripts", "AudioKit.cs")
    installed = os.path.exists(asmdef) and os.path.exists(audiokit)

    log("=" * 74)
    log("QFramework 安装检查")
    log("=" * 74)
    log("   目标目录 : %s" % os.path.relpath(DEST, PROJ))
    log("   当前状态 : %s" % ("已安装" if installed else "未安装"))

    if args.check:
        return 0 if installed else 1

    if installed and not args.force:
        log("   已安装，跳过。需要重装请加 --force")
        return 0

    log("")
    log("[1/4] 获取包")
    pkg = download()

    log("")
    log("[2/4] 解包到 Assets/ThirdParty/QFramework")
    w, s = extract(pkg)
    log("   写出 %d 个文件，跳过 %d 个已存在" % (w, s))

    log("")
    log("[3/4] 清理嵌套示例包")
    n = prune_nested_packages()
    log("   删除 %d 个 .unitypackage" % n)

    log("")
    log("[4/4] 修补 UIRoot.prefab")
    log("   " + patch_uiroot())

    cs = sum(1 for dp, dn, fn in os.walk(DEST) for f in fn if f.endswith(".cs"))
    log("")
    log("=" * 74)
    log("完成：%d 个 .cs 文件已就位" % cs)
    log("下一步：Unity 会自动导入；PrimeTween 由 Packages/manifest.json 的")
    log("        scopedRegistries(npm) 解析，无需手工操作。")
    log("=" * 74)
    return 0


if __name__ == "__main__":
    sys.exit(main())
