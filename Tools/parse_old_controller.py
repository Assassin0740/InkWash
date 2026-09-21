# parse_old_controller.py — 解析老 Player.controller 的状态与转移拓扑
import re, sys

p = r'E:\Programs\Project\InkWash\Tools\reports\old_player_controller.yaml'
txt = open(p, encoding='utf-8').read()

blocks = {}
for m in re.finditer(r'--- !u!(\d+) &(-?\d+)\n(.*?)(?=--- !u!|\Z)', txt, re.S):
    blocks[m.group(2)] = (m.group(1), m.group(3))

ids = {}
for fid, (t, b) in blocks.items():
    if t == '1102':
        n = re.search(r'm_Name: (.+)', b)
        ids[fid] = n.group(1) if n else '?'

def transitions_of(b):
    m = re.search(r'm_Transitions:\n((?:\s*- \{fileID: -?\d+\}\n)+)', b)
    if not m:
        return []
    return re.findall(r'fileID: (-?\d+)\}', m.group(1))

for fid, (t, b) in blocks.items():
    if t != '1102':
        continue
    name = ids.get(fid, fid)
    tids = transitions_of(b)
    print(f'== {name} ({len(tids)} out)')
    for tid in tids:
        tb = blocks[tid][1]
        dst = re.search(r'm_DstState: \{fileID: (-?\d+)\}', tb)
        ex = re.search(r'm_ExitTime: ([\d.]+)', tb)
        het = re.search(r'm_HasExitTime: (\d)', tb)
        dur = re.search(r'm_TransitionDuration: ([\d.]+)', tb)
        it = re.search(r'm_InterruptionSource: (\d+)', tb)
        conds = re.findall(r'm_ConditionEvent: (\S+)', tb)
        cmodes = re.findall(r'm_ConditionMode: (\d+)\n\s*m_ConditionEvent: (\S+)\n\s*m_EventTreshold: ([\d.]+)', tb)
        d = ids.get(dst.group(1), 'SM' if dst is None else dst.group(1))
        print(f'   -> {d:<9} exit={ex.group(1):>5} hasExit={het.group(1)} dur={dur.group(1)} int={it.group(1)} cond={cmodes}')

# 状态机默认态
for fid, (t, b) in blocks.items():
    if t == '1107':
        dv = re.search(r'm_DefaultState: \{fileID: (-?\d+)\}', b)
        if dv:
            print('DEFAULT =', ids.get(dv.group(1), dv.group(1)))
