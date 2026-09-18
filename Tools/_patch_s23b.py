# 一次性补丁：烟「刚可见」那一帧，强制指定槽位的烟中电弧立刻重生
# 每个替换都带 count(old) 断言 —— 同文件并行 Edit 会静默丢改动（第二十三轮踩过）。
import io

P = r"D:/Unity Project/InkWash/Assets/_Project/Scripts/Effects/DragonStormVfx.cs"
s = io.open(P, encoding="utf-8", newline="").read()
NL = "\r\n" if "\r\n" in s else "\n"
A = NL.join
print("换行符:", repr(NL), "长度:", len(s))

reps = [
    # ① 字段
    ("        private readonly Puff[] _puffs = new Puff[24];",
     A(["        private readonly Puff[] _puffs = new Puff[24];",
        "        /// <summary>判据 4 的补丁用：烟是否已经「可见且账本里有烟」，以及它的上升沿。</summary>",
        "        private bool _smokeArmedPrev;",
        "        private bool _forceSmokeRespawn;"])),

    # ② Update 里算出上升沿（必须在 UpdateArcs 之前）
    ("            _smokeLevel = Mathf.MoveTowards(_smokeLevel, _smokeTarget, 2.2f * dt);",
     A(["            _smokeLevel = Mathf.MoveTowards(_smokeLevel, _smokeTarget, 2.2f * dt);",
        "            // ★★ 判据 4 的补丁（第二十三轮实测）：`wantSmoke` 是**槽位重生那一刻**掷的骰子，",
        "            //   而烟中电弧只挂在 `slot % 3 == 2` 这几个槽上 ⇒ 若它们在烟起来之前刚重生过，",
        "            //   就得等自己 0.12~0.30 s 的寿命走完才轮得到重掷 ⇒ 俯冲起手那 ~0.3 s 里",
        "            //   可能**一条烟中电弧都没有**。而判据 4 量的是「这一帧有没有一条电弧在烟里」，",
        "            //   于是整段占比被起手几帧拖到 60~75%（实测双峰：距离中位 0.05 m 却只有 63% 达标）。",
        "            //   这里在烟的**上升沿**把指定槽位标记为待重生，`UpdateArcs` 里立刻执行。",
        "            bool smokeArmed = _smokeLevel > 0.08f && LivePuffCount > 0;",
        "            _forceSmokeRespawn = smokeArmed && !_smokeArmedPrev;",
        "            _smokeArmedPrev = smokeArmed;"])),

    # ③ 寿命判定带上"待重生"
    ("                else if (Time.time - a.born > a.life)",
     A(["                // `_forceSmokeRespawn`：烟的上升沿那一帧，把指定的烟中电弧槽位立刻换锚点",
        "                else if (Time.time - a.born > a.life || (_forceSmokeRespawn && i % 3 == 2))"])),

    # ④ 清标志
    (A(["            }",
        "        }",
        "",
        "        private Camera ViewCam()"]),
     A(["            }",
        "            _forceSmokeRespawn = false;   // 只在本帧生效，下一帧交回寿命判定",
        "        }",
        "",
        "        private Camera ViewCam()"])),
]

for old, new in reps:
    n = s.count(old)
    assert n == 1, "期望 1 处，实际 %d 处：%s" % (n, old[:70].replace(NL, "\\n"))
    s = s.replace(old, new)
    print("ok:", old[:56].replace(NL, "\\n"))

io.open(P, "w", encoding="utf-8", newline="").write(s)
print("已写入", P)
