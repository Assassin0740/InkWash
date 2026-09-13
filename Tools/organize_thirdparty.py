# -*- coding: utf-8 -*-
"""
把下载的第三方素材从 Assets 根目录的临时堆放处，整理进规范的 ThirdParty 结构。

设计原则：
1. 第三方包**整体保持内部结构**，不拆散 —— Unity 的 FBX 导入器靠"向上递归"找贴图，
   把 Exports/FBX 和 Textures 拆开会导致贴图绑不上。
2. 移动文件时**连同 .meta 一起移动**，保住 GUID（否则 Unity 会重发 GUID，引用全断）。
3. Unity **不原生支持**的格式（.glb/.gltf/.bin/.blend）以及 Unreal 专用法线贴图，
   移到 `_RawDownloads/`（Assets 之外）—— 文件不删，但 Unity 不再导入、不再报错。
4. 授权与预览文件复制一份到 `Docs/素材授权/`，用于论文留证（在 git 里）。

用法：
    python organize_thirdparty.py            # 空转，只打印计划
    python organize_thirdparty.py --apply    # 实际执行
"""
import os
import sys
import shutil

sys.stdout.reconfigure(encoding="utf-8")

PROJ = r"D:\Unity Project\InkWash"
ASSETS = os.path.join(PROJ, "Assets")
RAW = os.path.join(PROJ, "_RawDownloads")
SRC = os.path.join(ASSETS, "Ziyuan")          # 当前临时堆放处
DOCS_LIC = os.path.join(PROJ, "Docs", "素材授权")

# ---------- Phase 1：移出 Assets（Unity 用不到 / 会报错的东西）----------
# (源路径, 目标路径)  —— 相对 SRC / 相对 RAW
MOVE_OUT = [
    # Kenney 像素风 2D 图集：与水墨 3D 风格不搭，1052 个小文件纯拖慢导入
    ("kenney_ui-pack-pixel-adventure", "Kenney/UI-Pack-PixelAdventure"),

    # Bestiary：GLB 是给 Godot/Unreal 的，Unity 不原生认
    ("Bestiary - Dungeon Monsters Kit[Standard]/Exports/GLB (Godot-Unreal)",
     "Quaternius/Bestiary-DungeonMonsters/_GodotUnreal-GLB"),
    ("Bestiary - Dungeon Monsters Kit[Standard]/Textures/Unreal Normals",
     "Quaternius/Bestiary-DungeonMonsters/_UnrealNormals"),

    # Modular Outfits：glTF 目录里整份复制了贴图（约 150 MB），且 Unity 用不了
    ("Modular Character Outfits - Fantasy[Standard]/Modular Character Outfits - Fantasy[Standard]/Exports/glTF (Godot-Unreal)",
     "Quaternius/ModularCharacterOutfits-Fantasy/_GodotUnreal-glTF"),
    ("Modular Character Outfits - Fantasy[Standard]/Modular Character Outfits - Fantasy[Standard]/Textures/Peasant/Normals-UnrealEngine",
     "Quaternius/ModularCharacterOutfits-Fantasy/_UnrealNormals_Peasant"),
    ("Modular Character Outfits - Fantasy[Standard]/Modular Character Outfits - Fantasy[Standard]/Textures/Ranger/Normals-UnrealEngine",
     "Quaternius/ModularCharacterOutfits-Fantasy/_UnrealNormals_Ranger"),

    # UAL2：Unreal/Godot 变体 + .blend（本机无 Blender，Unity 每次导入都报错）
    ("Universal Animation Library 2[Standard]/Unreal-Godot",
     "Quaternius/UniversalAnimationLibrary2/_GodotUnreal"),
    ("Universal Animation Library 2[Standard]/Female Mannequin/Unreal-Godot",
     "Quaternius/UniversalAnimationLibrary2/_GodotUnreal_Mannequin"),

    # ---- 第二批：Stylized Nature MegaKit ----
    # 该包**没有包名外壳**（解压后直接是 FBX/ OBJ/ glTF/ Textures/），已在解压时补壳。
    # glTF：47.5 MB，含 .bin/.gltf，Unity 不原生认
    ("Stylized Nature MegaKit[Standard]/glTF",
     "Quaternius/StylizedNatureMegaKit/_GodotUnreal-glTF"),
    # OBJ：13.0 MB，与 FBX 重复（Unity 用 FBX 更稳），且含 .mtl
    ("Stylized Nature MegaKit[Standard]/OBJ",
     "Quaternius/StylizedNatureMegaKit/_OBJ"),
    # 裸 FBX：与 `FBX (Unity)` 同名同数（68 个），后者才是对齐 Unity 轴/单位的版本
    ("Stylized Nature MegaKit[Standard]/FBX",
     "Quaternius/StylizedNatureMegaKit/_FBX-plain"),
]

