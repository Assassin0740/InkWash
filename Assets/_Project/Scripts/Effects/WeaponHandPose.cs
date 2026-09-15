using System.Collections.Generic;
using UnityEngine;

namespace InkWash.Effects
{
    /// <summary>
    /// 徒手动画的「握拳补丁」。
    ///
    /// 背景：本项目用的动画包全是**徒手**动作（Quaternius UAL1/UAL2、KayKit、Kevin Iglesias
    /// 的攻击/待机），手一直是张开的「放松手」。挂上真剑之后看上去像把剑"搁"在手心，
    /// 而不是握住 —— 这是换真模型才暴露出来的问题。
    ///
    /// 做法：在 Animator 写完姿态**之后**（LateUpdate；执行顺序拉到很后面），
    /// 把手的指骨绕**拳头的铰链轴**卷起来，捏成一个握拳。
    ///
    /// 三条关键约定：
    /// 1. **铰链轴 = 食指根 → 小指根**。这是四指屈曲时共用的转轴，也正是"拳头隧道"的轴
    ///    （真实握剑时剑柄就穿在这条轴上）。注意它**不等于**手指伸出的方向（手骨 +Y），
    ///    第一批接刀时就是错把剑身对齐到手指方向，看着像剑从指缝里长出来。
    /// 2. **局部轴先用"卷指前"的姿态算一次，再统一卷**。这样每节指骨是"绕固定局部轴"转，
    ///    等价于真实指关节的铰链；若边卷边取轴，得到的是各节各绕一个世界轴，指头会散开。
    /// 3. **左右手的符号是反的，必须在运行时实测**（见 <see cref="MeasureSign"/>），
    ///    而且是**每帧**实测 —— 一次性标定会在换片段后静默翻转成正号，把左手掰开。
    ///    `spreadDir = 小指根 − 食指根` 是个**镜像轴**：同一个正角度在右手是"屈"、
    ///    在左手是"伸"。实测左手 +70° 让「中指指尖到腕」从 0.124 m 涨到 0.214 m
    ///    （反向掰直），所以左手照抄右手的角度只会把手掰开。
    ///
    /// 不改动画资产、不动第三方源码；把本组件禁用即退回原始的张开手。
    /// </summary>
    [DefaultExecutionOrder(20000)]
    [DisallowMultipleComponent]
    public class WeaponHandPose : MonoBehaviour
    {
        [Header("开关")]
        [Tooltip("只在手上真的有武器时才握拳（会自动找同物体上的 SwordVfx）")]
        public bool onlyWhenArmed = true;

        [Tooltip("右手握拳（持剑手）")]
        public bool rightHand = true;

        [Tooltip("左手握拳（空手）。战斗时也建议打开 —— 徒手动画的攻击段左手是**五指摊开**的，\n" +
                 "看着像手废了；卷起来就有神。非战斗时本组件整体由 CombatStance 关掉，不影响垂手待机。")]
        public bool leftHand = true;

        [Header("四指卷曲（近节 / 中节 / 末节，单位：度）")]
        public Vector3 fingerCurl = new Vector3(70f, 85f, 55f);

        [Header("拇指卷曲（近节 / 中节 / 末节，单位：度）")]
        public Vector3 thumbCurl = new Vector3(30f, 30f, 25f);

        [Header("左手专用卷曲（空手，比持剑手松一点）")]
        [Tooltip("空手不必像握剑那么死。但**不能太松** —— UAL2 的攻击片段基础姿势是徒手张开的，\n" +
                 "补丁叠上去后握拳度会明显偏高（实测用 58/70/45 时峰值 1.83 > 阈值 1.7），\n" +
                 "所以这里取与右手相同。验收行「攻击全程两手都握着」的阈值为 1.7（摊开≈2.2 / 握拳≈1.2）。")]
        public Vector3 leftFingerCurl = new Vector3(70f, 85f, 55f);

        public Vector3 leftThumbCurl = new Vector3(30f, 30f, 25f);

