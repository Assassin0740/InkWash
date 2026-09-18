#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
store_cn_search.py —— Unity 中国资源商店（assetstore.u3d.cn）检索

为什么需要它
------------
用户网络访问不了 `assetstore.unity.com`（.com 域），只能用国内的
**团结引擎商店 assetstore.u3d.cn**。而那个站的搜索页是客户端渲染的、
`/api/graphql` 又被腾讯云 WAF 挡（403 WAF拦截页面）—— 于是「按关键词搜」这条路走不通。

绕法（本站独有，已验证）：
  1. `https://assetstore.u3d.cn/sitemap.xml` —— **1.2 MB，全量商品 URL + 分类路径**，
     服务端直出，curl 就能拿。⇒ 这就是「全量商品 id 清单」。
  2. 每个商品页 HTML 里内嵌 `<script type="application/ld+json">` 的
     **schema.org Product**（name / description / category / brand / price / currency），
     以及 Apollo state 里的 `supportedTuanjieVersions`（团结引擎版本）。
     ⇒ 这就是「商品元数据」，不用碰被 WAF 挡的 API。

所以本脚本 = sitemap 拿清单 + 逐商品页抓元数据 + 本地缓存 + 关键词/免费筛选。

用法
----
  # 1) 拉（或刷新）sitemap 并建立商品索引缓存
  python Tools/store_cn_search.py --refresh

  # 2) 按分类抓取并缓存（只抓需要的分类，别抓全量 5711 个）
  python Tools/store_cn_search.py --cat vfx/particles --cat vfx/particles/spells

  # 3) 在缓存里按关键词筛（不联网）
  python Tools/store_cn_search.py --kw 闪电 雷电 电弧 电流 烟雾 烟 --free --show

  # 4) 一步到位：抓 + 筛
  python Tools/store_cn_search.py --cat vfx --kw 闪电 烟 --free --show

  # 5) 看有哪些分类、各多少商品
  python Tools/store_cn_search.py --cats

缓存位置 `Tools/reports/store_cn_cache.json`，可随 git 走（体积小，是选型依据）。

★ 下载本身是账号门禁动作，脚本拿不到（商品页 HTML 里没有 unitypackage 直链）。
  流程必然是：**人点「添加至我的资源」→ 编辑器 Package Manager > My Assets > Download
  → 再跑 Tools/install_store_pkg.py 接管安装**。
