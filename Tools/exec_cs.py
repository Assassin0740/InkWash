# -*- coding: utf-8 -*-
"""
把一段 C# 代码文件送进正在运行的 Unity 编辑器执行，并打印返回结果。

用途：自动化验证 / 场景操作 / 读取编辑器内部状态。
比直接 `unity_bridge.py execute_csharp "<一大堆代码>"` 好用的地方：
C# 写在独立文件里，不受 shell 转义折磨，可以多行、可以带引号。

用法：
    python exec_cs.py <csharp文件路径>
    python exec_cs.py cs/check_texture_binding.cs
"""
import json
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from unity_bridge import send_tcp_command  # noqa: E402


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    path = sys.argv[1]
    if not os.path.isabs(path):
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)), path)
    if not os.path.exists(path):
        print(f"找不到 C# 文件: {path}")
        return 1

    with open(path, encoding="utf-8") as fh:
        code = fh.read()

    res = send_tcp_command({"type": "execute_csharp_script", "params": {"script": code}})
    print(json.dumps(res, ensure_ascii=False, indent=2))

    inner = (res or {}).get("data", {}) or {}
    payload = inner.get("data", inner) or {}
    print("\n--- result ---")
    print(payload.get("result", "(无返回值)"))
    for line in payload.get("logs", []) or []:
        print("   log:", line)
    return 0


if __name__ == "__main__":
    sys.exit(main())
