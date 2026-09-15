# -*- coding: utf-8 -*-
"""抽出 shader 里所有 UnityPerMaterial CBUFFER 块，逐块比对布局。
为什么要这么干：URP 的 SRP Batcher 要求 UnityPerMaterial 在所有 pass 里**逐字段同序同类型**。
不一致不会报错、不会崩，只会让 SRP Batcher 静默失效（掉性能），
且新增 pass 时极容易漏字段。所以必须机器比对，不靠眼睛。
"""
import re, sys, io

def norm(path):
    src = io.open(path, encoding='utf-8').read()
    pat = re.compile(r'CBUFFER_START\(UnityPerMaterial\)(.*?)CBUFFER_END', re.S)
    blocks = pat.findall(src)
    out = []
    for b in blocks:
        fields = []
        for line in b.split('\n'):
            line = line.split('//')[0].strip()
            if not line:
                continue
            # 一个声明行可能写多个：half4 _A; half4 _B;
            for decl in line.split(';'):
                decl = decl.strip()
                if not decl:
                    continue
                m = re.match(r'^(half4|half3|half2|half|float4|float3|float2|float)\s+([_A-Za-z0-9]+)$', decl)
                if m:
                    fields.append((m.group(1), m.group(2)))
                else:
                    fields.append(('??', decl))
        out.append(fields)
    return out

for path in sys.argv[1:]:
    blocks = norm(path)
    print("=" * 70)
    print(path)
    print("CBUFFER 块数 =", len(blocks))
    for i, f in enumerate(blocks):
        print("  #%d 字段数=%d" % (i, len(f)))
    base = blocks[0]
    ok = True
    for i, f in enumerate(blocks[1:], 1):
        if f == base:
            print("  #%d 与 #0 完全一致" % i)
        else:
            ok = False
            bs, fs = set(base), set(f)
            miss = [x for x in base if x not in fs]
            extra = [x for x in f if x not in bs]
            print("  #%d 与 #0 **不一致**" % i)
            if miss:
                print("     #0 有而 #%d 缺: %s" % (i, miss))
            if extra:
                print("     #%d 有而 #0 缺: %s" % (i, extra))
            # 顺序差异
            common = [x for x in base if x in fs]
            order = [x for x in f if x in bs]
            if common != order:
                print("     顺序不同（同集合，SRP Batcher 也会失效）")
    print("  >>> 结论:", "全部一致 ✅" if ok else "存在差异 ❌")
