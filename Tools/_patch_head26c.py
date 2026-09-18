# -*- coding: utf-8 -*-
"""修 CS1003：我给 headAlignPitchDeg 的 Tooltip 补了几行，但**忘了写 `+`**。
C# 不像 C/C++ 会自动拼接相邻字符串字面量 ⇒ `"a" "b"` 是语法错误。
（lint 不管语法；Console 说"干净"；第一次 check_compile 也回了"干净"——
  因为 refresh 触发的编译那时还没跑完。教训见坑表。）
"""
import io

P = 'Assets/_Project/Scripts/Enemies/EnemyDragon.cs'
s = io.open(P, encoding='utf-8', newline='').read()
rep = []

LINES = [
    u'                 "★★ 第二十六轮更正（本条**作废**，见坑表条目 48）：以上档位全部是在**拉直之后**的基准上对照的，"',
    u'                 "而拉直把颈根（头簇挂点 `drgon_025`）拧了 **93.37°** ⇒ 那些「档位」建立在歪基准上。"',
    u'                 "现在 `headLockUseRestPose` 默认锁 **FBX 原生姿态**，实测（`drg_headfix`，同一物件同机位）："',
    u'                 "pitch=−12 时头仍偏 **12.00°**（= headExtra 的角度）；pitch=0 时偏 **0.00°**、"',
    u'                 "★ 旧记录（仅供追溯）：−12 出自第二十二轮十档扫描；更早的 30 出自第十八轮「头簇 PCA 第一主轴俯仰 30.2°」"',
]
for L in LINES:
    broken = L + u'\n'
    fixed = L + u' +\n'
    got = s.count(broken)
    assert got == 1, u'期望 1 处，实际 %d：\n%s' % (got, L[:80])
    s = s.replace(broken, fixed, 1)
    rep.append(u'[OK] 补 +  %s...' % L[24:52])

io.open(P, 'w', encoding='utf-8', newline='').write(s)
print(u'\n'.join(rep))
