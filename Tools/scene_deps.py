# -*- coding: utf-8 -*-
"""列出一个 Unity 资产（.unity / .prefab / 任意 SerializedFile）真正依赖的**工程外/被 ignore** 文件。

为什么要单独写这个
------------------
`.unity` 可能是 **旧二进制格式**，guid 在里面不是文本 "guid: xxx" 而是 16 字节裸值，
且**每个字节的两位十六进制是互换的**（text `d3ef99fc…` ↔ 文件 `3d fe 99 cf …`）。
用文本正则扫二进制场景 ⇒ **一条都读不到，且静默返回 0**（这不是 bug 报告，是假阴性）。
本工具两种格式都处理，并且**在结果为 0 时明确报警**，避免把"读不到"当成"没依赖"。

用法
----
    python Tools/scene_deps.py Assets/_Project/Scenes/Main.unity
    python Tools/scene_deps.py <file> --pack out.zip     # 把需要搬运的文件（含 .meta）打包
"""
import argparse
import os
import re
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Assets")

# 被 .gitignore 排除的目录（克隆/换机时一律缺失）
IGNORED = ("Assets/ThirdParty", "Assets/Char_Feng", "Assets/W_Sword",
           "Assets/QFrameworkData", "Assets/_Project/Audio")
TEXT_EXT = (".prefab", ".unity", ".asset", ".mat", ".controller", ".anim",
            ".overrideController", ".playable", ".mixer", ".preset",
            ".shader", ".meta", ".json", ".txt")
META_GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)
GUID_REF_RE = re.compile(r"guid:\s*([0-9a-f]{32})")


def nib_swap_text(raw):
    """Unity 二进制里 guid 的 16 字节 -> 文本形式（每字节 nibble 互换）。"""
    return "".join("%02x" % ((b >> 4) | ((b & 0x0F) << 4)) for b in raw)


def build_guid_map():
    m = {}
    for base in ("Assets", "Packages", os.path.join("Library", "PackageCache")):
        b = os.path.join(ROOT, base)
        if not os.path.isdir(b):
            continue
        for dp, _dn, fns in os.walk(b):
            for fn in fns:
                if not fn.endswith(".meta"):
                    continue
                p = os.path.join(dp, fn)
                try:
                    t = open(p, encoding="utf-8", errors="ignore").read(4096)
                except OSError:
                    continue
                mm = META_GUID_RE.search(t)
                if mm:
                    m.setdefault(mm.group(1), p[:-5])
    return m


def refs_text(path):
    t = open(path, encoding="utf-8", errors="ignore").read()
    return {g for g in GUID_REF_RE.findall(t)}


