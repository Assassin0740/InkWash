# -*- coding: utf-8 -*-
"""prefab 结构审计：一个角色 prefab 的 SkinnedMeshRenderer 是"自带数据"还是"占位引用"？

为什么需要它
------------
"模型看不见 / 编辑模式读到 mesh=null"有两种完全不同的成因，肉眼与普通读法分不开：

  A. **inline（烘焙式）**——GameObject / SMR / mesh 全都写在本 prefab 文件里。
     读得到就是读得到，读不到就是资产真缺东西。
  B. **stripped（拼装式）**——本文件里只有占位记录（连 `m_Mesh` 字段都不存在），
     真身在源里（`m_CorrespondingSourceObject` 指过去）。读数**取决于源的导入产物**，
     所以"文件在盘上、mesh 却是 null"极可能是**导入陈旧**，而不是资产坏了。

本工程真实案例：`Z_Enemy_MoLong.prefab` 仅 16 992 字节、2 个 SMR **全是 stripped**，
其一是 `Z_Dragon.prefab#6533441441231540133`（inline 完整，180 骨）；而 3 个 Ziyuan
敌人是 43/111/91 个 GameObject 的 **inline 烘焙式**。同一批敌人，两种结构。

判据
----
把 (源 guid, 源内 fileID) 一路追下去，直到**带数据的对象**或**模型文件（.fbx）**。
链路上每一环都打印它的形态 —— 断在哪一环，问题就在哪一环。

用法
----
    python Tools/enemy_prefab_audit.py                       # 默认审 Assets/_Project/Prefabs/Enemies
    python Tools/enemy_prefab_audit.py --dir Assets/_Project/Prefabs
    python Tools/enemy_prefab_audit.py --file path/to/x.prefab
"""
import argparse
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
os.chdir(ROOT)

GUID_RE = re.compile(r"guid:\s*([0-9a-f]{32})")
META_RE = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)
CLASS = {1: "GameObject", 4: "Transform", 114: "MonoBehaviour", 137: "SkinnedMeshRenderer",
         1001: "PrefabInstance", 136: "MeshFilter", 23: "MeshRenderer", 95: "Animator",
         54: "Rigidbody", 65: "BoxCollider", 195: "NavMeshAgent"}
MODEL_EXT = (".fbx", ".obj", ".blend", ".dae", ".gltf", ".glb")


def build_maps():
    gmap = {}
    for dp, _dn, fns in os.walk("Assets"):
        for fn in fns:
            if not fn.endswith(".meta"):
                continue
            p = os.path.join(dp, fn)
            try:
                t = open(p, encoding="utf-8", errors="ignore").read(4096)
            except OSError:
                continue
            m = META_RE.search(t)
            if m:
                gmap.setdefault(m.group(1), p[:-5].replace(os.sep, "/"))
    return gmap


GMAP = build_maps()
_CACHE = {}


def load(path):
    if path in _CACHE:
        return _CACHE[path]
    try:
        text = open(path, encoding="utf-8", errors="ignore").read()
    except OSError:
        _CACHE[path] = ({}, [])
        return _CACHE[path]
    byid, order, cur = {}, [], None
    for line in text.split("\n"):
        m = re.match(r"^--- !u!(\d+) &(\d+)(\s+stripped)?\s*$", line)
        if m:
            cur = {"type": int(m.group(1)), "id": m.group(2),
                   "stripped": bool(m.group(3)), "lines": []}
            byid[m.group(2)] = cur
            order.append(cur)
        elif cur is not None:
            cur["lines"].append(line)
    for b in order:
        b["body"] = "\n".join(b["lines"])
    _CACHE[path] = (byid, order)
    return _CACHE[path]


def fld(body, key):
    m = re.search(r"^\s*%s:\s*(.*)$" % re.escape(key), body, re.M)
    return m.group(1).strip() if m else None


def ref(body, key):
    m = re.search(r"^\s*%s:\s*\{fileID:\s*(-?\d+)(?:,\s*guid:\s*([0-9a-f]{32}))?[^}]*\}"
                  % re.escape(key), body, re.M)
    return (m.group(1), m.group(2)) if m else (None, None)


