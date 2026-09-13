# -*- coding: utf-8 -*-
"""
毕设迭代计划表 —— 《基于Unity框架的3D水墨动作游戏全流程设计与实现》
范式：计划型(plan) 主 + 打卡/进度型(progress) 副
"""
import os
from datetime import date

try:
    import openpyxl
except ImportError:
    import subprocess, sys
    subprocess.check_call([sys.executable, "-m", "pip", "install", "--quiet", "openpyxl>=3.1.0"])
    import openpyxl

from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.utils import get_column_letter
from openpyxl.worksheet.datavalidation import DataValidation
from openpyxl.formatting.rule import CellIsRule

# ---------------------------------------------------------------- 颜色
def xl_color(css_hex: str) -> str:
    value = css_hex.removeprefix("#").upper()
    if len(value) != 6:
        raise ValueError(f"Expected #RRGGBB, got: {css_hex}")
    return "FF" + value

XL_HEAD_BG   = xl_color("#4472C4")   # 表头背景
XL_HEAD_FG   = xl_color("#FFFFFF")   # 表头字色
XL_SOFT      = xl_color("#D9E2F3")   # 浅底/甘特条
XL_ACCENT    = xl_color("#2F5597")   # 强调
XL_BORDER    = xl_color("#BFBFBF")
XL_INPUT     = xl_color("#FAFAFA")
XL_TITLE_FG  = xl_color("#1F3864")
XL_GREEN_BG  = xl_color("#C6EFCE"); XL_GREEN_FG = xl_color("#006100")
XL_RED_BG    = xl_color("#FFC7CE"); XL_RED_FG   = xl_color("#9C0006")
XL_YELLOW_BG = xl_color("#FFEB9C"); XL_YELLOW_FG= xl_color("#9C6500")

thin  = Side(style="thin", color=XL_BORDER)
BORDER = Border(left=thin, right=thin, top=thin, bottom=thin)

F_TITLE  = Font(name="微软雅黑", size=14, bold=True, color=XL_TITLE_FG)
F_HEAD   = Font(name="微软雅黑", size=10, bold=True, color=XL_HEAD_FG)
F_BODY   = Font(name="微软雅黑", size=10)
F_BOLD   = Font(name="微软雅黑", size=10, bold=True)
F_NOTE   = Font(name="微软雅黑", size=9, color=xl_color("#595959"))
F_SEC    = Font(name="微软雅黑", size=11, bold=True, color=XL_ACCENT)

FILL_HEAD   = PatternFill("solid", fgColor=XL_HEAD_BG)
FILL_SOFT   = PatternFill("solid", fgColor=XL_SOFT)
FILL_INPUT  = PatternFill("solid", fgColor=XL_INPUT)
FILL_ACCENT = PatternFill("solid", fgColor=XL_ACCENT)

AL_C   = Alignment(horizontal="center", vertical="center", wrap_text=True)
AL_L   = Alignment(horizontal="left",   vertical="center", wrap_text=True)
AL_LT  = Alignment(horizontal="left",   vertical="top",    wrap_text=True)

FMT_DATE = "YYYY-MM-DD"
FMT_INT  = "#,##0"
FMT_PCT  = "0%"

OUT = r"D:\Unity Project\InkWash\Docs\毕设迭代计划表.xlsx"

wb = Workbook()

def style_header(ws, row, cols, height=30):
    for c in cols:
        cell = ws[f"{c}{row}"]
        cell.font, cell.fill, cell.border, cell.alignment = F_HEAD, FILL_HEAD, BORDER, AL_C
    ws.row_dimensions[row].height = height

def put_title(ws, rng, text):
    ws.merge_cells(rng)
    first = rng.split(":")[0]
    ws[first] = text
    ws[first].font = F_TITLE
    ws[first].alignment = Alignment(horizontal="left", vertical="center")
    ws.row_dimensions[int(first[1:])].height = 26


# ===================================================================
# Sheet 1 · 甘特总览       锚点：1 标题 / 2 表头 / 3-12 数据 / 13 合计
# ===================================================================
ws = wb.active
ws.title = "甘特总览"
put_title(ws, "A1:S1", "毕设迭代计划表 · 甘特总览   —— 7 天冲刺（2026-09-13 ~ 2026-09-20）")

GANTT_COLS = list("HIJKLMNO")          # D0..D7
HEAD1 = ["序号", "阶段", "主要任务", "负责人", "开始日期", "结束日期", "持续天数",
         "D0\n09-13", "D1\n09-14", "D2\n09-15", "D3\n09-16",
         "D4\n09-17", "D5\n09-18", "D6\n09-19", "D7\n09-20",
         "关键产出 / 里程碑", "状态", "优先级", "备注"]
for i, h in enumerate(HEAD1, start=1):
    ws.cell(row=2, column=i, value=h)
style_header(ws, 2, [get_column_letter(i) for i in range(1, 20)])

