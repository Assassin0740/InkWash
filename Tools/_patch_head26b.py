# -*- coding: utf-8 -*-
"""第二十六轮（续）：headAlignPitchDeg −12 → 0，并更正它的 Tooltip。

判据（drg_headfix，同一物件/同一相机/同一材质，唯一变量=姿态）：
    old（锁拉直后基准）        头载体偏 130.09°
    new（锁 FBX 原生基准）     头载体偏  12.00°   ← 恰好等于 headExtra 的角度
    new + headAlignPitchDeg=0  头载体偏   0.00°   ← 俯仰/偏航/滚转三列与基准帧逐个相同
⇒ 用户明确的判据来源是「FBX 精致姿态里头的相对位置和方向都是对的」，
  而 0 就是那个姿态的精确实现；−12 是第二十二轮在**歪基准**上挑的补偿量，必须作废。
"""
import io

P = 'Assets/_Project/Scripts/Enemies/EnemyDragon.cs'
s = io.open(P, encoding='utf-8', newline='').read()
rep = []


def sub(old, new, cnt=1, tag=''):
    global s
    got = s.count(old)
    assert got == cnt, u'[%s] 期望 %d，实际 %d：\n%s' % (tag, cnt, got, old[:200])
    s = s.replace(old, new, cnt)
    rep.append(u'[OK] %-24s %d' % (tag, cnt))


sub(
    u'                 "★ 现值 −12 出自第二十二轮十档扫描（+3 到 −24 步长 3）。旧值 30 出自第十八轮的「头簇 PCA 第一主轴俯仰 30.2°」" +\n'
    u'                 "—— 那把尺子量的是**颈+颅的质量走向**，不是吻部指向，属坑表条目 29。\\n" +\n',

    u'                 "★★ 第二十六轮更正（本条**作废**，见坑表条目 48）：以上档位全部是在**拉直之后**的基准上对照的，"\n'
    u'                 "而拉直把颈根（头簇挂点 `drgon_025`）拧了 **93.37°** ⇒ 那些「档位」建立在歪基准上。"\n'
    u'                 "现在 `headLockUseRestPose` 默认锁 **FBX 原生姿态**，实测（`drg_headfix`，同一物件同机位）："\n'
    u'                 "pitch=−12 时头仍偏 **12.00°**（= headExtra 的角度）；pitch=0 时偏 **0.00°**、"\n'
    u'                 "俯仰/偏航/滚转三列与 FBX 基准帧逐个相同 ⇒ **现值改为 0**。\\n" +\n'
    u'                 "★ 旧记录（仅供追溯）：−12 出自第二十二轮十档扫描；更早的 30 出自第十八轮「头簇 PCA 第一主轴俯仰 30.2°」"\n'
    u'                 "—— 那把尺子量的是**颈+颅的质量走向**，不是吻部指向，属坑表条目 29。\\n" +\n',
    1, u'Tooltip 作废旧档位')

sub(u'        public float headAlignPitchDeg = -12f;\n',
    u'        public float headAlignPitchDeg = 0f;\n',
    1, u'C# 默认值改 0')

io.open(P, 'w', encoding='utf-8', newline='').write(s)

# ── prefab 同步 ──
PP = 'Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab'
t = io.open(PP, encoding='utf-8', newline='').read()
got = t.count(u'  headAlignPitchDeg: -12\n')
assert got == 1, u'[prefab] 期望 1 处，实际 %d' % got
t = t.replace(u'  headAlignPitchDeg: -12\n', u'  headAlignPitchDeg: 0\n', 1)
io.open(PP, 'w', encoding='utf-8', newline='').write(t)
rep.append(u'[OK] %-24s %d' % (u'prefab headAlignPitchDeg', 1))

print(u'\n'.join(rep))
