#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""check_prefab_overrides.py —— 揪出「C# 默认值 与 prefab 序列化值不一致」的字段。

★ 为什么需要它（09-18 实测踩到）：
    第十四轮把 `restMotionScale / restLift / restSpeedScale` 从
    `0.06 / 0.35 / 0.12` 改成 `0.5 / 4.5 / 0.45`，**只写了 prefab，没写 C# 默认值**。
    演示场（走 prefab）验收通过，但源码里仍留着旧行为 —— 一旦组件被 Reset、
    prefab 被重建或回退，静默段就会重新把龙「拉直 + 贴地」。
    这类不一致**不报错、Console 干净**，只能靠比对发现。

判据（两层，缺一不可）：
    ① prefab YAML 里**确实写了**这个字段（写了才叫「覆盖」，没写就是跟脚本默认值走）
    ② 写了的字段里，值与脚本默认值**不一致** ⇒ 报告为「覆盖」，
       并标注方向（prefab 更新 / 更旧无法自动判定，只报差异）

用法：
    python check_prefab_overrides.py <prefab> <script.cs> [--only-script-dir]
    python check_prefab_overrides.py                       # 跑内置的墨龙默认组合
退出码：0 = 无不一致；1 = 有覆盖项；2 = 用法/文件错误。
"""
import io
import os
import re
import sys
import argparse

sys.stdout.reconfigure(encoding="utf-8")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 墨龙默认组合（用户最关心的那条）
DEFAULT_PAIRS = [
    ("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab",
     "Assets/_Project/Scripts/Enemies/EnemyDragon.cs"),
]


def read_text(p):
    with io.open(p, "r", encoding="utf-8", errors="replace") as f:
        return f.read()


def parse_script_defaults(cs_text):
    """从 .cs 里抓 `[特性] public/private <type> name = value;` 的初始值。

    返回 {name: (type, value_text, is_serialized)}。

    ★ 三个坑（都实测踩到）：
      ① 正则里若把 `\\s` 也算作修饰符，方法体内的**局部变量**（`float t = 0f;`）
         会被一起抓进来（本轮多出 80+ 条假条目）⇒ 必须要求**显式访问修饰符**。
      ② 表达式属性 `public bool Airborne => xxx;` 里含 `=`，会被误当成字段初值
         ⇒ 值以 `>` 开头的一律排除。
      ③ 私有运行期字段（`_perfTimer` 等）没有 [SerializeField] ⇒ Unity 根本不序列化，
         不该出现在「prefab 未写」清单里 ⇒ 只统计会被序列化的字段。
    """
    out = {}
    pat = re.compile(
        r"^[ \t]*(?P<attrs>(?:\[[^\]]*\][ \t]*)*)"
        r"(?P<mods>(?:(?:public|private|protected|internal|static|readonly|volatile|new)[ \t]+)+)"
        r"(float|int|bool|double|Vector2|Vector3|Color|string)[ \t]+"
        r"(?P<name>\w+)[ \t]*=[ \t]*(?P<val>[^;]+);",
        re.MULTILINE)
    for m in pat.finditer(cs_text):
        name = m.group("name")
        typ = m.group(3)
        val = re.sub(r"//.*$", "", m.group("val")).strip()
        if val.startswith(">"):                 # 表达式属性，不是字段初值
            continue
        mods = m.group("mods")
        attrs = m.group("attrs") or ""
        is_static = "static" in mods
        is_public = "public" in mods
        has_ser = "SerializeField" in attrs
        serialized = (not is_static) and (is_public or has_ser)
        out[name] = (typ, val, serialized)
    return out


def parse_prefab_fields(pf_text, script_guid):
    """返回 prefab 里那个 MonoBehaviour 块的 {字段名: 原始文本}。

    script_guid 为 None 时退化成「取第一个 MonoBehaviour 块」。
    """
    lines = pf_text.split("\n")
    blocks = []
    cur = None
    for i, ln in enumerate(lines):
        if ln.startswith("--- "):
            if cur is not None:
                blocks.append(cur)
            cur = {"head": ln, "lines": []}
        elif cur is not None:
            cur["lines"].append(ln)
    if cur is not None:
        blocks.append(cur)

    # ★ 坑：块头是 `--- !u!114 &123456`，**不含** "MonoBehaviour" 字样（它在正文首行）
    #   ⇒ 判据必须用整个块的文本去找 `guid: <脚本 guid>`，不能靠块头字符串。
    cands = []
    for b in blocks:
        body = "\n".join(b["lines"])
        if script_guid:
            if ("guid: " + script_guid) not in body:
                continue
        elif not body.lstrip().startswith("MonoBehaviour:"):
            continue
        cands.append(body)
    if not cands:
        return None, None
    return cands[0], len(cands)


def num_eq(a, b):
    """按数值比较，允许 '4.5f' / '4.5' / '0.5f' 等写法差异。

    ★ 坑：bool 在 prefab 里序列化成 0/1，脚本里写 true/false
      ⇒ 直接 float() 会失败、退化成字符串比较，把 `true vs 1` 误报成差异（实测 4 条假报）。
    """
    def clean(s):
        return s.strip().rstrip("fFdDmM")

    truthy = {"true": 1.0, "1": 1.0, "yes": 1.0, "on": 1.0}
    falsy = {"false": 0.0, "0": 0.0, "no": 0.0, "off": 0.0}

    la, lb = clean(a).lower(), clean(b).lower()
    if la in truthy or la in falsy:
        la = str(truthy.get(la, falsy.get(la)))
    if lb in truthy or lb in falsy:
        lb = str(truthy.get(lb, falsy.get(lb)))

    try:
        return abs(float(la) - float(lb)) < 1e-6
    except Exception:
        return la == lb


def check_one(prefab_path, cs_path):
    print("=" * 78)
    print("prefab : " + prefab_path)
    print("script : " + cs_path)
    print("=" * 78)

    if not os.path.isfile(prefab_path):
        print("  !! prefab 不存在")
        return None
    if not os.path.isfile(cs_path):
        print("  !! 脚本不存在")
        return None

    pf_text = read_text(prefab_path)
    cs_text = read_text(cs_path)

    # 用 .meta 里的 guid 精确定位 MonoBehaviour 块，避免抓错组件
    guid = None
    meta = cs_path + ".meta"
    if os.path.isfile(meta):
        m = re.search(r"^guid:\s*([0-9a-fA-F]+)", read_text(meta), re.MULTILINE)
        if m:
            guid = m.group(1)

    body, nblocks = parse_prefab_fields(pf_text, guid)
    if body is None:
        tag = ("guid " + guid) if guid else "任意 MonoBehaviour"
        print("  !! prefab 里没找到该脚本的 MonoBehaviour 块（找的是 %s）" % tag)
        return None
    print("  命中 MonoBehaviour 块（同 guid 共 %d 个）" % nblocks)
    if nblocks > 1:
        print("  ⚠ 同一个脚本在 prefab 里有 %d 个实例，本报告只比对了第一个" % nblocks)

    # prefab 里写了的字段
    pf_fields = {}
    for ln in body.split("\n"):
        m = re.match(r"^  ([A-Za-z_]\w*):\s*(.+?)\s*$", ln)
        if m:
            pf_fields[m.group(1)] = m.group(2)
    # 顺便抓数组长度这类（name: 后面跟列表的写成 name: 空 + 子行）

    defaults = parse_script_defaults(cs_text)

    overridden, same, only_script, skipped = [], [], [], []
    for name, (typ, dval, serialized) in sorted(defaults.items()):
        if not serialized:
            continue                              # Unity 不会序列化 ⇒ 谈不上 prefab 覆盖
        # 只比对标量；对象引用 / null 默认值 / 字符串 不参与（无法可靠比）
        if typ not in ("float", "int", "bool", "double"):
            skipped.append((name, typ, dval))
            continue
        if dval.strip() in ("null", "true", "false") and typ not in ("bool",):
            skipped.append((name, typ, dval))
            continue
        if name not in pf_fields:
            only_script.append((name, typ, dval))
            continue
        pval = pf_fields[name]
        if ("{" in pval or "}" in pval):     # 对象引用行
            skipped.append((name, typ, dval))
            continue
        if num_eq(pval, dval):
            same.append((name, typ, dval))
        else:
            overridden.append((name, typ, dval, pval))

    print("")
    print("--- ★ prefab 覆盖了脚本默认值（真差异）: %d 个 ---" % len(overridden))
    if overridden:
        w = max(len(n) for n, _, _, _ in overridden)
        for name, typ, dval, pval in overridden:
            print("   %-*s  %-7s  脚本 %-10s → prefab %s" % (w, name, typ, dval, pval))
    else:
        print("   （无）")

    print("")
    print("--- prefab 未写、跟脚本默认值走: %d 个 ---" % len(only_script))
    if only_script:
        names = ", ".join(n for n, _, _ in only_script)
        print("   " + (names if len(names) < 900 else names[:900] + " …"))
        print("   ⚠ 这些字段**没有**被 prefab 固定住：脚本默认值一改就跟着变，")
        print("     且旧 YAML 里没写 ⇒ 换机/回退时行为可能与预期不符。")

    print("")
    print("--- 值与脚本默认值一致: %d 个 ---" % len(same))
    if skipped:
        print("（跳过的非标量/引用字段 %d 个：%s）"
              % (len(skipped), ", ".join(n for n, _, _ in skipped[:12])
                 + (" …" if len(skipped) > 12 else "")))
    return overridden


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("prefab", nargs="?")
    ap.add_argument("script", nargs="?")
    args = ap.parse_args()

    pairs = DEFAULT_PAIRS if not args.prefab else [(args.prefab, args.script)]

    total = 0
    for pf, cs in pairs:
        pf = pf if os.path.isabs(pf) else os.path.join(ROOT, pf)
        cs = cs if os.path.isabs(cs) else os.path.join(ROOT, cs)
        r = check_one(pf, cs)
        if r is None:
            return 2
        total += len(r)

    print("")
    print("=" * 78)
    if total:
        print("结论：共 %d 个字段「脚本默认值 ≠ prefab 序列化值」。" % total)
        print("      → 脚本默认值该改成 prefab 的值（prefab 才是被验收过的那份），")
        print("        否则组件被 Reset / prefab 重建回退时会退回旧行为。")
        return 1
    print("结论：没有发现不一致 ✅")
    return 0


if __name__ == "__main__":
    sys.exit(main())
