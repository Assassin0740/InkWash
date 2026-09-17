using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace InkWash.DebugTools
{
    /// <summary>
    /// 动画 / 特效演示场（Showcase）—— **给人和 AI 都看得见的一站式检阅台**。
    ///
    /// ## 为什么要有它
    ///
    /// 之前每次要看"龙的攻击效果"，都要：写探针 `.cs` → `exec_cs --runtime` → 等落盘 →
    /// 读 Console → 拼出图。成本高、且**只有 AI 看得到**，用户看不到。
    /// 结果就是用户说"我看不了"。
    ///
    /// 这个组件把"看"这件事变成**运行时按一下就播**：一个 IMGUI 面板列出
    /// 所有角色 × 所有动作，点一下就在聚光灯下播给你看。不依赖任何探针与落盘。
    ///
    /// ## 设计要点
    ///
    /// - **单场景自包含**：本组件自己铺地面、打光、放相机，不依赖 `Main.unity` 里的任何东西。
    /// - **复用真资产**：演员从**真预制体**实例化，招式走**真代码路径**（龙的俯冲循环、
    ///   玩家的连招状态机），所以看到的就是游戏里会发生的。
    /// - **一次只演一个**：切换动作时把上一个演员销毁，台上永远只有一个主体，
    ///   构图稳定、可比较（对比不同怪的同一类动作时特别有用）。
    /// - **相机可切**：跟随 / 环绕 / 固定侧视。固定侧视用于"量动作"（看位移与高度），
    ///   环绕用于"看形体"。
    /// </summary>
    [DisallowMultipleComponent]
    public class ActionShowcase : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────
        //  配置
        // ────────────────────────────────────────────────────────────

        [Header("场地")]
        [Tooltip("舞台中心。演员都在这个点附近活动。")]
        public Vector3 stageCenter = new Vector3(0f, 0f, 0f);
        [Tooltip("地面尺寸（米）。")]
        public float groundSize = 60f;

        [Header("相机")]
        public float camDistance = 11f;
        public float camHeight = 4.2f;
        public float camLookHeight = 1.6f;

        [Header("行为")]
        [Tooltip("切换动作时是否自动把玩家摆到舞台中央。")]
        public bool recenterPlayer = true;
        [Tooltip("不播放任何动作时，是否让敌人保持常驻（用于观察待机/盘旋）。")]
        public bool keepIdleAlive = true;

        // ────────────────────────────────────────────────────────────
        //  运行时
        // ────────────────────────────────────────────────────────────

        private enum CamMode { Follow, Orbit, Side }
        private CamMode _camMode = CamMode.Follow;

        /// <summary>一个可演示的条目：谁 + 做什么。</summary>
        private class Item
        {
            public string group;        // 分组：主角 / 敌人 / 特效
            public string label;        // 显示名
            public GameObject prefab;   // 要实例化的预制体（主角为 null = 用场内已有的）
            public Action<GameObject, ActionShowcase> play;   // 开场怎么演（一次性）
            /// <summary>
            /// ★ 每帧续驱（可空）。
            ///
            /// **为什么需要它**：战斗动作不是"设一下就成的" —— 龙要经过
            /// 选招 → 预兆 → 俯冲 → 打击 → 拉起 五个相位，敌人要经过
            /// Idle→Chase→Attack 三次状态切换。只调一次 `ForceNextAttackForTest()`
            /// 的话，那一招要等冷却走完才被消费，采样窗口里**画面纹丝不动**
            /// （实测：盘旋/撕咬/吐息三张图完全一样）。
            /// 所以这里给一个每帧钩子，由驱动方决定"什么时候真的开打"。
            /// </summary>
            public Action<GameObject, float, ActionShowcase> tick;
            public bool isEffect;       // 特效条目（不召唤演员，直接在世界里放）
            public float _dur;          // 播放时长覆盖（<=0 用默认 6 s）
        }

        private readonly List<Item> _items = new List<Item>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private GameObject _player;
        private Component _playerController;
        private int _playingIndex = -1;
        private float _playTimer;
        private string _status = "未开始";

        /// <summary>当前条目的演员（可能是召唤物，也可能是玩家本人）。tick 要用。</summary>
        private GameObject _actor;
        /// <summary>本条目的开场时刻（`Time.time`），tick 靠它算已播多少秒。</summary>
        private float _playStart;

        // GUI
        private bool _showPanel = true;
        private Vector2 _scroll;
        private readonly Dictionary<string, bool> _foldout = new Dictionary<string, bool>();
        private Camera _cam;
        private float _orbitAngle;
        private bool _autoClose = true;
        private GameObject _camGoCache;

        // ────────────────────────────────────────────────────────────
        //  启动
        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            BuildStage();
            CollectItems();
            EnsurePlayer();
            Debug.Log(string.Format("[Showcase] 就位：{0} 个演示条目。左上面板按分组展开，点条目即播。",
                _items.Count));
        }

        private void Update()
        {
            UpdateCamera();
            HandleKeys();

            // ★ 每帧续驱当前条目：战斗动作靠时间演进，不是"设一下就成"
            if (_playingIndex >= 0 && _playingIndex < _items.Count)
            {
                var it = _items[_playingIndex];
                if (it.tick != null && _actor != null)
                {
                    try { it.tick(_actor, Time.time - _playStart, this); }
                    catch (Exception e) { Debug.LogError("[Showcase] tick 出错：" + e); }
                }
            }

            // 播放计时：到点自动收场，回到"空台"
            if (_playingIndex >= 0 && _autoClose)
            {
                _playTimer -= Time.unscaledDeltaTime;
                if (_playTimer <= 0f) StopCurrent();
            }
        }

        /// <summary>
        /// 相机取景。
        ///
        /// ★ 关键设计：焦点 = **玩家与演员的中点**（而不是单纯跟演员）。
        ///   因为大部分动作是"怪攻击玩家"，只有把两边都框进去，才能看出
        ///   攻击的方向、距离和判定范围 —— 只盯演员的话，会看不到它打的是谁。
        ///   没有演员时（主角自己演）就只盯玩家。
        ///
        /// ⚠️ 别用"固定偏移 + LookAt"，实测那个偏移是按绝对坐标拍的，
        ///    一旦焦点从场景中心换到玩家身上，画面就偏掉、演员直接出画。
        ///    这里改成：**按"要看的宽度"反算相机距离**，构图才稳定。
        /// </summary>
        private void UpdateCamera()
        {
            if (_cam == null) return;

            Vector3 pA = stageCenter + Vector3.up * camLookHeight;
            Vector3 pB = pA;
            bool hasA = false, hasB = false;

            if (_player != null) { pA = PlayerVisualPos(); hasA = true; }
            var actor = CurrentActor;
            if (actor != null && actor != _player)
            {
                // ★ 演员锚点取**包围盒中心**，不取 `_modelRoot` 的原点 ——
                //   龙的模型原点在肚皮下方，`_modelRoot` 一样；用原点会把画面
                //   整个压在"龙肚子底下"，飞起来之后龙直接出画（实测踩到）。
                pB = ActorBoundsCenter(actor);
                hasB = true;
            }

            Vector3 target;
            float span;
            if (hasA && hasB)
            {
                target = (pA + pB) * 0.5f;
                span = Vector3.Distance(pA, pB);
            }
            else if (hasB) { target = pB; span = 4f; }
            else if (hasA) { target = pA; span = 2.5f; }
            else { target = stageCenter + Vector3.up * camLookHeight; span = 5f; }

            // 目标距离：让"跨度 + 边距"刚好装进 FOV。
            // 上限放宽到 40 m —— 墨龙身长 6 m、还会飞到 5 m 高，26 m 装不下。
            float wantDist = _camDistOverride > 0f ? _camDistOverride : Mathf.Clamp(span * 0.85f + 7f, 7f, 40f);

            _focus = Vector3.Lerp(_focus, target, 3.5f * Time.unscaledDeltaTime);
            _dist = Mathf.Lerp(_dist, wantDist, 2.2f * Time.unscaledDeltaTime);

            // ★ 观察方向：**垂直于"玩家↔演员"连线**，而不是顺着连线看。
            //   踩过的坑：顺着连线看时，玩家正好把演员挡在身后（实测敌人 14 m 处
            //   与玩家在同一视线上 ⇒ 画面上只有玩家，看起来像"敌人根本没生成"）。
            //   垂直看 ⇒ 两边左右分开，攻防关系一眼可读。
            Vector3 axis = Vector3.forward;
            if (hasA && hasB)
            {
                Vector3 d = pB - pA; d.y = 0f;
                if (d.sqrMagnitude > 0.04f) axis = d.normalized;
            }
            Vector3 side = Vector3.Cross(Vector3.up, axis).normalized;   // 垂直方向

            // ★ 相机绕**焦点**摆位：用「水平距离 + 俯仰角」而不是「加一个竖直偏移」。
            //   加竖直偏移时，焦点越高画面越平（相机相对高度不够），
            //   龙在 4~6 m 高时就成了"从下往上看肚皮"。用俯仰角则恒为 16° 俯视。
            const float pitchDeg = 7f;
            float pitch = pitchDeg * Mathf.Deg2Rad;
            float horiz = _dist * Mathf.Cos(pitch);
            float vert = _dist * Mathf.Sin(pitch);

            Vector3 flat;
            switch (_camMode)
            {
                case CamMode.Follow:
                    flat = (side * 0.82f - axis * 0.28f).normalized;
                    break;
                case CamMode.Orbit:
                    _orbitAngle += 18f * Time.unscaledDeltaTime;
                    float r = _orbitAngle * Mathf.Deg2Rad;
                    flat = new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
                    break;
                default: // Side
                    flat = side;
                    break;
            }
            _cam.transform.position = _focus + flat * horiz + Vector3.up * vert;
            // 别让相机钻进地面
            var cp = _cam.transform.position;
            if (cp.y < stageCenter.y + 0.6f) cp.y = stageCenter.y + 0.6f;
            _cam.transform.position = cp;
            _cam.transform.LookAt(_focus);
        }

        private Vector3 _focus = new Vector3(0f, 1.6f, 0f);
        private float _dist = 10f;
        /// <summary>固定相机距离（0 = 按主体跨度自动算）。演示绕圈类条目时用。</summary>
        private float _camDistOverride;
        private Vector3 _followDir = new Vector3(-0.55f, 0f, -0.83f).normalized;

        private Vector3 PlayerVisualPos()
        {
            if (_player == null) return stageCenter + Vector3.up * camLookHeight;
            var a = ResolveVisualAnchor(_player);
            return a.position + Vector3.up * camLookHeight * 0.55f;
        }

        /// <summary>
        /// 当前活着的演员（没演员就 null）。
        ///
        /// ★ 只认**生成的演员**，不把 `_player` 算进来。
        ///   原因：主角条目是"让玩家本人做动作"，此时相机应该看着玩家；
        ///   而敌人条目是"召唤一个怪"，此时相机应该看着怪。
        ///   两件事的焦点不同，所以这里返回"有召唤物就用召唤物、否则用玩家"。
        /// </summary>
        private GameObject CurrentActor
        {
            get
            {
                for (int i = _spawned.Count - 1; i >= 0; i--)
                    if (_spawned[i] != null) return _spawned[i];
                // 没有召唤物：主角条目就用玩家当焦点
                if (_playingIndex >= 0 && _playingIndex < _items.Count && _items[_playingIndex].prefab == null)
                    return _player;
                return null;
            }
        }

        /// <summary>
        /// 找一个物体的"视觉锚点"。
        /// 对有 `_modelRoot` 的敌人取那个容器（骨架节点的世界位置会偏很远）；
        /// 否则取所有渲染器包围盒的中心。**不用 transform.position** ——
        /// 主角的 transform 在脚底，龙的在模型原点，各不一样。
        /// </summary>
        private static Transform ResolveVisualAnchor(GameObject go)
        {
            if (go == null) return null;
            // 敌人 / 龙：优先 _modelRoot
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var f = mb.GetType().GetField("_modelRoot", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    var t = f.GetValue(mb) as Transform;
                    if (t != null) return t;
                }
            }
            // 兜底：渲染器包围盒中心 —— 用个空物体挂上去太脏，直接返回 transform（脚底）
            // 但把 y 抬到包围盒中心高度，靠 LookAt 的 up 偏移补偿
            return go.transform;
        }

        /// <summary>
        /// 取演员的**渲染包围盒中心**（世界坐标）。
        ///
        /// ★ 这是取景最可靠的锚点：`transform.position` 对主角在脚底、对龙在肚皮下，
        ///   用它会算错"要抬多高"。包围盒中心直接把"这东西画在屏幕哪一段"答出来。
        ///   龙的包围盒会随后续骨骼动画每帧变（飞高/俯冲），所以每帧都要算 —— 正好
        ///   让相机跟着它上抬下降，构图一直把龙装在画面里。
        /// </summary>
        private static Vector3 ActorBoundsCenter(GameObject go)
        {
            if (go == null) return Vector3.zero;
            var rs = go.GetComponentsInChildren<Renderer>(true);
            bool has = false;
            Bounds b = new Bounds();
            foreach (var r in rs)
            {
                if (r == null || !r.enabled) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            if (has) return b.center;
            return ResolveVisualAnchor(go).position + Vector3.up * 1.2f;
        }

        /// <summary>键盘快捷键：Tab 显隐 / 空格下一个 / 1-3 切相机。</summary>
        private void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Tab)) _showPanel = !_showPanel;
            if (Input.GetKeyDown(KeyCode.Space)) NextItem();
            if (Input.GetKeyDown(KeyCode.Alpha1)) _camMode = CamMode.Follow;
            if (Input.GetKeyDown(KeyCode.Alpha2)) _camMode = CamMode.Orbit;
            if (Input.GetKeyDown(KeyCode.Alpha3)) _camMode = CamMode.Side;
        }

        // ────────────────────────────────────────────────────────────
        //  搭台
        // ────────────────────────────────────────────────────────────

        private void BuildStage()
        {
            stageCenter = transform.position;

            // 地面：**复用项目真正的白盒地面材质**，不要运行时 `Shader.Find` 拼一个 ——
            // 实测手拼的材质在宣纸后处理下会过曝成一片白（看不出踩地关系）。
            var ground = GameObject.Find("Showcase_Ground");
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Showcase_Ground";
            }
            ground.transform.position = stageCenter;
            ground.transform.localScale = Vector3.one * (groundSize / 10f);
            ground.transform.rotation = Quaternion.identity;
            var rend = ground.GetComponent<Renderer>();
            var groundMat = LoadAsset("Assets/_Project/Art/Materials/M_Whitebox_Ground.mat") as Material;
            if (groundMat != null) rend.sharedMaterial = groundMat;
            else Debug.LogWarning("[Showcase] 没找到 M_Whitebox_Ground.mat，地面将用默认材质");

            // 相机（场景里可能已有旧的，复用）
            var camGo = GameObject.Find("Showcase_Camera");
            if (camGo == null) camGo = new GameObject("Showcase_Camera");
            _cam = camGo.GetComponent<Camera>();
            if (_cam == null) _cam = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
            _cam.fieldOfView = 45f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 500f;
            _camGoCache = camGo;

            // 灯：角色是水墨 shader，主要靠环境光 + 主光。
            //
            // ★ 只有主光投影，且**阴影强度压低**。若多盏方向光都投影，
            //   地面会出现几片互相叠加的巨大黑楔形（实测很难看，还盖住演员）。
            var key = EnsureLight("Showcase_KeyLight", 52f, -38f, 0.95f);
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.42f;

            var fill = EnsureLight("Showcase_FillLight", 24f, 152f, 0.55f);
            fill.shadows = LightShadows.None;

            var rim = EnsureLight("Showcase_RimLight", 14f, 205f, 0.35f);
            rim.shadows = LightShadows.None;

            _focus = stageCenter + Vector3.up * camLookHeight;
        }

        private Light EnsureLight(string name, float pitch, float yaw, float intensity)
        {
            var go = GameObject.Find(name);
            if (go == null) go = new GameObject(name);
            var l = go.GetComponent<Light>();
            if (l == null) l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = intensity;
            l.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            return l;
        }

        /// <summary>编辑器下按路径取资产；打包后返回 null（演示场只在编辑器用）。</summary>
        private static UnityEngine.Object LoadAsset(string path)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
