# -*- coding: utf-8 -*-
# 第二十七轮补丁：头部「随颈」权重 headNeckFollowWeight
#   R1 新增字段（Tooltip 长注解）
#   R2 两处锁定写点（ApplySpineOffsetsRaw / ApplySerpentineSpine）
#   R3 prefab 同步
# 规矩：每个替换先断言出现次数，再一次性写回（并行 Edit 同一文件会静默丢改动）。
import io

SRC = r'D:\Unity Project\InkWash\Assets\_Project\Scripts\Enemies\EnemyDragon.cs'
PF = r'D:\Unity Project\InkWash\Assets\_Project\Prefabs\Enemies\Z_Enemy_MoLong.prefab'

s = io.open(SRC, encoding='utf-8').read()
p = io.open(PF, encoding='utf-8').read()
n_before = len(s)


def rep(text, old, new, cnt, tag):
    c = text.count(old)
    assert c == cnt, '[%s] 期望 %d 处，实际 %d 处' % (tag, cnt, c)
    return text.replace(old, new)


# ─────────── R1 新增字段 ───────────
R1_OLD = '''        public bool headLockUseRestPose = true;
'''

R1_NEW = r'''        public bool headLockUseRestPose = true;

        [Tooltip("★★ 头部「随颈」权重（第二十七轮，坑表 50）：0 = 容器系绝对锁（上一轮行为），1 = 完全随颈。\n" +
                 "背景（用户：「现在确实是面对到位置了，但是脖子动的时候，头一直保持一个方向没有旋转，要面向过来」）：\n" +
                 "  上一轮换基把头**方向**修对了，但写点是 `rotation = rootRot · headExtra · _restRel[i]`\n" +
                 "  —— 这是**相对容器**的绝对锁：容器一转头就跟着转（所以「面向前方」那条成立），\n" +
                 "  可**脖子自己摆（行波）时头一动不动** ⇒ 头成了钉在颈根上的装饰，读起来「是死的」。\n" +
                 "★ 实测量级（drg_headfollow27，按 67 帧 = 整周期采样，captureFramerate 已钉）：\n" +
                 "  w=0 时头载体容器系偏航极差 **0.00°**（真的没动）；\n" +
                 "  w=1 时改成 `R_i = R_{i−1} · headExtra · (inv(_restRel[i−1]) · _restRel[i])`\n" +
                 "  即「与**父节**保持 FBX 原生局部角」⇒ 父节在哪头就在哪，脖子摆头跟着摆。\n" +
                 "★ w=0 逐位等于旧行为（可溯源）；中间值是两者的球面插值，只调「头甩多大」。\n" +
                 "★ 两条驱动路径（`ApplySpineOffsetsRaw` / `ApplySerpentineSpine`）必须同权重，\n" +
                 "  否则攻击相位一进一出头会「啪」地弹一下。")]
        public float headNeckFollowWeight = 1f;
'''
s = rep(s, R1_OLD, R1_NEW, 1, 'R1 字段')

# ─────────── R2 两处锁定写点 ───────────
R2_OLD = '''                if (lockHead && i >= headFrom)
                {
                    // ★★ 第二十六轮（坑表 48）：默认锁**拉直之前**的 FBX 原生姿态。
                    //   锁拉直后的 `_baseRel` 会把头永久钉在「被拉直转歪 93°」的姿态上 ——
                    //   这正是用户说的「三档全是歪的」。两条驱动路径必须选同一个源。
                    Quaternion hb = (headLockUseRestPose && _restRel.Count == _spine.Count)
                                    ? _restRel[i] : _baseRel[i];
                    _spine[i].rotation = rootRot * headExtra * hb;
                    continue;
                }'''

R2_NEW = r'''                if (lockHead && i >= headFrom)
                {
                    // ★★ 第二十六轮（坑表 48）：默认锁**拉直之前**的 FBX 原生姿态。
                    //   锁拉直后的 `_baseRel` 会把头永久钉在「被拉直转歪 93°」的姿态上 ——
                    //   这正是用户说的「三档全是歪的」。两条驱动路径必须选同一个源。
                    var src = (headLockUseRestPose && _restRel.Count == _spine.Count) ? _restRel : _baseRel;
                    // ★★ 第二十七轮（坑表 50）：**相对容器**的绝对锁 ⇒ 脖子摆、头不摆。
                    //   用户：「脖子动的时候，头一直保持一个方向没有旋转，要面向过来」。
                    //   改成「与父节保持 FBX 原生局部角」= 父节在哪头就在哪。
                    //   ★ `headExtra` 放在父节之后、局部偏置之前 ⇒ 仍是「相对父节抬/转头」。
                    //   ★ `hw = 0` 走原式（逐位等于旧行为，便于溯源与回退）。
                    Quaternion qAbs = rootRot * headExtra * src[i];
                    float hw = Mathf.Clamp01(headNeckFollowWeight);
                    if (hw <= 0f || i <= 0 || _spine[i - 1] == null)
                    {
                        _spine[i].rotation = qAbs;
                        continue;
                    }
                    Quaternion lRel = Quaternion.Inverse(src[i - 1]) * src[i];
                    Quaternion qRel = _spine[i - 1].rotation * headExtra * lRel;
                    _spine[i].rotation = hw >= 1f ? qRel : Quaternion.Slerp(qAbs, qRel, hw);
                    continue;
                }'''
s = rep(s, R2_OLD, R2_NEW, 2, 'R2 两处写点')

# ─────────── R3 prefab 同步 ───────────
p = rep(p, '  headLockUseRestPose: 1\n',
        '  headLockUseRestPose: 1\n  headNeckFollowWeight: 1\n', 1, 'R3 prefab')

io.open(SRC, 'w', encoding='utf-8', newline='').write(s)
io.open(PF, 'w', encoding='utf-8', newline='').write(p)

# 回读自检
s2 = io.open(SRC, encoding='utf-8').read()
p2 = io.open(PF, encoding='utf-8').read()
assert s2.count('headNeckFollowWeight') == 3, 'R1/R2 计数异常: %d' % s2.count('headNeckFollowWeight')
assert s2.count('Quaternion qAbs') == 2, '写点没改够'
assert s2.count('Quaternion hb =') == 0, '旧写点残留'
assert p2.count('headNeckFollowWeight: 1') == 1
assert '\\n' in s2, 'HTML?\n'  # 占位断言，保证 \\n 没被吃掉
print('OK  EnemyDragon.cs %d -> %d bytes (+%d) | prefab %d -> %d bytes'
      % (n_before, len(s), len(s) - n_before, len(p), len(p2)))