def chain(guid, fid, depth=1):
    """从 (源 guid, 源内 fileID) 追到带数据的对象或模型文件。"""
    pad = "      " + "  " * depth + "└─ "
    path = GMAP.get(guid)
    if path is None:
        print(pad + "guid %s… 无对应文件（UPM 包 / 内建资源）" % guid[:10])
        return
    base = os.path.basename(path)
    if path.lower().endswith(MODEL_EXT):
        print(pad + "**模型本体** %s  （二进制，链终点）" % base)
        return
    byid, _order = load(path)
    b = byid.get(fid)
    if b is None:
        print(pad + "%s 内 **找不到 fileID %s** ← 链在此断裂" % (base, fid))
        return
    kind = "stripped" if b["stripped"] else "inline"
    extra = ""
    if b["type"] == 137 and not b["stripped"]:
        mf, mg = ref(b["body"], "m_Mesh")
        nb = len(re.findall(r"-\s*\{fileID", b["body"]))
        extra = "  mesh=%s  bones字段=%d" % (
            os.path.basename(GMAP.get(mg) or "?") if mg else "null", nb)
    print(pad + "[%s %s] #%s  @ %s%s" % (CLASS.get(b["type"], b["type"]), kind, fid, base, extra))
    if b["stripped"]:
        f2, g2 = ref(b["body"], "m_CorrespondingSourceObject")
        if g2:
            chain(g2, f2, depth + 1)


def audit(path):
    byid, order = load(path)
    smrs = [b for b in order if b["type"] == 137]
    gos = [b for b in order if b["type"] == 1]
    pis = [b for b in order if b["type"] == 1001]
    print("=" * 84)
    print("%s   块=%d  GameObject=%d(占位 %d)  SMR=%d(占位 %d)  PrefabInstance=%d"
          % (path, len(order), len(gos), sum(1 for b in gos if b["stripped"]),
             len(smrs), sum(1 for b in smrs if b["stripped"]), len(pis)))
    for b in smrs:
        if b["stripped"]:
            f, g = ref(b["body"], "m_CorrespondingSourceObject")
            print("    SMR #%s  **stripped 占位**（本文件无 m_Mesh 字段）⇒ 源 %s"
                  % (b["id"], os.path.basename(GMAP.get(g) or g or "?")))
            chain(g, f, 2)
        else:
            mf, mg = ref(b["body"], "m_Mesh")
            nb = len(re.findall(r"-\s*\{fileID", b["body"]))
            matn = len(re.findall(r"-\s*\{fileID", fld(b["body"], "m_Materials") or ""))
            print("    SMR #%s  **inline 自带数据**  mesh=%s  材质=%d  bones字段=%d"
                  % (b["id"], os.path.basename(GMAP.get(mg) or "?") if mg else "null", matn, nb))
    for b in pis:
        f, g = ref(b["body"], "m_SourcePrefab")
        print("    PrefabInstance #%s → %s" % (b["id"], os.path.basename(GMAP.get(g) or g or "?")))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default="Assets/_Project/Prefabs/Enemies")
    ap.add_argument("--file")
    args = ap.parse_args()

    if args.file:
        targets = [args.file]
    else:
        targets = []
        for dp, _dn, fns in os.walk(args.dir):
            for fn in sorted(fns):
                if fn.endswith(".prefab"):
                    targets.append(os.path.join(dp, fn))
    if not targets:
        print("没有找到 prefab。")
        return 1
    for t in sorted(targets):
        audit(t.replace(os.sep, "/"))
    print()
    print("判读：")
    print("  · **inline 自带数据** = 读不到就是资产真缺东西（查 mesh 文件的 guid 能否解析）")
    print("  · **stripped 占位**    = 读数取决于源的导入产物 ⇒ 先查源的 artifact 是否最新")
    print("    （`python Tools/check_import_freshness.py`），再怀疑读取方式。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
