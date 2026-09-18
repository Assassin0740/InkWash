# -*- coding: utf-8 -*-
"""修 drg_headfix：rest 档空帧 —— 强行回基准时误改了容器节点的 localPosition。

改成：只回写 **drgon_* 骨骼的局部旋转**（容器节点一个都不碰）；
并把「同名歧义」显式检出来报告；补诊断列与远景图，防止取景问题掩盖结论。
"""
import io

P = 'Tools/cs/drg_headfix.cs'
s = io.open(P, encoding='utf-8', newline='').read()
rep = []


def sub(old, new, cnt=1, tag=''):
    global s
    got = s.count(old)
    assert got == cnt, u'[%s] 期望 %d，实际 %d：\n%s' % (tag, cnt, got, old[:160])
    s = s.replace(old, new, cnt)
    rep.append(u'[OK] %-20s %d' % (tag, cnt))


# 1) 镜像表只收 drgon_* 骨骼，并检出同名歧义（原先按全名匹配，容器同名会把模型推走）
sub(
    "        var all = _modelRoot.GetComponentsInChildren<Transform>(true);\n"
    "        var map = new Dictionary<string, Transform>();\n"
    "        foreach (var t in _clone.GetComponentsInChildren<Transform>(true))\n"
    "            if (!map.ContainsKey(t.name)) map[t.name] = t;\n"
    "\n"
    "        _mirror = new Transform[all.Length];\n"
    "        for (int i = 0; i < all.Length; i++)\n"
    "        {\n"
    "            Transform r;\n"
    "            if (all[i] != _modelRoot && map.TryGetValue(all[i].name, out r)) _mirror[i] = r;\n"
    "            if (_mirror[i] != null) _mirrorN++;\n"
    "        }\n"
    "        _sb.AppendLine(\"\u955c\u50cf\u8868\uff1a\u8fd0\u884c\u65f6\u6a21\u578b\u5b50\u6811 \" + all.Length + \" \u8282\u70b9 \u2192 \u5339\u914d\u4e0a \" + _mirrorN\n"
    "                       + \"\uff08\u542b EnemyDragon = \" + (_clone.GetComponentInChildren(_dt, true) != null ? \"\u6709\" : \"\u65e0\") + \"\uff09\");\n",

    "        // \u2605 \u53ea\u5bf9 **drgon_* \u9aa8\u9abc** \u505a\u57fa\u51c6\u59ff\u6001\u56de\u5199\uff1a\n"
    "        //   \u5bb9\u5668\u8282\u70b9\uff08Visual / Model / Object_xxx\uff09\u4e00\u4e2a\u90fd\u4e0d\u80fd\u78b0 \u2014\u2014\n"
    "        //   \u7b2c\u4e00\u7248\u8fde localPosition \u4e00\u8d77\u56de\u5199\u4e86\uff0c\u7ed3\u679c\u6a21\u578b\u88ab\u63a8\u51fa\u753b\u9762\uff08rest \u51fa\u56fe\u5168\u7a7a\uff09\u3002\n"
    "        //   \u540c\u540d\u6b67\u4e49\u4e5f\u4f1a\u5bfc\u81f4\u540c\u4e00\u8282\u70b9\u88ab\u5199\u4e24\u6b21\uff0c\u5fc5\u987b\u663e\u5f0f\u68c0\u51fa\u3002\n"
    "        var all = _modelRoot.GetComponentsInChildren<Transform>(true);\n"
    "        var map = new Dictionary<string, Transform>();\n"
    "        var dup = new HashSet<string>();\n"
    "        foreach (var t in _clone.GetComponentsInChildren<Transform>(true))\n"
    "        {\n"
    "            if (!t.name.StartsWith(\"drgon_\")) continue;\n"
    "            if (map.ContainsKey(t.name)) dup.Add(t.name); else map[t.name] = t;\n"
    "        }\n"
    "\n"
    "        _mirror = new Transform[all.Length];\n"
    "        int noName = 0, ambiguous = 0;\n"
    "        for (int i = 0; i < all.Length; i++)\n"
    "        {\n"
    "            if (!all[i].name.StartsWith(\"drgon_\")) continue;\n"
    "            Transform r;\n"
    "            if (dup.Contains(all[i].name)) { ambiguous++; continue; }\n"
    "            if (map.TryGetValue(all[i].name, out r)) _mirror[i] = r; else noName++;\n"
    "            if (_mirror[i] != null) _mirrorN++;\n"
    "        }\n"
    "        _sb.AppendLine(\"\u955c\u50cf\u8868\uff1a\u8fd0\u884c\u65f6\u9aa8\u9abc\u8282\u70b9 \" + all.Length + \" \u4e2a\uff0c\u5176\u4e2d drgon_* \u547d\u4e2d \" + _mirrorN\n"
    "                       + \" \uff08\u7f3a\u5bf9\u7167 \" + noName + \" / \u540c\u540d\u6b67\u4e49 \" + ambiguous + \"\uff09\"\n"
    "                       + \"\uff08\u57fa\u51c6\u5b9e\u4f8b\u5185\u542b EnemyDragon = \" + (_clone.GetComponentInChildren(_dt, true) != null ? \"\u6709\" : \"\u65e0\") + \"\uff09\");\n",
    1, u'镜像表收紧到骨骼')

