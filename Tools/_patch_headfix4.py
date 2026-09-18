# -*- coding: utf-8 -*-
"""给 drg_headfix 加第四档「新锁 + headAlignPitchDeg = 0」当闭环验证。

推理：写入是 `rotation = rootRot * headExtra * restRel[i]`，而 `headExtra = Euler(-pitch, yaw, roll)`。
三个角全 0 时 headExtra = 单位 ⇒ 头载体的旋转**恒等于** FBX 基准 ⇒ 偏差必须精确 0.00°。
若实测不是 0，说明「12.00° = headExtra」这条推理错了，必须重查。

★ 读法那行**另起一个 AppendLine**，不去拆原有字符串字面量 ——
  项目硬规矩：探针里出现 `\n" +` 会被工具链变成真换行 ⇒ CS1010。
★ 不用 bash 的 python -c：反引号会被 shell 当命令替换吃掉。
"""
import io

P = 'Tools/cs/drg_headfix.cs'
s = io.open(P, encoding='utf-8', newline='').read()
rep = []


def sub(old, new, cnt=1, tag=''):
    global s
    got = s.count(old)
    assert got == cnt, u'[%s] 期望 %d，实际 %d：\n%s' % (tag, cnt, got, old[:200])
    s = s.replace(old, new, cnt)
    rep.append(u'[OK] %-22s %d' % (tag, cnt))


old = (u"        fUseRest.SetValue(_drg, true);\n"
       u"        fPitch.SetValue(_drg, -12f);\n"
       u"\n"
       u"        _sb.AppendLine();")
new = (u"        // ── 闭环验证：新锁 + headAlignPitchDeg = 0 ⇒ 应当 **0.00°**（与 FBX 一模一样）──\n"
       u"        //   因为 headExtra = Euler(-pitch, yaw, roll)；三个角全 0 时 headExtra = 单位\n"
       u"        //   ⇒ 头载体旋转恒等于基准。若实测不是 0，说明「12.00° = headExtra」这条推理是错的。\n"
       u"        fUseRest.SetValue(_drg, true);\n"
       u"        fPitch.SetValue(_drg, 0f);\n"
       u"        yield return null;\n"
       u"        yield return WaitPhase(PHASES[0]);\n"
       u"        {\n"
       u"            Quaternion rz = Quaternion.Inverse(_modelRoot.rotation) * carrier.rotation;\n"
       u"            Row(\"new+pitch0\", PHASES[0], rz, false);\n"
       u"        }\n"
       u"        ShootAll(\"new0_ph000\");\n"
       u"        fPitch.SetValue(_drg, -12f);\n"
       u"\n"
       u"        _sb.AppendLine();")
sub(old, new, 1, u'加第四档 new+pitch0')

anchor = u'        _sb.AppendLine("  · rest 档只有一行（它与相位无关，本来就不该随相位变）。");'
sub(anchor,
    u'        _sb.AppendLine("  · **new+pitch0 = 新锁 + headAlignPitchDeg=0 ⇒ 应为 0.00°**（完全回到 FBX 原生姿态）。");\n'
    + anchor,
    1, u'读法补一行')

io.open(P, 'w', encoding='utf-8', newline='').write(s)
print(u'\n'.join(rep))
