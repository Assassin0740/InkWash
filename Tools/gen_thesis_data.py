#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
从 Tools/reports/ 的「当前基线报告」抽取论文可引用的测试数据，生成
Docs/论文-测试数据与图表素材.md。

为什么需要它：
  534 份历史报告是逐轮验收的流水，论文只该引用**最新基线**那一批。
  手抄容易出错（曾出现「累计 185 项」这类与报告对不上、且漏计一个批次的数字）。
  本脚本每次重跑即可刷新论文数据，并对「解析到的条数」与「报告自称的条数」做对账，
  避免静默漏读导致论文里出现偏小的数。

用法：python Tools/gen_thesis_data.py
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPORTS = os.path.join(ROOT, "Tools", "reports")
OUT = os.path.join(ROOT, "Docs", "论文-测试数据与图表素材.md")

# 用例型报告：(文件, 里程碑, 论文小节标题)
CASES = [
    ("S1_latest.txt",       "M1 能动",   "S1 · 角色操控与镜头跟随"),
    ("S2_latest.txt",       "M2 能打",   "S2 · 战斗内核与动作手感"),
    ("S2_pose_latest.txt",  "M2 能打",   "S2 · 姿态体检（专项）"),
    ("S2_audio_latest.txt", "M2 能打",   "S2 · 音频链路（专项）"),
    ("S3_latest.txt",       "M3 有对手", "S3 · 敌人 AI 与关卡组织"),
    ("S4_latest.txt",       "M4 有墨味", "S4 · 水墨渲染管线"),
    ("S5_latest.txt",       "M5 完整循环", "S5 · Roguelike 循环与水墨特效"),
]

# 闸门型报告：用 OK / → OK 表达，单独一节
GATES = [
    ("s6_final_gate.txt", "M6 稳定可交付", "S6 · 提交闸门（编译 / DLL / 着色器）"),
]

METRICS_FILE = "d_metrics.txt"
S4_FILE = "S4_latest.txt"

# 个别指标的目标写在报告的「同帧 A/B」附行里而非本行，这里补一句指向，避免论文表格出现空判据
GOAL_HINT = {
    "M9": "净贡献 ≥0.03（见下表同帧 A/B）",
}


def read(path):
    """报告为 UTF-8 with BOM，统一按 utf-8-sig 读。"""
    with open(path, encoding="utf-8-sig", errors="replace") as fh:
        return fh.read()


def split_kv(line):
    """把 '[通过] 判据    实测值' 拆成 (判据, 实测值)。"""
    body = re.sub(r"^\[[^\]]+\]\s*", "", line).strip()
    parts = re.split(r"\s{2,}|\t+", body, maxsplit=1)
    return (parts[0].strip(), parts[1].strip() if len(parts) > 1 else "")


def parse_report(path):
    """返回 (通过项, 未通过项, 元信息)。

    坑：判定行有带缩进（"  [通过] ..."）与不带缩进两种，必须先 strip 再判前缀，
    否则会静默漏计（实测漏掉近一半，113 vs 208）。
    """
    ok, ng, meta = [], [], {}
    for raw in read(path).splitlines():
        s = raw.strip()
        if not s:
            continue
        if s.startswith("[通过]"):
            ok.append(split_kv(s))
        elif s.startswith(("[未通过]", "[失败]", "[×]")):
            ng.append(split_kv(s))
        m = re.search(r"生成时间[:：]\s*(.+)", s)
        if m:
            meta["时间"] = m.group(1).strip()
        m = re.search(r"分辨率[:：]\s*(\S+).*?帧率[:：]\s*([\d.]+)\s*fps", s)
        if m:
            meta["分辨率"] = m.group(1)
        # 报告自称的通过/未通过数（只取第一处 = 本报告汇总；S2 后面还有专项的行）
        if "声明" not in meta:
            m = re.search(r"通过\s*(\d+)\s*项?\s*/\s*未通过\s*(\d+)", s)
            if m:
                meta["声明"] = (int(m.group(1)), int(m.group(2)))
    return ok, ng, meta