        [Header("手掌额外的掌侧偏移（米，正数=往掌心方向推）")]
        [Tooltip("拳心取四指根均值，落在掌背一侧；真正的隧道中心要往掌心推一点。\n" +
                 "仅供离线求解挂点时的参考量，本组件的卷指不读它。")]
        public float palmOffset = 0.015f;

        // ---- 只读探针（验收报告直接打印，别让测试去猜状态）----
        /// <summary>本帧实际卷了几根指骨。</summary>
        public int CurledBoneCount { get; private set; }
        /// <summary>是否处于"应该握拳"的状态。</summary>
        public bool Armed { get; private set; }
        /// <summary>实测出来的左右手卷曲符号（+1 / −1）。验收时打出来，别猜。</summary>
        public float RightCurlSign { get; private set; }
        public float LeftCurlSign { get; private set; }

        /// <summary>
        /// 握拳程度（**尺度无关**）：中指指尖到腕的距离 ÷ 食指根到小指根的距离。
        /// 五指摊开约 2.1~2.4；握成拳约 1.1~1.4。没解析出来时 = −1。
        /// </summary>
        public float RightFistRatio { get; private set; } = -1f;
        public float LeftFistRatio { get; private set; } = -1f;

        /// <summary>MeasureSign 探针角度。太小测不出差别、太大可能越过"最卷"点，60° 实测够用。</summary>
        private const float ProbeAngle = 60f;

        /// <summary>握拳度超过这个值就认为"不是拳"（五指张开≈2.2），用于主动告警。</summary>
        private const float FistOpenWarn = 2.05f;

        private Animator _anim;
        private SwordVfx _vfx;
        /// <summary>进入 Armed 后累计了多少帧 —— 前几帧骨骼姿势还没稳，不做告警判定。</summary>
        private int _armedFrames;
        private bool _warnedRightOpen, _warnedLeftOpen;
        private Transform _rightHand, _leftHand;
        private readonly List<Bone> _rightBones = new List<Bone>();
        private readonly List<Bone> _leftBones = new List<Bone>();
        private readonly List<Transform> _rightRoots = new List<Transform>();
        private readonly List<Transform> _leftRoots = new List<Transform>();
        private Transform _rightMidTip, _leftMidTip;
        private float _rightSpread = 1f, _leftSpread = 1f;
        private bool _resolved;

        /// <summary>某根待卷的指骨。LocalAxis 在卷之前算好，之后整帧复用。</summary>
        private struct Bone
        {
            public Transform T;
            public float Degrees;
            public Vector3 LocalAxis;
        }

        private static readonly string[] ChainKeys = { "Thumb", "Index", "Mid", "Ring", "Pinky" };

        private void Awake()
        {
            _anim = GetComponentInChildren<Animator>();
            _vfx = GetComponent<SwordVfx>();
        }

        private void LateUpdate()
        {
            if (!_resolved) Resolve();

            Armed = !onlyWhenArmed || (_vfx != null && _vfx.HasWeapon);

            CurledBoneCount = 0;
            RightFistRatio = -1f;
            LeftFistRatio = -1f;
            if (!Armed) { _armedFrames = 0; _warnedRightOpen = false; _warnedLeftOpen = false; return; }

            // 卷曲符号**每帧当场实测**（不能一次性标定，见 MeasureSign 的注释）
            if (rightHand && _rightHand != null)
            {
                RightCurlSign = MeasureSign(_rightHand, _rightRoots, RightCurlSign);
                Curl(_rightHand, _rightRoots, _rightBones, RightCurlSign);
            }
            if (leftHand && _leftHand != null)
            {
                LeftCurlSign = MeasureSign(_leftHand, _leftRoots, LeftCurlSign);
                Curl(_leftHand, _leftRoots, _leftBones, LeftCurlSign);
            }

            RightFistRatio = FistRatio(_rightHand, _rightMidTip, _rightSpread);
            LeftFistRatio = FistRatio(_leftHand, _leftMidTip, _leftSpread);

            _armedFrames++;
            if (_armedFrames > 12) WarnIfFistOpen();   // 前几帧姿势还没稳，别急着喊
        }