def refs_binary(path):
    """二进制 SerializedFile：只认 externals 段里的 guid（16 字节，逐字节 nibble 互换）。"""
    import UnityPy
    env = UnityPy.load(path)
    sf = None
    for f in env.files.values():
        if hasattr(f, "externals"):
            sf = f
            break
    if sf is None:
        return set(), 0
    out = set()
    for e in sf.externals:
        raw = e.guid if isinstance(e.guid, (bytes, bytearray)) else bytes.fromhex(e.guid)
        if len(raw) == 16 and any(raw):
            out.add(nib_swap_text(bytes(raw)))
    return out, len(sf.objects)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("asset")
    ap.add_argument("--pack", help="把需要搬运的文件打包到这个 zip")
    ap.add_argument("--manifest", help="导出「外部引用清单」(guid/期望路径/引用方) 供目标机核验")
    args = ap.parse_args()

    path = os.path.abspath(args.asset)
    head = open(path, "rb").read(8)
    is_text = head.startswith(b"%YAML")

    print("=" * 72)
    print("资产依赖列举: %s" % os.path.relpath(path, ROOT).replace(os.sep, "/"))
    print("格式: %s" % ("文本 YAML" if is_text else "二进制 SerializedFile"))
    print("=" * 72)

    if is_text:
        refs = refs_text(path)
        nobj = -1
    else:
        refs, nobj = refs_binary(path)
        print("对象数: %d" % nobj)

    gmap = build_guid_map()
    print("可解析 guid 定义: %d" % len(gmap))
    print()

    if not refs:
        print("!! 没有读到任何 guid 引用。")
        if not is_text and nobj > 0:
            print("!! 但文件里确实有 %d 个对象 —— 这是**扫描失败**，不是「没有依赖」。" % nobj)
            print("!! 别把它当成通过。检查 guid 编码规则是否变化。")
            return 2
        return 0

    resolved, dangling, builtin = [], [], []
    for g in sorted(refs):
        p = gmap.get(g)
        if p is None:
            # Unity 内建资源（全 0 前后缀）不算悬空
            (builtin if re.match(r"^0{16}[0-9a-f]{16}$", g) else dangling).append(g)
        else:
            resolved.append(p)

    need = []          # 需要搬运（被 ignore 目录内）
    for p in sorted(set(resolved)):
        rel = os.path.relpath(p, ROOT).replace(os.sep, "/")
        if rel.startswith(IGNORED):
            need.append(rel)

    print("引用 guid 数: %d （可解析 %d，悬空 %d）" % (len(refs), len(resolved), len(dangling)))
    print("其中落在被 ignore 目录内、**跨机必须搬运**的资产: %d 个" % len(need))
    print()
    for rel in need:
        print("   %s" % rel)
    if dangling:
        print()
        print("悬空 guid（%d）：" % len(dangling))
        for g in sorted(dangling):
            print("   %s" % g)

    if args.manifest:
        rel_asset = os.path.relpath(path, ROOT).replace(os.sep, "/")
        lines = ["# 外部引用清单 —— 由 Tools/scene_deps.py 在源机器生成，随 git 走",
                 "# 格式: <guid>\\t<期望路径或 builtin>\\t<引用方资产>",
                 "# 目标机器跑 Tools/check_clone_deps.py 会逐条核验这些 guid 能否解析。",
                 "# 为什么需要它：二进制场景（如 Main.unity）里的 guid 是 16 字节裸值，",
                 "# 文本正则扫不到 —— 只靠文本扫描会给出「通过」的假阴性。"]
        for g in sorted(refs):
            p = gmap.get(g)
            if p is None:
                kind = "builtin" if re.match(r"^0{16}[0-9a-f]{16}$", g) else "DANGLING"
                lines.append("%s\t%s\t%s" % (g, kind, rel_asset))
            else:
                lines.append("%s\t%s\t%s"
                             % (g, os.path.relpath(p, ROOT).replace(os.sep, "/"), rel_asset))
        out = os.path.abspath(args.manifest)
        os.makedirs(os.path.dirname(out), exist_ok=True)
        with open(out, "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(lines) + "\n")
        print()
        print("已导出引用清单 %d 条 -> %s" % (len(refs), out))

    if args.pack and need:
        files = []
        for rel in need:
            files.append(rel)
            if os.path.isfile(os.path.join(ROOT, rel) + ".meta"):
                files.append(rel + ".meta")
        # 祖先目录的 .meta（Unity 靠它认文件夹，缺了会重生成 GUID）
        for rel in need:
            d = os.path.dirname(rel).replace(os.sep, "/")
            while d.startswith("Assets") and d != "Assets":
                m = os.path.join(ROOT, d.replace("/", os.sep)) + ".meta"
                if os.path.isfile(m):
                    files.append(d + ".meta")
                d = os.path.dirname(d)
        files = sorted(set(files))
        out = os.path.abspath(args.pack)
        with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
            for rel in files:
                z.write(os.path.join(ROOT, rel.replace("/", os.sep)), rel)
        print()
        print("已打包 %d 个文件 -> %s (%.1f MB)"
              % (len(files), out, os.path.getsize(out) / 1048576.0))
    return 0


if __name__ == "__main__":
    sys.exit(main())