D = date
ROWS1 = [
    ("S0 环境基线", "打通 Codely Bridge 通道、装齐依赖包、建目录骨架与计划文档", "你+AI", D(2026,9,13), D(2026,9,13), "M0 通道贯通", "进行中", "高", "唯一阻塞项：先开 Unity 等编译"),
    ("S1 角色操控", "白盒场景、PlayerController 移动/转向/冲刺、Cinemachine 跟随、Animator 骨架", "AI", D(2026,9,14), D(2026,9,14), "M1 能动", "未开始", "高", "素材入库后统一规范为 Humanoid"),
    ("S2 战斗内核", "三段连击、蓄力重击、闪避 i-frame、Hitbox 框架、顿帧与镜头震动", "AI", D(2026,9,15), D(2026,9,15), "M2 能打", "未开始", "高", "打击感优先于数值平衡"),
    ("S3 敌人与关卡", "NavMesh 烘焙、敌人行为状态机、3 种敌人、波次生成与房间控制", "AI", D(2026,9,16), D(2026,9,16), "M3 有对手", "未开始", "高", "依赖 com.unity.ai.navigation"),
    ("S4 水墨渲染 ★", "多阶色调映射光照、噪波扰动飞白边缘、程序化宣纸底纹", "AI", D(2026,9,17), D(2026,9,17), "M4 有墨味（论文第3章核心）", "未开始", "高", "高风险项，降级链见风险登记册 R3"),
    ("S5 完整循环", "游戏状态机、技能数据层、三选一随机池、墨晕后处理、水墨动作特效", "AI", D(2026,9,18), D(2026,9,18), "M5 完整循环可玩", "未开始", "高", "Roguelike 闭环成型"),
    ("S6 测试优化", "自动化测试用例、Profiler 性能基线、对象池与批处理优化、手感调参", "AI", D(2026,9,19), D(2026,9,19), "M6 稳定 60fps", "未开始", "中", "同时输出论文第5章数据"),
    ("S7 交付", "Recorder 录屏、渲染对比截图集、Windows Build 打包、论文章节成稿", "你+AI", D(2026,9,20), D(2026,9,20), "M7 交付", "未开始", "高", "答辩材料齐套"),
    ("贯穿·论文写作", "每个 Sprint 收尾同步成稿对应章节（第 3 章为重点）", "AI+你", D(2026,9,13), D(2026,9,20), "六章成稿", "进行中", "高", "绝不后置，避免末期挤占"),
    ("贯穿·素材采购", "Mixamo 角色与动画、Quaternius 敌人、Kenney/PolyHaven 场景、音效", "你", D(2026,9,13), D(2026,9,14), "素材库就绪", "未开始", "中", "优先 CC0 授权源"),
]

FIRST, LAST = 3, 12
for k, (stage, task, who, s, e, deliver, status, prio, note) in enumerate(ROWS1):
    r = FIRST + k
    ws[f"A{r}"] = k + 1
    ws[f"B{r}"] = stage
    ws[f"C{r}"] = task
    ws[f"D{r}"] = who
    ws[f"E{r}"] = s; ws[f"E{r}"].number_format = FMT_DATE
    ws[f"F{r}"] = e; ws[f"F{r}"].number_format = FMT_DATE
    ws[f"G{r}"] = f'=IF(OR(E{r}="",F{r}=""),"",F{r}-E{r}+1)'
    ws[f"P{r}"] = deliver
    ws[f"Q{r}"] = status
    ws[f"R{r}"] = prio
    ws[f"S{r}"] = note
    for c in range(1, 20):
        ws.cell(row=r, column=c).border = BORDER
        ws.cell(row=r, column=c).font = F_BODY

# 甘特条：S0-S7 各占一天(第 k 天)，论文写作全周，素材采购 D0-D1
for k in range(8):
    ws.cell(row=FIRST + k, column=8 + k).fill = FILL_SOFT
for c in range(8, 16):
    ws.cell(row=11, column=c).fill = FILL_SOFT
for c in (8, 9):
    ws.cell(row=12, column=c).fill = FILL_SOFT

for r in range(FIRST, LAST + 1):
    for c in range(1, 20):
        al = AL_C if c in (1,4,5,6,7,17,18) or c in range(8,16) else AL_L
        ws.cell(row=r, column=c).alignment = al
    ws.row_dimensions[r].height = 34
for r in range(FIRST, LAST + 1):
    ws[f"C{r}"].alignment = AL_L
    ws[f"P{r}"].alignment = AL_L
    ws[f"S{r}"].alignment = AL_L
    ws[f"S{r}"].font = F_NOTE

# 合计行（第 13 行）
TOT = LAST + 1
ws[f"A{TOT}"] = "合计"
ws.merge_cells(f"A{TOT}:C{TOT}")
ws[f"E{TOT}"] = f"=MIN(E{FIRST}:E{LAST})"; ws[f"E{TOT}"].number_format = FMT_DATE
ws[f"F{TOT}"] = f"=MAX(F{FIRST}:F{LAST})"; ws[f"F{TOT}"].number_format = FMT_DATE
ws[f"G{TOT}"] = f"=SUM(G{FIRST}:G{LAST})"
ws[f"P{TOT}"] = "计划任务完成率"
ws[f"Q{TOT}"] = f'=IF(COUNTA(Q{FIRST}:Q{LAST})=0,"",COUNTIF(Q{FIRST}:Q{LAST},"已完成")/COUNTA(Q{FIRST}:Q{LAST}))'
ws[f"Q{TOT}"].number_format = FMT_PCT
for c in range(1, 20):
    cell = ws.cell(row=TOT, column=c)
    cell.font = F_BOLD; cell.fill = FILL_ACCENT; cell.border = BORDER
    cell.alignment = AL_C
    if c in (1,):
        cell.font = Font(name="微软雅黑", size=10, bold=True, color=XL_HEAD_FG)