def parse_gate(path):
    """解析闸门报告，返回 [(小节, [(检查项, 结果)])]。"""
    groups, cur = [], None
    for raw in read(path).splitlines():
        s = raw.strip()
        if not s:
            continue
        m = re.match(r"^-{2,}\s*[①②③④⑤⑥⑦⑧⑨⑩]?\s*(.+?)\s*-{2,}$", s)
        if m:
            cur = (m.group(1).strip(), [])
            groups.append(cur)
            continue
        if cur is None:
            continue
        m = re.match(r"^OK\s+(.+)$", s)                       # 类型 / 成员可见性
        if m:
            cur[1].append((m.group(1).strip(), "OK"))
            continue
        m = re.match(r"^(.+?)\s*→\s*(OK|FAIL\S*)\s*(.*)$", s)  # 着色器表 / 判定
        if m:
            extra = m.group(3).strip()
            cur[1].append((m.group(1).strip() + ("　" + extra if extra else ""), m.group(2)))
            continue
        m = re.match(r"^(.+?)\s*=\s*(.+)$", s)                 # key = value
        if m and len(m.group(1)) < 24:
            cur[1].append((m.group(1).strip(), m.group(2).strip()))
    return groups


def parse_metrics(path):
    """解析 d_metrics.txt，返回 (场景列表, 净贡献列表, 帧率行)。"""
    scenes, ab, fps = [], [], []
    if not os.path.isfile(path):
        return scenes, ab, fps
    cur = None
    for raw in read(path).splitlines():
        s = raw.strip()
        if not s:
            continue
        m = re.match(r"^-{5,}\s*\[(\d+)\]\s*(\S+)\s*-{5,}$", s)
        if m:
            cur = (m.group(2), [])
            if re.match(r"^[a-zA-Z]+$", m.group(2)):
                scenes.append(cur)
            else:
                cur = None
            continue
        if "M8" in s and "fps" in s:
            fps.append(s)
            continue
        if "净贡献" in s:
            ab.append((cur[0] if cur else "—", s))
            continue
        if cur is not None:
            cur[1].append(s)
    return scenes, ab, fps


def parse_s4(path):
    """从 S4 报告提取论文核心图数据。

    返回 (四阶段表, 逐层像素差, 量化阶数表)：
      四阶段表  = [(阶段号, 组成说明, 平均亮度, 标准差, 出现级数, 80%级数)]
      逐层像素差 = [(标签, 差值)]
      量化阶数表 = [(材质, 平均亮度, 出现级数, 前4覆盖率%, 最大单级占比%)]
    """
    stages, diffs, quants = [], [], []
    if not os.path.isfile(path):
        return stages, diffs, quants
    in_diff = False
    for raw in read(path).splitlines():
        s = raw.strip()
        if not s:
            in_diff = False
            continue
        if "逐层像素差" in s:
            in_diff = True
            continue
        if in_diff:
            m = re.match(r"^([^=]+?)\s*=\s*([\d.]+)$", s)
            if m:
                diffs.append((m.group(1).strip(), m.group(2)))
                continue
            in_diff = False
        m = re.match(r"^阶段\s*(\d)\s*[（(](.+)[）)]\s+平均亮度\s*([\d.]+)\s+标准差\s*([\d.]+)"
                     r"\s+出现级数\s*(\d+)\s+80%级数\s*(\d+)", s)
        if m:
            stages.append((m.group(1), m.group(2).strip(), m.group(3), m.group(4),
                           m.group(5), m.group(6)))
            continue
        m = re.match(r"^(URP/Lit（PBR 对照）|水墨\s*\d+\s*阶)\s*[:：]\s*平均亮度\s*([\d.]+)"
                     r"[，,]\s*出现过\s*(\d+)\s*级[，,]\s*前\s*4\s*种墨色覆盖\s*([\d.]+)%"
                     r"[，,]\s*最大一级占\s*([\d.]+)%", s)
        if m:
            quants.append((m.group(1), m.group(2), m.group(3), m.group(4), m.group(5)))
    return stages, diffs, quants


def parse_metric_line(s):
    """'M6 分离度(...) = 0.586　(主体墨 …)　(目标 ≥0.30)' → (代号, 名称, 值, 判据)。"""
    m = re.match(r"^(M\d+\w?)\s+(.+?)\s*=\s*([-\d.]+)\s*(%?)\s*(.*)$", s)
    if not m:
        return None
    code = m.group(1)
    name = m.group(2).split("(")[0].strip()
    val = m.group(3) + m.group(4)
    rest = m.group(5)
    goal = ""
    g = re.search(r"目标\s*[≤≥<>]?=?[^\s，,）)]*(?:\s*[^\s，,）)]+)?", rest)
    if g:
        goal = g.group(0).strip()
        goal = re.sub(r"\s*——.*$", "", goal)
    return code, name, val, goal


