# -*- coding: utf-8 -*-
"""
跨机搬运的**最小闭包**计算 —— 在**源机器**的工程根目录运行：

    python Tools/transfer_closure.py            # 只报告
    python Tools/transfer_closure.py --pack     # 报告 + 打包到 ../InkWash_Transfer/

为什么需要它
------------
`/Assets/ThirdParty/` 共 647 MB，但两个工程真正「引用到」的只是一小部分。
盲拷全量在 U 盘/微信上很浪费；只拷 `ThirdParty` 又会漏掉目录级 `.meta` 与外部贴图。

判据（不依赖 git 分支状态，只看磁盘）
------------------------------------
1. **根** = Assets 下、**不在**被 ignore 目录里的资产文件（.prefab/.unity/.mat/
   .controller/.anim/.asset/...）—— 这些是入库的，目标机器由 git 还原。
2. 从根出发按 `guid:` 引用做 BFS，落到被 ignore 目录里的文件 → 收进搬运集。
3. 递归处理被引用文件自身的 `.meta`（`externalObjects` 里的贴图重映射）。
4. 所有被收文件的**祖先目录 `.meta`** 也要（Unity 靠它认文件夹，漏了会重生成 GUID）。

输出
----
- 每个被 ignore 顶层包的「引用文件数 / 总文件数 / 引用体积 / 总体积」
- 建议：引用覆盖率低的包建议整包拷（省得漏），覆盖率高的按文件清单拷
- `Tools/reports/transfer_manifest.txt` —— 精确到文件的搬运清单（目标机器可对账）
"""

import os
import re
import sys
import collections

TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS_DIR)
ASSETS = os.path.join(ROOT, "Assets")
REPORTS = os.path.join(TOOLS_DIR, "reports")

# 被 .gitignore 排除、必须物理搬运的根目录（相对工程根，正斜杠）
IGNORED_ROOTS = [
    "Assets/ThirdParty",
    "Assets/Char_Feng",
    "Assets/W_Sword",
    "Assets/QFrameworkData",
]

# BGM 是「目录被入库、但 *.mp3 被单独 ignore」，所以单独按文件名规则判定
BGM_SUFFIX = (".mp3", ".wav")
BGM_DIR_HINT = "BGM"

SCAN_EXT = (".prefab", ".unity", ".asset", ".mat", ".controller", ".anim",
            ".overrideController", ".playable", ".mixer", ".preset",
            ".renderTexture", ".physicMaterial", ".physicsMaterial2D",
            ".spriteatlas", ".shadervariants")

META_GUID_RE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)
GUID_REF_RE = re.compile(r"guid:\s*([0-9a-f]{32})")
BUILTIN_RE = re.compile(r"^0{16}[0-9a-f]{16}$")


def rel(p):
    return os.path.relpath(p, ROOT).replace("\\", "/")


def is_ignored_path(r):
    if any(r == d or r.startswith(d + "/") for d in IGNORED_ROOTS):
        return True
    return r.lower().endswith(BGM_SUFFIX) and BGM_DIR_HINT in r


def build_guid_map():
    """guid -> 定义它的文件（去掉 .meta 后缀）。"""
    m = {}
    for dp, _dn, fns in os.walk(ASSETS):
        for fn in fns:
            if not fn.endswith(".meta"):
                continue
            p = os.path.join(dp, fn)
            try:
                with open(p, encoding="utf-8", errors="ignore") as f:
                    t = f.read(4096)
            except OSError:
                continue
            mm = META_GUID_RE.search(t)
            if mm:
                m.setdefault(mm.group(1), p[:-5])
    return m


def refs_of(path):
    """读一个文本资产里出现的所有 guid。"""
    try:
        with open(path, encoding="utf-8", errors="ignore") as f:
            t = f.read()
    except OSError:
        return set()
    return {g for g in GUID_REF_RE.findall(t) if not BUILTIN_RE.match(g)}