ws[f"A{TOT}"].font = Font(name="微软雅黑", size=10, bold=True, color=XL_HEAD_FG)
ws[f"P{TOT}"].font = Font(name="微软雅黑", size=10, bold=True, color=XL_HEAD_FG); ws[f"P{TOT}"].alignment = AL_L
ws[f"Q{TOT}"].font = Font(name="微软雅黑", size=10, bold=True, color=XL_HEAD_FG)
ws.row_dimensions[TOT].height = 24

dv1 = DataValidation(type="list", formula1='"未开始,进行中,已完成,已延期"', allow_blank=True)
ws.add_data_validation(dv1); dv1.add(f"Q{FIRST}:Q{LAST}")

widths1 = {"A":6,"B":15,"C":42,"D":8,"E":12,"F":12,"G":9,
           "H":9,"I":9,"J":9,"K":9,"L":9,"M":9,"N":9,"O":9,
           "P":26,"Q":10,"R":7,"S":30}
for k, v in widths1.items():
    ws.column_dimensions[k].width = v
ws.freeze_panes = "C3"
ws.auto_filter.ref = f"A2:S{LAST}"
ws.sheet_view.showGridLines = False


# ===================================================================
# Sheet 2 · Sprint任务看板  锚点：1 标题 / 2 表头 / 3-36 任务 / 右侧汇总 L1:P12
# ===================================================================
ws2 = wb.create_sheet("Sprint任务看板")
put_title(ws2, "A1:J1", "Sprint 任务看板 · Product Backlog 执行明细（共 98 故事点）")
HEAD2 = ["序号", "Sprint", "任务ID", "任务", "负责人", "故事点", "状态", "完成日期", "产出物", "备注"]
for i, h in enumerate(HEAD2, start=1):
    ws2.cell(row=2, column=i, value=h)
style_header(ws2, 2, list("ABCDEFGHIJ"))

TASKS = [
    ("S0","A1","打通 Codely Bridge 远程通道","你+AI",1,"进行中",None,"doctor 返回数字端口","需先开 Unity 等首次编译"),
    ("S0","A2","装齐依赖包（桥/导航/摄像机/录制）","AI",1,"已完成",D(2026,9,13),"manifest.json 更新","已核实 2022.3 兼容版本"),
    ("S0","A3","建目录骨架与项目约定","AI",1,"已完成",D(2026,9,13),"Assets/_Project 33 个目录","—"),
    ("S1","A4","外部模型导入并规范为 Humanoid","AI",2,"未开始",None,"可复用动画的角色","Mixamo 导出勾 In Place"),
    ("S1","A5","水墨禅院白盒场景","AI",2,"未开始",None,"Scenes/Game.unity","地形+光照+边界"),
    ("S1","B1","角色移动/转向/冲刺 + 跟随镜头","AI",3,"未开始",None,"PlayerController.cs","Cinemachine 2.10.7"),
    ("S2","B2","三段连击与蓄力重击","AI",5,"未开始",None,"PlayerCombat.cs","第 3 段带前冲位移"),
    ("S2","B3","闪避翻滚与无敌帧","AI",3,"未开始",None,"闪避模块（含 i-frame）","可取消后摇"),
    ("S2","B4","受击硬直/击退/顿帧/镜头震动","AI",3,"未开始",None,"HitStop.cs","打击感核心"),
    ("S3","C1","NavMesh 烘焙流程","AI",2,"未开始",None,"NavMesh 数据","依赖 com.unity.ai.navigation"),
    ("S3","C2","敌人行为状态机","AI",3,"未开始",None,"EnemyFSM.cs","巡逻/追击/攻击/硬直/死亡"),
    ("S3","C3","3 种差异化敌人","AI",5,"未开始",None,"近战/远程/精英","精英含可弹反窗口"),
    ("S3","C4","波次生成器与房间控制","AI",2,"未开始",None,"WaveSpawner.cs","清空房间才开门"),
    ("S4","E1","多阶色调映射光照模型","AI",5,"未开始",None,"InkCharacter.shader","论文 3.2"),
    ("S4","E2","噪波扰动的“飞白”边缘提取","AI",5,"未开始",None,"InkRendererFeature.cs","论文 3.3 · 核心创新点"),
    ("S4","E3","程序化宣纸质感底纹","AI",3,"未开始",None,"多层 Perlin 噪声","论文 3.4 前半"),
    ("S5","D1","游戏状态机（菜单→进局→结算）","AI",3,"未开始",None,"GameStateMachine.cs","论文 4.1"),
    ("S5","D2","ScriptableObject 技能数据层","AI",2,"未开始",None,"SkillData.cs","论文 4.4"),
    ("S5","D3","加权随机技能池 + 三选一 UI","AI",5,"未开始",None,"UpgradePanelUI.cs","论文 4.4"),
    ("S5","D4","属性叠加与局内成长","AI",3,"未开始",None,"PlayerStats.cs","—"),
    ("S5","D5","房间流程与推进目标","AI",3,"未开始",None,"RoomController.cs","—"),
    ("S5","E4","墨晕扩散后处理","AI",5,"未开始",None,"InkPostProcess 组件","论文 3.4 后半"),
    ("S5","E5","水墨动作粒子（刀光/溅墨/墨花）","AI",3,"未开始",None,"Prefabs/FX","论文 3.5"),
    ("S6","F1","关键逻辑自动化测试","AI",2,"未开始",None,"Tests/ 用例集","论文 5.1"),
    ("S6","F2","Profiler 性能基线数据","AI",2,"未开始",None,"性能数据表","论文 5.2"),
    ("S6","F3","对象池与批处理优化","AI",3,"未开始",None,"稳定 60fps","论文 5.2"),
    ("S6","F4","用户测试反馈记录","你",2,"未开始",None,"反馈记录表","论文 5.3"),
    ("S6","E6","渲染效果对比截图集","AI",2,"未开始",None,"截图集","论文第 3 章插图"),
    ("S7","G6","演示录屏与 Build 打包","你+AI",2,"未开始",None,"Demo 视频 + exe","答辩材料"),
    ("贯穿","G1","第 1、2 章成稿","AI+你",3,"未开始",None,"绪论 / 技术概述","—"),
    ("贯穿","G2","第 3 章成稿（方案+公式+对比图）","AI+你",5,"未开始",None,"论文核心章","与 S4/S5 同步"),
    ("贯穿","G3","第 4 章成稿（架构图+类图+流程图）","AI+你",3,"未开始",None,"系统设计章","与 S1-S5 同步"),
    ("贯穿","G4","第 5 章成稿（测试用例+性能数据）","AI+你",2,"未开始",None,"测试与优化章","与 S6 同步"),
    ("贯穿","G5","第 6 章 + 摘要 + 目录 + 参考文献","AI+你",2,"未开始",None,"收尾章","—"),
]
T_FIRST = 3
T_LAST  = T_FIRST + len(TASKS) - 1
for k, (sp, tid, task, who, pt, st, done, out, note) in enumerate(TASKS):
    r = T_FIRST + k
    ws2[f"A{r}"] = k + 1
    ws2[f"B{r}"] = sp
    ws2[f"C{r}"] = tid
    ws2[f"D{r}"] = task
    ws2[f"E{r}"] = who
    ws2[f"F{r}"] = pt; ws2[f"F{r}"].number_format = FMT_INT
    ws2[f"G{r}"] = st
    ws2[f"H{r}"] = done
    if done: ws2[f"H{r}"].number_format = FMT_DATE
    ws2[f"I{r}"] = out
    ws2[f"J{r}"] = note
    for c in range(1, 11):
        cell = ws2.cell(row=r, column=c)
        cell.border = BORDER
        cell.font = F_BODY
        cell.alignment = AL_C if c in (1,2,3,5,6,7,8) else AL_L
    ws2.row_dimensions[r].height = 28

