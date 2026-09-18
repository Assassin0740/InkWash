# -*- coding: utf-8 -*-
"""
第十九轮补丁：① 尾巴 = 身体 sin 波形的延续  ② 恢复巡游（hoverStationary=false）

一次性脚本，跑完即弃（不入库）。每一步都带 count(old) 断言 —— 见坑表「Edit 并行编辑会静默丢改动」。
"""
import io
import sys

ROOT = r"D:\Unity Project\InkWash"
CS = ROOT + r"\Assets\_Project\Scripts\Enemies\EnemyDragon.cs"
PREFAB = ROOT + r"\Assets\_Project\Prefabs\Enemies\Z_Enemy_MoLong.prefab"

sys.stdout.reconfigure(encoding="utf-8")


def patch(path, pairs):
    with io.open(path, "r", encoding="utf-8", newline="") as f:
        s = f.read()
    crlf = "\r\n" in s
    if crlf:
        # 模式串一律用 \n 写，这里按实际行尾展开（CRLF 守卫）
        pairs = [(o.replace("\n", "\r\n"), w.replace("\n", "\r\n"), e) for (o, w, e) in pairs]
    for idx, (old, new, expect) in enumerate(pairs):
        c = s.count(old)
        if c != expect:
            raise SystemExit("FAIL %s step %d: count(old)=%d, expect=%d\n--- old ---\n%s"
                             % (path, idx, c, expect, old[:400]))
        s = s.replace(old, new)
    with io.open(path, "w", encoding="utf-8", newline="") as f:
        f.write(s)
    print("OK   %s  (%d steps, eol=%s)" % (path, len(pairs), "CRLF" if crlf else "LF"))


# ── 1. 新增字段：延续模式开关 + 增益 ────────────────────────────────────────────
OLD_FIELDS = '        public bool tailLevel = true;\n' \
             '        [Tooltip("尾巴水平摆幅（m）：与身体行波同相位的横向波，尾根 0 → 尾尖最大。0 = 尾巴笔直")]\n' \
             '        public float tailWaveAmp = 0.35f;\n' \
             '        [Tooltip("尾巴上铺几个波（相对尾长）")]\n' \
             '        public float tailWaveSpan = 0.5f;\n' \
             '        [Tooltip("尾巴波相对身体行波的相位滞后（度）—— 让波看起来是**从身体流到尾巴上**")]\n' \
             '        public float tailPhaseLagDeg = 0f;'

NEW_FIELDS = '        public bool tailLevel = true;\n' \
             '        [Tooltip("★ 尾巴 = 身体 sin 波形的**延续**（第十九轮）：把身体曲线的参数往 u<0 外推当作尾链中心线 ⇒ " +\n' \
             '                 "波长 / 相位 / 传播方向 / 接点切向**按构造相等**，不需要任何额外约束。关掉则回退旧版独立行波。" +\n' \
             '                 "旧版实测两处对不上：① 空间相位符号相反 ⇒ 尾波传播方向与身体相反（两条波对着撞）；" +\n' \
             '                 "② 接点切向横向分量 π·bodyAmp 对 tailWaveAmp（≈3.61 vs 0.35，差 10 倍）⇒ 身体尾端在摆、尾根几乎不动")]\n' \
             '        public bool tailFollowBodyWave = true;\n' \
             '        [Tooltip("延续模式的幅度增益（1 = 与身体同幅）。> 1 会逼近折返上限 " +\n' \
             '                 "（判据 amp·gain·6.02/Σ骨长 < 1，本龙 Σ骨长 8.266 m）")]\n' \
             '        public float tailFollowGain = 0.9f;\n' \
             '        [Tooltip("（仅 tailFollowBodyWave = false 时生效）尾巴水平摆幅（m）：尾根 0 → 尾尖最大")]\n' \
             '        public float tailWaveAmp = 0.35f;\n' \
             '        [Tooltip("（仅 tailFollowBodyWave = false 时生效）尾巴上铺几个波（相对尾长）")]\n' \
             '        public float tailWaveSpan = 0.5f;\n' \
             '        [Tooltip("（仅 tailFollowBodyWave = false 时生效）尾巴波相对身体行波的相位滞后（度）")]\n' \
             '        public float tailPhaseLagDeg = 0f;'