#else
            return null;
#endif
        }

        private void EnsurePlayer()
        {
            var t = FindType("InkWash.Combat.PlayerRef");
            if (t != null)
            {
                var fi = t.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                if (fi != null)
                {
                    var tr = fi.GetValue(null) as Transform;
                    if (tr != null) { _player = tr.root.gameObject; }
                }
            }
            if (_player != null && recenterPlayer)
            {
                // 玩家站在舞台一侧、面向演员（演员在中心）⇒ 攻防关系一眼可读
                _player.transform.position = stageCenter + new Vector3(0f, 0.05f, -5f);
                _player.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            }
            Debug.Log("[Showcase] 玩家 = " + (_player != null ? _player.name : "<未找到，主角动作将不可用>"));
        }

        // ────────────────────────────────────────────────────────────
        //  收集条目
        // ────────────────────────────────────────────────────────────

        private void CollectItems()
        {
            _items.Clear();

            // ── 主角：移动 + 连招 ──
            _items.Add(new Item { group = "主角 墨客", label = "待机 Idle", prefab = null, play = (g, s) => s.DrivePlayer("Idle") });
            _items.Add(new Item { group = "主角 墨客", label = "走 Walk", prefab = null, play = (g, s) => s.DrivePlayer("Walk"), });
            _items.Add(new Item { group = "主角 墨客", label = "跑 Run", prefab = null, play = (g, s) => s.DrivePlayer("Run") });
            _items.Add(new Item { group = "主角 墨客", label = "冲刺 Dash（挥墨拖尾）", prefab = null, play = (g, s) => s.DrivePlayer("Dash") });
            _items.Add(new Item { group = "主角 墨客", label = "连招一段 Atk1", prefab = null, play = (g, s) => s.DrivePlayer("Atk1") });
            _items.Add(new Item { group = "主角 墨客", label = "连招二段 Atk2", prefab = null, play = (g, s) => s.DrivePlayer("Atk2") });
            _items.Add(new Item { group = "主角 墨客", label = "连招三段 Atk3", prefab = null, play = (g, s) => s.DrivePlayer("Atk3") });
            _items.Add(new Item { group = "主角 墨客", label = "重击 HeavyAttack（K）", prefab = null, play = (g, s) => s.DrivePlayer("Heavy") });

            // ── 敌人：三个"真身" ──
            // 注意：墨偶/墨魇的 prefab 文件名【没有 Z_ 前缀】（Enemy_MoOu / Enemy_MoYan），
            // 而墨徒是 Z_Enemy_MoGuai。写错前缀会让 LoadEnemy 返回 null，
            // Play() 就会静默退回 actor = _player ⇒ 面板显示"已就位"其实在驱动主角。
            AddEnemy("敌人 墨徒（人形近战）", "Z_Enemy_MoGuai", "墨徒");
            AddEnemy("敌人 墨偶（远程）", "Enemy_MoOu", "墨偶");
            AddEnemy("敌人 墨魇（精英）", "Enemy_MoYan", "墨魇");
            AddEnemy("敌人 墨山（重型）", "Z_Enemy_MoShan", "墨山");
            AddEnemy("敌人 墨骨（不死兵）", "Z_Enemy_MoGu", "墨骨");

            // ── BOSS 墨龙：单独特判（它是程序驱动，不走 Animator）──
            _items.Add(new Item
            {
                group = "BOSS 墨龙",
                label = "盘旋 Circling（默认态）",
                prefab = LoadEnemy("Z_Enemy_MoLong"),
                play = (g, s) => s.DriveDragon(g, DragonPose.Circling),
                tick = (g, t, s) => s.TickDragon(g, t, s),
                _dur = 10f
            });
            _items.Add(new Item
            {
                group = "BOSS 墨龙",
                label = "俯冲撕咬 Dive→Bite",
                prefab = LoadEnemy("Z_Enemy_MoLong"),
                play = (g, s) => s.DriveDragon(g, DragonPose.Bite),
                tick = (g, t, s) => s.TickDragon(g, t, s),
                _dur = 10f
            });
            _items.Add(new Item
            {
                group = "BOSS 墨龙",
                label = "俯冲扫尾 Dive→Sweep",
                prefab = LoadEnemy("Z_Enemy_MoLong"),
                play = (g, s) => s.DriveDragon(g, DragonPose.Sweep),
                tick = (g, t, s) => s.TickDragon(g, t, s),
                _dur = 10f
            });
            _items.Add(new Item
            {
                group = "BOSS 墨龙",
                label = "吐息 Breath（悬浮放弹）",
                prefab = LoadEnemy("Z_Enemy_MoLong"),
                play = (g, s) => s.DriveDragon(g, DragonPose.Breath),
                tick = (g, t, s) => s.TickDragon(g, t, s),
                _dur = 10f
            });

            // ── 特效：直接在世界里放 ──
            _items.Add(new Item { group = "特效", label = "命中溅墨 InkHitVfx", isEffect = true, play = (g, s) => s.PlayHitVfx() });
            _items.Add(new Item { group = "特效", label = "落地墨晕 InkLandingBloom", isEffect = true, play = (g, s) => s.PlayBloom() });
        }

        private void AddEnemy(string group, string prefabName, string nickname)
        {
            var pf = LoadEnemy(prefabName);
            _items.Add(new Item
            {
                group = group, label = "待机", prefab = pf,
                play = (g, s) => s.SetupEnemy(g, EnemyDemo.Idle),
                tick = (g, t, s) => s.TickEnemy(g, t, s)
            });
            _items.Add(new Item
            {
                group = group, label = "追击玩家", prefab = pf,
                play = (g, s) => s.SetupEnemy(g, EnemyDemo.Chase),
                tick = (g, t, s) => s.TickEnemy(g, t, s),
                _dur = 10f
            });
            _items.Add(new Item
            {
                group = group, label = "发起攻击", prefab = pf,
                play = (g, s) => s.SetupEnemy(g, EnemyDemo.Attack),
                tick = (g, t, s) => s.TickEnemy(g, t, s),
                _dur = 12f
            });
        }

        private static GameObject LoadEnemy(string name)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Prefabs/Enemies/" + name + ".prefab");
