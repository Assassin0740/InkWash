# -*- coding: utf-8 -*-
"""把 dg_tune 的参数扫描拼成对比图：左=受光面，右=背光面，第一行是主角参照。"""
import os
from PIL import Image, ImageDraw, ImageFont

SRC = r"D:/Unity Project/InkWash/Tools/screenshots/color"
OUT = os.path.join(SRC, "_墨色参数扫描_墨山_受光vs背光.png")

BG = (20, 22, 26)
FG = (232, 232, 232)
DIM = (148, 152, 158)
OK = (80, 190, 120)
BAD = (215, 80, 72)
WARN = (225, 170, 70)
ACC = (95, 160, 235)


def font(sz, bold=False):
    for p in ([r"C:/Windows/Fonts/msyhbd.ttc", r"C:/Windows/Fonts/msyh.ttc"] if bold
              else [r"C:/Windows/Fonts/msyh.ttc", r"C:/Windows/Fonts/msyhbd.ttc"]):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, sz)
            except Exception:
                pass
    return ImageFont.load_default()


F_TITLE = font(25, True)
F_SUB = font(17)
F_CAP = font(16, True)
F_CAP2 = font(15)
F_HEAD = font(19, True)

# (行标签, 左格文件名, 左格数据, 右格文件名, 右格数据, 数据颜色)
ROWS = [
    ("主角 墨客 ｜ M_Character_Ink ｜ _ChromaKeep 0.98 / _BandBias +0.15   ★ 参照基准",
     "CL_Player_0.png",    "彩度 15.9 ｜ 亮度 106",
     "CL_Player_180.png",  "彩度 20.2 ｜ 亮度 92",
     ACC),
    ("T0 现状 ｜ _ChromaKeep 0.30 / _BandBias -0.18   （当前敌人用的就是这一档）",
     "TN_T0_现状_0.png",   "彩度 14.1 ｜ 亮度 77 ｜ 纯黑 8%",
     "TN_T0_现状_180.png", "彩度 8.0 ｜ 亮度 43 ｜ 纯黑 40%",
     BAD),
    ("T1 只抬彩度 ｜ 0.75 / -0.18   （彩度管色相注入量）",
     "TN_T1_只抬彩度_0.png",   "彩度 16.6 ｜ 亮度 77 ｜ 纯黑 8%",
     "TN_T1_只抬彩度_180.png", "彩度 9.0 ｜ 亮度 43 ｜ 纯黑 30%",
     WARN),
    ("T2 只抬墨阶 ｜ 0.30 / +0.06   （墨阶管整体落哪一档墨）",
     "TN_T2_只抬墨阶_0.png",   "彩度 19.4 ｜ 亮度 112 ｜ 纯黑 6%",
     "TN_T2_只抬墨阶_180.png", "彩度 11.6 ｜ 亮度 58 ｜ 纯黑 16%",
     WARN),
    ("T3 两者都抬 ｜ 0.75 / +0.06   ★ 推荐",
     "TN_T3_两者都抬_0.png",   "彩度 23.5 ｜ 亮度 112 ｜ 纯黑 6%",
     "TN_T3_两者都抬_180.png", "彩度 13.5 ｜ 亮度 58 ｜ 纯黑 14%",
     OK),
    ("T4 接近主角 ｜ 0.95 / +0.13   （敌人会失去浓墨层级）",
     "TN_T4_接近主角_0.png",   "彩度 26.4 ｜ 亮度 117 ｜ 纯黑 6%",
     "TN_T4_接近主角_180.png", "彩度 16.3 ｜ 亮度 64 ｜ 纯黑 9%",
     WARN),
]

PW = 470
PH = int(PW * 720 / 1280)
M = 24
GAP = 16
CAP = 50
ROWGAP = 14
HEAD = 126
W = M + PW + GAP + PW + M
RH = PH + CAP + ROWGAP
H = M + HEAD + len(ROWS) * RH + M + 62

canvas = Image.new("RGB", (W, H), BG)
d = ImageDraw.Draw(canvas)

d.text((M, M + 2), "敌人「没有色彩」定位 → 墨色参数扫描（测试场景 Showcase · 演示场相机）",
       font=F_TITLE, fill=FG)
d.text((M, M + 44),
       "同一机位推近到占屏高约 50%，每档只改 (_ChromaKeep, _BandBias)，其余参数不动。画面无任何哨兵底色，就是正常渲染。",
       font=F_SUB, fill=DIM)
d.text((M, M + 70),
       "彩度 = max(R,G,B)-min(R,G,B) 的均值（≈0 就是灰黑、没有色彩）；纯黑 = 三通道均 < 8 的像素占比。",
       font=F_SUB, fill=DIM)

y = M + HEAD
# 列头
d.text((M + PW // 2 - 84, y - 30), "受光面（方位 0°）", font=F_HEAD, fill=(196, 200, 206))
d.text((M + PW + GAP + PW // 2 - 84, y - 30), "背光面（方位 180°）", font=F_HEAD, fill=(196, 200, 206))

for label, fa, da, fb, db, col in ROWS:
    for k, (f, txt) in enumerate(((fa, da), (fb, db))):
        x = M + k * (PW + GAP)
        try:
            im = Image.open(os.path.join(SRC, f)).convert("RGB").resize((PW, PH), Image.LANCZOS)
            canvas.paste(im, (x, y))
        except Exception as e:
            d.rectangle([x, y, x + PW, y + PH], fill=(40, 42, 46))
            d.text((x + 10, y + 10), "缺图 " + f + " " + str(e), font=F_CAP2, fill=BAD)
        d.rectangle([x, y, x + PW, y + PH], outline=(60, 64, 70))
        if k == 0:
            d.text((x + 6, y + PH + 4), label, font=F_CAP, fill=FG)
        d.text((x + 6, y + PH + 26), txt, font=F_CAP2, fill=col)
    y += RH

d.text((M, H - M - 48),
       "结论：主因是 _BandBias（墨阶偏移）——它决定暗部落在哪一档墨，抬它比抬彩度有效得多（背光纯黑 40% → 16%）。",
       font=F_SUB, fill=DIM)
d.text((M, H - M - 24),
       "_ChromaKeep 决定色相注入量，是次因。推荐 T3（0.75 / +0.06）：背光纯黑 40% → 14%，彩度 8.0 → 13.5。",
       font=F_SUB, fill=DIM)

canvas.save(OUT)
print("saved", OUT, canvas.size)
