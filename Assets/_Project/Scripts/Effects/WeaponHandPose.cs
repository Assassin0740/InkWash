using System.Collections.Generic;
using UnityEngine;

namespace InkWash.Effects
{
    /// <summary>
    /// 徒手动画的「握拳补丁」。
    ///
    /// 背景：本项目用的动画包全是**徒手**动作（Quaternius UAL1/UAL2 的攻击/待机，
    /// Kevin Iglesias 只有 Idle/Walk/Run 三个片段），手一直是张开的「放松手」。
    /// 挂上真剑之后看上去像把剑"搁"在手心，而不是握住 —— 这是换真模型才暴露出来的问题。
    ///
    /// 做法：在 Animator 写完姿态**之后**（LateUpdate；执行顺序拉到很后面），
    /// 把持剑手的指骨绕**拳头的铰链轴**卷起来，捏成一个握拳。
    ///
    /// 两条关键约定：
    /// 1. **铰链轴 = 食指根 → 小指根**。这是四指屈曲时共用的转轴，也正是"拳头隧道"的轴
    ///    （真实握剑时剑柄就穿在这条轴上）。注意它**不等于**手指伸出的方向（手骨 +Y），
    ///    第一批接刀时就是错把剑身对齐到手指方向，看着像剑从指缝里长出来。
    /// 2. **局部轴先用"卷指前"的姿态算一次，再统一卷**。这样每节指骨是"绕固定局部轴"转，
    ///    等价于真实指关节的铰链；若边卷边取轴，得到的是各节各绕一个世界轴，指头会散开。
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

        [Tooltip("左手握拳（一般关闭 —— 左手空着）")]
        public bool leftHand = false;

        [Header("四指卷曲（近节 / 中节 / 末节，单位：度）")]
        public Vector3 fingerCurl = new Vector3(70f, 85f, 55f);

        [Header("拇指卷曲（近节 / 中节 / 末节，单位：度）")]
        public Vector3 thumbCurl = new Vector3(30f, 30f, 25f);

        [Header("手掌额外的掌侧偏移（米，正数=往掌心方向推）")]
        [Tooltip("拳心取四指根均值，落在掌背一侧；真正的隧道中心要往掌心推一点")]
        public float palmOffset = 0.015f;

        // ---- 只读探针（验收报告直接打印，别让测试去猜状态）----
        /// <summary>本帧实际卷了几根指骨。</summary>
        public int CurledBoneCount { get; private set; }
        /// <summary>是否处于"应该握拳"的状态。</summary>
        public bool Armed { get; private set; }

        private Animator _anim;
        private SwordVfx _vfx;
        private Transform _rightHand, _leftHand;
        private readonly List<Bone> _rightBones = new List<Bone>();
        private readonly List<Bone> _leftBones = new List<Bone>();
        private readonly List<Transform> _rightRoots = new List<Transform>();
        private readonly List<Transform> _leftRoots = new List<Transform>();
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
            if (!Armed) return;

            if (rightHand && _rightHand != null) Curl(_rightHand, _rightRoots, _rightBones);
            if (leftHand && _leftHand != null) Curl(_leftHand, _leftRoots, _leftBones);
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

            CollectChains(_rightHand, "R_", _rightRoots, _rightBones);
            CollectChains(_leftHand, "L_", _leftRoots, _leftBones);

            if (_rightHand != null && _rightRoots.Count < 4)
                Debug.LogWarning("[WeaponHandPose] 右手只找到 " + _rightRoots.Count + " 条指链，握拳会不完整");
        }

        private void CollectChains(Transform hand, string sideTag,
                                   List<Transform> roots, List<Bone> bones)
        {
            roots.Clear();
            bones.Clear();
            if (hand == null) return;

            var all = hand.GetComponentsInChildren<Transform>(true);
            foreach (var key in ChainKeys)
            {
                float[] ang = key == "Thumb"
                    ? new[] { thumbCurl.x, thumbCurl.y, thumbCurl.z }
                    : new[] { fingerCurl.x, fingerCurl.y, fingerCurl.z };

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
        private void Curl(Transform hand, List<Transform> roots, List<Bone> bones)
        {
            if (roots.Count < 2) return;

            Transform index = null, pinky = null, thumb = null;
            for (int i = 0; i < roots.Count; i++)
            {
                var n = roots[i].name;
                if (n.Contains("Thumb")) thumb = roots[i];
                else if (n.Contains("Index")) index = roots[i];
                else if (n.Contains("Pinky")) pinky = roots[i];
            }
            if (index == null || pinky == null) return;

            Vector3 fingerDir = hand.TransformDirection(Vector3.up).normalized;
            Vector3 spreadDir = (pinky.position - index.position).normalized;
            Vector3 palmN = Vector3.Cross(fingerDir, spreadDir).normalized;

            // 第一遍：全部用"未卷"的姿态取局部轴
            for (int i = 0; i < bones.Count; i++)
            {
                var b = bones[i];
                bool isThumb = b.T.name.Contains("Thumb");
                Vector3 worldAxis = isThumb && thumb != null ? palmN : spreadDir;
                b.LocalAxis = b.T.InverseTransformDirection(worldAxis);
                bones[i] = b;
            }

            // 第二遍：统一卷。左乘还是右乘都试过，这里用右乘（在骨骼自身坐标系里转）——
            // 实测近节 70° 卷出 68.7°，误差来自轴不是严格垂直，可接受。
            for (int i = 0; i < bones.Count; i++)
            {
                var b = bones[i];
                if (b.T == null || b.Degrees == 0f) continue;
                b.T.localRotation = b.T.localRotation * Quaternion.AngleAxis(b.Degrees, b.LocalAxis);
                CurledBoneCount++;
            }
        }
    }
}