# 合计行
TSUM = T_LAST + 1
ws2[f"A{TSUM}"] = "合计"
ws2.merge_cells(f"A{TSUM}:E{TSUM}")
ws2[f"F{TSUM}"] = f"=SUM(F{T_FIRST}:F{T_LAST})"
ws2[f"F{TSUM}"].number_format = FMT_INT
ws2[f"G{TSUM}"] = "已完成点占比"
ws2[f"H{TSUM}"] = f'=IF(SUM(F{T_FIRST}:F{T_LAST})=0,"",SUMIF(G{T_FIRST}:G{T_LAST},"已完成",F{T_FIRST}:F{T_LAST})/SUM(F{T_FIRST}:F{T_LAST}))'
ws2[f"H{TSUM}"].number_format = FMT_PCT
for c in range(1, 11):
    cell = ws2.cell(row=TSUM, column=c)
    cell.fill = FILL_ACCENT; cell.border = BORDER; cell.alignment = AL_C
    cell.font = Font(name="微软雅黑", size=10, bold=True, color=XL_HEAD_FG)
ws2[f"G{TSUM}"].alignment = AL_L
ws2.row_dimensions[TSUM].height = 24

# 右侧汇总区：L1 标题 / L2 表头 / L3:L12 各 Sprint
put_title(ws2, "L1:P1", "各 Sprint 故事点完成情况")
for i, h in enumerate(["Sprint", "故事点合计", "已完成点数", "完成率", "备注"], start=12):
    ws2.cell(row=2, column=i, value=h)
style_header(ws2, 2, list("LMNOP"))
SPRINTS = ["S0","S1","S2","S3","S4","S5","S6","S7","贯穿"]
for k, sp in enumerate(SPRINTS):
    r = 3 + k
    ws2[f"L{r}"] = sp
    ws2[f"M{r}"] = f'=SUMIF($B${T_FIRST}:$B${T_LAST},$L{r},$F${T_FIRST}:$F${T_LAST})'
    ws2[f"M{r}"].number_format = FMT_INT
    ws2[f"N{r}"] = f'=SUMIFS($F${T_FIRST}:$F${T_LAST},$B${T_FIRST}:$B${T_LAST},$L{r},$G${T_FIRST}:$G${T_LAST},"已完成")'
    ws2[f"N{r}"].number_format = FMT_INT
    ws2[f"O{r}"] = f'=IF(OR(M{r}="",M{r}=0),"",N{r}/M{r})'
    ws2[f"O{r}"].number_format = FMT_PCT
    ws2[f"P{r}"] = ""
    for c in range(12, 17):
        cell = ws2.cell(row=r, column=c)
        cell.border = BORDER; cell.font = F_BODY
        cell.alignment = AL_C if c < 16 else AL_L
