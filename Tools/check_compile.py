#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
check_compile.py —— 从 Unity 的 Editor.log 判「C# / Shader 到底编译过没有」。

★★ 为什么必须要有这个脚本（第二十三轮实测踩到的静默失效）：
   `Tools/read_console.py` 报「控制台干净：无 error / 无 warning」**≠ 编译通过**。
   实测：`DragonStormVfx.cs` 里写了团结版不存在的 `cycleMode`，脚本编译**失败**（CS1061/CS0103），
   但 `read_console.py` 依然报干净。原因是 Unity 的脚本编译错误走的是
   `ScriptCompilationBuildProgram` 那条路，**不进 Console 的日志条目**。
   ⇒ 凡「改完 .cs 后判编译」，判据只认 Editor.log 里的 `error CS` / `Shader error`。
   （工程坑表里的「编译三层判据」由此再加一条：Console 是一层，但不是唯一一层。）

用法：
    python Tools/check_compile.py                # 看最近一次编译窗口
    python Tools/check_compile.py --tail 600000  # 只看日志末尾 N 字符（默认 400000）
    python Tools/check_compile.py --all          # 不裁窗口，全文扫（慢）

退出码：0 = 干净；1 = 有编译/Shader 错误（方便 CI 或串在一条命令里）。
"""
import argparse
import os
import re
import sys

DEFAULT_LOGS = [
    os.path.expandvars(r"%LOCALAPPDATA%\Unity\Editor\Editor.log"),
    os.path.expanduser(r"~/.local/share/unity3d/Editor.log"),
]

ERR_PAT = re.compile(r"error CS\d+|Shader error|Shader warning|## Script Compilation Error")


def find_log():
    for p in DEFAULT_LOGS:
        if p and os.path.isfile(p):
            return p
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--log", default=None)
    ap.add_argument("--tail", type=int, default=400000)
    ap.add_argument("--all", action="store_true")
    a = ap.parse_args()

    p = a.log or find_log()
    if not p or not os.path.isfile(p):
        print("× 找不到 Editor.log")
        return 2

    size = os.path.getsize(p)
    with open(p, encoding="utf-8", errors="replace") as f:
        if a.all:
            text = f.read()
            window = "全文 %d 字符" % len(text)
        else:
            f.seek(max(0, size - a.tail))
            text = f.read()
            window = "末尾 %d 字符（文件共 %d）" % (a.tail, size)

    print("=== check_compile : %s ===" % p)
    print("窗口：%s" % window)

    # 最后一次「请求编译」之后的内容才算数 —— 更早的错误可能早就修好了
    marks = [m.start() for m in re.finditer(r"\[ScriptCompilation\] Requested script compilation", text)]
    seg = text[marks[-1]:] if marks else text
    print("最后一次请求编译之后：%d 字符" % len(seg))

    hits = [ln.strip() for ln in seg.split("\n") if ERR_PAT.search(ln)]
    # 去重（同一条错误 Unity 会写好几遍）
    uniq = []
    for h in hits:
        if h not in uniq:
            uniq.append(h)

    if uniq:
        print("\n× 发现 %d 条唯一编译/Shader 错误：" % len(uniq))
        for h in uniq[:40]:
            print("   " + h[:220])
        print("\n结论：编译**未通过** —— 运行时读的还是旧程序集，量出来的数字全是旧的。")
        return 1

    print("\n√ 最后一次编译请求之后没有 error CS / Shader error")
    print("结论：编译干净（注意这只证明没有错误的行，不证明逻辑对）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
