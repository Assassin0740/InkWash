# -*- coding: utf-8 -*-
"""修 drg_headfix（二）：rest 档网格没跟着骨骼变形。

两个候选解释，一次全部堵死：
  ① 剔除：`SkinnedMeshRenderer.updateWhenOffscreen = false` 时用上一次蒙皮算的包围盒，
     姿态把网格搬走 6.7 m 后视锥测试直接剔掉 ⇒ 置 true。
  ② 被覆盖：驱动可能在渲染前把骨骼写回去 ⇒ rest 出图前**停掉 EnemyDragon 组件**，
     出完再打开。
另加 `BakeMesh` 顶点质心自检（CPU 蒙皮，直接证明"网格到底在哪"）。
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
    rep.append(u'[OK] %-22s %d' % (tag, cnt))


# 1) 取 SMR + 关掉"离屏不更新包围盒"
sub(
    "        _sb.AppendLine(\"\u955c\u50cf\u8868\uff1a\u8fd0\u884c\u65f6\u9aa8\u9abc\u8282\u70b9 \"",
    "        // \u2605 \u5173\u6389 updateWhenOffscreen=false \u7684\u5751\uff1a\u5b83\u4f1a\u7528\u4e0a\u4e00\u6b21\u8499\u76ae\u7b97\u51fa\u7684\u5305\u56f4\u76d2\u505a\u89c6\u9525\u5254\u9664\uff0c\n"
    "        //   \u59ff\u6001\u628a\u7f51\u683c\u642c\u8d70\u4e4b\u540e\u5c31\u88ab\u6574\u4e2a\u5254\u6389\uff08\u51fa\u56fe\u5168\u7a7a\uff09\u3002\n"
    "        _smrs = _modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);\n"
    "        for (int i = 0; i < _smrs.Length; i++) if (_smrs[i] != null) _smrs[i].updateWhenOffscreen = true;\n"
    "\n"
    "        _sb.AppendLine(\"\u955c\u50cf\u8868\uff1a\u8fd0\u884c\u65f6\u9aa8\u9abc\u8282\u70b9 \"",
    1, u'updateWhenOffscreen')

# 2) _smrs 字段
sub(
    "    Camera _cam;\n    int _shots;",
    "    Camera _cam;\n    int _shots;\n    SkinnedMeshRenderer[] _smrs;\n    readonly Mesh _bake = new Mesh();",
    1, u'_smrs 字段')

# 3) MeshCen 自检
sub(
    "    // \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 \u51fa\u56fe \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500",
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
    "\n"
    "    // \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 \u51fa\u56fe \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500",
    1, u'MeshCen 自检')

# 4) Row 里补网格质心
sub(
    "                       + \"\u5934 pos\" + carrier.position.ToString(\"F1\"));",
    "                       + \"\u5934pos\" + carrier.position.ToString(\"F1\")\n"
    "                       + \" \u7f51\u683c\u8d28\u5fc3\" + MeshCen().ToString(\"F1\"));",
    1, u'Row 补网格质心')

# 5) rest 出图时停掉组件
sub(
    "        Snapshot();\n"
    "        ApplyRest();\n",
    "        Snapshot();\n"
    "        // \u2605 \u51fa rest \u56fe\u524d**\u505c\u6389 EnemyDragon**\uff1a\u9a71\u52a8\u82e5\u5728\u6e32\u67d3\u524d\u628a\u9aa8\u9abc\u5199\u56de\u53bb\uff0c\u57fa\u51c6\u5e27\u5c31\u662f\u5047\u7684\u3002\n"
    "        _drgC.enabled = false;\n"
    "        ApplyRest();\n",
    1, u'rest 前停组件')

sub(
    "        ShootAll(\"rest\");\n"
    "        RestoreSnapshot();\n",
    "        ShootAll(\"rest\");\n"
    "        _drgC.enabled = true;\n"
    "        RestoreSnapshot();\n",
    1, u'rest 后恢复组件')

# 6) rest 行也报网格质心（Row 已统一带出，这里只要保证顺序：先 ApplyRest 再 Row 再出图）
io.open(P, 'w', encoding='utf-8', newline='').write(s)
print(u'\n'.join(rep))
