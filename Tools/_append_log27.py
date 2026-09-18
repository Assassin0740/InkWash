# -*- coding: utf-8 -*-
# 追加当日日志（append-only）
import io

P = r'D:\Unity Project\InkWash\.workbuddy\memory\2026-09-18.md'

TXT = u'''

## 第二十七轮 · 头「随颈」（傍晚）

用户：「现在确实是面对到位置了，但是脖子动的时候，头一直保持一个方向没有旋转，要面向过来」

**问题定性**：第二十六轮把「锁的**基准**」修对了（拉直前的 FBX 原生姿态），但写点
`rotation = rootRot · headExtra · _restRel[i]` 里的 `rootRot` 是**模型容器**的世界旋转
⇒ 这是「**相对容器**的绝对锁」：容器（整条龙）转向时头跟着转，但**脖子自己摆（行波）时头一动不动**。
实测头载体容器系偏航极差 = **0.02°**（尺子本底）——头真的没动。

**修法**：新增 `headNeckFollowWeight`（0 = 旧绝对锁，1 = 随颈，默认 1）
`R_i = R_{i−1} · headExtra · (inv(基准[i−1]) · 基准[i])`（`headExtra` 在父节之后、局部偏置之前）。
两处写点（`ApplySpineOffsetsRaw` / `ApplySerpentineSpine`）同步 + prefab 同步。改动 3 处，一次脚本 + 断言。

**实测（150 帧 = 2.25 个行波周期，`captureFramerate=30` 钉死）**
`w=0`：头偏航极差 0.02°｜头/颈比 0.001｜相对角误差 91.75°｜vs 基准 0.00°
`w=1`：头偏航极差 **35.58°**｜头/颈比 **1.010**｜相对角误差 **0.00°**｜vs 基准 91.75°
A/B（冻结姿态 + 同机位，6 个任意时刻）头−颈夹角：A 行漂 +4.1°…−27.9°（跨度 32.0°），B 行恒 −6.6°（跨度 0.0°）。
忠实性自检（探针手算 vs 组件刚写下的值）6/6 = **0.0000°**。

**踩到三把尺子**
1. 探针第一版没冻 `hoverStationary` / `enablePerformanceCycle` ⇒ 三档采样窗口落在性能周期不同相位，
   自检 C 读出 35.37 / 35.79 / 18.81。
2. 「等相位」出图是假的：巡游用 `swimWaveFreq`(0.45)，贴地扫击用 `sweepFrequency`(1.15)，
   我按错的频率等相位（甚至一度"修正"成 1.15，更错）⇒ 改成「冻姿态 + 同机位」。
3. 「容器系朝向」混了一条**未定位的慢分量** ⇒ 30 帧窗口给 28% 散布、150 帧给 1.1%
   ⇒ 判据换成比值 / 差 / 角误差。
   附带发现：`w=0 的相对角误差 ≡ w=1 的 vs 基准 = 91.75°` 是**代数恒等**（都 = `Angle(驱动颈, 基准颈)`）。

**产物**：`EnemyDragon.cs`（`headNeckFollowWeight` + 2 处写点）｜`Z_Enemy_MoLong.prefab`｜
`Tools/cs/drg_headfollow27.cs`｜`reports/drg_headfollow27.txt`｜`H28/_头随颈_v1_{俯视,前视}.png`｜
`Tools/make_head27_sheet.py`｜`Tools/_patch_head27.py`｜`Tools/_patch_hf27b/c.py`｜
`Docs/工程坑表.md` **50 / 51**｜`Docs/长期记忆_数值参考.md` 补第二十七轮
门禁：cs_lint **160 文件 0 不合格**｜check_prefab_overrides **122 字段 0 不一致 0 未写** ✅

**待办**：用户开编辑器用眼睛验收；若嫌头甩得厉害把 `headNeckFollowWeight` 往 0 调（**别用 0.5 附近**）。
'''

with io.open(P, 'a', encoding='utf-8') as f:
    f.write(TXT)
s = io.open(P, encoding='utf-8').read()
print(u'OK 日志 %d chars' % len(s))