r = 3 + len(SPRINTS) - 1
ws2[f"L{r+1}"] = "合计"
ws2[f"M{r+1}"] = f"=SUM(M3:M{r})"; ws2[f"M{r+1}"].number_format = FMT_INT
ws2[f"N{r+1}"] = f"=SUM(N3:N{r})"; ws2[f"N{r+1}"].number_format = FMT_INT
ws2[f"O{r+1}"] = f'=IF(OR(M{r+1}="",M{r+1}=0),"",N{r+1}/M{r+1})'; ws2[f"O{r+1}"].number_format = FMT_PCT
for c in range(12, 17):
    cell = ws2.cell(row=r+1, column=c)
    cell.fill = FILL_SOFT; cell.border = BORDER; cell.font = F_BOLD; cell.alignment = AL_C

dv2 = DataValidation(type="list", formula1='"未开始,进行中,已完成,已延期"', allow_blank=True)
ws2.add_data_validation(dv2); dv2.add(f"G{T_FIRST}:G{T_LAST}")

# 状态条件格式
ws2.conditional_formatting.add(f"G{T_FIRST}:G{T_LAST}",
    CellIsRule(operator="equal", formula=['"已完成"'], fill=PatternFill("solid", fgColor=XL_GREEN_BG), font=Font(color=XL_GREEN_FG, bold=True)))
ws2.conditional_formatting.add(f"G{T_FIRST}:G{T_LAST}",
    CellIsRule(operator="equal", formula=['"进行中"'], fill=PatternFill("solid", fgColor=XL_YELLOW_BG), font=Font(color=XL_YELLOW_FG, bold=True)))
ws2.conditional_formatting.add(f"G{T_FIRST}:G{T_LAST}",
    CellIsRule(operator="equal", formula=['"已延期"'], fill=PatternFill("solid", fgColor=XL_RED_BG), font=Font(color=XL_RED_FG, bold=True)))
ws2.conditional_formatting.add("O3:O12",
    CellIsRule(operator="greaterThanOrEqual", formula=["1"], fill=PatternFill("solid", fgColor=XL_GREEN_BG), font=Font(color=XL_GREEN_FG, bold=True)))
ws2.conditional_formatting.add("O3:O12",
    CellIsRule(operator="lessThan", formula=["0.5"], fill=PatternFill("solid", fgColor=XL_RED_BG), font=Font(color=XL_RED_FG)))

widths2 = {"A":6,"B":9,"C":9,"D":38,"E":8,"F":8,"G":10,"H":12,"I":26,"J":26,
           "K":3,"L":9,"M":12,"N":12,"O":10,"P":8}
for k, v in widths2.items():
    ws2.column_dimensions[k].width = v
ws2.freeze_panes = "D3"
ws2.auto_filter.ref = f"A2:J{T_LAST}"
ws2.sheet_view.showGridLines = False


# ===================================================================
# Sheet 3 · 论文写作对照   锚点：1 标题 / 2 表头 / 3-22 数据
# ===================================================================
ws3 = wb.create_sheet("论文写作对照")
put_title(ws3, "A1:H1", "论文大纲 ↔ 实现产出 对照表（赛点：每个 Sprint 收尾即写对应章节）")
HEAD3 = ["序号", "章节", "小节", "对应实现 / 产出", "需用素材与图表", "数据来源", "状态", "计划完成"]
for i, h in enumerate(HEAD3, start=1):
    ws3.cell(row=2, column=i, value=h)
style_header(ws3, 2, list("ABCDEFGH"))

