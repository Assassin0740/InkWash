# -*- coding: utf-8 -*-
"""第二十六轮补丁：头锁定改用「拉直之前」的 FBX 原生基准姿态（_restRel）。

病根（drg_headref 实测）：StraightenSpineBase() 会把 _spine[Count-2]（drgon_025 = 头簇挂点）
也转过去 —— 实测 93.37°、段方向偏 66.56°。头簇是刚性分支、从不被驱动 ⇒ 头被整块甩歪。
而 headLockToBase 锁的是拉直**之后**采的 _baseRel ⇒ 三档（不锁 / 锁pitch0 / 锁pitch−12）
全锁在同一个歪基准上 = 用户说的「ABC 全是歪的」。

多处改同一文件 ⇒ 一次性脚本 + 逐条 count 断言（项目硬规矩 ②）。
"""
import io, sys

P = 'Assets/_Project/Scripts/Enemies/EnemyDragon.cs'
s = io.open(P, encoding='utf-8', newline='').read()
orig = s
report = []


def sub(old, new, cnt=1, tag=''):
    global s
    got = s.count(old)
    assert got == cnt, u'[%s] 期望 %d 处，实际 %d 处：\n%s' % (tag, cnt, got, old[:180])
    s = s.replace(old, new, cnt)
    report.append(u'[OK] %-22s %d 处' % (tag, cnt))


# ── 1. ResolveSpine 里清表 ──
sub(
    "            _spine.Clear();\n            _baseRot.Clear();\n            _baseRel.Clear();\n",
    "            _spine.Clear();\n            _baseRot.Clear();\n            _baseRel.Clear();\n            _restRel.Clear();\n",
    1, u'ResolveSpine 清 _restRel')