def bin_guid(guid_hex):
    """文本 guid -> Unity 二进制里**实际写出的** 16 字节。

    ★ 关键：不是 `bytes.fromhex(g)`。Unity 的 GUID 在文件里每个字节的两位十六进制
    是**互换**的（文本 `d3ef99fc…` ↔ 文件 `3d fe 99 cf …`）。
    用错这一步，字节扫描会**恒 0 命中且不报错** —— 2026-09-17 那次跨机搬运正是栽在
    这里：`Main.unity` 依赖的 13 个 Nature FBX 一个都没进清单，那边道具全断。
    """
    out = bytearray()
    for i in range(0, 32, 2):
        b = int(guid_hex[i:i + 2], 16)
        out.append((b >> 4) | ((b & 0x0F) << 4))
    return bytes(out)


def refs_of_binary(path, ignored_bytes):
    """★ 二进制资产（本工程的 `Main.unity` 就是）里 guid 是 **16 字节裸值**，
    文本正则一条都读不到 ⇒ 闭包会整段瞎掉。这里直接按字节串做子串查找。

    只查「被 ignore 目录里的 guid」—— 入库资产的引用目标必是入库文件，不必查。
    """
    try:
        with open(path, "rb") as f:
            data = f.read()
    except OSError:
        return set()
    found = set()
    for gb, p in ignored_bytes.items():
        if data.find(gb) >= 0:
            found.add(p)
    return found