PAPER = [
    ("第一章 绪论","1.1 研究背景与意义","水墨风格化游戏的发展脉络","现状梳理引用","文献调研","未开始",D(2026,9,14)),
    ("第一章 绪论","1.2 国内外研究与发展现状","相关作品与技术路线对比表","作品对比表","文献调研","未开始",D(2026,9,14)),
    ("第一章 绪论","1.3 主要研究内容与论文结构","本文技术路线图","技术路线图","自制","未开始",D(2026,9,14)),
    ("第二章 开发环境与核心技术","2.1 Unity引擎与URP通用渲染管线","工程实测版本与管线架构图","管线架构图","工程实测","未开始",D(2026,9,14)),
    ("第二章 开发环境与核心技术","2.2 基于HLSL的自定义着色器编程技术","URP Shader 编写流程图","Shader 结构图","工程实测","未开始",D(2026,9,15)),
    ("第二章 开发环境与核心技术","2.3 Roguelike游戏设计理念","随机性与成长曲线说明","成长曲线示意","文献+设计","未开始",D(2026,9,15)),
    ("第三章 3D水墨风格化渲染 ★","3.1 3D水墨视觉特征提取与技术难点","水墨画视觉特征分析","特征分析图 + 难点归纳表","自制","未开始",D(2026,9,17)),
    ("第三章 3D水墨风格化渲染 ★","3.2 基于多阶色调映射的光照模型重构","多阶量化 Ramp 光照","Ramp 示意 + 效果对比截图","S4 产出","未开始",D(2026,9,17)),
    ("第三章 3D水墨风格化渲染 ★","3.3 结合噪波扰动的“飞白”边缘提取算法","飞白边缘算法（核心创新）","算法流程图 + 公式 + 参数对比图","S4 产出","未开始",D(2026,9,17)),
    ("第三章 3D水墨风格化渲染 ★","3.4 宣纸质感与晕染后处理特效","宣纸底纹 + 墨晕扩散","效果对比截图","S4/S5 产出","未开始",D(2026,9,18)),
    ("第三章 3D水墨风格化渲染 ★","3.5 粒子特效驱动的水墨动作表现","刀光/溅墨/墨花","特效截图","S5 产出","未开始",D(2026,9,18)),
    ("第四章 Roguelike动作游戏系统","4.1 游戏整体架构与核心玩法循环","状态机与玩法闭环","系统架构图 + 玩法循环流程图","S5 产出","未开始",D(2026,9,18)),
    ("第四章 Roguelike动作游戏系统","4.2 角色控制与战斗交互模块","移动/连击/闪避/命中反馈","类图 + 连击状态机图","S1/S2 产出","未开始",D(2026,9,16)),
    ("第四章 Roguelike动作游戏系统","4.3 敌人AI逻辑与导航网格寻路","行为状态机 + NavMesh","FSM 状态图 + NavMesh 示意","S3 产出","未开始",D(2026,9,17)),
    ("第四章 Roguelike动作游戏系统","4.4 随机技能池与局内成长机制","SO 数据层 + 加权随机","数据结构图 + 权重公式","S5 产出","未开始",D(2026,9,19)),
    ("第五章 游戏测试与性能优化","5.1 游戏功能与玩法逻辑测试","自动化测试用例","测试用例表 + 通过率","S6 产出","未开始",D(2026,9,19)),
    ("第五章 游戏测试与性能优化","5.2 渲染性能分析与优化","Profiler 采集与优化对比","性能数据表 + 优化前后对比","S6 产出","未开始",D(2026,9,19)),
    ("第五章 游戏测试与性能优化","5.3 用户体验反馈与调整","问卷与改进记录","问卷结果与改进记录","S6 产出","未开始",D(2026,9,20)),
    ("第六章 总结与展望","6.1 本文工作总结","全文成果归纳","—","汇总","未开始",D(2026,9,20)),
    ("第六章 总结与展望","6.2 存在不足与未来展望","不足与后续方向","—","汇总","未开始",D(2026,9,20)),
]
P_FIRST = 3
for k, (ch, sec, impl, need, src, st, due) in enumerate(PAPER):
    r = P_FIRST + k
    ws3[f"A{r}"] = k + 1
    ws3[f"B{r}"] = ch
    ws3[f"C{r}"] = sec
    ws3[f"D{r}"] = impl
    ws3[f"E{r}"] = need
    ws3[f"F{r}"] = src
    ws3[f"G{r}"] = st
    ws3[f"H{r}"] = due; ws3[f"H{r}"].number_format = FMT_DATE
    for c in range(1, 9):
        cell = ws3.cell(row=r, column=c)
        cell.border = BORDER; cell.font = F_BODY
        cell.alignment = AL_C if c in (1,7,8) else AL_L
    ws3.row_dimensions[r].height = 28
P_LAST = P_FIRST + len(PAPER) - 1

dv3 = DataValidation(type="list", formula1='"未开始,写作中,已完稿,待导师确认"', allow_blank=True)
ws3.add_data_validation(dv3); dv3.add(f"G{P_FIRST}:G{P_LAST}")
ws3.conditional_formatting.add(f"G{P_FIRST}:G{P_LAST}",
    CellIsRule(operator="equal", formula=['"已完稿"'], fill=PatternFill("solid", fgColor=XL_GREEN_BG), font=Font(color=XL_GREEN_FG, bold=True)))
ws3.conditional_formatting.add(f"G{P_FIRST}:G{P_LAST}",
    CellIsRule(operator="equal", formula=['"写作中"'], fill=PatternFill("solid", fgColor=XL_YELLOW_BG), font=Font(color=XL_YELLOW_FG, bold=True)))
ws3.conditional_formatting.add(f"G{P_FIRST}:G{P_LAST}",
    CellIsRule(operator="equal", formula=['"待导师确认"'], fill=PatternFill("solid", fgColor=XL_SOFT), font=Font(color=XL_ACCENT, bold=True)))

for k, v in {"A":6,"B":22,"C":30,"D":26,"E":30,"F":11,"G":12,"H":12}.items():
    ws3.column_dimensions[k].width = v
ws3.freeze_panes = "C3"
ws3.auto_filter.ref = f"A2:H{P_LAST}"
ws3.sheet_view.showGridLines = False