# ── 2. 新表声明（放在 _baseRel 之后）──
sub(
    "        private readonly List<Quaternion> _baseRel = new List<Quaternion>();\n\n"
    "        // \u2605\u2605 \u86c7\u5f62\u6e38\u52a8\uff08\u4f4d\u7f6e\u7ea7 sin \u8d34\u5408\uff09\u7528\u7684\u4e09\u5f20\u8868",
    "        private readonly List<Quaternion> _baseRel = new List<Quaternion>();\n\n"
    "        /// <summary>\n"
    "        /// \u2605\u2605 \u7b2c\u4e8c\u5341\u516d\u8f6e\uff08\u5751\u8868 48\uff09\uff1a**\u62c9\u76f4\u4e4b\u524d**\u7684\u300cFBX \u539f\u751f\u57fa\u51c6\u59ff\u6001\u300d\uff0c\u8868\u8fbe\u4e3a\u6a21\u578b\u5bb9\u5668\u5c40\u90e8\u7cfb\u3002\n"
    "        ///\n"
    "        /// \u4e3a\u4ec0\u4e48\u5fc5\u987b\u5355\u72ec\u5b58\u4e00\u4efd\uff1a`StraightenSpineBase()` \u662f**\u5168\u5c40**\u59ff\u6001\u6539\u5199 \u2014\u2014\n"
    "        ///   \u5b83\u628a\u6574\u6761\u810a\u9aa8\uff08\u542b `_spine[Count-2]` = \u9888\u6839 = \u5934\u7c07\u6302\u70b9\uff09\u9010\u8282\u8f6c\u6210\u4e00\u6761\u76f4\u7ebf\u3002\n"
    "        ///   \u5b9e\u6d4b\uff08`drg_headref`\uff09\uff1a`_spine[22] = drgon_025` \u88ab\u8f6c\u4e86 **93.37\u00b0**\u3001\u6bb5\u65b9\u5411\u504f **66.56\u00b0**\n"
    "        ///   \uff08\u6b63\u662f\u57fa\u51c6\u59ff\u6001\u91cc\u90a3\u4e2a 65.6\u00b0 \u7684\u9888\u6298\uff09\u3002\u800c\u5934\u7c07\uff0839 \u679a\u53f6\u5b50\u9aa8 + 14 \u6761\u9b03/\u987b/\u89d2/\u988c\uff09\n"
    "        ///   \u662f**\u521a\u6027\u5206\u652f\u3001\u4ece\u4e0d\u88ab\u9a71\u52a8** \u21d2 \u5934\u7684\u4e16\u754c\u671d\u5411 **\u5b8c\u5168\u7b49\u4e8e** `_spine[Count-2].rotation`\n"
    "        ///   \u00d7 \u5e38\u6570 \u21d2 **\u62c9\u76f4\u90a3\u4e00\u6b65\u5c31\u628a\u5934\u6574\u5757\u7529\u6b6a\u4e86**\u3002\n"
    "        ///\n"
    "        /// \u4e8e\u662f\u300c\u5934\u6c38\u8fdc\u9501\u5b9a\u5728\u62c9\u76f4\u540e\u7684\u59ff\u6001\u300d= \u9501\u5b9a\u5728\u4e00\u4e2a\u6b6a\u59ff\u6001\u4e0a \u21d2\n"
    "        /// \u7528\u6237\u770b\u5230\u7684\u300c\u4e0d\u9501 / \u9501 pitch=0 / \u9501 pitch=\u221212 **\u4e09\u6863\u5168\u662f\u6b6a\u7684**\u300d\u3002\n"
    "        ///\n"
    "        /// \u21d2 \u5934\u9501\u5b9a\u8981\u7528\u7684\u662f**\u8fd9\u4e00\u4efd**\uff08FBX \u539f\u751f\uff09\uff0c\u4e0d\u662f `_baseRel`\uff08\u62c9\u76f4\u540e\uff09\u3002\n"
    "        ///   \u86c7\u5f62\u9a71\u52a8\u4ecd\u5fc5\u987b\u7528\u62c9\u76f4\u540e\u7684 `_baseRel` / `_baseSegLocal`\uff1a\u90a3\u662f\u300c\u4e00\u6761\u86c7\u300d\u7684\u524d\u63d0\u3002\n"
    "        /// \u957f\u5ea6 = `_spine.Count`\u3002\n"
    "        /// </summary>\n"
    "        private readonly List<Quaternion> _restRel = new List<Quaternion>();\n\n"
    "        // \u2605\u2605 \u86c7\u5f62\u6e38\u52a8\uff08\u4f4d\u7f6e\u7ea7 sin \u8d34\u5408\uff09\u7528\u7684\u4e09\u5f20\u8868",
    1, u'_restRel 声明')

# ── 3. 拉直之前采一份 ──
sub(
    "            if (straightenSpine) StraightenSpineBase();\n"
    "            // \u2605 \u4e0d\u5f00\u62c9\u76f4\u65f6\u4e5f\u8981\u91c7\u4e00\u6b21\uff0c\u4fdd\u8bc1 _baseRel \u4e0e _spine \u7b49\u957f\uff08\u9a71\u52a8\u5199\u4e16\u754c\u65cb\u8f6c\u8981\u9760\u5b83\uff09\u3002\n"
    "            else CaptureBaseRel();",
    "            // \u2605\u2605 \u7b2c\u4e8c\u5341\u516d\u8f6e\uff08\u5751\u8868 48\uff09\uff1a**\u5fc5\u987b\u5728\u62c9\u76f4\u4e4b\u524d**\u91c7\u300cFBX \u539f\u751f\u57fa\u51c6\u59ff\u6001\u300d\u3002\n"
    "            //   \u62c9\u76f4\u4f1a\u628a\u9888\u6839\uff08\u5934\u7c07\u6302\u70b9\uff09\u4e5f\u8f6c\u8fc7\u53bb\uff08\u5b9e\u6d4b 93.37\u00b0\uff09\u21d2 \u5934\u88ab\u6574\u5757\u7529\u6b6a\uff0c\n"
    "            //   \u800c\u5934\u9501\u5b9a\u8981\u7684\u6b63\u662f\u300c\u7528\u6237\u8ba4\u53ef\u7684\u90a3\u4e2a FBX \u59ff\u6001\u300d\u3002**\u987a\u5e8f\u4e0d\u80fd\u53cd**\u3002\n"
    "            CaptureRestRel();\n"
    "            if (straightenSpine) StraightenSpineBase();\n"
    "            // \u2605 \u4e0d\u5f00\u62c9\u76f4\u65f6\u4e5f\u8981\u91c7\u4e00\u6b21\uff0c\u4fdd\u8bc1 _baseRel \u4e0e _spine \u7b49\u957f\uff08\u9a71\u52a8\u5199\u4e16\u754c\u65cb\u8f6c\u8981\u9760\u5b83\uff09\u3002\n"
    "            else CaptureBaseRel();",
    1, u'拉直前采 _restRel')

