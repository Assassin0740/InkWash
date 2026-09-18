# -*- coding: utf-8 -*-
"""
make_vfx_shortlist.py —— 从中国站缓存里挑 VFX 候选，出「封面图鉴 + 链接清单」交给用户点单

为什么要有它
------------
用户的网络**上不了 .com**（assetstore.unity.com 直接 Access denied），
所以之前挑的 `FX Lightning II free` / `Free Stylized Smoke Effects Pack` **拿不到**。
只能在 **团结引擎商店 assetstore.u3d.cn** 里找替代 —— 缓存（Tools/reports/store_cn_cache.json）
已覆盖该站**全部 VFX 分类 345 个商品**，本脚本负责把候选排成能"用眼睛点"的图鉴。

★ 判据只用**确实抓到的字段**：价格 / 团结版本 / 分类 / 发布者。
  版本兼容按 `2022.3.62` 段判（后缀 t2/t5/f3c1 是不同发布线，要求字面相等会误杀）。

用法：python Tools/make_vfx_shortlist.py
产物：Docs/素材候选_中国站_v1.png   （图鉴）
      Docs/素材候选_中国站_v1.md    （链接 + 下载步骤）
"""
import io
import json
import os
import sys
import urllib.request

from PIL import Image, ImageDraw, ImageFont

sys.stdout.reconfigure(encoding="utf-8")

ROOT = r"D:\Unity Project\InkWash"
CACHE = os.path.join(ROOT, "Tools", "reports", "store_cn_cache.json")
COVER_DIR = os.path.join(ROOT, "Tools", "screenshots", "store_cn", "covers")
PNG_OUT = os.path.join(ROOT, "Docs", "素材候选_中国站_v1.png")
MD_OUT = os.path.join(ROOT, "Docs", "素材候选_中国站_v1.md")

# ── 候选（人工筛过一遍的结果，不是关键词全量）──────────────────────────
# (id, 一句话：为什么留它)
SECTIONS = [
    ("A · 墨黑吐息 / 烟（口部向前的纯墨黑）", [
        ("20003498", "★首选：写实墨水法术，自带笔触+深色调，可当黑烟本体"),
        ("20003825", "★¥13 墨迹化开：贴图极便宜，做墨团扩散最省"),
        ("20006941", "VFX Graph 风格化烟 33 款（URP/VFX Graph）"),
        ("20001590", "火与烟，粒子响应风速风向，适合吐息的尾部拖散"),
        ("20002242", "烟雾弹 20+ 变化，施放/循环/收束三段齐全"),
        ("20005957", "免费：Unity 官方粒子示例包（含烟/尘）"),
        ("20001064", "免费：卡通粒子（含烟/尘/爆炸）"),
        ("20004869", "免费：雾颗粒，做环境墨雾"),
        ("20006713", "免费：WarFX 战争粒子（含烟）"),
    ]),
    ("B · 闪电 / 雷（俯冲、云层电光）", [
        ("20004407", "写实风暴激涌：带电云层+风+闪电（同一家更新最勤）"),
        ("20004709", "逼真风暴激涌：同上，写实一版"),
        ("20002262", "真实 ARPG 闪电法术（Piloto，2022.3.62t2）"),
        ("20002337", "电击&闪电 风格化入门套件（224，偏风格化）"),
        ("20006913", "VFX Graph 闪电 Vol.1（59.5，最便宜的成体系闪电）"),
        ("20006915", "VFX Graph 闪电 Vol.2"),
        ("20006933", "VFX Graph 程序化电（可做电弧拖尾）"),
        ("20006088", "ARPG 魔法宝石：含闪电爆发+带电武器"),
        ("20003546", "2D 电弧效果（¥34，2D 电弧贴图，最便宜）"),
    ]),
    ("C · 顺带（水墨项目高相关，非本次必需）", [
        ("20006878", "Unique Sword Slashes Vol.1 —— 20 种剑击，**含「墨」**"),
        ("20003524", "免费：通用拖尾特效（URP），可做墨刃拖尾"),
        ("20004867", "免费：低聚火焰粒子"),
    ]),
]

TW, TH = 470, 264          # 封面绘制尺寸
PAD, BAR = 10, 74          # 间距 / 文字条高
COLS = 3


