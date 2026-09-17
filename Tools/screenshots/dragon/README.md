# 龙出图索引（`Tools/screenshots/dragon/`）

> 同一个目录里混着**三代状态**的图，看图前先对一下时间戳，别把坏图的结论用到好图上。

---

## ✅ 当前状态（回退 `b540f26` 之后，可信）

**时间戳 2026-09-17 16:58**｜探针 `Tools/cs/dg_play_verify.cs`｜报告 `Tools/reports/dg_play_verify.txt`

| 文件 | 拍的是什么 |
|---|---|
| `play_molong_top.png` / `play_molong_side.png` | `Z_Enemy_MoLong.prefab` 的 Play 内实例 |
| `play_dragononly_top.png` / `play_dragononly_side.png` | `Z_Dragon.prefab` **单独实例化**（视觉子树源，隔离验证用） |

**口径**：`EnemyDragon` 停机 → 等 2 帧让蒙皮更新 → 品红哨兵背景 → 量非背景像素。

**实测**：MoLong `9.43%`(top) / `9.14%`(side)｜Z_Dragon `16.59%` / `13.20%`。
对照重建态的 **0.00%** ⇒ 龙确实回来了。

**⚠ 取景是探针按包围盒自动算的，没做构图优化** —— 尾巴/头可能出画。
那是**测量工具的取景**问题，不是模型问题。要看构图请用下面的历史参考图。

---

## 📌 历史参考（搬迁前记录的正常龙，构图好）

**时间戳 2026-09-16 18:47**：`R1_侧视` `R2_前视` `R3_俯视` `R4_近景`、`V1_侧视_左` `V2_侧视_右` `V3_正视` `V4_俯视` `V5_近景头`

**时间戳 2026-09-16 18:07**：`dg_1701..1705` `dg_1801..1805` `dg_1901..1905` `dg_2001..2005`（更早的一批）

**时间戳 2026-09-17 14:44**：`hover_before.png` / `hover_after.png`（上下游动前后的对比）

---

## ⛔ `_BROKEN_rebuild_state_20260917/` —— 别当参考

`look_raw_side.png` `look_raw_top.png` `look_straight_side.png` `look_straight_top.png`

**这 4 张是 `d31bc8e` 重建态（坏的那版）的实测图。**
判据：对应报告 `Tools/reports/dg_look.txt` 里 `_spine` 全是**自造的 `drgon_dup2…drgon_dup25`**
（180 骨），而网格 `bindposes=273` ⇒ 数量不匹配 ⇒ **几何塌缩**（看图就是一堆破碎多边形）。

留档**只为当反例**：证明"那次重建确实坏了"，**不是"龙长得这样"**。
（顺带：`look_raw_side.png` 与 `look_straight_side.png` 字节数完全相同 `716687` —— 因为几何已经塌缩，
"拉直脊柱"这一步自然看不出任何变化。）