# ── 2. ApplyTailLevel 主体 ─────────────────────────────────────────────────────
OLD_BODY = '''        /// ★ 必须**绝对写入**（每帧从基准重算）—— 叠乘会累积，见坑表条目 16。
        /// </summary>
        private void ApplyTailLevel(float phase)
        {
            int n = _tailChain.Count;
            if (!tailLevel || n < 2) return;
            if (_tailBaseRel.Count < n || _tailBaseSegLocal.Count < n || _tailSegLen.Count < n - 1) return;

            Vector3 origin = _tailChain[0] != null ? _tailChain[0].position : transform.position;
            // 与脊柱同源：都用**容器当前前向**（本方法在 ApplySerpentineSpine 里、LookRotation 之后调用）
            Vector3 fwd = Flat(transform.forward);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 back = -fwd;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            float L = Mathf.Max(1e-3f, _tailTotalLen);
            float ph = phase + tailPhaseLagDeg * Mathf.Deg2Rad;

            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            Quaternion invRoot = Quaternion.Inverse(rootRot);

            if (_tailTan == null || _tailTan.Length < n) _tailTan = new Vector3[n];
            Vector3[] tan = _tailTan;

            float acc = 0f;
            Vector3 prev = TailPoint(origin, back, right, L, ph, 0f);
            for (int i = 0; i + 1 < n; i++)
            {
                acc += _tailSegLen[i];
                float u = Mathf.Clamp01(acc / L);
                Vector3 p = TailPoint(origin, back, right, L, ph, u);
                Vector3 d = p - prev;
                tan[i] = d.sqrMagnitude > 1e-12f ? d.normalized : back;
                prev = p;
            }
            tan[n - 1] = tan[n - 2];'''