# ---- 第二批之二：Ultimate Monsters / Universal Base Characters ----
# Ultimate Monsters 结构重复度高（Big|Blob|Flying × Blends|glTF|OBJ），用循环生成，
# 避免手写 9 条同类项出错。注意 Blends/ 里有 50 个 .blend，本机无 Blender，留在 Assets 会持续报错。
for _grp in ("Big", "Blob", "Flying"):
    for _fmt in ("Blends", "glTF", "OBJ"):
        MOVE_OUT.append((f"Ultimate Monsters/{_grp}/{_fmt}",
                         f"Quaternius/UltimateMonsters/_{_fmt}_{_grp}"))

MOVE_OUT += [
    # UBC：glTF 目录（含 .bin，且整份复制了贴图）Unity 不原生认
    ("Universal Base Characters[Standard]/Base Characters/Godot - UE",
     "Quaternius/UniversalBaseCharacters/_GodotUE"),
    # UBC 发型：Unreal 版 FBX（轴向不同）+ 两套 glTF
    ("Universal Base Characters[Standard]/Hairstyles/Origin at 0/FBX (Unreal Engine)",
     "Quaternius/UniversalBaseCharacters/_Hair_UnrealFBX"),
    ("Universal Base Characters[Standard]/Hairstyles/Origin at 0/glTF (Godot)",
     "Quaternius/UniversalBaseCharacters/_Hair_glTF_OriginAt0"),
    ("Universal Base Characters[Standard]/Hairstyles/Rigged to Head Bone/glTF (Godot -Unreal)",
     "Quaternius/UniversalBaseCharacters/_Hair_glTF_Rigged"),
]

# ---------- Phase 1b：原始压缩包移出 Assets（Unity 用不到，且体积大）----------
ZIP_OUT = [
    ("Stylized Nature MegaKit[Standard].zip", "Zips/Stylized Nature MegaKit[Standard].zip"),
    ("kenney_impact-sounds.zip", "Zips/kenney_impact-sounds.zip"),
    ("Ultimate Monsters.zip", "Zips/Ultimate Monsters.zip"),
    ("Universal Base Characters[Standard].zip", "Zips/Universal Base Characters[Standard].zip"),
]

# 单个文件移出（会带上同名 .meta）
FILE_OUT = [
    ("Universal Animation Library 2[Standard]/Female Mannequin/Mannequin_F.blend",
     "Quaternius/UniversalAnimationLibrary2/Mannequin_F.blend"),
]

# ---------- Phase 2：字体与自有音频进自有资源区 ----------
FONT_MOVES = [
    ("MaShanZheng-Regular.ttf", os.path.join(ASSETS, "_Project", "Art", "Fonts", "MaShanZheng-Regular.ttf")),
]

# BGM 等自有音频：进 _Project/Audio/BGM。
# 保留原始文件名 —— Pixabay 的下载名里带曲目 ID（-247345），是授权溯源的关键线索。
AUDIO_MOVES = [
    ("et11lx-chinese-ancient-style-music-love-etlx-247345.mp3",
     os.path.join(ASSETS, "_Project", "Audio", "BGM", "et11lx-chinese-ancient-style-music-love-etlx-247345.mp3")),
]

# ---------- Phase 3：整包进 ThirdParty ----------
PACK_MOVES = [
    ("Bestiary - Dungeon Monsters Kit[Standard]", "ThirdParty/Quaternius/Bestiary-DungeonMonsters"),
    ("Universal Animation Library 2[Standard]", "ThirdParty/Quaternius/UniversalAnimationLibrary2"),
    ("Modular Character Outfits - Fantasy[Standard]", "ThirdParty/Quaternius/ModularCharacterOutfits-Fantasy"),
    ("Stylized Nature MegaKit[Standard]", "ThirdParty/Quaternius/StylizedNatureMegaKit"),
    ("Ultimate Monsters", "ThirdParty/Quaternius/UltimateMonsters"),
    ("Universal Base Characters[Standard]", "ThirdParty/Quaternius/UniversalBaseCharacters"),
    ("kenney_rpg-audio", "ThirdParty/Kenney/RPG-Audio"),
    ("kenney_ui-audio", "ThirdParty/Kenney/UI-Audio"),
    ("kenney_impact-sounds", "ThirdParty/Kenney/Impact-Sounds"),
]

# ---------- Phase 4：授权/预览复制到 Docs 留证 ----------
LIC_KEEP = ("license", "readme", "preview")  # 小写包含匹配


def mb(n):
    return f"{n / 1048576:.1f} MB"


def dirsize(p):
    return sum(os.path.getsize(os.path.join(dp, f))
               for dp, dn, fn in os.walk(p) for f in fn)


