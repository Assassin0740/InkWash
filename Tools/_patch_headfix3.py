# -*- coding: utf-8 -*-
"""修 drg_headfix（三）：rest 档出图仍是旧姿态。

真因：`SkinnedMeshRenderer` 的蒙皮在 **PostLateUpdate** 阶段统一算，手动 `Camera.Render()`
读到的是**上一帧**算好的蒙皮网格。同帧改完骨骼立刻渲染 ⇒ 网格还是旧的。
（这条也解释了为什么之前的探针从没暴露它：一帧滞后在缓慢运动上肉眼看不出来。）

数值判据不受影响（那些是读 Transform，不是读蒙皮顶点）。
修法：进入 rest 档后**让组件停掉、姿态连按两帧**，再过一整帧才渲染。
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
    rep.append(u'[OK] %-24s %d' % (tag, cnt))


# 1) rest 档：停组件 → 按两帧 → 再渲染
sub(
    "        Snapshot();\n"
    "        // \u2605 \u51fa rest \u56fe\u524d**\u505c\u6389 EnemyDragon**\uff1a\u9a71\u52a8\u82e5\u5728\u6e32\u67d3\u524d\u628a\u9aa8\u9abc\u5199\u56de\u53bb\uff0c\u57fa\u51c6\u5e27\u5c31\u662f\u5047\u7684\u3002\n"
    "        _drgC.enabled = false;\n"
    "        ApplyRest();\n"
    "        Quaternion carrierRest = Quaternion.Inverse(_modelRoot.rotation) * carrier.rotation;\n"
    "        _carrierRestRel = carrierRest;\n"
    "        _restFwd = carrierRest * Vector3.forward;\n"
    "        Row(\"rest(FBX)\", -1f, carrierRest, true);\n"
    "        ShootAll(\"rest\");\n"
    "        _drgC.enabled = true;\n"
    "        RestoreSnapshot();\n",

    "        Snapshot();\n"
    "        // \u2605 \u51fa rest \u56fe\u524d**\u505c\u6389 EnemyDragon**\uff1a\u9a71\u52a8\u82e5\u5728\u6e32\u67d3\u524d\u628a\u9aa8\u9abc\u5199\u56de\u53bb\uff0c\u57fa\u51c6\u5e27\u5c31\u662f\u5047\u7684\u3002\n"
    "        _drgC.enabled = false;\n"
    "        // \u2605\u2605 \u5173\u952e\uff1a`SkinnedMeshRenderer` \u8499\u76ae\u5728 **PostLateUpdate** \u7edf\u4e00\u7b97\uff0c\n"
    "        //   \u624b\u52a8 `Camera.Render()` \u8bfb\u5230\u7684\u662f**\u4e0a\u4e00\u5e27**\u7b97\u597d\u7684\u8499\u76ae\u7f51\u683c\u3002\n"
    "        //   \u540c\u5e27\u6539\u5b8c\u9aa8\u9abc\u7acb\u523b\u6e32\u67d3 \u21d2 \u7f51\u683c\u8fd8\u662f\u65e7\u7684\uff08\u7b2c\u4e00\u7248\u5c31\u662f\u8fd9\u4e48\u62a5\u5e9f\u7684\uff09\u3002\n"
    "        //   \u6240\u4ee5\u5fc5\u987b\uff1a\u6309\u4e00\u5e27 \u2192 \u518d\u6309\u4e00\u5e27 \u2192 \u7b2c\u4e09\u5e27\u624d\u6e32\u67d3\u3002\n"
    "        ApplyRest();\n"
    "        yield return null;\n"
    "        ApplyRest();\n"
    "        yield return null;\n"
    "        ApplyRest();\n"
    "        Quaternion carrierRest = Quaternion.Inverse(_modelRoot.rotation) * carrier.rotation;\n"
    "        _carrierRestRel = carrierRest;\n"
    "        _restFwd = carrierRest * Vector3.forward;\n"
    "        Row(\"rest(FBX)\", -1f, carrierRest, true);\n"
    "        ShootAll(\"rest\");\n"
    "        _drgC.enabled = true;\n"
    "        RestoreSnapshot();\n",
    1, u'rest 档跨帧再渲染')

# 2) 诊断换成不依赖 BakeMesh 的量（BakeMesh 那条路返回了 0，没用）
sub(
    "                       + \" \u7f51\u683c\u8d28\u5fc3\" + MeshCen().ToString(\"F1\"));",
    "                       + \" SMR\" + (_smrs != null ? _smrs.Length : -1)\n"
    "                       + \" \u5305\u56f4\u76d2\u4e2d\u5fc3\" + (_smrs != null && _smrs.Length > 0 && _smrs[0] != null\n"
    "                            ? _smrs[0].bounds.center.ToString(\"F1\") : \"-\"));",
    1, u'诊断换成包围盒中心')

# 3) 删掉没用的 MeshCen + _bake（留着会诱人再用）
sub(
    "    /// <summary>\n"
    "    /// \u7528 `BakeMesh`\uff08CPU \u8499\u76ae\uff09\u76f4\u63a5\u7b97\u5f53\u524d\u59ff\u6001\u7684\u9876\u70b9\u8d28\u5fc3\uff08\u4e16\u754c\u7a7a\u95f4\uff09\u3002\n"
    "    /// \u2605 \u5b83\u4e0d\u9760\u6e32\u67d3\u5668\u5305\u56f4\u76d2\uff0c\u4e5f\u4e0d\u9760\u89c6\u9525\uff0c\u662f\u300c\u7f51\u683c\u5230\u5e95\u5728\u54ea\u300d\u6700\u786c\u7684\u81ea\u68c0\u3002\n"
    "    /// </summary>\n"
    "    Vector3 MeshCen()\n"
    "    {\n"
    "        if (_smrs == null || _smrs.Length == 0 || _smrs[0] == null || _smrs[0].sharedMesh == null) return Vector3.zero;\n"
    "        try { _smrs[0].BakeMesh(_bake); } catch { return Vector3.zero; }\n"
    "        var vs = _bake.vertices;\n"
    "        if (vs == null || vs.Length == 0) return Vector3.zero;\n"
    "        Vector3 c = Vector3.zero;\n"
    "        int stride = Mathf.Max(1, vs.Length / 3000);\n"
    "        int n = 0;\n"
    "        var m = _smrs[0].transform.localToWorldMatrix;\n"
    "        for (int i = 0; i < vs.Length; i += stride) { c += m.MultiplyPoint3x4(vs[i]); n++; }\n"
    "        return n > 0 ? c / n : Vector3.zero;\n"
    "    }\n"
    "\n",
    "",
    1, u'删 MeshCen')

sub(
    "    SkinnedMeshRenderer[] _smrs;\n    readonly Mesh _bake = new Mesh();\n",
    "    SkinnedMeshRenderer[] _smrs;\n",
    1, u'删 _bake')

io.open(P, 'w', encoding='utf-8', newline='').write(s)
print(u'\n'.join(rep))