"""

from __future__ import annotations

import argparse
import io
import json
import os
import re
import sys
import time
from concurrent.futures import ThreadPoolExecutor, as_completed

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CACHE = os.path.join(ROOT, "Tools", "reports", "store_cn_cache.json")
SITEMAP_CACHE = os.path.join(ROOT, "Tools", "reports", "store_cn_sitemap.xml")
SITEMAP_URL = "https://assetstore.u3d.cn/sitemap.xml"
UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"

# 与本项目相关的默认分类（VFX 类）
DEFAULT_CATS = [
    "vfx",
    "vfx/particles",
    "vfx/particles/spells",
    "vfx/particles/fire-explosions",
    "vfx/particles/environment",
    "vfx/shaders",
    "tools/particles-effects",
]


# ────────────────────────────── 网络 ──────────────────────────────

def _get(url: str, timeout: int = 30) -> bytes:
    import urllib.request
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    return urllib.request.urlopen(req, timeout=timeout).read()


def fetch_sitemap(refresh: bool = False) -> str:
    if os.path.exists(SITEMAP_CACHE) and not refresh:
        return io.open(SITEMAP_CACHE, "r", encoding="utf-8", errors="replace").read()
    print("[sitemap] 下载 " + SITEMAP_URL)
    txt = _get(SITEMAP_URL, 90).decode("utf-8", "replace")
    os.makedirs(os.path.dirname(SITEMAP_CACHE), exist_ok=True)
    io.open(SITEMAP_CACHE, "w", encoding="utf-8").write(txt)
    print("[sitemap] 已缓存 %d 字节 → %s" % (len(txt), SITEMAP_CACHE))
    return txt


def parse_sitemap(txt: str) -> list[tuple[str, str, str]]:
    """→ [(id, 分类路径, 完整 URL)]"""
    out = []
    for loc in re.findall(r"<loc>([^<]+)</loc>", txt):
        m = re.search(r"/packages/(.+)/assetstore-package-(\d+)", loc)
        if m:
            out.append((m.group(2), m.group(1), loc))
    return out


# ────────────────────────── 商品页解析 ──────────────────────────

_LD = re.compile(r'<script type="application/ld\+json"[^>]*>(.*?)</script>', re.S)


def parse_product(html: str) -> dict:
    """从商品页 HTML 里挖 schema.org Product + 团结引擎版本 + 富文本注意。"""
    d: dict = {}
    for m in _LD.finditer(html):
        raw = m.group(1).strip()
        try:
            j = json.loads(raw)
        except Exception:
            continue
        if isinstance(j, dict) and j.get("@type") == "Product":
            d["name"] = j.get("name")
            d["desc"] = j.get("description")
            d["category_cn"] = j.get("category")
            d["image"] = (j.get("image") or [None])[0]
            b = j.get("brand") or {}
            d["publisher"] = b.get("name")
            d["publisher_url"] = b.get("url")
            o = j.get("offers") or {}
            d["price"] = o.get("price")
            d["currency"] = o.get("priceCurrency")
            break

    m = re.search(r'"supportedTuanjieVersions":\[(.*?)\]', html)
    if m:
        d["tuanjie"] = re.findall(r'"([^"]+)"', m.group(1))
    m = re.search(r'"supportedUnityVersions":\[(.*?)\]', html)
    if m:
        d["unity"] = re.findall(r'"([^"]+)"', m.group(1))

    m = re.search(r'"publishNotes":"(.*?)","supportedUnityVersions"', html, re.S)
    if m:
        raw = m.group(1)
        try:                                  # ★ 中文富文本是 UTF-8 被 latin-1 误解码
            raw = raw.encode("latin-1").decode("utf-8")
        except Exception:
            pass
        raw = raw.replace("\\n", "\n").replace('\\"', '"')
        d["notes"] = re.sub(r"<[^>]+>", "", raw).strip()

    m = re.search(r'"fileSize":"?([0-9.]+)"?', html)
    if m:
        d["size"] = m.group(1)
    return d


def scan(urls: list[tuple[str, str, str]], cache: dict, workers: int = 6) -> dict:
    todo = [u for u in urls if u[0] not in cache]
    print("[scan] 需抓 %d 个商品页（缓存已有 %d）" % (len(todo), len(cache)))
    if not todo:
        return cache

    done = 0
    t0 = time.time()
    with ThreadPoolExecutor(max_workers=workers) as ex:
        fut = {ex.submit(_get, u[2]): u for u in todo}
        for f in as_completed(fut):
            pid, cat, url = fut[f]
            rec = {"id": pid, "cat": cat, "url": url}
            try:
                rec.update(parse_product(f.result().decode("utf-8", "replace")))
            except Exception as e:
                rec["error"] = "%s: %s" % (type(e).__name__, e)
            cache[pid] = rec
            done += 1
            if done % 25 == 0 or done == len(todo):
                el = time.time() - t0
                print("  %d/%d  %.0fs  (%.2f 个/秒)" % (done, len(todo), el, done / max(el, 1e-6)))
                _save(cache)
    _save(cache)
    return cache


def _save(cache: dict) -> None:
    os.makedirs(os.path.dirname(CACHE), exist_ok=True)
    io.open(CACHE, "w", encoding="utf-8").write(
        json.dumps(cache, ensure_ascii=False, indent=1, sort_keys=True))


# ────────────────────────── 展示与筛选 ──────────────────────────

def matches(rec: dict, kws: list[str]) -> bool:
    if not kws:
        return True
    hay = " ".join(str(rec.get(k) or "") for k in
                   ("name", "desc", "notes", "category_cn", "publisher"))
    return any(k in hay for k in kws)


def show(rows: list[dict]) -> None:
    if not rows:
        print("（无匹配）")
        return
    print("\n%-10s %-26s %-8s %-22s %s" % ("id", "名称", "价格", "团结引擎版本", "分类/发布者"))
    print("-" * 118)
    for r in rows:
        tj = ",".join(r.get("tuanjie") or []) or "-"
        free = "免费" if str(r.get("price")) in ("0", "0.0", "0.00") else ("¥" + str(r.get("price")))
        pub = "%s / %s" % (r.get("category_cn") or r.get("cat"), r.get("publisher") or "?")
        print("%-10s %-26s %-8s %-22s %s" % (r["id"], (r.get("name") or "?")[:24], free, tj[:20], pub))
    print()
    for r in rows:
        print("  %s  %s" % (r["id"], r["url"]))
        if r.get("desc"):
            print("      %s" % r["desc"][:150])
    print()


def main() -> int:
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument("--refresh", action="store_true", help="重新下载 sitemap")
    ap.add_argument("--cats", action="store_true", help="只打印分类分布")
    ap.add_argument("--cat", action="append", default=[], help="要抓的分类前缀，可重复")
    ap.add_argument("--all-cats", action="store_true", help="抓默认 VFX 分类（见 DEFAULT_CATS）")
    ap.add_argument("--kw", nargs="*", default=[], help="关键词（对 名称/描述/注意/分类/发布者）")
    ap.add_argument("--free", action="store_true", help="只要免费的")
    ap.add_argument("--limit", type=int, default=0, help="最多展示多少条")
    ap.add_argument("--workers", type=int, default=6)
    ap.add_argument("--show", action="store_true", help="打印表格")
    ap.add_argument("--retry-failed", action="store_true",
                    help="把缓存里带 error 的记录丢掉重抓（抓取偶发超时/502 会留空洞）")
    a = ap.parse_args()

    items = parse_sitemap(fetch_sitemap(a.refresh))
    print("[sitemap] 商品 %d 个，分类 %d 个" % (len(items), len({c for _, c, _ in items})))

    if a.cats:
        import collections
        for c, n in sorted(collections.Counter(c for _, c, _ in items).items()):
            print("  %-52s %d" % (c, n))
        return 0

    cats = []
    if a.all_cats:
        cats += DEFAULT_CATS
    cats += a.cat
    if cats:
        sel = [u for u in items if any(u[1] == c or u[1].startswith(c + "/") for c in cats)]
        print("[scan] 选中分类 %s ⇒ %d 个商品" % (", ".join(cats), len(sel)))
    else:
        sel = items

    cache = {}
    if os.path.exists(CACHE):
        try:
            cache = json.loads(io.open(CACHE, "r", encoding="utf-8").read())
        except Exception:
            cache = {}
    if a.retry_failed:
        bad = [k for k, v in cache.items() if isinstance(v, dict) and "error" in v]
        for k in bad:
            cache.pop(k, None)
        print("[retry-failed] 清掉 %d 条失败记录" % len(bad))
    scan(sel, cache, a.workers)

    rows = [cache[u[0]] for u in sel if u[0] in cache]
    rows = [r for r in rows if "error" not in r]
    if a.free:
        rows = [r for r in rows if str(r.get("price")) in ("0", "0.0", "0.00")]
    rows = [r for r in rows if matches(r, a.kw)]
    rows.sort(key=lambda r: (r.get("name") or ""))

    print("[result] %d 条命中（缓存总量 %d）" % (len(rows), len(cache)))
    if a.show or a.kw or a.free:
        show(rows[:a.limit] if a.limit else rows)
    return 0


if __name__ == "__main__":
    sys.exit(main())
