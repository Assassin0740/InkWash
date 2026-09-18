# -*- coding: utf-8 -*-
"""第二十六轮补丁 6：把「头簇相对挂点 vs 基准」从位置差(m) 改成
归一化方向差(deg) + 半径比 —— 修掉 rest 行读出 0.1935m 的标尺底噪。
所有替换先断言 count==1 再写，最后一次性写回。"""
import io, sys

P = "Tools/cs/drg_headfix.cs"
src = io.open(P, encoding="utf-8").read()
orig = src

REPL = []

# 1) 文档注释
REPL.append((
'''    /// 头簇**相对挂点**的几何 vs 纯净基准实例：返回最大位置偏差(m) 与最大转角(°)。''',
'''    /// 头簇**相对挂点**的几何 vs 纯净基准实例：返回最大**方向**偏差(°)、最大转角(°)、半径比。
    /// ★ 第二十六轮补：**方向**才是姿态判据，位置差不是 —— 见方法体内注释。'''))

# 2) 签名
REPL.append((
'''    void HeadShapeVsRest(out float maxPos, out float maxRot, out int n)''',
'''    void HeadShapeVsRest(out float maxDir, out float maxRot, out float rRatio)'''))

# 3) 初始化
REPL.append((
'''        maxPos = 0f; maxRot = 0f; n = 0;''',
'''        maxDir = 0f; maxRot = 0f; rRatio = 0f; int m = 0; float sum = 0f;'''))

# 4) 核心：位置差 -> 归一化方向差
REPL.append((
'''            float dp = (pa - pb).magnitude;''',
'''            // ★★ 第二十六轮补（尺子自检）：**必须归一化后再比**。
            //   直接取 |pa-pb| 会把两个实例的累计 scale 差算进来（FBX 厘米制，
            //   SMR.lossyScale ≈ 0.0024 量级）⇒ 实测该差值**恒为 0.1935 m**，
            //   连 rest 行自己都照读 0.1935 ⇒ 那是**标尺底噪**，不是姿态误差。
            //   而头簇是刚性、不驱动的分支 ⇒ 位置差在任何档位都不会变，
            //   这一列**天生不能分辨档位**。均匀缩放不改变「相对挂点」的方向，
            //   所以归一化后的方向差才是干净、可比的姿态判据。
            float la = pa.magnitude, lb = pb.magnitude;
            if (la > 1e-6f && lb > 1e-6f)
            {
                float dp = Vector3.Angle(pa, pb);
                if (dp > maxDir) maxDir = dp;
                sum += la / lb; m++;
            }'''))

# 5) 尾部收敛
REPL.append((
'''            if (dp > maxPos) maxPos = dp;
            if (dr > maxRot) maxRot = dr;
            n++;
        }
    }''',
'''            if (dr > maxRot) maxRot = dr;
        }
        rRatio = m > 0 ? sum / m : 0f;
    }'''))

# 6) 调用点声明
REPL.append((
'''        float hp, hr; int hn;
        HeadShapeVsRest(out hp, out hr, out hn);''',
'''        float hp, hr, hratio;
        HeadShapeVsRest(out hp, out hr, out hratio);'''))

# 7) 报表列
REPL.append((
'''            + " 头簇相对挂点 vs 基准 Δpos" + hp.ToString("F4") + "m Δrot" + hr.ToString("F3") + "°(" + hn + "叶)"''',
'''            + " 头簇相对挂点 Δdir" + hp.ToString("F2") + "° Δrot" + hr.ToString("F3") + "° 半径比" + hratio.ToString("F4")'''))

# 8) 读法
REPL.append((
'''        _sb.AppendLine("  · **头簇相对挂点 vs 基准**：逐叶比「相对挂点的位置与方向」（对应用户口述判据）。");
        _sb.AppendLine("    ★ 这一项与渲染/相机/背景全无关 —— 它才是「头到底像不像 FBX」最硬的判据。");''',
'''        _sb.AppendLine("  · **头簇相对挂点 Δdir / Δrot**：逐叶比「相对挂点的方向」与朝向（对应用户口述判据），与渲染/相机全无关。");
        _sb.AppendLine("    ★ Δdir 是**归一化方向角**：均匀缩放不改变方向 ⇒ 两个实例 scale 不同也能比。");
        _sb.AppendLine("    ★ 头簇是刚性不驱动分支 ⇒ Δdir / Δrot 在**每一行**都必须 =0（含 rest 行）；");
        _sb.AppendLine("      它证明的是「锁定没有把头的形状拧变形」，**不是**分辨档位的判据 —— 分辨力在「总偏差」列。");
        _sb.AppendLine("  · **半径比** = |叶骨-挂点| 活体 / 基准（均值）。第一版这里报的是「Δpos 0.1935m」，");
        _sb.AppendLine("    恒定出现在**包括 rest 在内**的每一行 ⇒ 那是跨实例标尺差（厘米制 scale），已改为比值单列。");'''))

worst = 0
for i, (o, n) in enumerate(REPL, 1):
    c = src.count(o)
    if c != 1:
        print("FAIL #%d count=%d" % (i, c))
        worst = i
        continue
    src = src.replace(o, n)
    print("ok   #%d" % i)

if worst:
    sys.exit(1)

assert src != orig
io.open(P, "w", encoding="utf-8", newline="\n").write(src)
print("WROTE %s  (%d -> %d bytes)" % (P, len(orig), len(src)))