        /// <summary>
        /// 拿右手/左手的指骨链条。解析放在第一帧做：Awake 时 Avatar 有时还没就绪，
        /// GetBoneTransform 会返回 null（踩过）。
        /// </summary>
        private void Resolve()
        {
            _resolved = true;
            if (_anim == null) _anim = GetComponentInChildren<Animator>();
            if (_anim == null) return;

            _rightHand = _anim.GetBoneTransform(HumanBodyBones.RightHand);
            _leftHand = _anim.GetBoneTransform(HumanBodyBones.LeftHand);

            CollectChains(_rightHand, "R_", fingerCurl, thumbCurl, _rightRoots, _rightBones);
            CollectChains(_leftHand, "L_", leftFingerCurl, leftThumbCurl, _leftRoots, _leftBones);

            // 左右手的铰链轴是镜像的 ⇒ 符号必须实测，不能照抄（见类注释第 3 条）。
            // 这里只留"未定"（0），真正的符号交给 MeasureSign 每帧实测。
            RightCurlSign = 0f;
            LeftCurlSign = 0f;

            _rightMidTip = FindMidTip(_rightRoots);
            _leftMidTip = FindMidTip(_leftRoots);
            _rightSpread = SpreadOf(_rightRoots, _rightHand);
            _leftSpread = SpreadOf(_leftRoots, _leftHand);

            if (_rightHand != null && _rightRoots.Count < 4)
                Debug.LogWarning("[WeaponHandPose] 右手只找到 " + _rightRoots.Count + " 条指链，握拳会不完整");
            if (_leftHand != null && _leftRoots.Count < 4)
                Debug.LogWarning("[WeaponHandPose] 左手只找到 " + _leftRoots.Count + " 条指链，握拳会不完整");
        }

        private void CollectChains(Transform hand, string sideTag, Vector3 fourCurl, Vector3 thCurl,
                                   List<Transform> roots, List<Bone> bones)
        {
            roots.Clear();
            bones.Clear();
            if (hand == null) return;

            var all = hand.GetComponentsInChildren<Transform>(true);
            foreach (var key in ChainKeys)
            {
                float[] ang = key == "Thumb"
                    ? new[] { thCurl.x, thCurl.y, thCurl.z }
                    : new[] { fourCurl.x, fourCurl.y, fourCurl.z };

                Transform root = null;
                foreach (var t in all)
                {
                    if (!t.name.Contains(sideTag) || !t.name.Contains(key)) continue;
                    // ⚠ CC_Base 骨架把脚趾也叫 Index/Mid/Ring/PinkyToe1，必须排除
                    if (t.name.Contains("Toe")) continue;
                    if (!t.IsChildOf(hand)) continue;
                    if (t.name.EndsWith("1")) { root = t; break; }
                }
                if (root == null) continue;

                roots.Add(root);
                Transform cur = root;
                for (int seg = 0; seg < 3; seg++)
                {
                    if (cur == null) break;
                    bones.Add(new Bone { T = cur, Degrees = ang[seg] });
                    cur = cur.childCount > 0 ? cur.GetChild(0) : null;
                }
            }
        }

        /// <summary>把一只手卷成拳。轴必须在卷之前一次性取好。</summary>
        private void Curl(Transform hand, List<Transform> roots, List<Bone> bones, float sign)
        {
            if (roots.Count < 2 || bones.Count == 0) return;

            if (!HandFrame(hand, roots, out Vector3 fingerDir, out Vector3 spreadDir, out Vector3 palmN))
                return;

            // 第一遍：全部用"未卷"的姿态取局部轴
            for (int i = 0; i < bones.Count; i++)
            {
                var b = bones[i];
                bool isThumb = b.T.name.Contains("Thumb");
                Vector3 worldAxis = isThumb ? palmN : spreadDir;
                b.LocalAxis = b.T.InverseTransformDirection(worldAxis);
                bones[i] = b;
            }

            // 第二遍：统一卷。左乘还是右乘都试过，这里用右乘（在骨骼自身坐标系里转）——
            // 实测近节卷出量与标称值同量级，误差来自轴不是严格垂直，可接受。
            for (int i = 0; i < bones.Count; i++)
            {
                var b = bones[i];
                if (b.T == null || b.Degrees == 0f) continue;
                b.T.localRotation = b.T.localRotation * Quaternion.AngleAxis(b.Degrees * sign, b.LocalAxis);
                CurledBoneCount++;
            }
        }