# ── 4. CaptureRestRel 方法（放在 CaptureBaseRel 之后）──
sub(
    "            CaptureSegments();\n        }\n",
    "            CaptureSegments();\n        }\n"
    "\n"
    "        /// <summary>\n"
    "        /// \u628a**\u62c9\u76f4\u4e4b\u524d**\u7684\u810a\u9aa8\u59ff\u6001\u8bb0\u6210\u76f8\u5bf9\u6a21\u578b\u5bb9\u5668\u7684 `_restRel` \u2014\u2014 \u5373\u300cFBX \u539f\u751f\u57fa\u51c6\u59ff\u6001\u300d\u3002\n"
    "        /// \u2605 \u5fc5\u987b\u5728 `StraightenSpineBase()` **\u4e4b\u524d**\u8c03\u7528\uff1b\u53ea\u7ed9\u300c\u5934\u9501\u5b9a\u300d\u7528\uff0c\u4e0d\u53c2\u4e0e\u86c7\u5f62\u9a71\u52a8\u3002\n"
    "        /// </summary>\n"
    "        private void CaptureRestRel()\n"
    "        {\n"
    "            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;\n"
    "            _restRel.Clear();\n"
    "            for (int i = 0; i < _spine.Count; i++)\n"
    "            {\n"
    "                if (_spine[i] == null) { _restRel.Add(Quaternion.identity); continue; }\n"
    "                _restRel.Add(Quaternion.Inverse(rootRot) * _spine[i].rotation);\n"
    "            }\n"
    "        }\n",
    1, u'CaptureRestRel 方法')

