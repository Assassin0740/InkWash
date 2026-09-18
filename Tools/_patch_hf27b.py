# -*- coding: utf-8 -*-
# drg_headfollow27.cs 第二版补丁：
#   ① 冻结与「权重」无关的慢调制（hoverStationary / enablePerformanceCycle）
#   ② 增列「颈仰角p2p」与「头/颈 p2p 比」—— 后者与幅度漂移无关，是更硬的判据
import io

P = r'D:\Unity Project\InkWash\Tools\cs\drg_headfollow27.cs'
s = io.open(P, encoding='utf-8').read()
n0 = len(s)


def rep(old, new, cnt, tag):
    global s
    c = s.count(old)
    assert c == cnt, '[%s] 期望 %d 处，实际 %d 处' % (tag, cnt, c)
    s = s.replace(old, new)


# ── ① 冻结慢调制 ──
rep('''        _spine = fSpine.GetValue(_drg) as System.Collections.IList;''',
    '''        // ★★ 对照实验纪律（第二十七轮第一版踩过）：把与「权重」无关的慢调制**全部冻结**。
        //   第一版没冻 ⇒ 三档的 67 帧采样窗口落在性能周期的不同相位上（每档相隔约 2.9 s），
        //   读出的「颈偏航 p2p」成了 35.37 / 35.79 / 18.81 —— 自检 C 当场报警。
        //   ★ 教训：**对照实验里任何"会随时间自己变"的量都必须先钉死**，否则 p2p 不可比。
        var fHover = _dt.GetField("hoverStationary", BF);
        var fPerf = _dt.GetField("enablePerformanceCycle", BF);
        if (fHover != null) fHover.SetValue(_drg, true);
        if (fPerf != null) fPerf.SetValue(_drg, false);
        _sb.AppendLine("冻结：hoverStationary=" + (fHover != null ? fHover.GetValue(_drg).ToString() : "n/a")
                       + "｜enablePerformanceCycle=" + (fPerf != null ? fPerf.GetValue(_drg).ToString() : "n/a"));

        _spine = fSpine.GetValue(_drg) as System.Collections.IList;''', 1, '① 冻结')

# ── ② 统计量 ──
rep('''            float yMin = 1e9f, yMax = -1e9f, eMin = 1e9f, eMax = -1e9f;
            float nMin = 1e9f, nMax = -1e9f, rMax = 0f, sMax = 0f, nkMin = 1e9f, nkMax = -1e9f;''',
    '''            float yMin = 1e9f, yMax = -1e9f, eMin = 1e9f, eMax = -1e9f;
            float nkMin = 1e9f, nkMax = -1e9f, neMin = 1e9f, neMax = -1e9f;
            float rMax = 0f, sMax = 0f;''', 1, '② 变量')

rep('''                float neckYaw = Vector3.SignedAngle(_f0, fN, Vector3.up);''',
    '''                float neckYaw = Vector3.SignedAngle(_f0, fN, Vector3.up);
                float neckElev = Mathf.Asin(Mathf.Clamp(fN.y, -1f, 1f)) * Mathf.Rad2Deg;''', 1, '② 颈仰角')

rep('''                if (neckYaw < nkMin) nkMin = neckYaw; if (neckYaw > nkMax) nkMax = neckYaw;
                if (nMin > 0f) { }                 // （占位，保持字段语义清晰）
                if (relErr > rMax) rMax = relErr;''',
    '''                if (neckYaw < nkMin) nkMin = neckYaw; if (neckYaw > nkMax) nkMax = neckYaw;
                if (neckElev < neMin) neMin = neckElev; if (neckElev > neMax) neMax = neckElev;
                if (relErr > rMax) rMax = relErr;''', 1, '② 统计')

rep('''        _sb.AppendLine("档位      帧数   头偏航p2p   头仰角p2p   颈偏航p2p   相对角误差max   vs基准max");''',
    '''        _sb.AppendLine("档位      帧数   头偏航p2p   头仰角p2p   颈偏航p2p   颈仰角p2p   头/颈比   相对角误差max   vs基准max");''', 1, '② 表头')

rep('''            _sb.AppendLine(Pad(WEIGHTS[m].ToString("0.00"), 9) + Pad(CYCLE_FRAMES.ToString(), 6)
                           + Pad((yMax - yMin).ToString("0.00"), 12) + Pad((eMax - eMin).ToString("0.00"), 12)
                           + Pad((nkMax - nkMin).ToString("0.00"), 12) + Pad(rMax.ToString("0.00"), 15)
                           + sMax.ToString("0.00"));''',
    '''            float headP2P = yMax - yMin, neckP2P = nkMax - nkMin;
            float ratio = neckP2P > 0.5f ? headP2P / neckP2P : 0f;   // 与幅度漂移无关的判据
            _sb.AppendLine(Pad(WEIGHTS[m].ToString("0.00"), 9) + Pad(CYCLE_FRAMES.ToString(), 6)
                           + Pad(headP2P.ToString("0.00"), 12) + Pad((eMax - eMin).ToString("0.00"), 12)
                           + Pad(neckP2P.ToString("0.00"), 12) + Pad((neMax - neMin).ToString("0.00"), 12)
                           + Pad(ratio.ToString("0.000"), 10) + Pad(rMax.ToString("0.00"), 15)
                           + sMax.ToString("0.00"));''', 1, '② 数据行')

rep('''        _sb.AppendLine("  A 「头偏航p2p」= 一个整周期里头载体容器系朝向的极差 —— 用户说的「有没有旋转」就是它。");''',
    '''        _sb.AppendLine("  A 「头偏航p2p」= 一个整周期里头载体容器系朝向的极差 —— 用户说的「有没有旋转」就是它。");
        _sb.AppendLine("  A' 「头/颈比」= 头偏航 p2p ÷ 颈偏航 p2p。★ 这条与幅度漂移无关：即使波幅被人为改大改小，");
        _sb.AppendLine("     只要头是 1:1 跟着脖子转，比值就恒为 1.000；被锁死则恒为 0。比裸 p2p 更硬。");''', 1, '② 读法')

io.open(P, 'w', encoding='utf-8', newline='').write(s)
s2 = io.open(P, encoding='utf-8').read()
assert s2.count('neckElev') == 3, s2.count('neckElev')
assert s2.count('颈仰角p2p') == 1
assert s2.count('hFover') == 0
print('OK  drg_headfollow27.cs %d -> %d bytes' % (n0, len(s2)))