        /// <summary>
        /// 实测「绕 +spreadDir 转到底是屈还是伸」，返回**此刻**该用的符号（+1 / −1）。
        ///
        /// 为什么必须实测：`spreadDir = 小指根 − 食指根` 在左右手上是**镜像**的，
        /// 人手本身也是镜像的 —— 沿同一个正角度旋转，一侧屈、另一侧伸。
        /// 写死"左手取负"能过，但换骨架/换手性就又错；当场试一次最省事。
        ///
        /// ⚠ **必须每帧测，不能一次性标定**（踩过，代价很大）：
        /// 符号本身是骨架性质，但**测量手段**依赖当时的姿态 —— 判据是「卷一节之后中指指尖
        /// 离腕更近还是更远」，而某些姿态下 ±角度引起的变化量会小到接近 0
        /// （旧版一次性实现在这里 `return 1f` 兜底）。后果：
        /// 换一个持剑待机片段之后，左手被标定成 +1（反号），补丁把**手指掰开**，
        /// 握拳度从 1.46 涨到 2.60（比"五指张开≈2.2"还高），
        /// 而全流程**没有任何一处报错**，只在验收里冒出一条看不懂的未通过。
        ///
        /// 现在的做法：每帧拿食指链当探针，±<see cref="ProbeAngle"/>° 各拧一次，
        /// 取「指尖离腕更近」那个方向；两方向差别过小 = 这一帧判不出来 ⇒ 沿用上一帧；
        /// 换符号需要明显更优（迟滞），避免逐帧左右横跳把手指抖花。
        /// </summary>
        private float MeasureSign(Transform hand, List<Transform> roots, float prev)
        {
            float fallback = prev != 0f ? prev : 1f;
            if (hand == null || roots.Count < 2) return fallback;

            Transform index = null, pinky = null;
            for (int i = 0; i < roots.Count; i++)
            {
                var n = roots[i].name;
                if (n.Contains("Index")) index = roots[i];
                else if (n.Contains("Pinky")) pinky = roots[i];
            }
            if (index == null || pinky == null) return fallback;

            var chain = new List<Transform>();
            Transform cur = index;
            for (int i = 0; i < 3 && cur != null; i++) { chain.Add(cur); cur = cur.childCount > 0 ? cur.GetChild(0) : null; }
            if (chain.Count < 2) return fallback;

            Transform tip = chain[chain.Count - 1];
            if (tip.childCount > 0) tip = tip.GetChild(0);

            Vector3 spreadDir = (pinky.position - index.position).normalized;
            if (spreadDir.sqrMagnitude < 1e-8f) return fallback;

            // 轴必须一次性取好（卷了父骨之后子骨的局部轴会变）；探针**改完必须原样还原**
            var axes = new Vector3[chain.Count];
            for (int i = 0; i < chain.Count; i++) axes[i] = chain[i].InverseTransformDirection(spreadDir);
            var saved = new Quaternion[chain.Count];
            for (int i = 0; i < chain.Count; i++) saved[i] = chain[i].localRotation;

            for (int i = 0; i < chain.Count; i++) chain[i].localRotation = saved[i] * Quaternion.AngleAxis(ProbeAngle, axes[i]);
            float dPlus = (tip.position - hand.position).magnitude;

            for (int i = 0; i < chain.Count; i++) chain[i].localRotation = saved[i] * Quaternion.AngleAxis(-ProbeAngle, axes[i]);
            float dMinus = (tip.position - hand.position).magnitude;

            for (int i = 0; i < chain.Count; i++) chain[i].localRotation = saved[i];

            // 两个方向几乎没差别 ⇒ 这一帧判不出来（指尖正对着转轴等），沿用上一帧，别瞎翻
            if (Mathf.Abs(dPlus - dMinus) < 0.0005f) return fallback;

            float best = dPlus < dMinus ? 1f : -1f;
            if (prev != 0f && best != prev)
            {
                // 迟滞：另一个符号要**明显**更优（> 2 mm）才换，否则逐帧横跳会把手指抖花
                float prevD = prev > 0f ? dPlus : dMinus;
                float bestD = best > 0f ? dPlus : dMinus;
                if (prevD - bestD < 0.002f) return prev;
            }
            return best;
        }

