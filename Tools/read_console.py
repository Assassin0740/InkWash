# -*- coding: utf-8 -*-
"""读 Unity Console，按「报错优先」的方式打印。

桥返回的 JSON 嵌套层数不稳定（有时 data.data.logs，有时直接是数组），
这里递归地把所有字符串日志项都挖出来，避免每次手写路径都写错。

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


if __name__ == "__main__":
    main()