def main():
    pack = "--pack" in sys.argv

    print("=" * 72)
    print("InkWash 跨机搬运最小闭包")
    print("工程根: %s" % ROOT)
    print("=" * 72)

    gmap = build_guid_map()
    print("\n可解析 guid 定义: %d 个" % len(gmap))

    # ---- 根：入库的资产文件 ----
    roots = []
    for dp, _dn, fns in os.walk(ASSETS):
        for fn in fns:
            if not fn.lower().endswith(SCAN_EXT):
                continue
            p = os.path.join(dp, fn)
            if not is_ignored_path(rel(p)):      # 被 ignore 的不算根
                roots.append(p)
    print("入库资产文件（BFS 起点）: %d 个" % len(roots))

    # 被 ignore 目录里每个 guid 的 **16 字节裸值**，用于扫二进制资产
    ignored_bytes = {}
    for g, p in gmap.items():
        if is_ignored_path(rel(p)):
            ignored_bytes[bin_guid(g)] = p
    binary_roots = []
    for p in roots:
        try:
            with open(p, "rb") as f:
                if not f.read(8).startswith(b"%YAML"):
                    binary_roots.append(p)
        except OSError:
            pass
    if binary_roots:
        print("其中非文本资产: %d 个（guid 为 16 字节裸值，走字节扫描）" % len(binary_roots))

    # ---- BFS ----
    wanted = set()          # 绝对路径（被 ignore 目录内、需要搬运的文件）
    queue = list(roots)
    seen = set(queue)
    unresolved = collections.Counter()
    for p in binary_roots:
        for tgt in refs_of_binary(p, ignored_bytes):
            wanted.add(tgt)
    while queue:
        p = queue.pop()
        for g in refs_of(p):
            tgt = gmap.get(g)
            if tgt is None:
                unresolved[rel(p)] += 1
                continue
            if is_ignored_path(rel(tgt)):
                if tgt not in wanted:
                    wanted.add(tgt)
                    # 被引用文件自身可能是资产（继续递归）+ 它的 .meta 也要
                    if tgt.lower().endswith(SCAN_EXT) and tgt not in seen:
                        seen.add(tgt)
                        queue.append(tgt)
            if tgt.lower().endswith(SCAN_EXT) and tgt not in seen:
                seen.add(tgt)
                queue.append(tgt)
        # 被引用文件自身的 .meta 里还可能有 externalObjects 贴图重映射
        meta = p + ".meta"
        if os.path.isfile(meta):
            for g in refs_of(meta):
                tgt = gmap.get(g)
                if tgt and is_ignored_path(rel(tgt)):
                    wanted.add(tgt)

    # ---- 补 .meta 与祖先目录 .meta ----
    for p in list(wanted):
        mp = p + ".meta"
        if os.path.isfile(mp):
            wanted.add(mp)
    for p in list(wanted):
        d = os.path.dirname(p)
        while len(d) > len(ASSETS) and d.startswith(ASSETS):
            dm = d + ".meta"
            if os.path.isfile(dm):
                wanted.add(dm)
            d = os.path.dirname(d)

    # ---- 排除不可再分发的？保留（本工具只管闭包，授权见 Docs/素材授权/） ----
    # ---- 统计 ----
    def size_of(paths):
        tot = 0
        for p in paths:
            try:
                tot += os.path.getsize(p)
            except OSError:
                pass
        return tot

    def top_pkg(relpath):
        parts = relpath.split("/")
        if len(parts) >= 2 and parts[0] == "Assets":
            if parts[1] in ("_Project",):
                return "Assets/" + "/".join(parts[1:3])
            return "Assets/" + parts[1]
        return relpath

    print("\n" + "-" * 72)
    print("%-34s %8s %8s %10s %10s" % ("包", "引用文件", "总文件", "引用体积", "总体积"))
    print("-" * 72)

    stats = {}
    for d in IGNORED_ROOTS:
        base = os.path.join(ROOT, d.replace("/", os.sep))
        if not os.path.isdir(base):
            stats[d] = None
            continue
        allf = []
        for dp, _dn, fns in os.walk(base):
            allf += [os.path.join(dp, f) for f in fns]
        mine = [p for p in allf if p in wanted]
        stats[d] = (len(mine), len(allf), size_of(mine), size_of(allf))

    # BGM 单独一行
    bgm_all = [p for dp, _dn, fns in os.walk(ASSETS) for f in fns
               for p in [os.path.join(dp, f)]
               if f.lower().endswith(BGM_SUFFIX) and BGM_DIR_HINT in rel(p)]

    for d, st in stats.items():
        if st is None:
            print("%-34s %8s" % (d, "目录不存在"))
            continue
        n, na, b, ba = st
        print("%-34s %8d %8d %9.1fM %9.1fM" % (
            d, n, na, b / 1048576.0, ba / 1048576.0))

    all_ignored = 0
    for d in IGNORED_ROOTS:
        base = os.path.join(ROOT, d.replace("/", os.sep))
        if os.path.isdir(base):
            for dp, _dn, fns in os.walk(base):
                all_ignored += sum(os.path.getsize(os.path.join(dp, f)) for f in fns)
    bgm_bytes = size_of(bgm_all)
    merged = {}
    for p in list(wanted) + bgm_all:
        merged[p] = True
    wanted_all = set(merged)

    print("\n需搬运合计: %d 个文件 / %.1f MB（含 BGM %.1f MB）"
          % (len(wanted_all), size_of(wanted_all) / 1048576.0, bgm_bytes / 1048576.0))
    print("被 ignore 素材全量: %.1f MB —— 闭包省下 %.1f MB"
          % ((all_ignored + bgm_bytes) / 1048576.0,
             (all_ignored + bgm_bytes - size_of(wanted_all)) / 1048576.0))

    # 未被解析的引用（源机器上都不通，说明引用了未安装的包）
    if unresolved:
        print("\n[!] 源机器上就找不到定义的引用（%d 个文件）：" % len(unresolved))
        for f, c in unresolved.most_common(10):
            print("    %-62s %d 处" % (f, c))

    # ---- 写清单 ----
    os.makedirs(REPORTS, exist_ok=True)
    mp = os.path.join(REPORTS, "transfer_manifest.txt")
    lines = sorted(rel(p) for p in wanted_all)
    with open(mp, "w", encoding="utf-8") as f:
        f.write("# InkWash 跨机搬运文件清单（源机器生成，%d 个文件）\n" % len(lines))
        f.write("# 判据：从入库资产出发按 guid 引用做闭包，落到被 ignore 目录的文件\n")
        f.write("# 用法：整目录拷贝时可直接对照本清单确认无遗漏\n")
        f.write("# 授权提醒：本清单里的素材不可再分发，仅供毕设本机使用\n")
        for l in lines:
            f.write(l + "\n")
    print("\n清单已写入: %s" % mp)

    if pack:
        pass  # 打包在 shell 里用 tar/7z 完成，保持本脚本纯 Python

    print("=" * 72)
    return 0


if __name__ == "__main__":
    sys.exit(main())