# ===================================================================
# Sheet 4 · 素材采购清单  锚点：1 标题 / 2 表头 / 3-16 数据
# ===================================================================
ws4 = wb.create_sheet("素材采购清单")
put_title(ws4, "A1:I1", "美术/音频素材采购清单（授权优先级：CC0 > CC-BY > 站内授权；逐条登记备查）")
HEAD4 = ["序号","素材类别","具体素材","来源平台","授权类型","优先级","状态","放置路径","备注"]
for i, h in enumerate(HEAD4, start=1):
    ws4.cell(row=2, column=i, value=h)
style_header(ws4, 2, list("ABCDEFGHI"))

ASSETS = [
    ("角色模型","持刀人形角色（1 个，带骨骼）","Mixamo","Adobe 免费账号","高","未开始","Art/Characters/","需注册 Adobe 账号"),
    ("角色动画","Idle / Running / Walking","Mixamo","Adobe 免费账号","高","未开始","Art/Characters/Animations/","导出时勾 In Place"),
    ("角色动画","Great Sword Slash ×2-3 套","Mixamo","Adobe 免费账号","高","未开始","Art/Characters/Animations/","供三段连击使用"),
    ("角色动画","Dodging / Roll","Mixamo","Adobe 免费账号","高","未开始","Art/Characters/Animations/","闪避动作"),
    ("角色动画","Hit Reaction / Death","Mixamo","Adobe 免费账号","中","未开始","Art/Characters/Animations/","受击与死亡反馈"),
    ("敌人模型","低模怪物（3 种）","Quaternius","CC0","高","未开始","Art/Characters/Enemies/","免登录直下"),
    ("环境模型","山石 / 竹 / 树 / 建筑部件","Kenney Nature Kit","CC0","中","未开始","Art/Environment/","免登录直下"),
    ("环境光照","中式庭院或黄昏 HDRI","Poly Haven","CC0","高","未开始","Art/Environment/","后期压成水墨调"),
    ("PBR 材质","石材 / 木材 / 泥土","ambientCG","CC0","中","未开始","Art/Environment/","—"),
    ("墨迹贴图","水墨笔刷 / 墨迹 PNG（含 alpha）","爱给网","站内授权","高","未开始","Art/Textures/Ink/","用于刀光与溅墨"),
    ("宣纸纹理","纸纹（备用，优先程序化生成）","ambientCG","CC0","低","未开始","Art/Textures/Ink/","程序化生成可写进论文"),
    ("UI 素材","通用 UI 图集（按钮/边框）","Kenney UI Pack","CC0","中","未开始","Art/","再用水墨笔刷重制"),
    ("音效","挥刀 / 命中 / 闪避 / UI","freesound.org、Kenney Audio","CC0 / CC-BY","低","未开始","Audio/SFX/","CC-BY 须在论文署名"),
    ("字体","思源宋体（正文备选）","Google Fonts","SIL OFL","中","未开始","Art/","书法体必须核对授权"),
]
A_FIRST = 3
for k, row in enumerate(ASSETS):
    r = A_FIRST + k
    ws4[f"A{r}"] = k + 1
    for i, v in enumerate(row, start=2):
        ws4.cell(row=r, column=i, value=v)
    for c in range(1, 10):
        cell = ws4.cell(row=r, column=c)
        cell.border = BORDER; cell.font = F_BODY
        cell.alignment = AL_C if c in (1,5,6,7) else AL_L
    ws4.row_dimensions[r].height = 28
A_LAST = A_FIRST + len(ASSETS) - 1

dv4 = DataValidation(type="list", formula1='"未开始,已下载,已入库"', allow_blank=True)
ws4.add_data_validation(dv4); dv4.add(f"G{A_FIRST}:G{A_LAST}")
ws4.conditional_formatting.add(f"G{A_FIRST}:G{A_LAST}",
    CellIsRule(operator="equal", formula=['"已入库"'], fill=PatternFill("solid", fgColor=XL_GREEN_BG), font=Font(color=XL_GREEN_FG, bold=True)))
ws4.conditional_formatting.add(f"G{A_FIRST}:G{A_LAST}",
    CellIsRule(operator="equal", formula=['"已下载"'], fill=PatternFill("solid", fgColor=XL_YELLOW_BG), font=Font(color=XL_YELLOW_FG, bold=True)))
ws4.conditional_formatting.add(f"F{A_FIRST}:F{A_LAST}",
    CellIsRule(operator="equal", formula=['"高"'], fill=PatternFill("solid", fgColor=XL_RED_BG), font=Font(color=XL_RED_FG, bold=True)))

for k, v in {"A":6,"B":12,"C":28,"D":24,"E":15,"F":8,"G":11,"H":30,"I":26}.items():
    ws4.column_dimensions[k].width = v
ws4.freeze_panes = "C3"
ws4.auto_filter.ref = f"A2:I{A_LAST}"
ws4.sheet_view.showGridLines = False


# ===================================================================
# Sheet 5 · 风险登记册  锚点：1 标题 / 2 表头 / 3-11 数据
# ===================================================================
ws5 = wb.create_sheet("风险登记册")
put_title(ws5, "A1:I1", "风险登记册（概率/影响按 1-3 打分；风险值 = 概率 × 影响）")
HEAD5 = ["ID","风险描述","概率(1-3)","影响(1-3)","风险值","等级","应对策略","状态","负责人"]
for i, h in enumerate(HEAD5, start=1):
    ws5.cell(row=2, column=i, value=h)