        /// <summary>
        /// 握拳度明显高于「五指张开」基准 ⇒ 卷曲方向反了或指链没解析全。
        /// 这类失效**不会自己报错**（本轮就是靠验收里一条孤零零的"未通过"才暴露的），
        /// 所以主动喊一声。只在跨越阈值那一帧打，恢复正常后再打一条，不刷屏。
        /// </summary>
        private void WarnIfFistOpen()
        {
            CheckFistOpen("右手", RightFistRatio, ref _warnedRightOpen);
            CheckFistOpen("左手", LeftFistRatio, ref _warnedLeftOpen);
        }

        private void CheckFistOpen(string label, float ratio, ref bool warned)
        {
            if (ratio < 0f) return;
            if (!warned && ratio > FistOpenWarn)
            {
                warned = true;
                Debug.LogWarning("[WeaponHandPose] " + label + "卷曲方向可能反了：握拳度 "
                    + ratio.ToString("F2") + " > " + FistOpenWarn.ToString("F1")
                    + "（五指张开≈2.2 / 握拳≈1.2）。查 MeasureSign 实测符号与指链解析。");
            }
            else if (warned && ratio <= FistOpenWarn)
            {
                warned = false;
            }
        }

        /// <summary>手的参考系：手指方向、指根展开方向、掌法向（都在世界空间）。</summary>
        private bool HandFrame(Transform hand, List<Transform> roots,
                               out Vector3 fingerDir, out Vector3 spreadDir, out Vector3 palmN)
        {
            fingerDir = Vector3.up; spreadDir = Vector3.right; palmN = Vector3.forward;
            Transform index = null, pinky = null;
            for (int i = 0; i < roots.Count; i++)
            {
                var n = roots[i].name;
                if (n.Contains("Index")) index = roots[i];
                else if (n.Contains("Pinky")) pinky = roots[i];
            }
            if (index == null || pinky == null) return false;

            fingerDir = hand.TransformDirection(Vector3.up).normalized;
            spreadDir = (pinky.position - index.position).normalized;
            palmN = Vector3.Cross(fingerDir, spreadDir).normalized;
            return spreadDir.sqrMagnitude > 1e-8f;
        }

        private Transform FindMidTip(List<Transform> roots)
        {
            for (int i = 0; i < roots.Count; i++)
            {
                if (!roots[i].name.Contains("Mid")) continue;
                // 走到最后那一节指骨；它若有子节点（指尖标记）就再下走一层。
                // ⚠ 别写死走 3 步：CC_Base 的指链只有 3 节，第 3 步会走到 null。
                Transform cur = roots[i];
                for (int s = 0; s < 2 && cur != null; s++) cur = cur.childCount > 0 ? cur.GetChild(0) : null;
                if (cur == null) return null;
                return cur.childCount > 0 ? cur.GetChild(0) : cur;
            }
            return null;
        }

        private float SpreadOf(List<Transform> roots, Transform hand)
        {
            if (hand == null) return 1f;
            Transform index = null, pinky = null;
            for (int i = 0; i < roots.Count; i++)
            {
                var n = roots[i].name;
                if (n.Contains("Index")) index = roots[i];
                else if (n.Contains("Pinky")) pinky = roots[i];
            }
            if (index == null || pinky == null) return 1f;
            float d = (pinky.position - index.position).magnitude;
            return d > 1e-4f ? d : 1f;
        }

        private static float FistRatio(Transform hand, Transform tip, float spread)
        {
            if (hand == null || tip == null || spread <= 1e-4f) return -1f;
            return (tip.position - hand.position).magnitude / spread;
        }
    }
}
