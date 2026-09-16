#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
cs_lint.py —— 探针 .cs 的低级错误体检（运行探针前跑一次，几毫秒）

为什么需要它：探针是**中文注释 + 中文报文字符串**的混合体。最高频的低级错误是
把 ASCII 双引号写进中文字符串里，例如：

    sb.Append("就是"甲"的地板");

它把串切成 两个字符串 + 一个裸标识符，整份脚本编译失败；而报错信息（"应输入 ;"）
在几千行里根本指不到病灶。这类错误上一轮已经真实发生过一次。

★ 走错过一条路，记下来免得再犯：
  · 「引号计数奇偶」检查 —— 对**成对出现**的这种错误完全失效（4 个引号，偶数，检查通过）。
  · 「词法上串是否闭合」检查 —— 同样失效：上面的例子在词法层是合法的
    （"就是"、"的地板" 是两个合法字符串，中间夹一个标识符），只有语法分析才判得出。
  · 真正有效的判据：**闭引号后面紧跟"标识符字符"就是错的**。
    C# 里 `"..."` 之后只允许出现运算符/分隔符/空白/行尾；`"甲"` 这种紧跟字母或
    汉字的写法必然语法错误（合法的相邻串必须写成 `"a" + "b"`）。

用法:
    python Tools/cs_lint.py                # 体检 Tools/cs/*.cs
    python Tools/cs_lint.py <file.cs> ...   # 只体检指定文件
"""
import io
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_DIR = os.path.join(ROOT, "Tools", "cs")


def is_ident_char(ch):
    """标识符字符：ASCII 字母数字下划线，或非 ASCII（汉字等）。"""
    if ch.isascii():
        return ch.isalnum() or ch == "_"
    return ch.isalpha() or ch.isalnum()


def scan(text):
    errs = []
    i, n, line = 0, len(text), 1
    n_str = 0
    in_line_cmt = in_block_cmt = False
    # str_kind: None / '"' 普通串 / '@' 逐字串 / "'" 字符字面量
    str_kind = None
    str_start_line = 0
    interp = False       # 当前普通串是不是内插串 $"..."
    brace_depth = 0      # 内插串 { } 内部深度（>0 表示在表达式里，字符串规则恢复正常）

    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""

        if c == "\n":
            line += 1
            in_line_cmt = False
            i += 1
            continue
        if in_line_cmt:
            i += 1
            continue
        if in_block_cmt:
            if c == "*" and nxt == "/":
                in_block_cmt = False
                i += 2
                continue
            i += 1
            continue

        if str_kind is not None:
            if brace_depth > 0:
                # 内插串的表达式内部：与普通 C# 代码同规则
                if c == "{":
                    brace_depth += 1
                elif c == "}":
                    brace_depth -= 1
                elif c == '"':
                    # 表达式里可以再嵌字符串（如 $"{x.ToString("0.0")}"）
                    sub, i2, l2, ns2 = _skip_string(text, i, line)
                    n_str += ns2
                    if sub is not None:
                        errs.append((line, sub))
                    i = i2
                    line = l2
                    continue
                i += 1
                continue

            if str_kind == "@":
                if c == '"':
                    if nxt == '"':
                        i += 2
                        continue
                    str_kind = None
                    interp = False
                    # ★ 核心判据：闭引号后紧跟标识符字符 ⇒ 必错
                    if nxt and is_ident_char(nxt):
                        errs.append((line, "字符串闭合后紧跟标识符字符 %r（多半是中文串里混了 ASCII 双引号）" % nxt))
                    i += 1
                    continue
                i += 1
                continue

            if c == "\\":
                i += 2
                continue
            if str_kind == '"' and interp and c == "{" and nxt != "{":
                brace_depth = 1
                i += 1
                continue
            if str_kind == '"' and interp and c == "{" and nxt == "{":
                i += 2
                continue
            if str_kind == '"' and interp and c == "}" and nxt == "}":
                i += 2
                continue
            if (str_kind == '"' and c == '"') or (str_kind == "'" and c == "'"):
                str_kind = None
                interp = False
                if nxt and is_ident_char(nxt):
                    errs.append((line, "字符串闭合后紧跟标识符字符 %r（多半是中文串里混了 ASCII 双引号）" % nxt))
                i += 1
                continue
            i += 1
            continue

        # ---- 不在注释/字符串里 ----
        if c == "/" and nxt == "/":
            in_line_cmt = True
            i += 2
            continue
        if c == "/" and nxt == "*":
            in_block_cmt = True
            i += 2
            continue
        if (c == "@" or c == "$") and nxt == '"':
            str_kind = "@" if c == "@" else '"'
            interp = False
            str_start_line = line
            n_str += 1
            i += 2
            continue
        if c == "$" and nxt == "@":
            str_kind = "@"
            interp = False
            str_start_line = line
            n_str += 1
            i += 3
            continue
        if c == '"':
            str_kind = '"'
            interp = _prev_is_dollar(text, i)
            str_start_line = line
            n_str += 1
            i += 1
            continue
        if c == "'":
            str_kind = "'"
            interp = False
            str_start_line = line
            n_str += 1
            i += 1
            continue
        i += 1

    if str_kind is not None:
        kind = {'"': "普通字符串", "@": "逐字字符串", "'": "字符字面量"}.get(str_kind, "字面量")
        errs.append((str_start_line, "文件结束时仍有未闭合的%s（第 %d 行开始）" % (kind, str_start_line)))
    if in_block_cmt:
        errs.append((line, "文件结束时块注释 /* 未闭合"))
    return errs, {"strings": n_str}


def _prev_is_dollar(text, i):
    """i 指向 '"'；判断它是否属于 $ 前缀的内插串（允许 $ @ 之间无空白）。"""
    j = i - 1
    if j >= 0 and text[j] == "$":
        return True
    if j - 1 >= 0 and text[j - 1] == "$" and text[j] in "@ ":
        return True
    return False


def _skip_string(text, i, line):
    """从 i（'"'）跳过一个字符串，返回 (错误或None, 新i, 新行号, 串数)。"""
    n = len(text)
    i += 1
    while i < n:
        if text[i] == "\n":
            line += 1
            i += 1
            continue
        if text[i] == "\\":
            i += 2
            continue
        if text[i] == '"':
            i += 1
            break
        i += 1
    return None, i, line, 1


def main():
    args = sys.argv[1:]
    if args:
        files = [a if os.path.isabs(a) else os.path.join(ROOT, a) for a in args]
    else:
        if not os.path.isdir(DEFAULT_DIR):
            print("找不到目录 %s" % DEFAULT_DIR)
            return 1
        files = [os.path.join(DEFAULT_DIR, f) for f in sorted(os.listdir(DEFAULT_DIR))
                 if f.endswith(".cs")]

    bad = 0
    for p in files:
        if not os.path.isfile(p):
            print("[跳过] 不存在: %s" % p)
            continue
        text = io.open(p, encoding="utf-8").read()
        errs, st = scan(text)
        name = os.path.relpath(p, ROOT)
        if errs:
            bad += 1
            print("[不合格] %s" % name)
            lines = text.splitlines()
            for ln, msg in errs:
                print("    第 %d 行: %s" % (ln, msg))
                for k in range(max(1, ln - 1), min(len(lines), ln + 1) + 1):
                    print("      %4d| %s" % (k, lines[k - 1][:130]))
        else:
            print("[通过] %s（字符串 %d 个）" % (name, st["strings"]))

    print()
    print("体检 %d 个文件，不合格 %d 个" % (len(files), bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
