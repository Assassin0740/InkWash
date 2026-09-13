# -*- coding: utf-8 -*-
"""读 Unity Console，按「报错优先」的方式打印。

⚠ 重要：**Console 干净 ≠ 编译成功**。
域重载（脚本重新编译）会把 Console 整个清空，编译错误也随之被冲掉 ——
本机踩过一次：.cs 里有编译错误，编译失败，但本脚本报"控制台干净"，
验收跑的还是上一次编译出来的程序集，报告里新加的字段一个都没出现。
所以本脚本在读 Console 之后**额外查一次真正的编译状态**
（EditorUtility.scriptCompilationFailed），失败会显著报出来。

用法:
    python read_console.py [条数] [--all]
        --all 时不折叠，把 warning 也全打；默认只详列 error/exception
"""
import json
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from unity_bridge import send_tcp_command  # noqa: E402

# 查询真实编译状态。不要用 Console 判断编译成功与否（见文件头）。
_COMPILE_PROBE = (
    'var sb = new System.Text.StringBuilder();'
    'sb.AppendLine("isCompiling=" + UnityEditor.EditorApplication.isCompiling);'
    'sb.AppendLine("compilationFailed=" + UnityEditor.EditorUtility.scriptCompilationFailed);'
    'return sb.ToString();'
)


def query_compile_status():
    """返回 (是否编译失败, 原始文本)。桥不通时返回 (None, 原因)。"""
    try:
        res = send_tcp_command({"type": "execute_csharp_script",
                                "params": {"script": _COMPILE_PROBE}}, timeout=20)
    except Exception as e:  # noqa: BLE001
        return None, "查询失败: %s" % e
    texts = []
    dig_logs(res, texts)
    blob = " ".join(texts)
    if "compilationFailed=True" in blob:
        return True, blob
    if "compilationFailed=False" in blob:
        return False, blob
    return None, blob or "(无返回)"


def dig_logs(node, out):
    """递归收集日志项。

    桥的返回结构在不同版本/命令下并不统一，已见过两种：
      a) data.data 是 list[dict]，每项形如 {"type","message","file","line"}
      b) data.data 是 dict，日志藏在 logs/entries 键下
    这里统一处理：凡是带 message 字段的字典就当一条日志收下。
    """
    if isinstance(node, str):
        out.append(node)
        return
    if isinstance(node, list):
        for x in node:
            dig_logs(x, out)
        return
    if isinstance(node, dict):
        if "message" in node and isinstance(node["message"], str):
            t = node.get("type") or node.get("level") or ""
            prefix = ""
            if t and t.lower() in ("error", "exception", "assert"):
                prefix = "[Error] "
            elif t and t.lower() == "warning":
                prefix = "[Warning] "
            loc = ""
            if node.get("file"):
                loc = "  @ %s:%s" % (node.get("file"), node.get("line"))
            out.append(prefix + node["message"] + loc)
            return
        for k, v in node.items():
            if isinstance(v, (dict, list)):
                dig_logs(v, out)


def main():
    count = 30
    show_all = "--all" in sys.argv
    for a in sys.argv[1:]:
        if a.isdigit():
            count = int(a)

    res = send_tcp_command({"type": "read_console", "params": {"count": count}})
    logs = []
    dig_logs(res, logs)

    # 去掉重复（请求回显和 data 里可能有同一份）
    seen, uniq = set(), []
    for l in logs:
        if l not in seen:
            seen.add(l)
            uniq.append(l)

    errs = [l for l in uniq if ("[Error]" in l or "Exception" in l or "error CS" in l)]
    warns = [l for l in uniq if ("[Warning]" in l or "warning CS" in l) and l not in errs]
    info = [l for l in uniq if l not in errs and l not in warns]

    print("=== 共 %d 条 | error %d | warning %d | 其他 %d ===" % (len(uniq), len(errs), len(warns), len(info)))
    if errs:
        print("\n--- ERROR ---")
        for l in errs:
            print(l)
    if warns:
        print("\n--- WARNING ---")
        for l in warns[: 40 if show_all else 12]:
            print(l)
        if not show_all and len(warns) > 12:
            print("... 其余 %d 条 warning 省略（加 --all 看全）" % (len(warns) - 12))
    if not errs and not warns:
        print("\n(控制台干净：无 error / 无 warning)")
    elif not errs:
        print("\n(无 error)")
    if info and show_all:
        print("\n--- 其他 ---")
        for l in info[-20:]:
            print(l)

    # Console 干净 ≠ 编译成功（域重载会清空 Console）。这里独立查一次真实状态。
    failed, detail = query_compile_status()
    print()
    if failed is True:
        print("!!! 脚本编译失败（scriptCompilationFailed=True）——"
              " 当前跑的是上一次成功编译的程序集，改动不会生效 !!!")
        print("    去 Editor.log 里找 'error CS'，或直接看 Unity 的 Console 面板。")
    elif failed is False:
        print("编译状态：正常（scriptCompilationFailed=False）")
    else:
        print("编译状态：查不到（%s）" % detail)


if __name__ == "__main__":
    main()