def font(size, bold=False):
    cands = ([r"C:\Windows\Fonts\msyhbd.ttc", r"C:\Windows\Fonts\msyh.ttc"]
             if bold else [r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msyhbd.ttc"])
    for p in cands + [r"C:\Windows\Fonts\simhei.ttf", r"C:\Windows\Fonts\simsun.ttc"]:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()


def cover(rec):
    """封面图本地缓存；失败返回 None。"""
    os.makedirs(COVER_DIR, exist_ok=True)
    url = rec.get("image")
    if not url:
        return None
    ext = os.path.splitext(url)[1] or ".jpg"
    local = os.path.join(COVER_DIR, "%s%s" % (rec["id"], ext))
    if not os.path.exists(local) or os.path.getsize(local) < 1024:
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
            data = urllib.request.urlopen(req, timeout=40).read()
            io.open(local, "wb").write(data)
        except Exception as e:
            print("  × 封面失败 %s %s" % (rec["id"], e))
            return None
    try:
        im = Image.open(local).convert("RGB")
    except Exception:
        return None
    # 等比裁切填充到 16:9
    tw, th = TW, TH
    s = max(tw / im.width, th / im.height)
    im = im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)
    x = (im.width - tw) // 2
    y = (im.height - th) // 2
    return im.crop((x, y, x + tw, y + th))


def price_str(rec):
    p = str(rec.get("price"))
    return "免费" if p in ("0", "0.0", "0.00") else "¥" + p


def ver_str(rec):
    tj = rec.get("tuanjie") or []
    return tj[0] if tj else "-"


def main():
    cache = json.loads(io.open(CACHE, "r", encoding="utf-8").read())
    f = font(19)
    fb = font(20, True)
    fh = font(30, True)
    fs = font(15)

    rows = []          # (标题, [(rec, note, PIL.Image|None)])
    for title, items in SECTIONS:
        tiles = []
        for pid, note in items:
            rec = cache.get(pid)
            if not rec:
                print("  × 缓存缺 %s" % pid)
                continue
            tiles.append((rec, note, cover(rec)))
        rows.append((title, tiles))

    cw = PAD + COLS * (TW + PAD)
    ch = PAD
    for title, tiles in rows:
        ch += 54 + ((len(tiles) + COLS - 1) // COLS) * (TH + BAR + PAD)
    cv = Image.new("RGB", (cw, ch), (240, 242, 246))
    d = ImageDraw.Draw(cv)
    d.rectangle([0, 0, cw, 46], fill=(22, 24, 30))
    d.text((12, 9), "InkWash 素材候选（团结引擎商店 assetstore.u3d.cn，本项目 345 个 VFX 商品全量筛出）",
           font=font(23, True), fill=(255, 236, 200))

    y = 46 + PAD
    for title, tiles in rows:
        d.rectangle([PAD, y, cw - PAD, y + 44], fill=(206, 214, 226))
        d.text((PAD + 12, y + 7), title, font=fh, fill=(24, 28, 38))
        y += 54
        for k, (rec, note, im) in enumerate(tiles):
            x = PAD + (k % COLS) * (TW + PAD)
            if k and k % COLS == 0:
                y += TH + BAR + PAD
            if im is not None:
                cv.paste(im, (x, y))
            else:
                d.rectangle([x, y, x + TW, y + TH], fill=(200, 204, 212))
                d.text((x + 150, y + 120), "封面缺失", font=f, fill=(120, 124, 132))
            d.rectangle([x, y + TH, x + TW, y + TH + BAR], fill=(28, 31, 38))
            d.text((x + 8, y + TH + 4), "%s  %s  [%s]" % (price_str(rec), (rec.get("name") or "")[:34], ver_str(rec)),
                   font=fb, fill=(255, 246, 220) if price_str(rec) == "免费" else (150, 226, 250))
            d.text((x + 8, y + TH + 30), "id %s  %s" % (rec["id"], (rec.get("publisher") or "")[:24]),
                   font=fs, fill=(168, 176, 190))
            d.text((x + 8, y + TH + 50), note[:52], font=fs, fill=(196, 204, 216))
            d.rectangle([x, y, x + TW, y + TH], outline=(150, 156, 168))
        y += TH + BAR + PAD
    cv.save(PNG_OUT)
    print("完成图鉴：%s（%dx%d）" % (PNG_OUT, cw, ch))

    # ── markdown 清单 ──
    mb = io.StringIO()
    mb.write("# InkWash 素材候选（中国站）\n\n")
    mb.write("> 来源：团结引擎资源商店 `assetstore.u3d.cn`（本项目已把该站 **VFX 全部 345 个商品** 抓成本地缓存）。\n")
    mb.write("> 之前挑的 `FX Lightning II free` / `Free Stylized Smoke Effects Pack` 在 `.com` 站，用户网络访问不了，故全部替换为下表。\n")
    mb.write("> 版本一栏是该商品声明的**团结引擎**支持版本；本项目是 `2022.3.62f2c1`，按 `2022.3.62` 段判兼容。\n\n")
    for title, tiles in rows:
        mb.write("## %s\n\n" % title)
        mb.write("| 价格 | 名称 | 团结版本 | id | 链接 |\n|---|---|---|---|---|\n")
        for rec, note, _ in tiles:
            mb.write("| %s | %s | %s | %s | %s |\n" % (
                price_str(rec), rec.get("name"), ver_str(rec), rec["id"], rec["url"]))
        mb.write("\n")
        for rec, note, _ in tiles:
            mb.write("- **%s**（%s）：%s\n" % (rec.get("name"), price_str(rec), note))
        mb.write("\n")
    mb.write("## 怎么下载（中国站路线）\n\n")
    mb.write("1. 浏览器登录 `assetstore.u3d.cn`，进商品页点 **加入我的资源 / 添加至我的资源**。\n")
    mb.write("2. 打开团结引擎编辑器 → `Window > Package Manager` → 左上角切到 **My Assets / 我的资源** → 刷新 → Download → Import。\n")
    mb.write("3. 若 My Assets 列表为空：确认编辑器里登录的是**同一个 Unity 中国账号**（团结引擎用中国区账号，国际账号看不到）。\n")
    mb.write("4. 落盘后把包放进项目，登记到 [Docs/素材来源与授权.md] 并确认 `.gitignore` 已排除（素材不入 git）。\n")
    io.open(MD_OUT, "w", encoding="utf-8").write(mb.getvalue())
    print("完成清单：%s" % MD_OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main())
