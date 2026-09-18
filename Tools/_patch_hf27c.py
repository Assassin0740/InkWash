# -*- coding: utf-8 -*-
# drg_headfollow27.cs 第三版补丁：
#   ① DRIVE_HZ 0.45 -> 1.15（实测 sweepFrequency；第一版是猜的 ⇒ 出图相位是乱的）
#   ② 采样窗口 67 -> 30 帧（1.15 个周期，够取真极值，同时把「容器系朝向」的慢漂移暴露面积减半）
#   ③ 出图段两档都打数值（便于给拼图写注释）
import io

P = r'D:\Unity Project\InkWash\Tools\cs\drg_headfollow27.cs'
s = io.open(P, encoding='utf-8').read()
n0 = len(s)


def rep(old, new, cnt, tag):
    global s
    c = s.count(old)
    assert c == cnt, '[%s] 期望 %d 处，实际 %d 处' % (tag, cnt, c)
    s = s.replace(old, new)


rep('    const float DRIVE_HZ = 0.45f;',
    '    const float DRIVE_HZ = 1.15f;          // ★ 实测 sweepFrequency（第一版猜的 0.45 是错的）',
    1, '① 频率')

rep('    const int CYCLE_FRAMES = 67;',
    '    const int CYCLE_FRAMES = 30;',
    1, '② 窗口')

rep('// 【采样纪律】`Time.captureFramerate = 30` 钉死（坑表 27），每档连采 **67 帧**\n'
    '//   （0.45 Hz ⇒ 周期 2.2222 s ⇒ 66.67 帧）⇒ 恰好一个整周期，p2p 才是真极差，不是周期零头。',
    '// 【采样纪律】`Time.captureFramerate = 30` 钉死（坑表 27），每档连采 **30 帧**\n'
    '//   （sweepFrequency = 1.15 Hz ⇒ 周期 26.09 帧）⇒ 1.15 个周期。\n'
    '//   ★ p2p 只要求窗口 **≥ 1 个周期**（更长也行，不必整数倍）：任何 ≥ 1 周期的窗口必含极值。\n'
    '//   ★ 但**也别太长**：「容器系朝向」里含一条慢漂移（容器跟随滞后 / 整身摆动，30 s 里能漂 ±13°），\n'
    '//     窗口越长 p2p 越被漂移撑大 ⇒ 判据优先用「头/颈比」与「相对角误差」这两条**不含慢漂**的量。',
    1, '② 注释')

rep('''                       + "（captureFramerate=30，0.45 Hz ⇒ 周期 66.67 帧）");''',
    '''                       + "（captureFramerate=30，sweepFrequency=" + SweepHz().ToString("0.00")
                       + " Hz ⇒ 周期 " + (30f / Mathf.Max(1e-4f, SweepHz())).ToString("0.00")
                       + " 帧，窗口 " + CYCLE_FRAMES + " 帧 ⇒ " + (CYCLE_FRAMES * SweepHz() / 30f).ToString("0.00")
                       + " 个周期）");''', 1, '② 报告头')

rep('''    float Phase()
    {
        if (_fSweep == null) return -1f;
        float hz = (float)_fSweep.GetValue(_drg);
        return Mathf.Repeat(hz * Time.time, 1f);
    }''',
    '''    float SweepHz()
    {
        if (_fSweep == null) return 0f;
        return (float)_fSweep.GetValue(_drg);
    }

    float Phase() { return Mathf.Repeat(SweepHz() * Time.time, 1f); }''', 1, '② SweepHz')

rep('''                string tag = "w" + (mi == 0 ? "0" : "1") + "_p" + pi;
                ShootAll(tag);
                if (mi == 0)
                    _sb.AppendLine("  相位 " + PHASES[pi].ToString("0.00") + "  实测 " + Phase().ToString("0.0000")
                                   + "  头偏航 " + HeadYawNow().ToString("+0.00;-0.00") + "°"
                                   + "  颈偏航 " + NeckYawNow().ToString("+0.00;-0.00") + "°");''',
    '''                string tag = "w" + (mi == 0 ? "0" : "1") + "_p" + pi;
                ShootAll(tag);
                _sb.AppendLine("  相位 " + PHASES[pi].ToString("0.00") + " 实测 " + Phase().ToString("0.0000")
                               + "  w" + (mi == 0 ? "0" : "1")
                               + " 头偏航 " + HeadYawNow().ToString("+0.00;-0.00") + "°"
                               + " 颈偏航 " + NeckYawNow().ToString("+0.00;-0.00") + "°");''', 1, '③ 出图数值')

io.open(P, 'w', encoding='utf-8', newline='').write(s)
s2 = io.open(P, encoding='utf-8').read()
assert s2.count('SweepHz') == 5, s2.count('SweepHz')
assert '0.45f' not in s2, '0.45 残留'
assert s2.count('CYCLE_FRAMES') == 7, s2.count('CYCLE_FRAMES')
print('OK  drg_headfollow27.cs %d -> %d bytes' % (n0, len(s2)))