NEW_BODY = '''        /// ★ 必须**绝对写入**（每帧从基准重算）—— 叠乘会累积，见坑表条目 16。
        ///
        /// ── 第十九轮：尾巴改成「身体 sin 波形的**延续**」──
        ///
        /// ★ 为什么必须这样改（用户反馈「尾巴还是没有跟上 sin 的」）：旧版给尾巴铺的是
        ///   **第二条独立的波** `amp·u·sin(2π·span·u + ph)`，它和身体的波之间没有任何几何约束，
        ///   实测两处对不上：
        ///     ① **传播方向相反**。身体是 `sin(2πWu+φ)`，定义域 u∈[0,1]（u=0 尾根 / u=1 头），
        ///        相位随时间增 ⇒ 波峰位置 u_c=(π/2−φ)/2πW **随时间减小** ⇒ 波往 u=0 走 = **头→尾**。
        ///        尾巴的 u 是「尾根→尾尖」，同一形式下波峰也往 u=0 走 = **尾尖→尾根**
        ///        ⇒ 两条波**对着撞**，尾巴读起来是自己在那儿抖，不是身体的波流过去。
        ///     ② **接点切向不连续**。把两段曲线在接点处的一阶导写出来（δ→0）：
        ///          身体段方向 ∝ δ·[ +fwd·T + right·π·amp·gain·sin φ ]      （T = 身体总骨长）
        ///          尾巴段方向 ∝ δ·[ −fwd·L + right·tailWaveAmp·sin φ ]      （L = 尾长）
        ///        横向分量之比 = π·amp·gain : tailWaveAmp = **3.61 : 0.35**（差 10 倍）
        ///        ⇒ 身体尾端在摆、尾根几乎不动 ⇒ 视觉上就是「尾巴没跟上」。
        ///   ⇒ 正解不是"再调两个数"，而是让尾巴中心线**就是身体的同一条曲线**：把身体曲线的
        ///      参数往 u < 0 外推（尾巴本来就在身体末端之外，曲线参数就该是负的）。
        ///      这样波长 / 相位 / 传播方向 / 接点切向**全部按构造相等**，无需任何额外约束。
        ///
        /// ★ 外推时 `sin(πu)` 在 u<0 上取**负值** —— 这个负号正是接点切向能连续的原因
        ///   （相当于自动反相）。**绝不能取绝对值**：取了绝对值接点切向就反号，又变回折角。
        /// </summary>
        /// <param name="bodyAmp">脊柱曲线的横向振幅（m）。为 0 时自动落回旧版独立行波
        /// （攻击相位走 `ApplySpineOffsetsRaw`，那条路上身体不是 sin 曲线，没有可延续的对象）。</param>
        private void ApplyTailLevel(float phase, float bodyAmp = 0f, float bodyWaveCount = 1f,
                                    float bodyGain = 1f, float bodyTotal = 0f)
        {
            int n = _tailChain.Count;
            if (!tailLevel || n < 2) return;
            if (_tailBaseRel.Count < n || _tailBaseSegLocal.Count < n || _tailSegLen.Count < n - 1) return;

            bool follow = tailFollowBodyWave && bodyAmp > 1e-6f && bodyTotal > 1e-3f
                          && _spine.Count > 0 && _spine[0] != null;

            // 与脊柱同源：都用**容器当前前向**（本方法在 ApplySerpentineSpine 里、LookRotation 之后调用）
            Vector3 fwd = Flat(transform.forward);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 back = -fwd;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            // ★ 延续模式必须与脊柱**同源**：同一个原点（`_spine[0]`）、同一个轴向、同一个总骨长。
            //   用 `_tailChain[0].position` 当原点会差一个骨长量级的偏移。
            //   （本方法只取**方向**，所以顶点整体平移不影响结果，但同源更不容易出错。）
            Vector3 origin = follow
                ? _spine[0].position
                : (_tailChain[0] != null ? _tailChain[0].position : transform.position);

            float L = Mathf.Max(1e-3f, _tailTotalLen);
            float ph = phase + tailPhaseLagDeg * Mathf.Deg2Rad;

            float amp = follow ? tailFollowGain * bodyAmp : tailWaveAmp;
            float span = follow ? bodyWaveCount : tailWaveSpan;
            float gain = follow ? bodyGain : 1f;
            float axial = follow ? bodyTotal : L;
            Vector3 axialDir = follow ? fwd : back;
            float uScale = follow ? -(L / bodyTotal) : 1f;   // 尾链 u → 曲线参数 u（负 = 外推）

            Quaternion rootRot = _modelRoot != null ? _modelRoot.rotation : transform.rotation;
            Quaternion invRoot = Quaternion.Inverse(rootRot);

            if (_tailTan == null || _tailTan.Length < n) _tailTan = new Vector3[n];
            Vector3[] tan = _tailTan;

            float acc = 0f;
            Vector3 prev = TailPoint(origin, axialDir, right, axial, amp, span, gain, ph, 0f, follow);
            for (int i = 0; i + 1 < n; i++)
            {
                acc += _tailSegLen[i];
                float u = Mathf.Clamp01(acc / L);
                Vector3 p = TailPoint(origin, axialDir, right, axial, amp, span, gain, ph, u * uScale, follow);
                Vector3 d = p - prev;
                tan[i] = d.sqrMagnitude > 1e-12f ? d.normalized : back;
                prev = p;
            }
            tan[n - 1] = tan[n - 2];'''

# ── 3. TailPoint：改成带参数 + 两种模式 ───────────────────────────────────────
OLD_TAILPOINT = '''        /// <summary>尾巴中心线上参数 u 处的点（世界空间）。u 在 [0,1] 上对应「尾根→尾尖」。</summary>
        private Vector3 TailPoint(Vector3 origin, Vector3 back, Vector3 right, float L, float phase, float u)
        {
            float lat = tailWaveAmp * u * Mathf.Sin(2f * Mathf.PI * tailWaveSpan * u + phase);
            return origin + back * (u * L) + right * lat;
        }'''

