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
    /// 3. **左右手的符号是反的，必须在运行时实测**（见 <see cref="CalibrateSign"/>）。
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
        [Tooltip("空手握拳不必像握剑那么死，略松更自然")]
        public Vector3 leftFingerCurl = new Vector3(58f, 70f, 45f);

        public Vector3 leftThumbCurl = new Vector3(25f, 25f, 20f);

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

        private Animator _anim;
        private SwordVfx _vfx;
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
            if (!Armed) return;

            if (rightHand && _rightHand != null) Curl(_rightHand, _rightRoots, _rightBones, RightCurlSign);
            if (leftHand && _leftHand != null) Curl(_leftHand, _leftRoots, _leftBones, LeftCurlSign);

            RightFistRatio = FistRatio(_rightHand, _rightMidTip, _rightSpread);
            LeftFistRatio = FistRatio(_leftHand, _leftMidTip, _leftSpread);
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

            // 左右手的铰链轴是镜像的 ⇒ 符号必须实测，不能照抄（见类注释第 3 条）
            RightCurlSign = CalibrateSign(_rightHand, _rightRoots);
            LeftCurlSign = CalibrateSign(_leftHand, _leftRoots);

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
        /// 实测「绕 +spreadDir 转到底是屈还是伸」，返回该用的符号（+1 / −1）。
        ///
        /// 为什么必须实测：`spreadDir = 小指根 − 食指根` 在左右手上是**镜像**的，
        /// 人手本身也是镜像的 —— 沿同一个正角度旋转，一侧屈、另一侧伸。
        /// 写死"左手取负"能过，但换骨架/换手性就又错；当场试一次最省事：
        /// 卷 3 节各 40°，若中指指尖**离腕更远**，说明掰反了，取反。
        /// </summary>
        private float CalibrateSign(Transform hand, List<Transform> roots)
        {
            if (hand == null || roots.Count < 2) return 1f;

            Transform index = null, pinky = null;
            for (int i = 0; i < roots.Count; i++)
            {
                var n = roots[i].name;
                if (n.Contains("Index")) index = roots[i];
                else if (n.Contains("Pinky")) pinky = roots[i];
            }
            if (index == null || pinky == null) return 1f;

            var chain = new List<Transform>();
            Transform cur = index;
            for (int i = 0; i < 3 && cur != null; i++) { chain.Add(cur); cur = cur.childCount > 0 ? cur.GetChild(0) : null; }
            if (chain.Count < 2) return 1f;

            Transform tip = chain[chain.Count - 1];
            if (tip.childCount > 0) tip = tip.GetChild(0);

            Vector3 spreadDir = (pinky.position - index.position).normalized;
            if (spreadDir.sqrMagnitude < 1e-8f) return 1f;

            float before = (tip.position - hand.position).magnitude;

            // 轴必须一次性取好（卷了父骨之后子骨的局部轴会变）
            var axes = new Vector3[chain.Count];
            for (int i = 0; i < chain.Count; i++) axes[i] = chain[i].InverseTransformDirection(spreadDir);
            var saved = new Quaternion[chain.Count];
            for (int i = 0; i < chain.Count; i++) saved[i] = chain[i].localRotation;

            for (int i = 0; i < chain.Count; i++)
                chain[i].localRotation = chain[i].localRotation * Quaternion.AngleAxis(40f, axes[i]);
            float after = (tip.position - hand.position).magnitude;

            for (int i = 0; i < chain.Count; i++) chain[i].localRotation = saved[i];

            if (Mathf.Abs(after - before) < 0.001f) return 1f;   // 判不出来（手已经卷到底）就当正向
            return after > before ? -1f : 1f;
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