def move(src, dst, apply, log):
    """移动 src -> dst，并同步搬走 src + '.meta'。"""
    if not os.path.exists(src):
        log.append(f"   [跳过] 源不存在: {src}")
        return 0
    size = dirsize(src) if os.path.isdir(src) else os.path.getsize(src)
    kind = "目录" if os.path.isdir(src) else "文件"
    log.append(f"   {kind} {mb(size):>9}  {os.path.relpath(src, PROJ)}")
    log.append(f"                    ->  {os.path.relpath(dst, PROJ)}")
    if apply:
        try:
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            if os.path.exists(dst):
                log.append(f"   [警告] 目标已存在，跳过: {dst}")
                return 0
            shutil.move(src, dst)
            meta_src, meta_dst = src + ".meta", dst + ".meta"
            if os.path.exists(meta_src):
                shutil.move(meta_src, meta_dst)
        except Exception as exc:                                   # 文件被 Unity 占用等
            log.append(f"   [失败] {type(exc).__name__}: {exc}")
            log.append(f"          -> 重跑本脚本可继续处理剩余项")
            return 0
    return size


def main():
    apply = "--apply" in sys.argv
    log, total_out, total_pack = [], 0, 0

    log.append("=" * 78)
    log.append("Phase 1 · 移出 Assets（Unity 用不到 / 会报错）")
    log.append("=" * 78)
    for s, d in MOVE_OUT:
        total_out += move(os.path.join(SRC, s), os.path.join(RAW, d), apply, log)
    for s, d in FILE_OUT:
        total_out += move(os.path.join(SRC, s), os.path.join(RAW, d), apply, log)

    log.append("")
    log.append("=" * 78)
    log.append("Phase 1b · 原始压缩包移出 Assets（Unity 用不到）")
    log.append("=" * 78)
    for s, d in ZIP_OUT:
        total_out += move(os.path.join(SRC, s), os.path.join(RAW, d), apply, log)

    log.append("")
    log.append("=" * 78)
    log.append("Phase 2 · 字体 / 自有音频归入自有资源区")
    log.append("=" * 78)
    for s, d in FONT_MOVES:
        move(os.path.join(SRC, s), d, apply, log)
    for s, d in AUDIO_MOVES:
        move(os.path.join(SRC, s), d, apply, log)

    log.append("")
    log.append("=" * 78)
    log.append("Phase 3 · 整包归入 Assets/ThirdParty")
    log.append("=" * 78)
    for s, d in PACK_MOVES:
        total_pack += move(os.path.join(SRC, s), os.path.join(ASSETS, d), apply, log)

    log.append("")
    log.append("=" * 78)
    log.append("Phase 4 · 授权与预览复制到 Docs/素材授权（论文留证）")
    log.append("=" * 78)
    if apply:
        os.makedirs(DOCS_LIC, exist_ok=True)
    copied = 0
    for dp, dn, fn in os.walk(ASSETS, followlinks=False):
        if os.sep + "Ziyuan" in dp:
            continue
        for f in fn:
            low = f.lower()
            if f.endswith(".meta") or not any(k in low for k in LIC_KEEP):
                continue
            if not low.endswith((".txt", ".jpg", ".png", ".md")):
                continue
            rel = os.path.relpath(os.path.join(dp, f), ASSETS).replace(os.sep, "__")
            dst = os.path.join(DOCS_LIC, rel)
            log.append(f"   复制 {rel}")
            if apply:
                shutil.copy2(os.path.join(dp, f), dst)
            copied += 1

    log.append("")
    log.append("=" * 78)
    log.append("Phase 5 · 清理空壳 Ziyuan")
    log.append("=" * 78)
    leftover = []
    if os.path.isdir(SRC):
        for dp, dn, fn in os.walk(SRC):
            if fn:
                leftover.append(os.path.relpath(dp, PROJ))
    if leftover:
        log.append("   [注意] 仍有残留，不删除 Ziyuan：")
        for x in leftover[:20]:
            log.append(f"      {x}")
    else:
        log.append("   Ziyuan 已空，删除目录及其 .meta")
        if apply:
            shutil.rmtree(SRC, ignore_errors=True)
            mp = SRC + ".meta"
            if os.path.exists(mp):
                os.remove(mp)

    log.append("")
    log.append("=" * 78)
    log.append(f"汇总：移出 Assets {mb(total_out)} | 归入 ThirdParty {mb(total_pack)} | 复制授权 {copied} 个")
    log.append("模式：" + ("已实际执行" if apply else "空转（加 --apply 才真执行）"))
    log.append("=" * 78)

    out = "\n".join(log)
    print(out)
    with open(os.path.join(PROJ, ".workbuddy", "reorg_report.txt"), "w", encoding="utf-8") as fh:
        fh.write(out)


if __name__ == "__main__":
    main()