NEW_TAILPOINT = '''        /// <summary>
        /// 尾巴中心线上参数 u 处的点（世界空间；u 在 [0,1] 上对应「尾根→尾尖」）。
        ///
        /// `follow = true`：**身体 sin 曲线往 u&lt;0 的外推** —— 与 `SerpPoint` 同一个公式，
        ///   只去掉 `u` 的 clamp（尾巴的曲线参数本来就是负的）。
        /// `follow = false`：旧版独立行波，空间项取**负**号（波峰向尾尖走，与身体同向）。
        /// </summary>
        private static Vector3 TailPoint(Vector3 origin, Vector3 axial, Vector3 right,
                                        float axialLen, float amp, float waveCount, float gain,
                                        float phase, float u, bool follow)
        {
            if (follow)
            {
                // u < 0 ⇒ sin(πu) < 0。这个负号是接点切向连续的关键，不能取绝对值。
                float env = Mathf.Sin(Mathf.PI * u) * gain;
                float latF = amp * env * Mathf.Sin(2f * Mathf.PI * waveCount * u + phase);
                return origin + axial * (u * axialLen) + right * latF;
            }
            float lat = amp * u * Mathf.Sin(-2f * Mathf.PI * waveCount * u + phase);
            return origin + axial * (u * axialLen) + right * lat;
        }'''

# ── 4. 巡游调用点：把脊柱曲线的参数传下去 ───────────────────────────────────────
OLD_CALL = '''            ApplyTailLevel(phase);
            ApplyLimbMotion(phase);
        }

        /// <summary>头部对齐是否生效（三条角度全 0 或节数 ≤ 0 时视为关闭）。</summary>'''

NEW_CALL = '''            // ★ 把脊柱曲线的**同一组参数**交给尾巴 ⇒ 尾巴是这条曲线往 u<0 的延长，不是第二条波
            ApplyTailLevel(phase, amp, W, tg, total);
            ApplyLimbMotion(phase);
        }

        /// <summary>头部对齐是否生效（三条角度全 0 或节数 ≤ 0 时视为关闭）。</summary>'''

# ── 5. 恢复巡游 ───────────────────────────────────────────────────────────────
OLD_STATIONARY = '        public bool hoverStationary = true;'
NEW_STATIONARY = '        public bool hoverStationary = false;'

patch(CS, [
    (OLD_FIELDS, NEW_FIELDS, 1),
    (OLD_BODY, NEW_BODY, 1),
    (OLD_TAILPOINT, NEW_TAILPOINT, 1),
    (OLD_CALL, NEW_CALL, 1),
    (OLD_STATIONARY, NEW_STATIONARY, 1),
])

# ── 6. prefab：hoverStationary 1 → 0（与源码默认值双向对齐，见坑表十三）──────────
patch(PREFAB, [
    ('  hoverStationary: 1\n', '  hoverStationary: 0\n', 1),
])

print("\n--- 复核 ---")
with io.open(CS, "r", encoding="utf-8") as f:
    t = f.read()
for probe, want in [("tailFollowBodyWave", 2), ("tailFollowGain", 1),
                    ("ApplyTailLevel(phase, amp, W, tg, total);", 1),
                    ("private void ApplyTailLevel(float phase, float bodyAmp", 1),
                    ("public bool hoverStationary = false;", 1),
                    ("TailPoint(origin, axialDir, right, axial, amp, span, gain, ph, 0f, follow)", 1)]:
    print("  %-70s %d (期望 %d) %s" % (probe[:68], t.count(probe), want,
                                       "OK" if t.count(probe) == want else "!!"))
print("  tooltip 里残留的转义换行 (\\\\n) 个数：%d" % t.count('\\n'))
