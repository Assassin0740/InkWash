# -*- coding: utf-8 -*-
import io, sys

P = r"D:\Unity Project\InkWash\.workbuddy\memory\2026-09-18.md"
s = io.open(P, encoding="utf-8").read()
old_len = len(s)

BLOCK = u"""

## 第二十六轮 09-18 墨龙头部：三档全歪的**结构性**真因 = 拉直把头载体一起转掉

### 一、用户的输入，以及它的信息量
「这三个都是歪的，ABC，全是歪的」（A 不锁 / B 锁 pitch=0 / C 锁 pitch=−12）。
★★ **B、C 相对 A 只差「去掉摆动」⇒ 三档共有的那一项才是真因**，逐档调参永远调不出来。
判据来源（用户第二十二轮口述，本轮仍在用）：FBX 精致姿态里「头的相对位置与方向就是对的」。

### 二、真因（结构性）
`_baseRel` 采在 `StraightenSpineBase()` **之后**；而拉直循环 `for (i=0; i+1<Count; i++)`
**把头载体 `_spine[Count-2] = drgon_025` 也转了 93.37°**（段方向偏 66.56°，见 `reports/drg_headref.txt`）。
头簇（39 叶 + 鬃/须/角/颌）刚性、从不被驱动 ⇒ **头的世界朝向 ≡ 该骨世界旋转 × 常量**
⇒ 三档全在同一个歪基准上。这是坑表 42 / 46 的**结构性后果**（那两条只说「最小旋转会把俯仰拧进偏航」）。

### 三、修法
- 拉直**之前**再采一份 `_restRel`（`CaptureRestRel()`，**顺序是载荷**）
- 新增 `headLockUseRestPose`（默认 **true**），两处锁定写点必须同源
  （`ApplySpineOffsetsRaw` / `ApplySerpentineSpine`），否则攻击相位一进一出会弹
- 蛇形驱动仍走拉直后的 `_baseRel` / `_baseSegLocal`（那才是「它是一条蛇」的来源）
- `headAlignPitchDeg` 默认值 `−12 → 0`（prefab 同步）

### 四、实测（`Tools/cs/drg_headfix.cs`：同一物件就地摆回 FBX 姿态，唯一变量 = 姿态）
| 档位 | 相位 0.00/0.30/0.60 | 头载体总偏差 | 俯仰 | 偏航 | 滚转 |
|---|---|---|---|---|---|
| rest(FBX) | — | 0.00° | −46.9 | +86.6 | +0.0 |
| old | 三相位全同 | **138.97°** | −36.6 | +84.2 | **−175.6** |
| **new（出货）** | 三相位全同 | **0.00°** | −46.9 | +86.6 | +0.0 |

自检：头簇 8 叶局部旋转恒 **0.0000°**；出图 `H27/_头锁定换基_v3.png`（眼睛裁决：rest 与 new 一致，old 明显拧飞）。
闭合验证：`pitch = 0` 时偏差**恰为 0.00°** ⇒ 第二十二轮那个 `−12` 是在歪基准上挑的，作废。

### 五、尺子自检（本轮踩了两次）
1. 「头簇相对挂点」第一版报 `Δpos` = **每一行都 0.1935 m —— 连基准行自己也是**
   ⇒ 基准行不为 0 就立刻判尺子坏，一个数都不许引用。
   真因（`Tools/cs/drg_scale26.cs` 钉死）：两 prefab 根 scale 不同 ——
   `Z_Enemy_MoLong.prefab` **1.0000**（骨骼 lossy 0.0024）vs `Z_Dragon.prefab` **0.7546**（0.0018）
   ⇒ `1/0.7546 = 1.3252` = 报表里恒定的「半径比」；`0.1935/0.3252 = 0.595 m` = 叶骨到挂点平均距离。
   ⇒ 改成**归一化方向角 `Δdir` ≤ 0.03° + 单列半径比**（均匀缩放不改变方向）。
   ★★ 但更要紧的是：头簇**刚性且不被驱动** ⇒ 这一列**天生不能分辨档位**，
   它只能证明「锁定没把头的形状拧变形」。
   **给一个量设计判据之前先问一句：被测的差异会体现在这个量上吗？**
2. 基准帧必须**跨帧**才拍得到（蒙皮在 `PostLateUpdate` 算）：`写姿态 → yield return null` ×3，
   并在拍基准帧时 `Behaviour.enabled = false`（**`Component` 没有 `enabled`**）。
   纯 prefab 当基准要用 `AssetDatabase.LoadAssetAtPath` + `Instantiate`（别指望场景里那份），
   且**只拷 `localRotation`、绝不拷 `localPosition`**（容器层级不同，拷位置会把模型甩出画面）。

### 六、产物 / 门禁
- 新增：`Tools/cs/drg_headfix.cs`、`Tools/cs/drg_scale26.cs`；补丁 `Tools/_patch_headfix4~7.py`、
  `Tools/_patch_head26b/c.py`；`Tools/reports/drg_headfix.txt`、`Tools/reports/drg_scale26.txt`
- 修改：`EnemyDragon.cs`（`_restRel` + `CaptureRestRel()` + `headLockUseRestPose` + 两处锁定点 + pitch 默认 0）、
  `Z_Enemy_MoLong.prefab`（`headLockUseRestPose: 1`、`headAlignPitchDeg: 0`）
- 文档：`工程坑表.md` 新增 **48 / 49**；`长期记忆_数值参考.md` 补第二十三（风暴）+ 第二十六（头部）数值，
  并把 `pitch=−12` 标注作废
- 门禁：lint **158 文件 0 不合格**｜prefab **121 字段 0 不一致 0 未写** ✅｜`check_prefab_overrides` 结论 ✅
- ★ **仍未解决**（别记成风暴的锅）：判据 7「0 B/帧」未达标（~20.5 KB/帧，**但「特效关」也 20527 B/帧**
  ⇒ 归因在别处，待查）；吐息档「烟浓度 0.000 却有 24 个活烟团」自相矛盾（浓度尺子不敏感，待查）；
  `drg_storm_ab.cs` 的「走廊内青」掩码已盲（五档全 0），该探针作废
"""

io.open(P, "a", encoding="utf-8", newline="\n").write(BLOCK)
print("appended %d chars (was %d)" % (len(BLOCK), old_len))
