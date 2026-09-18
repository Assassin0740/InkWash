# -*- coding: utf-8 -*-
# 第二十五轮：新增 headLockToBase（头锁定基准姿态），两条驱动路径同步 + prefab 同步。
# 规矩：同文件多处改 → 一次性脚本 + count 断言 + 只写盘一次；newline='' 保持原换行。
import io

CS = r'D:/Unity Project/InkWash/Assets/_Project/Scripts/Enemies/EnemyDragon.cs'
PF = r'D:/Unity Project/InkWash/Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab'


def load(p):
    return io.open(p, encoding='utf-8', newline='').read()


def save(p, s):
    io.open(p, 'w', encoding='utf-8', newline='').write(s)


def rep(t, old, new, tag, n=1):
    c = t.count(old)
    assert c == n, u'期望 %d 处，实际 %d 处：%s' % (n, c, tag)
    print(u'OK  ' + tag)
    return t.replace(old, new)


# ═══════════ 1) EnemyDragon.cs ═══════════
t = load(CS)

old1 = '        public int headAlignLinks = 2;\n'
new1 = (u'        public int headAlignLinks = 2;\n'
        u'\n'
        u'        [Tooltip("★★ 头部锁定基准姿态（第二十五轮）：末尾 `headAlignLinks` 节**不跟随行波**，" +\n'
        u'                 "直接采用基准朝向 ⇒ 移动时头纹丝不动地朝前（= 模型原生姿态 + 上面三条对齐角）。\\n" +\n'
        u'                 "★ 为什么必须锁：`FromToRotation(倾斜基准段, 水平切向)` 是**最小旋转**，" +\n'
        u'                 "切向左右摆时会把偏航**耦合出俯仰** ⇒ 头簇刚性挂在 `spine[n-2]` 上只能跟着甩" +\n'
        u'                 "（`drg_headnod` 实测一个行波周期内吻部仰角极差 49.65°、偏航 13.42°）。\\n" +\n'
        u'                 "★ 与 `swimWaveHeadGain = 0` 的区别：那个把**头端一大段**的波都抽掉（脖子变直杆），" +\n'
        u'                 "这个只锁末尾 `headAlignLinks` 节 ⇒ **脖子照常摆动**，头绕颈根稳住。\\n" +\n'
        u'                 "★ 关掉则回到「头跟着曲线切向走」的旧行为。")]\n'
        u'        public bool headLockToBase = true;\n')
t = rep(t, old1, new1, u'C# 字段 headLockToBase')

old2 = (u'            // ── 绝对写入：每节从基准重算，无累积 ──\n'
        u'            for (int i = 0; i < n; i++)\n'
        u'            {\n'
        u'                if (_spine[i] == null) continue;\n'
        u'                Vector3 tLocal = invRoot * tan[i];\n'
        u'                Quaternion q = Quaternion.FromToRotation(_baseSegLocal[i], tLocal);\n'
        u'                if (alignHead && i >= headFrom) q = headExtra * q;\n'
        u'                _spine[i].rotation = rootRot * q * _baseRel[i];\n'
        u'            }\n')
new2 = (u'            // ── 绝对写入：每节从基准重算，无累积 ──\n'
        u'            // ★★ 第二十五轮「头锁定基准」（用户：「龙头要始终面向前方」）：\n'
        u'            //   末尾 `headAlignLinks` 节**不读曲线切向**，直接采用基准朝向\n'
        u'            //   ⇒ 跳过 `FromToRotation` ⇒ 头簇停在基准姿态、**与行波相位无关**。\n'
        u'            //   为什么必须锁：`FromToRotation(倾斜基准段, 水平切向)` 是最小旋转，\n'
        u'            //   切向左右摆时把偏航耦合出俯仰（`drg_headnod` 实测吻部仰角极差 49.65°）。\n'
        u'            bool lockHead = headLockToBase && headAlignLinks > 0;\n'
        u'            for (int i = 0; i < n; i++)\n'
        u'            {\n'
        u'                if (_spine[i] == null) continue;\n'
        u'                if (lockHead && i >= headFrom)\n'
        u'                {\n'
        u'                    _spine[i].rotation = rootRot * headExtra * _baseRel[i];\n'
        u'                    continue;\n'
        u'                }\n'
        u'                Vector3 tLocal = invRoot * tan[i];\n'
        u'                Quaternion q = Quaternion.FromToRotation(_baseSegLocal[i], tLocal);\n'
        u'                if (alignHead && i >= headFrom) q = headExtra * q;\n'
        u'                _spine[i].rotation = rootRot * q * _baseRel[i];\n'
        u'            }\n')
t = rep(t, old2, new2, u'C# ApplySerpentineSpine 写入循环')

old3 = (u'            bool alignHead = HeadAlignActive;\n'
        u'            Quaternion headExtra = HeadAlignExtra;\n'
        u'            int headFrom = HeadAlignFrom(n);\n')
new3 = (u'            bool alignHead = HeadAlignActive;\n'
        u'            Quaternion headExtra = HeadAlignExtra;\n'
        u'            int headFrom = HeadAlignFrom(n);\n'
        u'            bool lockHead = headLockToBase && headAlignLinks > 0;\n')
t = rep(t, old3, new3, u'C# ApplySpineOffsetsRaw 前置声明')

old4 = (u'                Quaternion q = Quaternion.AngleAxis(ex, Vector3.right)\n'
        u'                             * Quaternion.AngleAxis(yaw, Vector3.up)\n'
        u'                             * Quaternion.AngleAxis(pitch, Vector3.right);\n'
        u'                if (alignHead && i >= headFrom) q = headExtra * q;\n'
        u'                _spine[i].rotation = rootRot * q * _baseRel[i];\n')
new4 = (u'                // ★★ 头锁定基准（与 ApplySerpentineSpine 同一逻辑）：两条驱动路径必须行为一致，\n'
        u'                //   否则攻击相位一进一出、头会「啪」地弹一下。\n'
        u'                if (lockHead && i >= headFrom)\n'
        u'                {\n'
        u'                    _spine[i].rotation = rootRot * headExtra * _baseRel[i];\n'
        u'                    continue;\n'
        u'                }\n'
        u'                Quaternion q = Quaternion.AngleAxis(ex, Vector3.right)\n'
        u'                             * Quaternion.AngleAxis(yaw, Vector3.up)\n'
        u'                             * Quaternion.AngleAxis(pitch, Vector3.right);\n'
        u'                if (alignHead && i >= headFrom) q = headExtra * q;\n'
        u'                _spine[i].rotation = rootRot * q * _baseRel[i];\n')
t = rep(t, old4, new4, u'C# ApplySpineOffsetsRaw 写入循环')

save(CS, t)

# ═══════════ 2) prefab（119 个字段全显式，新字段必须同步） ═══════════
t2 = load(PF)
t2 = rep(t2,
         u'  headAlignLinks: 2\n  tailLevel: 1\n',
         u'  headAlignLinks: 2\n  headLockToBase: 1\n  tailLevel: 1\n',
         u'prefab headLockToBase')
save(PF, t2)

print(u'全部完成')
