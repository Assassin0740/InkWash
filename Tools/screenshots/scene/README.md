# `Tools/screenshots/scene/` —— 演示场出图索引

> 本目录的图**全部在测试场景 `Assets/_Project/Scenes/Showcase.unity` 里、用演示场相机**拍，
> 判据是**同 tick 背靠背差分帧**，**没有任何哨兵底色**。
> 上一批在正式关卡里自搭相机/灯光、用哨兵色拍的那些图在 `../enemies/`，口径已被本目录取代。
>
> **09-17 状态：三个 Ziyuan 敌人的「实渲 0 像素」已修复**（骨骼引用接回），
> 所以本目录同时留有「修复前」与「修复后」两套图 —— 对照着看最有说服力。

---

## 怎么读这里的图

每张 `*_开.png` / `*_关.png` 是一对：

| | 含义 |
|---|---|
| `_开` | 被测渲染器 **enabled = true**，其余条件不变 |
| `_关` | 同一相机、**同一 tick**（中间不推进帧），被测渲染器 **enabled = false** |

- **两张逐字节相同（md5 一致）** ⇒ 这个渲染器**一个像素都没画**。把"开"换成"关"，画面毫无变化。
- **两张不同** ⇒ 差异处就是它画出来的像素。

两帧都先把被测对象的 `shadowCastingMode` 置 `Off` ⇒ 差集是**纯几何像素**，不含投影。
画面就是 `Showcase_Camera` 的正常渲染结果 —— 台子/灯光/地面材质全部来自
`ActionShowcase.BuildStage()`，即游戏里那一套。

---

## ★ 主结论图（两张对着看）

| 图 | 内容 |
|---|---|
| `_修复前_四对象_开vs关.png` | 五组开/关并排 —— 墨山/墨徒/墨骨(SMR) **全部 0.000%（两帧 md5 相同）** |
| `_修复后_四对象_开vs关.png` | 同五组、同一把尺子 —— 全部变成有读数的正常渲染 |
| `_验收_演示场面板逐条目召唤_六个敌人.png` | 走演示场真实路径 `PlayForCapture`，六个敌人六格全画出来 |

**修复前 / 修复后读数对照**（`dg_sc_verify.cs`，1280×720 = 921600 px，差值阈值 6）：

| 对象 | 修复前 | 修复后 |
|---|---|---|
| 墨山 `Z_Enemy_MoShan`（14 SMR，骨骼 26/26） | 0.000%（两帧 md5 相同） | **6.269%** |
| 墨徒 `Z_Enemy_MoGuai`（10 SMR，骨骼 97/97） | 0.000%（仅 1 px 噪点） | **9.301%** |
| 墨骨 `Z_Enemy_MoGu` —— **只留 3 个 SMR** | **0.000%**（两帧 md5 相同） | **4.884%** |
| 墨骨 `Z_Enemy_MoGu` —— **只留 4 个 MeshRenderer** | 1.930% | 2.795% |
| 墨龙 `Z_Enemy_MoLong`（骨骼 273/273）**阳性对照** | 2.063% | 2.373% |

**演示场真实路径**（`dg_sc_vis.cs`，`PlayForCapture` 逐条目召唤，六个演员全部到位）：

| 面板条目 | 演员 | 纯几何像素 |
|---|---|---|
| 墨徒（条目 8） | `Showcase_Actor_8` | 1.015% |
| 墨偶（条目 11） | `Showcase_Actor_11` | 0.860% |
| 墨魇（条目 14） | `Showcase_Actor_14` | 1.329% |
| 墨山（条目 17） | `Showcase_Actor_17` | 1.407% |
| 墨骨（条目 20） | `Showcase_Actor_20` | 0.494% |
| 墨龙（条目 23） | `Showcase_Actor_23` | 2.964% |

六个目标「骨骼引用全 null 的 SMR」**全部为 0**。

---

## 文件命名

| 前缀 | 来历 |
|---|---|
| `SV_<prefab>_all_开/关.png` | `dg_sc_verify.cs`：全部渲染器（SMR + MeshRenderer） |
| `SV_<prefab>_smr_开/关.png` | 同上，**只留 `SkinnedMeshRenderer`** |
| `SV_<prefab>_mr_开/关.png` | 同上，**只留普通 `MeshRenderer`** |
| `SC_<昵称>_A_开/_B_关.png` | `dg_sc_vis.cs`：走演示场条目 `PlayForCapture`，相机跟着演员 |
| `_修复前_*` / `_修复后_*` | `Tools/make_sc_compare.py` 拼的对照图 |
| `_验收_*` | `Tools/make_sc_vis_sheet.py` 拼的六敌人验收图 |
| `SH_<prefab>_A_开/_B_关.png` | `dg_sc_shan.cs`（早期一版，取景未对准，留档） |
| `MG_*` | `dg_sc_mogu.cs`（早期拆解，取景有误，结论已被 `SV_*` 取代） |

生成工具：`Tools/cs/dg_sc_verify.cs`（★ 主）、`dg_sc_vis.cs`、`dg_sc_shan.cs`、`dg_sc_mogu.cs`；
拼图 `Tools/make_sc_compare.py`、`Tools/make_sc_vis_sheet.py`。报告在 `Tools/reports/dg_sc_*.txt`。

> ⚠️ `SC_*` 这批图的读数**会随演示场面板条目数变化而变** —— 09-17 给面板补了墨山/墨骨两组条目，
> 条目索引整体后移（墨龙 17 → 23）。脚本按**名字**查条目，不是硬编码索引。

---

## ⚠️ 这个目录里曾经出过的错（看到的图若与上表不符，就是踩了这些）

1. **相机方向算反** ⇒ 相机落到地面以下，被 60 m 地板整个挡住 ⇒ **连墨龙都读 0.000%**。
2. **相机没对准 / 没查是否在视锥内** ⇒ `MoGu` 一次读 0.331%、一次读 0.000%。
   ⇒ 现在的报告强制报 `TestPlanesAABB` 与相机坐标。
3. **一刀切禁 `MonoBehaviour`** ⇒ `InkMaterialSwap` 不跑 ⇒ 墨龙渲染成**品红**，
   又制造出一个"材质丢了"的假象（**看到品红先问是不是自己干的**）。
4. **用哨兵底色**（尤其洋红 `(255,0,255)` = Unity 缺材质色）⇒ 看图的任何人都分不清
   "我铺的画布"和"真的缺材质"。**本目录已全面弃用哨兵色。**
5. 清场时用 `StartsWith("dg_")` 扫根对象 ⇒ 把探针自己删了 ⇒ 脚本"启动了但没产出"。
6. **改完 `.cs` 没等编译就跑** ⇒ Play 模式下 Unity 用旧 Assembly 继续跑
   （实测 `ItemCount` 还是 23、`prefab` 仍为 `<null>`）⇒ **"改了没反应"多半是这个**。
   `stop` → `AssetDatabase.Refresh()` → 等 `isCompiling` 转 False → 再进 Play。
   探针 `Tools/cs/q_comp.cs`。
