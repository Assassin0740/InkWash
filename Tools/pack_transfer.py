# -*- coding: utf-8 -*-
"""
按 Tools/reports/transfer_manifest.txt 打包跨机搬运集。

    python Tools/pack_transfer.py [输出路径.zip]

产物结构保留工程内相对路径（`Assets/ThirdParty/...`），在目标工程根解压即可。
"""
import os
import sys
import zipfile

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TOOLS)
MANIFEST = os.path.join(TOOLS, "reports", "transfer_manifest.txt")
DEFAULT_OUT = os.path.join(os.path.dirname(ROOT),
                           "InkWash_ThirdParty_closure_20260917.zip")

# 这些是源机器与目标机器**已确认字节一致**的文件，打进去只是重复劳动。
# chinese_dragon.fbx：md5 fd6907f21bfebe96cef46b16c17a63ba（两边相同），
# 且 guid 相同 ⇒ 拷不拷都不影响引用解析。留着是为了"包内自洽"（可单独删）。
SKIP = set()


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT
    with open(MANIFEST, encoding="utf-8") as f:
        files = [l.strip().replace("/", os.sep) for l in f
                 if l.strip() and not l.startswith("#")]

    total = 0
    for rel in files:
        p = os.path.join(ROOT, rel)
        if os.path.isfile(p):
            total += os.path.getsize(p)

    print("准备打包 %d 个文件 / %.1f MB" % (len(files), total / 1048576.0))
    print("输出: %s" % out)

    n = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for rel in files:
            p = os.path.join(ROOT, rel)
            if not os.path.isfile(p):
                print("  [跳过·不存在] %s" % rel)
                continue
            arc = rel.replace(os.sep, "/")
            if arc in SKIP:
                continue
            z.write(p, arc)
            n += 1
            if n % 20 == 0:
                print("  已写入 %d/%d" % (n, len(files)))

    sz = os.path.getsize(out)
    print("\n完成: %d 个文件 -> %.1f MB（压缩率 %.0f%%）"
          % (n, sz / 1048576.0, 100.0 * sz / max(total, 1)))
    print("目标机器：在工程根解压本包 → 运行 python Tools/check_clone_deps.py 应报 0 处悬空")
    return 0


if __name__ == "__main__":
    sys.exit(main())