style_header(ws5, 2, list("ABCDEFGHI"))

RISKS = [
    ("R1","依赖包拉不下来 / 桥起不来 / 端口发现失败",2,3,"先用 doctor 定位；不行改方式 B 内嵌包整目录拷贝；再用 UNITY_BRIDGE_PORT 钉死端口","监控中","AI"),
    ("R2","中国版 Unity 与国际版包源冲突",1,2,"工程已是 packages.unity.cn 主源，风险已消除","已关闭","AI"),
    ("R3","水墨 Shader 效果不达预期（核心风险）",3,3,"Day4 留整日 + Day6 机动；降级链：全效果 → 去墨晕 → 只保多阶光照+飞白；把参数化分级方案本身写成工程贡献","监控中","AI"),
    ("R4","素材版权风险 / 下载受阻",2,2,"一律 CC0 优先；模型退化时用基础几何体+水墨材质，论文中说明聚焦渲染与系统实现","监控中","你"),
    ("R5","一周工期不足",3,3,"严格执行 MoSCoW 范围冻结；Should 项按 Day6 余量取舍；论文写作绝不后置","监控中","你+AI"),
    ("R6","论文被开发挤占，末期没时间写",3,3,"每 Sprint 收尾同步写对应章节；AI 产出初稿，你做表述润色","监控中","AI+你"),
    ("R7","Unity 编译 / 导入等待时间不可控",2,2,"把离线可做的工作（写脚本、写 Shader、写论文、整理数据）排在编译等待期","监控中","AI"),
    ("R8","AI 远程改场景与手动改动冲突",2,2,"约定施工期间不手动改场景；所有资产走版本控制，变更可回滚","监控中","你"),
    ("R9","水墨后处理性能不达标",2,2,"后处理统一走半分辨率 + 降采样；敌人与特效对象池化；Day6 专项优化","监控中","AI"),
]
R_FIRST = 3
for k, (rid, desc, p, im, action, st, owner) in enumerate(RISKS):
    r = R_FIRST + k
    ws5[f"A{r}"] = rid
    ws5[f"B{r}"] = desc
    ws5[f"C{r}"] = p; ws5[f"C{r}"].number_format = FMT_INT
    ws5[f"D{r}"] = im; ws5[f"D{r}"].number_format = FMT_INT
    ws5[f"E{r}"] = f'=IF(OR(C{r}="",D{r}=""),"",C{r}*D{r})'
    ws5[f"F{r}"] = f'=IF(E{r}="","",IF(E{r}>=6,"高",IF(E{r}>=3,"中","低")))'
    ws5[f"G{r}"] = action
    ws5[f"H{r}"] = st
    ws5[f"I{r}"] = owner
    for c in range(1, 10):
        cell = ws5.cell(row=r, column=c)
        cell.border = BORDER; cell.font = F_BODY
        cell.alignment = AL_C if c in (1,3,4,5,6,8,9) else AL_L
    ws5.row_dimensions[r].height = 34
R_LAST = R_FIRST + len(RISKS) - 1

dv5 = DataValidation(type="list", formula1='"监控中,已关闭,已发生"', allow_blank=True)
ws5.add_data_validation(dv5); dv5.add(f"H{R_FIRST}:H{R_LAST}")
ws5.conditional_formatting.add(f"F{R_FIRST}:F{R_LAST}",
    CellIsRule(operator="equal", formula=['"高"'], fill=PatternFill("solid", fgColor=XL_RED_BG), font=Font(color=XL_RED_FG, bold=True)))
ws5.conditional_formatting.add(f"F{R_FIRST}:F{R_LAST}",
    CellIsRule(operator="equal", formula=['"中"'], fill=PatternFill("solid", fgColor=XL_YELLOW_BG), font=Font(color=XL_YELLOW_FG, bold=True)))
ws5.conditional_formatting.add(f"F{R_FIRST}:F{R_LAST}",
    CellIsRule(operator="equal", formula=['"低"'], fill=PatternFill("solid", fgColor=XL_GREEN_BG), font=Font(color=XL_GREEN_FG, bold=True)))
ws5.conditional_formatting.add(f"H{R_FIRST}:H{R_LAST}",
    CellIsRule(operator="equal", formula=['"已关闭"'], fill=PatternFill("solid", fgColor=XL_GREEN_BG), font=Font(color=XL_GREEN_FG, bold=True)))
ws5.conditional_formatting.add(f"H{R_FIRST}:H{R_LAST}",
    CellIsRule(operator="equal", formula=['"已发生"'], fill=PatternFill("solid", fgColor=XL_RED_BG), font=Font(color=XL_RED_FG, bold=True)))

for k, v in {"A":7,"B":36,"C":10,"D":10,"E":9,"F":8,"G":55,"H":10,"I":9}.items():
    ws5.column_dimensions[k].width = v
ws5.freeze_panes = "C3"
ws5.auto_filter.ref = f"A2:I{R_LAST}"
ws5.sheet_view.showGridLines = False

# ---------------------------------------------------------------- 收尾
wb.properties.title = "毕设迭代计划表"
os.makedirs(os.path.dirname(OUT), exist_ok=True)
wb.save(OUT)
print("SAVED:", OUT)
print("SHEETS:", wb.sheetnames)
