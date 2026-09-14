#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""Unity 中国资源商店 · 商品页探针

中国站是服务端渲染的：商品页 HTML 里直接内嵌了商品 JSON（国际站不行，只有 SPA 外壳）。
本脚本抓一个（或多个）包 ID 的关键字段，用来做选型前的体检。

用法:
    python Tools/store_probe.py 20003228
    python Tools/store_probe.py 20003228 20004799 --json out.json
    python Tools/store_probe.py --from-url "https://assetstore.u3d.cn/packages/3d/props/weapons/assetstore-package-20003228"

注意:
  - 富文本正文（publishNotes / description）在 HTML 里是 UTF-8 被 latin-1 误解码的，需还原。
  - 只探"元信息"，拿不到面数/贴图格式 —— 那些必须下载后用 Unity API 实测。
"""
import argparse
import json
import html
import re
import sys
import urllib.request

UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/120.0 Safari/537.36")

# 想要抓的字段（按需要增减）
FIELDS = [
    "title", "price", "category", "description", "content",
    "supportedTuanjieVersions", "supportedUnityVersions",
    "publishNotes", "version", "publisher", "id", "uploadDate", "fileSize",
]


def fetch(url, timeout=30):
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        raw = r.read()
    # 先按 UTF-8 读，读不通就退回 latin-1（页面本身可能是混合编码）
    try:
        return raw.decode("utf-8")
    except UnicodeDecodeError:
        return raw.decode("latin-1")


def fix_mojibake(s):
    """UTF-8 被 latin-1 误解码 → 还原。还原失败就原样返回。"""
    if not s:
        return s
    try:
        return s.encode("latin-1").decode("utf-8")
    except (UnicodeEncodeError, UnicodeDecodeError):
        return s


def clean(s):
    s = fix_mojibake(s)
    s = re.sub(r"<[^>]+>", " ", s)          # 去 HTML 标签
    s = html.unescape(s)                     # 去实体
    s = re.sub(r"[ \t\u00a0]+", " ", s)
    return re.sub(r"\s+", " ", s).strip()


def grab_field(h, key):
    """从内嵌 JSON 里取一个字段。值可能是字符串 / 数组 / 标量。"""
    pat = r'"%s"\s*:\s*("(?:[^"\\]|\\.)*"|\[[^\]]*\]|true|false|null|-?[\d.]+)' % re.escape(key)
    m = re.search(pat, h)
    if not m:
        return None
    v = m.group(1).strip()
    if v.startswith('"'):
        try:
            v = json.loads(v)
        except json.JSONDecodeError:
            v = v[1:-1]
    elif v.startswith("["):
        try:
            v = json.loads(v)
        except json.JSONDecodeError:
            v = v.strip("[]")
    return str(v)


def pkg_id_from_url(url):
    m = re.search(r"assetstore-package-(\d+)", url)
    return m.group(1) if m else None


def bare_id(token):
    m = re.search(r"(\d{5,})", token)
    return m.group(1) if m else None


def probe(pkg_id, timeout=30):
    url = "https://assetstore.u3d.cn/packages/x/assetstore-package-%s" % pkg_id
    h = fetch(url, timeout=timeout)
    out = {"pkg_id": pkg_id, "url": url, "html_bytes": len(h)}
    for k in FIELDS:
        v = grab_field(h, k)
        if v:
            if k in ("publishNotes", "description", "content"):
                v = clean(v)
            out[k] = v[:1200]
    # 预览图
    og = re.findall(r'og:image[^>]*content="([^"]+)"', h)
    if og:
        out["preview"] = og[0]
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("ids", nargs="*", help="一个或多个 assetstore-package-<id> 里的数字 ID")
    ap.add_argument("--from-url", action="append", default=[], help="直接给商品页 URL")
    ap.add_argument("--json", help="把结果另存为 JSON")
    ap.add_argument("--timeout", type=int, default=30)
    a = ap.parse_args()

    ids = []
    for t in a.ids:
        i = bare_id(t)
        if i:
            ids.append(i)
        else:
            print("跳过无法识别的参数: %s" % t, file=sys.stderr)
    for u in a.from_url:
        i = pkg_id_from_url(u)
        if i:
            ids.append(i)

    if not ids:
        ap.error("至少给一个 ID 或 --from-url")

    results = []
    for i in ids:
        try:
            r = probe(i, timeout=a.timeout)
        except Exception as e:
            print("== %s == 抓取失败: %s" % (i, e), file=sys.stderr)
            continue
        results.append(r)
        print("=" * 66)
        print("包 ID : %s" % r["pkg_id"])
        print("URL   : %s" % r["url"])
        print("页面  : %d 字节" % r["html_bytes"])
        price = r.get("price")
        print("价格  : %s%s" % (price, "   ← 免费" if price in ("0", "0.0", "0.00") else ""))
        for k in ("title", "category", "version", "publisher", "uploadDate", "fileSize",
                  "supportedTuanjieVersions", "supportedUnityVersions"):
            if r.get(k):
                print("%-8s: %s" % (k, r[k][:300]))
        if r.get("description"):
            print("简介  : %s" % r["description"][:400])
        if r.get("publishNotes"):
            print("正文  : %s" % r["publishNotes"][:400])
        if r.get("preview"):
            print("预览图: %s" % r["preview"])

    if a.json:
        with open(a.json, "w", encoding="utf-8") as f:
            json.dump(results, f, ensure_ascii=False, indent=2)
        print("\n已写 %s" % a.json)


if __name__ == "__main__":
    main()
