# -*- coding: utf-8 -*-
"""给 drg_headfix 加第三条自检：**头簇相对挂点的几何** vs 纯净基准实例。

用户的原话判据是「跟脖子的相对位置是对的，方向也是」——
那就直接量这个：对每枚抽样的头簇叶子骨，
    活体： Inv(carrier.rotation) * (leaf.position - carrier.position)
    基准： Inv(cloneCarrier.rotation) * (cloneLeaf.position - cloneCarrier.position)
两者之差 = 头簇相对挂点的位置偏差（m）；再比 Inv(carrier)*leaf.rotation 的角度差（°）。
这一步**不依赖渲染、不依赖相机、不依赖背景**，是"头到底像不像 FBX"最硬的判据。
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
    rep.append(u'[OK] %-26s %d' % (tag, cnt))


# 1) 字段
sub(u"    Transform[] _headLeaves = new Transform[8];\n",
    u"    Transform[] _headLeaves = new Transform[8];\n"
    u"    Transform _cloneCarrier;\n"
    u"    Transform[] _headLeafClone = new Transform[8];\n",
    1, u'头簇基准对照字段')

# 2) 建 name->clone 索引并存下来
sub(u"        var map = new Dictionary<string, Transform>();\n",
    u"        var map = new Dictionary<string, Transform>();\n",
    1, u'锚点存在（name map）')

sub(u"        _sb.AppendLine(\"头簇叶子骨抽样 \" + got + \" 枚：\" + NamesOf(_headLeaves, got));",
    u"        _cloneCarrier = map.ContainsKey(carrier.name) ? map[carrier.name] : null;\n"
    u"        int leafMapped = 0;\n"
    u"        for (int i = 0; i < _headLeaves.Length; i++)\n"
    u"        {\n"
    u"            if (_headLeaves[i] == null) continue;\n"
    u"            Transform cm;\n"
    u"            if (map.TryGetValue(_headLeaves[i].name, out cm)) { _headLeafClone[i] = cm; leafMapped++; }\n"
    u"        }\n"
    u"        _sb.AppendLine(\"头簇叶子骨抽样 \" + got + \" 枚（基准实例里找到同名 \" + leafMapped + \" 枚）：\" + NamesOf(_headLeaves, got));",
    1, u'头簇叶子骨基准镜像')

# 3) 自检函数
sub(u"    // ───────────────────────── 出图 ─────────────────────────",
    u"    /// <summary>\n"
    u"    /// 头簇**相对挂点**的几何 vs 纯净基准实例：返回最大位置偏差(m) 与最大转角(°)。\n"
    u"    /// ★ 这直接对应判据来源「跟脖子的相对位置是对的，方向也是」；\n"
    u"    ///   与渲染/相机/背景全都无关。\n"
    u"    /// </summary>\n"
    u"    void HeadShapeVsRest(out float maxPos, out float maxRot, out int n)\n"
    u"    {\n"
    u"        maxPos = 0f; maxRot = 0f; n = 0;\n"
    u"        var carrier = _spine[_NS - 2] as Transform;\n"
    u"        if (carrier == null || _cloneCarrier == null) return;\n"
    u"        Quaternion ic = Quaternion.Inverse(carrier.rotation);\n"
    u"        Quaternion ir = Quaternion.Inverse(_cloneCarrier.rotation);\n"
    u"        for (int i = 0; i < _headLeaves.Length; i++)\n"
    u"        {\n"
    u"            var a = _headLeaves[i]; var b = _headLeafClone[i];\n"
    u"            if (a == null || b == null) continue;\n"
    u"            Vector3 pa = ic * (a.position - carrier.position);\n"
    u"            Vector3 pb = ir * (b.position - _cloneCarrier.position);\n"
    u"            float dp = (pa - pb).magnitude;\n"
    u"            float dr = Quaternion.Angle(ic * a.rotation, ir * b.rotation);\n"
    u"            if (dp > maxPos) maxPos = dp;\n"
    u"            if (dr > maxRot) maxRot = dr;\n"
    u"            n++;\n"
    u"        }\n"
    u"    }\n"
    u"\n"
    u"    // ───────────────────────── 出图 ─────────────────────────",
    1, u'HeadShapeVsRest')

# 4) Row 里带出
sub(u"                       + \" SMR\" + (_smrs != null ? _smrs.Length : -1)",
    u"                       + \" \u5934\u7c07\u76f8\u5bf9\u6302\u70b9 vs \u57fa\u51c6 \u0394pos\" + hp.ToString(\"F4\") + \"m \u0394rot\" + hr.ToString(\"F3\") + \"\u00b0(\" + hn + \"\u53f6)\"\n"
    u"                       + \" SMR\" + (_smrs != null ? _smrs.Length : -1)",
    1, u'Row 带出头簇自检')

sub(u"        float leafMax = 0f;\n",
    u"        float hp, hr; int hn;\n"
    u"        HeadShapeVsRest(out hp, out hr, out hn);\n"
    u"        float leafMax = 0f;\n",
    1, u'Row 调用自检')

# 5) 读法
sub(u"        _sb.AppendLine(\"  \u00b7 **new+pitch0 = \u65b0\u9501 + headAlignPitchDeg=0 \u21d2 \u5e94\u4e3a 0.00\u00b0**\uff08\u5b8c\u5168\u56de\u5230 FBX \u539f\u751f\u59ff\u6001\uff09\u3002\");",
    u"        _sb.AppendLine(\"  \u00b7 **new+pitch0 = \u65b0\u9501 + headAlignPitchDeg=0 \u21d2 \u5e94\u4e3a 0.00\u00b0**\uff08\u5b8c\u5168\u56de\u5230 FBX \u539f\u751f\u59ff\u6001\uff09\u3002\");\n"
    u"        _sb.AppendLine(\"  \u00b7 **\u5934\u7c07\u76f8\u5bf9\u6302\u70b9 vs \u57fa\u51c6**\uff1a\u9010\u53f6\u6bd4\u300c\u76f8\u5bf9\u6302\u70b9\u7684\u4f4d\u7f6e\u4e0e\u65b9\u5411\u300d\uff08\u5bf9\u5e94\u7528\u6237\u53e3\u8ff0\u5224\u636e\uff09\u3002\");\n"
    u"        _sb.AppendLine(\"    \u2605 \u8fd9\u4e00\u9879\u4e0e\u6e32\u67d3/\u76f8\u673a/\u80cc\u666f\u5168\u65e0\u5173 \u2014\u2014 \u5b83\u624d\u662f\u300c\u5934\u5230\u5e95\u50cf\u4e0d\u50cf FBX\u300d\u6700\u786c\u7684\u5224\u636e\u3002\");",
    1, u'读法补头簇自检')

io.open(P, 'w', encoding='utf-8', newline='').write(s)
print(u'\n'.join(rep))
