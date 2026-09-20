#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""通用：解析 Unity AnimatorController YAML，打印状态名/速度/转移拓扑。

用法: python Tools/parse_controller.py <controller.yaml> [关键字过滤]
"""
import re
import sys


def parse(path):
    txt = open(path, encoding='utf-8', errors='ignore').read()
    blocks = {}
    for m in re.finditer(r'--- !u!(\d+) &(-?\d+)\n(.*?)(?=--- !u!|\Z)', txt, re.S):
        blocks[m.group(2)] = (m.group(1), m.group(3))
    names = {}
    for fid, (t, b) in blocks.items():
        if t == '1102':
            n = re.search(r'm_Name: (.+)', b)
            names[fid] = n.group(1).strip() if n else '?'
    return blocks, names


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else r'E:\Programs\Project\InkWash\Assets\_Project\Animations\Player.controller'
    filt = sys.argv[2] if len(sys.argv) > 2 else None
    blocks, names = parse(path)
    print('=== STATES ===')
    for fid, (t, b) in blocks.items():
        if t != '1102':
            continue
        name = names[fid]
        sp = re.search(r'm_Speed: ([\d.]+)', b)
        mot = re.search(r'm_Motion: \{fileID: (-?\d+)', b)
        cyc = re.search(r'm_CycleOffset: ([\d.]+)', b)
        motname = names.get(mot.group(1), 'EXT') if mot else 'none'
        print(f'  {name:<12} speed={sp.group(1) if sp else "1":<6} motion_ref={motname:<12} fileID={mot.group(1) if mot else "-"}')

    print('=== TRANSITIONS ===')
    for fid, (t, b) in blocks.items():
        if t != '1102':
            continue
        name = names[fid]
        mt = re.search(r'm_Transitions:\n((?:  - \{fileID: -?\d+\}\n)*)', b)
        tids = re.findall(r'fileID: (-?\d+)', mt.group(1)) if mt else []
        for tid in tids:
            if tid not in blocks:
                continue
            tb = blocks[tid][1]
            dst = re.search(r'm_DstState: \{fileID: (-?\d+)\}', tb)
            ex = re.search(r'm_ExitTime: ([\d.]+)', tb)
            het = re.search(r'm_HasExitTime: (\d)', tb)
            dur = re.search(r'm_TransitionDuration: ([\d.]+)', tb)
            off = re.search(r'm_TransitionOffset: ([\d.]+)', tb)
            it = re.search(r'm_InterruptionSource: (\d+)', tb)
            ie = re.search(r'm_InterruptionSourceIsOrdered: (\d)', tb) or re.search(r'm_OrderedInterruption: (\d)', tb)
            conds = []
            cb = re.search(r'm_Conditions:\n((?:    .*\n)*)', tb)
            if cb:
                for line in cb.group(1).splitlines():
                    ev = re.search(r'm_ConditionEvent: (\S+)', line)
                    md = re.search(r'm_ConditionMode: (\d+)', line)
                    thr = re.search(r'm_EventThreshold: ([\d.]+)', line)
                    if ev:
                        conds.append(f'{ev.group(1)}[mode={md.group(1) if md else "?"}{"@" + thr.group(1) if thr else ""}]')
            d = names.get(dst.group(1), 'ExitSM') if dst else 'ExitSM'
            line = (f'  {name:<12} -> {d:<12} exit={ex.group(1) if ex else "?":<6} hasExit={het.group(1) if het else "?"} '
                    f'dur={dur.group(1) if dur else "?":<6} off={off.group(1) if off else "0":<6} '
                    f'int={it.group(1) if it else "?"}({ie.group(1) if ie else "?"}) cond={conds}')
            if filt is None or filt in line:
                print(line)


if __name__ == '__main__':
    main()