#else
            return null;
#endif
        }

        // ────────────────────────────────────────────────────────────
        //  驱动实现
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// 驱动主角。
        ///
        /// ★ 走 `PlayerController` 的**输入注入接口**（`BeginInputOverride` /
        ///   `SetInjectedMove` / `RequestInjectedAttack` / `RequestInjectedHeavyAttack`），
        ///   而不是反射塞私有字段 —— 那是本项目为"自动化验收 / 演示录屏"专门留的正门，
        ///   走正门才能保证演示到的就是**玩家实际操作时的同一条代码路径**。
        /// </summary>
        private void DrivePlayer(string what)
        {
            if (_player == null) { _status = "主角不在场"; return; }
            _player.transform.position = stageCenter + new Vector3(0f, 0.05f, -3.5f);
            _player.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

            if (_playerController == null)
            {
                var t = FindType("InkWash.Player.PlayerController");
                if (t != null) _playerController = _player.GetComponentInChildren(t, true);
            }
            if (_playerController == null) { _status = "找不到 PlayerController"; return; }

            var ty = _playerController.GetType();
            InvokePlayer(ty, "EndInputOverride", null);          // 清掉上一条的残留
            InvokePlayer(ty, "ResetToLocomotion", null);
            InvokePlayer(ty, "BeginInputOverride", null);

            switch (what)
            {
                case "Idle":
                    InvokePlayer(ty, "SetInjectedMove", new object[] { Vector2.zero, false });
                    break;
                case "Walk":
                    // 慢走：给一个不足跑步阈值的方向输入（不给 runHeld = Shift 慢走）
                    InvokePlayer(ty, "SetInjectedMove", new object[] { new Vector2(0f, 0.35f), false });
                    break;
                case "Run":
                    InvokePlayer(ty, "SetInjectedMove", new object[] { new Vector2(0f, 1f), true });
                    break;
                case "Dash":
                    InvokePlayer(ty, "RequestInjectedDash", null);
                    break;
                case "Atk1":
                case "Atk2":
                case "Atk3":
                    InvokePlayer(ty, "RequestInjectedAttack", null);
                    break;
                case "Heavy":
                    InvokePlayer(ty, "RequestInjectedHeavyAttack", null);
                    break;
            }
            _status = "主角：" + what;
        }

        /// <summary>反射调用 PlayerController 的公开方法（正门 API，见 DrivePlayer 注释）。</summary>
        private void InvokePlayer(Type ty, string name, object[] args)
        {
            var m = ty.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);
            if (m == null) { Debug.LogWarning("[Showcase] PlayerController 没有方法 " + name); return; }
            m.Invoke(_playerController, args);
        }

        private enum DragonPose { Circling, Bite, Sweep, Breath }
        private enum EnemyDemo { Idle, Chase, Attack }

        /// <summary>
        /// 驱动墨龙。
        ///
        /// ★★ 这里是本组件最容易踩坑的地方，记录清楚：
        ///
        /// `ForceNextAttackForTest(name)` **不是"立刻开打"**，它只是给一个"下一次选招时用
        /// 这招"的标记，真正的消费点在 `ChooseAttack()` —— 而那次调用发生在
        /// `Enter(EnemyState.Attack)`，前提是龙处于 Chase 且**距离/冷却都满足**。
        /// 只调一次 ForceNextAttack 然后马上出图 ⇒ 采样窗口里龙一直在盘旋，
        /// 三招的出图完全一样（实测踩到）。
        ///
        /// 所以这里按**时间轴分阶段**驱动：
        ///  t∈[0, 0.8)   摆位 + 强制起飞（`ForceBeginHoverForTest`），让它离地
        ///  t=0.8         强制进入攻击态（`ForceEnterAttackForTest` 会走 ChooseAttack）
        ///                 —— 必须在 `ForceNextAttackForTest` **之后**调，标记才被消费
        ///  之后           什么都不做，让动作自己演完；`tick` 不再干预
        ///
        /// 另外把 `aerialLoop` 打开，否则演示时它会自己落地收招，看到一半突然站着。
        /// </summary>
        private void DriveDragon(GameObject dragon, DragonPose pose)
        {
            if (dragon == null) { _status = "龙预制体没加载到"; return; }
            var t = FindType("InkWash.Enemies.EnemyDragon");
            if (t == null) { _status = "找不到 EnemyDragon 类型"; return; }
            var comp = dragon.GetComponentInChildren(t, true);
            if (comp == null) { _status = "龙身上没有 EnemyDragon"; return; }

            _dragonComp = comp;
            _dragonType = t;
            _dragonPose = pose;
            _dragonStage = 0;

            // 强制空中循环：演示期间不落地收招
            var fLoop = t.GetField("aerialLoop");
            if (fLoop != null) fLoop.SetValue(comp, true);

            // ★ 高度**不要自己设**。盘旋高度由龙的 `_currentLift` 写进
            //   `_modelRoot.localPosition.y`（`UpdateBodyLift`），而 `transform.position.y`
            //   是**另一层**。两边都写会叠加 ⇒ 龙被顶到画面外（实测踩到，只剩半截）。
            //   这里只摆 XZ，Y 交给它自己的 `BeginHover()`。
            var v = dragon.transform.position;
            dragon.transform.position = new Vector3(stageCenter.x, stageCenter.y + 0.05f, stageCenter.z);

            _status = "墨龙：" + pose + "（起飞中…）";

            // ★ 立刻起飞，别等 tick —— 否则开场那 0.8 s 拍到的是"趴在地上"的龙。
            //   盘旋条目本来就在 tick 里调，但攻击条目 0.8 s 后才开招，
            //   中间这段必须已经在空中，不然俯冲看起来是"从地面爬起"。
            var pHover0 = t.GetMethod("ForceBeginHoverForTest", BindingFlags.Public | BindingFlags.Instance);
            if (pHover0 != null) { try { pHover0.Invoke(comp, null); } catch { } }
            if (pose == DragonPose.Circling) _dragonStage = 1;

            // ★ 演示友好参数（演练场 ≠ 实战：要"看得清"，不是"打得凶"）。
            //   半径 7 m：轨道直径 14 m，配 2.6 m 蛇行幅度刚好读得出"左右蜿蜒"；
            //   角速度 60°/s ⇒ 线速度 ≈ 7 × 1.047 ≈ 7.3 m/s，与 swimSpeed 8 匹配
            //   ⇒ 蛇行相位不会相对实际前进"打滑"；
            //   攻击冷却在 TickDragon 里每帧冻结 ⇒ 半径虽小于攻击距离也不会俯冲打断。
            SetFloatField("hoverOrbitRadius", 7f);
            SetFloatField("orbitAngularSpeedDeg", 60f);
            SetFloatField("swimSpeed", 8f);
            SetFloatField("orbitLateralAmp", 2.6f);
            _camDistOverride = 15f;
        }

        private Component _dragonComp;
        private Type _dragonType;
        private DragonPose _dragonPose;
        private int _dragonStage;

        /// <summary>龙的每帧续驱：分阶段把"盘旋 → 攻击"推过去。见 `DriveDragon` 注释。</summary>
        private void TickDragon(GameObject dragon, float elapsed, ActionShowcase s)
        {
            if (_dragonComp == null || _dragonType == null) return;

            // ★ 循环演示"盘旋"时每帧冻结攻击冷却：否则龙每隔 `circleCooldown*` 秒俯冲一次，
            //   而用户此刻要看的正是"蜿蜒前进"这个**常态**，不能被攻击打断。
            if (_dragonPose == DragonPose.Circling && _dragonStage >= 1)
            {
                SetPrivateFloat("_cooldownTimer", 999f);
                SetPrivateFloat("_circleCd", 999f);
            }

            if (_dragonStage == 0 && elapsed >= 0.8f)
            {
                _dragonStage = 1;

                // ① 先挂"下一招"标记
                string atk = _dragonPose == DragonPose.Bite ? "Bite"
                           : _dragonPose == DragonPose.Sweep ? "TailSweep"
                           : "Breath";
                if (_dragonPose != DragonPose.Circling)
                {
                    var pForce = _dragonType.GetMethod("ForceNextAttackForTest",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (pForce != null) pForce.Invoke(_dragonComp, new object[] { atk });
                }

                // ② 再推进攻击态 —— 顺序不能反，否则标记还在、这一招要等下一轮冷却
                if (_dragonPose != DragonPose.Circling)
                {
                    var pEnter = _dragonType.GetMethod("ForceEnterAttackForTest",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (pEnter != null) pEnter.Invoke(_dragonComp, null);
                }
                else
                {
                    var pHover = _dragonType.GetMethod("ForceBeginHoverForTest",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (pHover != null) pHover.Invoke(_dragonComp, null);
                }

                _status = "墨龙：" + _dragonPose + "（已开招）";
            }

            // 循环演示"盘旋"时，到点再来一次（保持长链游动一直在动）
            if (_dragonPose == DragonPose.Circling && elapsed > 6f && _dragonStage == 1)
            {
                _dragonStage = 2;
                var pHover = _dragonType.GetMethod("ForceBeginHoverForTest",
                    BindingFlags.Public | BindingFlags.Instance);
                if (pHover != null) pHover.Invoke(_dragonComp, null);
            }
        }

        /// <summary>反射写龙的 public 浮点字段（演示时改巡游参数用）。</summary>
        private void SetFloatField(string name, float v)
        {
            if (_dragonComp == null || _dragonType == null) return;
            var f = _dragonType.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) f.SetValue(_dragonComp, v);
        }

        /// <summary>反射写龙的私有浮点字段（演示时冻结攻击冷却用）。</summary>
        private void SetPrivateFloat(string name, float v)
        {
            if (_dragonComp == null || _dragonType == null) return;
            var f = _dragonType.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(_dragonComp, v);
        }

        // ── 敌人驱动 ──

        /// <summary>
        /// 敌人三种演示的统一驱动（待机 / 追击 / 攻击）。
        ///
        /// ★★ 两个坑：
        ///
        /// 1. **演示场没有 NavMesh** ⇒ `NavMeshAgent` 报
        ///    `Failed to create agent because there is no valid NavMesh`，
        ///    敌人拿不到路径就原地不动，"追击"演示看不到任何位移。
        ///    解法：进演示场时**把 agent 关掉**（`enabled = false`），
        ///    由这里**手动位移** —— 演示的是"动作与观感"，不是寻路。
        ///
        /// 2. **待机/追击原本是空壳**（只写了 `_status`），所以画面里敌人根本不在焦点上。
        ///    改成显式摆位：把敌人放到玩家**斜前方 6 m**，并把它的朝向对准玩家。
        /// </summary>
        private void SetupEnemy(GameObject e, EnemyDemo demo)
        {
            if (e == null) { _status = "敌人预制体没加载到"; return; }

            // 关掉 NavMeshAgent：演示场没烤导航网格，开着只会报错 + 站桩
            foreach (var ag in e.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
                if (ag != null) ag.enabled = false;

            // 摆位：玩家在 (0, 0, -3.5) 附近，敌人放到其**斜前方**
            Vector3 pp = _player != null ? _player.transform.position : (stageCenter + Vector3.back * 3.5f);
            Vector3 fwd = _player != null ? _player.transform.forward : Vector3.forward;
            fwd.y = 0f; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;

            Vector3 ep = pp + fwd * 6.5f + side * 2.5f;
            ep.y = stageCenter.y + 0.05f;
            e.transform.position = ep;
            e.transform.rotation = Quaternion.LookRotation((pp - ep).normalized, Vector3.up);

            _enemyComp = e;
            _enemyDemo = demo;
            _enemyStage = 0;
        }

        private GameObject _enemyComp;
        private EnemyDemo _enemyDemo;
        private int _enemyStage;

        /// <summary>敌人的每帧续驱。</summary>
        private void TickEnemy(GameObject e, float elapsed, ActionShowcase s)
        {
            if (e == null) return;

            // ① 先等它过完 Spawning 状态（基类的 spawnDelay）
            if (_enemyStage == 0 && elapsed >= 1.2f)
            {
                _enemyStage = 1;
                if (_enemyDemo == EnemyDemo.Attack) ForceEnemyAttack(e);
                else if (_enemyDemo == EnemyDemo.Chase) ForceEnemyChase(e);
                else _status = "敌人：待机（已就位）";
            }

            // ② "追击"演示：手动朝玩家平移，让人看得出它在追
            if (_enemyDemo == EnemyDemo.Chase && _enemyStage >= 1 && _player != null)
            {
                Vector3 to = _player.transform.position - e.transform.position;
                to.y = 0f;
                float d = to.magnitude;
                if (d > 2.6f)
                {
                    Vector3 step = to.normalized * 2.4f * Time.deltaTime;
                    e.transform.position += step;
                }
                else
                {
                    // 追到了就开始打
                    if (_enemyStage == 1) { _enemyStage = 2; ForceEnemyAttack(e); }
                }
                Vector3 face = _player.transform.position - e.transform.position; face.y = 0f;
                if (face.sqrMagnitude > 0.04f)
                    e.transform.rotation = Quaternion.Slerp(e.transform.rotation,
                        Quaternion.LookRotation(face.normalized, Vector3.up), 6f * Time.deltaTime);
                _status = "敌人：追击中（距玩家 " + d.ToString("0.0") + " m）";
            }

            // ③ "攻击"演示：一招演完（约 2.6 s）再来一招，循环
            if (_enemyDemo == EnemyDemo.Attack && _enemyStage >= 1)
            {
                _enemyAtkTimer -= Time.deltaTime;
                if (_enemyAtkTimer <= 0f) { ForceEnemyAttack(e); }
            }
        }

        private float _enemyAtkTimer;

        private void ForceEnemyAttack(GameObject e)
        {
            _enemyAtkTimer = 4.2f;
            foreach (var mb in e.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var m = mb.GetType().GetMethod("ForceEnterAttackForTest",
                    BindingFlags.Public | BindingFlags.Instance);
                if (m != null)
                {
                    try { m.Invoke(mb, null); }
                    catch (Exception ex) { Debug.LogWarning("[Showcase] 强制攻击失败：" + ex.Message); }
                    _status = "敌人：攻击";
                    return;
                }
            }
            _status = "敌人：攻击（组件不支持强制）";
        }

        private void ForceEnemyChase(GameObject e)
        {
            // 基类的 Chase 是 protected，且它会自己判定距离 —— 这里不硬推状态，
            // 直接靠 TickEnemy 里的手动位移演"追"，状态机留在 Idle 无妨。
            _status = "敌人：追击中";
        }

        private void PlayHitVfx()
        {
            var t = FindType("InkWash.Effects.InkHitVfx");
            if (t == null) { _status = "找不到 InkHitVfx"; return; }
            var m = t.GetMethod("Spawn", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
            if (m != null) m.Invoke(null, new object[] { stageCenter + Vector3.up * 1.1f, Vector3.up });
            _status = "特效：溅墨";
        }

        private void PlayBloom()
        {
            _status = "特效：落地墨晕（需要玩家落地触发）";
        }

        // ────────────────────────────────────────────────────────────
        //  播放控制
        // ────────────────────────────────────────────────────────────

        private void Play(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            StopCurrent();

            var item = _items[index];
            _playingIndex = index;
            _playTimer = item._dur > 0f ? item._dur : 6f;

            GameObject actor = null;
            if (item.prefab != null)
            {
                // ★ 演员生成在**舞台中心**（不是偏移点）—— 相机跟的是演员，
                //   生成点偏了会让开场那几秒主体在画面外。
                actor = Instantiate(item.prefab, stageCenter + Vector3.up * 0.05f, Quaternion.identity);
                actor.name = "Showcase_Actor_" + index;
                _spawned.Add(actor);

                // 面向玩家（没有玩家就面向相机），这样"攻击"是朝你的
                Vector3 look = (_player != null ? _player.transform.position : _cam.transform.position);
                look.y = actor.transform.position.y;
                if ((look - actor.transform.position).sqrMagnitude > 0.01f)
                    actor.transform.rotation = Quaternion.LookRotation(look - actor.transform.position, Vector3.up);

                // ★ 玩家挪到演员的**斜前方**，别正对 —— 正对时两者在同一视线上，
                //   从侧面看也会互相遮挡（实测就是这个原因让敌人"消失"）。
                if (_player != null && recenterPlayer)
                {
                    Vector3 toPlayer = (stageCenter - actor.transform.position);
                    toPlayer.y = 0f;
                    if (toPlayer.sqrMagnitude < 0.04f) toPlayer = Vector3.back;
                    toPlayer.Normalize();
                    Vector3 side = Vector3.Cross(Vector3.up, toPlayer).normalized;
                    // 前方 5 m + 侧向 3.2 m ⇒ 从侧面看两者明确分开
                    Vector3 pp = actor.transform.position + toPlayer * 5f + side * 3.2f;
                    pp.y = stageCenter.y + 0.05f;
                    _player.transform.position = pp;
                    _player.transform.rotation = Quaternion.LookRotation(
                        (actor.transform.position - pp).normalized, Vector3.up);
                }
            }
            else if (!item.isEffect)
            {
                actor = _player;
            }

            try { item.play(actor, this); }
            catch (Exception e) { _status = "播放出错：" + e.Message; Debug.LogError("[Showcase] " + e); }

            _actor = actor;
            _playStart = Time.time;
        }

        private void StopCurrent()
        {
            foreach (var g in _spawned) if (g != null) Destroy(g);
            _spawned.Clear();
            _playingIndex = -1;
            _actor = null;
            _status = "空台";

            // 收回玩家的输入注入，否则上一条（比如"跑"）的控制会残留到下一次
            if (_playerController != null)
            {
                var ty = _playerController.GetType();
                InvokePlayer(ty, "EndInputOverride", null);
                InvokePlayer(ty, "ResetToLocomotion", null);
            }
        }

        public void NextItem()
        {
            Play((_playingIndex + 1) % Mathf.Max(1, _items.Count));
        }

        /// <summary>
        /// 按序号播放（给**截图器 / 录屏脚本**用的公开入口）。
        /// 有了它，"自动跑一遍所有动作并出图"就不需要反射摸私有成员了。
        /// </summary>
        public void PlayForCapture(int index) { Play(index); StopAutoClose(); }

        /// <summary>截图/录屏期间关掉自动收场，免得每 6 s 清一次台打断录制。</summary>
        public void StopAutoClose() { _autoClose = false; }
        public void ResumeAutoClose() { _autoClose = true; }

        /// <summary>当前条目数（供外部枚举）。</summary>
        public int ItemCount => _items.Count;
        /// <summary>取某条目的"分组 / 名称"（供外部打印清单）。</summary>
        public string ItemLabel(int i) { return (i >= 0 && i < _items.Count) ? (_items[i].group + " / " + _items[i].label) : "-"; }

        // ────────────────────────────────────────────────────────────
        //  GUI
        // ────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_showPanel) return;
            float w = 300f;
            GUILayout.BeginArea(new Rect(8f, 8f, w, Screen.height - 16f), GUI.skin.box);

            GUILayout.Label("■ 动作演示场 (ActionShowcase)");
            GUILayout.Label("状态：" + _status);
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("跟随", _camMode == CamMode.Follow ? GUI.skin.button : GUI.skin.box)) _camMode = CamMode.Follow;
            if (GUILayout.Button("环绕", _camMode == CamMode.Orbit ? GUI.skin.button : GUI.skin.box)) _camMode = CamMode.Orbit;
            if (GUILayout.Button("侧视", _camMode == CamMode.Side ? GUI.skin.button : GUI.skin.box)) _camMode = CamMode.Side;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("下一个")) NextItem();
            if (GUILayout.Button("停")) StopCurrent();
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            _scroll = GUILayout.BeginScrollView(_scroll);

            string lastGroup = null;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.group != lastGroup)
                {
                    lastGroup = it.group;
                    GUILayout.Space(6f);
                    if (!_foldout.ContainsKey(it.group)) _foldout[it.group] = true;
                    _foldout[it.group] = GUILayout.Toggle(_foldout[it.group], "▸ " + it.group, GUI.skin.button);
                }
                if (!_foldout[lastGroup]) continue;

                string mark = (_playingIndex == i) ? "▶ " : "   ";
                if (GUILayout.Button(mark + it.label)) Play(i);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // 顶部提示
            GUI.Label(new Rect(Screen.width / 2f - 180f, 8f, 360f, 24f),
                "Tab 显/隐面板   空格 下一个    数字键 = 切相机");
        }

        private void OnDisable() { }

        // ────────────────────────────────────────────────────────────
        //  小工具
        // ────────────────────────────────────────────────────────────

        private static Type FindType(string full)
        {
            var t = Type.GetType(full + ", Assembly-CSharp");
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            { t = asm.GetType(full); if (t != null) return t; }
            return null;
        }
    }
}
