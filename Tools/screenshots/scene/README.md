# `Tools/screenshots/scene/` —— 演示场出图索引

> 本目录的图**全部在测试场景 `Assets/_Project/Scenes/Showcase.unity` 里、用演示场相机**拍，
> 判据是**同 tick 背靠背差分帧**，**没有任何哨兵底色**。
> 上一批在正式关卡里自搭相机/灯光、用哨兵色拍的那些图在 `../enemies/`，口径已被本目录取代。

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

## ★ 主结论图

**`_对比_演示场正常相机_四个对象_开vs关.png`** —— 五组开/关并排，一眼看完：

| 行 | 对象 | 读数 |
|---|---|---|
| 1 | 墨山 `Z_Enemy_MoShan`（14 SMR，骨骼 0/14） | **0.000%**（两帧 md5 相同） |
| 2 | 墨徒 `Z_Enemy_MoGuai`（10 SMR，骨骼 0/10） | **0.000%**（仅 1 px 噪点） |
| 3 | 墨骨 `Z_Enemy_MoGu` —— **只留 3 个 SMR** | **0.000%**（两帧 md5 相同） |
| 4 | 墨骨 `Z_Enemy_MoGu` —— **只留 4 个 MeshRenderer** | **1.930%** ← 它唯一画得出来的部分 |
| 5 | 墨龙 `Z_Enemy_MoLong`（骨骼 273/273）**阳性对照** | **2.063%** |

口径：1280×720 = 921600 px，差值阈值 6。

---

## 文件命名

| 前缀 | 来历 |
|---|---|
| `SV_<prefab>_all_开/关.png` | `dg_sc_verify.cs`：全部渲染器（SMR + MeshRenderer） |
| `SV_<prefab>_smr_开/关.png` | 同上，**只留 `SkinnedMeshRenderer`** |
| `SV_<prefab>_mr_开/关.png` | 同上，**只留普通 `MeshRenderer`** |
| `SH_<prefab>_A_开/_B_关.png` | `dg_sc_shan.cs`（早期一版，取景未对准，留档） |
| `SC_<昵称>_A_开/_B_关.png` | `dg_sc_vis.cs`：走演示场条目 `PlayForCapture`，相机跟着演员 |
| `MG_*` | `dg_sc_mogu.cs`（早期拆解，取景有误，结论已被 `SV_*` 取代） |

生成工具：`Tools/cs/dg_sc_verify.cs`（★ 主）、`dg_sc_vis.cs`、`dg_sc_shan.cs`、`dg_sc_mogu.cs`；
拼图 `Tools/make_sc_compare.py`。报告在 `Tools/reports/dg_sc_*.txt`。

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
