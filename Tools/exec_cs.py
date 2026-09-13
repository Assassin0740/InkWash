# -*- coding: utf-8 -*-
"""
把一段 C# 代码文件送进正在运行的 Unity 编辑器执行，并打印返回结果。

比直接 `unity_bridge.py execute_csharp "<一大堆代码>"` 好用的地方：
C# 写在独立文件里，不受 shell 转义折磨，可以多行、可以带引号。

用法：
    python exec_cs.py <csharp文件路径>                       # 编辑器模式（默认）
    python exec_cs.py <文件> --runtime                       # 运行时（Play 模式）
    python exec_cs.py <文件> --runtime --record Tools/screenshots --seconds 15 --fps 20
    python exec_cs.py <文件> --timeout 120

说明：
  * 脚本内容按「顶层语句」编译，用 return 交出结果，例如：
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("hi");
        return sb.ToString();
  * 若脚本 return 一个 IEnumerator（协程），桥会跨帧驱动它直到结束。
  * --record 只在 --runtime 下有效；桥只能录 MP4（PNG 截图接口在本版本被禁用），
    且输出目录必须位于当前 Unity 工程内。相对路径按**工程根目录**解析
    （不是按 Tools/），所以约定的证据目录写成 Tools/screenshots。
"""
import argparse
import json
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from unity_bridge import send_tcp_command  # noqa: E402


def main():
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument("path", help="C# 文件路径")
    ap.add_argument("--runtime", action="store_true",
                    help="用 exec_runtime_script（Play 模式专用；--record 需要它）")
    ap.add_argument("--record", metavar="DIR", default=None,
                    help="录制 Game View 为 MP4 到该目录（须在工程内）")
    ap.add_argument("--seconds", type=float, default=10.0, help="录制时长（1-15，默认 10）")
    ap.add_argument("--fps", type=int, default=15, help="录制帧率（5-30，默认 15）")
    ap.add_argument("--scale", type=float, default=0.5, help="录制缩放（0.25-1，默认 0.5）")
    ap.add_argument("--timeout", type=float, default=15.0, help="TCP 等待秒数（默认 15）")
    ap.add_argument("--raw", action="store_true", help="只打印原始响应，不格式化")
    args = ap.parse_args()

    path = args.path
    if not os.path.isabs(path):
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)), path)
    if not os.path.exists(path):
        print(f"找不到 C# 文件: {path}")
        return 1

    with open(path, encoding="utf-8") as fh:
        code = fh.read()

    cmd_type = "exec_runtime_script" if args.runtime else "execute_csharp_script"
    params = {"script": code}
    if args.record:
        # 坑（已修）：--record 的相对路径会被桥按「Unity 工程根目录」解析，
        # 而不是按本脚本所在目录，于是从 Tools/ 下调用会让视频落到工程根，
        # 与约定位置 Tools/screenshots/ 不一致。这里统一按工程根解析成绝对路径。
        script_dir = os.path.dirname(os.path.abspath(__file__))   # <root>/Tools
        project_root = os.path.dirname(script_dir)                 # <root>
        rec_dir = args.record
        if not os.path.isabs(rec_dir):
            rec_dir = os.path.join(project_root, rec_dir)
        rec_dir = os.path.normpath(rec_dir)
        if not rec_dir.lower().startswith((project_root + os.sep).lower()):
            print(f"录制目录必须在 Unity 工程内: {rec_dir}")
            return 1
        os.makedirs(rec_dir, exist_ok=True)
        params["record_game_view"] = {
            "path": rec_dir,
            "duration_seconds": args.seconds,
            "fps": args.fps,
            "scale": args.scale,
        }

    timeout = max(args.timeout, args.seconds + 20 if args.record else args.timeout)
    res = send_tcp_command({"type": cmd_type, "params": params}, timeout=timeout)

    if args.raw:
        print(json.dumps(res, ensure_ascii=False, indent=2))
        return 0

    print(json.dumps(res, ensure_ascii=False, indent=2))

    inner = (res or {}).get("data", {}) or {}
    payload = inner.get("data", inner) or {}
    print("\n--- result ---")
    print(payload.get("result", "(无返回值)"))
    for line in payload.get("logs", []) or []:
        print("   log:", line)
    rec = payload.get("recording") or payload.get("recordings")
    if rec:
        print("--- recording ---")
        print(json.dumps(rec, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