# ── 5. 新开关（放在 headLockToBase 之后）──
sub(
    "        public bool headLockToBase = true;\n",
    "        public bool headLockToBase = true;\n"
    "\n"
    "        [Tooltip(\"\u2605\u2605 \u5934\u9501\u5b9a\u7528\u54ea\u4efd\u57fa\u51c6\uff08\u7b2c\u4e8c\u5341\u516d\u8f6e\uff0c\u5751\u8868 48\uff09\uff1a\\n\" +\n"
    "                 \"true\uff08\u9ed8\u8ba4\u3001\u63a8\u8350\uff09= **FBX \u539f\u751f\u57fa\u51c6\u59ff\u6001**\uff08\u62c9\u76f4\u4e4b\u524d\u91c7\u7684 `_restRel`\uff09\\n\" +\n"
    "                 \"   \u21d2 \u5934\u7684\u65b9\u5411\u4e0e\u4f4d\u7f6e\u56de\u5230\u6a21\u578b\u5e08\u5085\u6446\u7684\u90a3\u4e2a\u6837\u5b50\uff08\u7528\u6237\uff1a\u300c\u8fd9\u4e2a FBX \u7cbe\u81f4\u59ff\u6001\u91cc\u9762\u5934\u7684\u4f4d\u7f6e\u5c31\u662f\u5bf9\u7684\uff0c\" +\n"
    "                 \"\u8ddf\u8116\u5b50\u7684\u76f8\u5bf9\u4f4d\u7f6e\u662f\u5bf9\u7684\uff0c\u65b9\u5411\u4e5f\u662f\u300d\uff09\u3002\\n\" +\n"
    "                 \"false = \u65e7\u884c\u4e3a\uff1a\u9501\u62c9\u76f4**\u4e4b\u540e**\u7684 `_baseRel`\u3002\\n\" +\n"
    "                 \"\u2605 \u4e3a\u4ec0\u4e48\u65e7\u884c\u4e3a\u5fc5\u5b9a\u6b6a\uff1a`StraightenSpineBase()` \u4f1a\u628a `_spine[Count-2]`\uff08\u9888\u6839 = \u5934\u7c07\u6302\u70b9\uff09\" +\n"
    "                 \"\u4e5f\u8f6c\u8fc7\u53bb\uff0c\u5b9e\u6d4b `drgon_025` \u88ab\u8f6c **93.37\u00b0**\u3001\u6bb5\u65b9\u5411\u504f **66.56\u00b0**\uff1b\" +\n"
    "                 \"\u800c\u5934\u7c07\u662f**\u521a\u6027\u5206\u652f\u3001\u4ece\u4e0d\u88ab\u9a71\u52a8** \u21d2 \u5934\u7684\u4e16\u754c\u671d\u5411\u5b8c\u5168\u53d6\u51b3\u4e8e\u8fd9\u4e00\u8282\u3002\" +\n"
    "                 \"\u9501\u62c9\u76f4\u540e\u7684\u57fa\u51c6 = \u628a\u5934**\u6c38\u4e45\u9489\u5728\u4e00\u4e2a\u88ab\u8f6c\u6b6a\u4e86 93\u00b0 \u7684\u59ff\u6001**\u4e0a\u3002\")]\n"
    "        public bool headLockUseRestPose = true;\n",
    1, u'headLockUseRestPose 声明')

# ── 6. 两处锁定写入点统一换源（两处文本完全相同 ⇒ replace_all，断言 2）──
sub(
    "                if (lockHead && i >= headFrom)\n"
    "                {\n"
    "                    _spine[i].rotation = rootRot * headExtra * _baseRel[i];\n"
    "                    continue;\n"
    "                }\n",
    "                if (lockHead && i >= headFrom)\n"
    "                {\n"
    "                    // \u2605\u2605 \u7b2c\u4e8c\u5341\u516d\u8f6e\uff08\u5751\u8868 48\uff09\uff1a\u9ed8\u8ba4\u9501**\u62c9\u76f4\u4e4b\u524d**\u7684 FBX \u539f\u751f\u59ff\u6001\u3002\n"
    "                    //   \u9501\u62c9\u76f4\u540e\u7684 `_baseRel` \u4f1a\u628a\u5934\u6c38\u4e45\u9489\u5728\u300c\u88ab\u62c9\u76f4\u8f6c\u6b6a 93\u00b0\u300d\u7684\u59ff\u6001\u4e0a \u2014\u2014\n"
    "                    //   \u8fd9\u6b63\u662f\u7528\u6237\u8bf4\u7684\u300c\u4e09\u6863\u5168\u662f\u6b6a\u7684\u300d\u3002\u4e24\u6761\u9a71\u52a8\u8def\u5f84\u5fc5\u987b\u9009\u540c\u4e00\u4e2a\u6e90\u3002\n"
    "                    Quaternion hb = (headLockUseRestPose && _restRel.Count == _spine.Count)\n"
    "                                    ? _restRel[i] : _baseRel[i];\n"
    "                    _spine[i].rotation = rootRot * headExtra * hb;\n"
    "                    continue;\n"
    "                }\n",
    2, u'两处锁定写入换源')

io.open(P, 'w', encoding='utf-8', newline='').write(s)
print(u'\n'.join(report))
print(u'--- 文件 %d → %d 字符（+%d）---' % (len(orig), len(s), len(s) - len(orig)))