# 2) ApplyRest：只写局部旋转，不写局部位置
sub(
    "            if (_mirror[i] == null) continue;\n"
    "            all[i].localRotation = _mirror[i].localRotation;\n"
    "            all[i].localPosition = _mirror[i].localPosition;\n",
    "            if (_mirror[i] == null) continue;\n"
    "            all[i].localRotation = _mirror[i].localRotation;\n"
    "            // \u2605 \u7edd\u4e0d\u80fd\u5199 localPosition\uff1a\u5bb9\u5668\u8282\u70b9\u7684 local \u4f4d\u7f6e\u4e00\u65e6\u88ab\u4ece\u53e6\u4e00\u4e2a\u5b9e\u4f8b\u8986\u76d6\uff0c\n"
    "            //   \u6a21\u578b\u4f1a\u88ab\u6574\u5757\u63a8\u51fa\u753b\u9762\uff08\u7b2c\u4e00\u7248\u5c31\u662f\u8fd9\u4e48\u6389\u8fdb\u53bb\u7684\uff09\u3002\u9aa8\u9abc\u59ff\u6001\u53ea\u53d6\u51b3\u4e8e\u65cb\u8f6c\u3002\n",
    1, u'ApplyRest 只写旋转')

# 3) Snapshot 也不再存位置（省事，且避免"存了却不用"的歧义）
sub(
    "            _saveRot[all[i]] = all[i].localRotation;\n"
    "            _savePos[all[i]] = all[i].localPosition;\n",
    "            _saveRot[all[i]] = all[i].localRotation;\n",
    1, u'Snapshot 只存旋转')

sub(
    "    Dictionary<Transform, Vector3> _savePos = new Dictionary<Transform, Vector3>();\n",
    "",
    1, u'删 _savePos 字段')

sub(
    "        foreach (var kv in _saveRot) if (kv.Key != null) kv.Key.localRotation = kv.Value;\n"
    "        foreach (var kv in _savePos) if (kv.Key != null) kv.Key.localPosition = kv.Value;\n",
    "        foreach (var kv in _saveRot) if (kv.Key != null) kv.Key.localRotation = kv.Value;\n",
    1, u'RestoreSnapshot 只恢复旋转')

# 4) 每行补位置诊断 + 每个状态补远景图
sub(
    "                       + (isRest ? \"\uff08\u57fa\u51c6\u5e27\uff09\" : leafMax.ToString(\"F4\") + \"\u00b0\"));",
    "                       + Pad(isRest ? \"\uff08\u57fa\u51c6\u5e27\uff09\" : leafMax.ToString(\"F4\") + \"\u00b0\", 16)\n"
    "                       + \"\u5934 pos\" + carrier.position.ToString(\"F1\"));",
    1, u'Row 补位置诊断')

sub(
    "        Shot(\"head_\" + tag + \"_front\", focus, _dist, 0.99f, 0.12f, 0.06f);\n",
    "        Shot(\"head_\" + tag + \"_front\", focus, _dist, 0.99f, 0.12f, 0.06f);\n"
    "        // \u2605 \u8865\u4e00\u5f20\u8fdc\u666f\uff1a\u5373\u4f7f\u8fd1\u666f\u53d6\u666f\u51fa\u9519\uff0c\u773c\u775b\u4e5f\u80fd\u5728\u8fdc\u666f\u91cc\u5224\u59ff\u6001\n"
    "        Shot(\"wide_\" + tag + \"_side\", _modelRoot.TransformPoint(new Vector3(0f, 0f, 4f)), 13f, 0.05f, 0.99f, 0.06f);\n",
    1, u'补远景图')

io.open(P, 'w', encoding='utf-8', newline='').write(s)
print(u'\n'.join(rep))