def main():
    if not os.path.isdir(REPORTS):
        sys.exit("找不到报告目录：%s" % REPORTS)

    L = []
    add = L.append

    add("# 论文测试数据与图表素材")
    add("")
    add("> 来源：`Tools/reports/` 中的**当前基线报告**（各阶段 `*_latest` + S6 终检），"
        "全部为 Unity 编辑器 Play 模式下的自动化实测值，非估算。")
    add("> 生成脚本：`python Tools/gen_thesis_data.py` —— 重新验收后重跑即可刷新，**不要手抄数字**。")
    add("")

    # ---------- 一、用例总览 ----------
    add("## 一、自动化验收总览")
    add("")
    add("| 里程碑 | 验收批次 | 报告文件 | 通过 | 未通过 | 报告时间 | 对账 |")
    add("|---|---|---|---:|---:|---|---|")
    rows, details, audit = [], [], []
    total_ok = total_ng = 0
    missing = []
    for fname, milestone, title in CASES:
        path = os.path.join(REPORTS, fname)
        if not os.path.isfile(path):
            missing.append(fname)
            continue
        ok, ng, meta = parse_report(path)
        n_ok, n_ng = len(ok), len(ng)
        total_ok += n_ok
        total_ng += n_ng
        declared = meta.get("声明")
        if declared is None:
            verdict = "未声明"
        elif declared == (n_ok, n_ng):
            verdict = "一致"
        else:
            verdict = "**不一致**"
            audit.append((fname, n_ok, n_ng, declared[0], declared[1]))
        add("| %s | %s | `%s` | %d | %d | %s | %s |"
            % (milestone, title, fname, n_ok, n_ng, meta.get("时间", "—"), verdict))
        rows.append((title, fname))
        details.append((title, fname, ok, ng, meta.get("时间", "—")))
    add("| **合计** | **%d 个批次** | — | **%d** | **%d** | — | — |" % (len(rows), total_ok, total_ng))
    add("")
    add("**用例总数口径**：主用例（S1 31 + S2 54 + S3 22 + S4 32 + S5 59 = **198**）"
        "＋ 专项验收（姿态体检 4 + 音频链路 6 = 10）＝ **%d**。" % total_ok)
    add("")
    add("> ⚠️ 旧版计划文档中「累计 185 项」的说法与报告对不上（当时漏计了 S3 的 22 项），**以本表为准**。")
    add("")
    if audit:
        add("> ⚠️ **对账不一致**（抽取器漏读或报告格式变动，需人工核对）：")
        for fname, a_ok, a_ng, d_ok, d_ng in audit:
            add("> - `%s`：解析 %d/%d，报告声明 %d/%d" % (fname, a_ok, a_ng, d_ok, d_ng))
        add("")

    # ---------- 二、水墨量化指标 ----------
    add("## 二、水墨风格量化指标（画面风格验收）")
    add("")
    scenes, ab, fps = parse_metrics(os.path.join(REPORTS, METRICS_FILE))
    if len(scenes) >= 2:
        a_rows = [parse_metric_line(x) for x in scenes[0][1]]
        b_rows = [parse_metric_line(x) for x in scenes[1][1]]
        a_rows = [r for r in a_rows if r]
        b_rows = [r for r in b_rows if r]
        add("数据源：`Tools/reports/%s`（相机 1920×1080，场景 %s / %s）"
            % (METRICS_FILE, scenes[0][0], scenes[1][0]))
        add("")
        add("| 指标 | 判据 | %s | %s |" % (scenes[0][0], scenes[1][0]))
        add("|---|---|---|---|")
        for ra, rb in zip(a_rows, b_rows):
            goal = ra[3] or GOAL_HINT.get(ra[0], "—")
            add("| %s %s | %s | %s | %s |" % (ra[0], ra[1], goal, ra[2], rb[2]))
        add("")
    else:
        add("（未找到可解析的 `%s`）" % METRICS_FILE)
        add("")
    if ab:
        add("**同帧 A/B 净贡献**（关掉对应特性再测一次，差值即该特性的净贡献）")
        add("")
        add("| 场景 | 实测原文 |")
        add("|---|---|")
        for scene, s in ab:
            add("| %s | %s |" % (scene, s))
        add("")
    if fps:
        add("**帧率**")
        add("")
        for s in fps:
            add("- %s" % s)
        add("")

    # ---------- 三、S4 水墨管线逐层贡献 ----------
    stages, diffs, quants = parse_s4(os.path.join(REPORTS, S4_FILE))
    if stages or diffs or quants:
        add("## 三、水墨渲染管线逐层贡献（S4 实测，论文核心）")
        add("")
        if stages:
            add("**四阶段对照**（依次叠加：量化光照 → 飞白墨线 → 宣纸底纹）")
            add("")
            add("| 阶段 | 组成 | 平均亮度 | 标准差 | 出现级数 | 涵盖 80% 像素所需级数 |")
            add("|---|---|---:|---:|---:|---:|")
            for no, name, lum, sd, lv, n80 in stages:
                add("| %s | %s | %s | %s | %s | %s |" % (no, name, lum, sd, lv, n80))
            add("")
        if diffs:
            add("**逐层像素差**（中央框平均绝对亮度差，取值 0–1）")
            add("")
            add("| 层间 | 差值 |")
            add("|---|---:|")
            for k, v in diffs:
                add("| %s | %s |" % (k, v))
            add("")
        if quants:
            add("**量化阶数对照**（白球：无贴图、飞白=0、轮廓=0、高光=0，只留一盏顺向平行光）")
            add("")
            add("| 材质 | 平均亮度 | 出现级数 | 前 4 种墨色覆盖 | 最大单级占比 |")
            add("|---|---:|---:|---:|---:|")
            for name, lum, lv, cover, top in quants:
                add("| %s | %s | %s | %s%% | %s%% |" % (name, lum, lv, cover, top))
            add("")

    # ---------- 四、S6 提交闸门 ----------
    for fname, milestone, title in GATES:
        path = os.path.join(REPORTS, fname)
        if not os.path.isfile(path):
            missing.append(fname)
            continue
        groups = parse_gate(path)
        if not groups:
            continue
        add("## 四、%s" % title)
        add("")
        for gname, items in groups:
            add("**%s**" % gname)
            add("")
            add("| 检查项 | 结果 |")
            add("|---|---|")
            for k, v in items:
                add("| %s | %s |" % (k, v))
            add("")

    # ---------- 四、逐条明细 ----------
    add("## 五、各批次逐条判定明细（论文附录可用）")
    add("")
    for title, fname, ok, ng, ts in details:
        add("### %s" % title)
        add("")
        if ts != "—":
            add("> 报告时间：%s　来源：`%s`" % (ts, fname))
            add("")
        add("| # | 判据 | 实测值 | 结果 |")
        add("|---:|---|---|---|")
        for i, (k, v) in enumerate(ok, 1):
            add("| %d | %s | %s | 通过 |" % (i, k, v or "—"))
        for k, v in ng:
            add("| — | %s | %s | **未通过** |" % (k, v or "—"))
        add("")
        add("小计：通过 %d / 未通过 %d" % (len(ok), len(ng)))
        add("")

    if missing:
        add("> ⚠️ 缺失报告：%s" % "、".join(missing))
        add("")

    with open(OUT, "w", encoding="utf-8") as fh:
        fh.write("\n".join(L) + "\n")

    print("已生成：%s" % OUT)
    print("用例批次 %d 个，解析通过 %d 项，未通过 %d 项" % (len(rows), total_ok, total_ng))
    if audit:
        print("[对账] 以下报告的解析数与自身声明不一致，需人工核对：")
        for fname, a_ok, a_ng, d_ok, d_ng in audit:
            print("  - %s：解析 %d/%d  报告声明 %d/%d" % (fname, a_ok, a_ng, d_ok, d_ng))
    else:
        print("[对账] 全部批次解析数与报告声明一致 ✓")
    print("[指标] 场景 %d 个，A/B 净贡献 %d 条，帧率 %d 条" % (len(scenes), len(ab), len(fps)))
    if missing:
        print("缺失：%s" % "、".join(missing))


if __name__ == "__main__":
    main()
