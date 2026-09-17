# Tools/cs_archive —— 历史诊断脚本归档

## 为什么有这个目录

`Tools/cs/` 在多个 sprint 累积到 **465 个**一次性诊断脚本（`a1_survey` / `q_foot3` / `s3_probe_lean` …）。
它们绝大多数是"当时为查某个具体问题写的一次性探针"，问题查完就没有再运行的价值，
但**删掉不可恢复**，所以采用**归档（移动）**而不是删除 —— 完全可逆。

## 归档规则（不是随手挑的）

**以引用为准**：凡是被 `Docs/*.md` 或 `Tools/*.py` 用 `cs/<名字>.cs` 形式点名过的脚本，
**一律留在 `Tools/cs/`**。其余全部移到本目录。

依据：这些被点名的脚本是**可重跑的工作流**（复原素材、烤场景、跑验收），
而没被点名的只服务过某一次排查。

## 归档结果（2026-09-17）

| | 数量 |
|---|---|
| 归档到本目录 | 408 |
| 保留在 `Tools/cs/` | 57 |

保留的 57 个例如：`sc_open.cs`（开演示场）、`dg_axis2.cs`（量蛇形波轴向）、
`g_props*.cs`（建瓦檐/道具）、`perf_*.cs`（性能基线）、`s3_swap_player_model.cs`（换主角模型）等。

## 怎么用归档里的脚本

移动**不影响执行** —— `exec_cs.py` 接受任意路径：

```bash
python Tools/exec_cs.py cs_archive/a33_dragon_final2.cs
```

## 怎么撤回

```powershell
Move-Item Tools\cs_archive\*.cs Tools\cs\
```

（`README.md` 留着即可。）
