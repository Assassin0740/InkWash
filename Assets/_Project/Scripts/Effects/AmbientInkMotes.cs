using UnityEngine;

namespace InkWash.Effects
{
    /// <summary>
    /// 环境氛围飘墨（第三十八轮 A5）。
    ///
    /// 为什么需要：画面是宣纸白 + 静态笔触，一动起来「空气」是死的 ——
    /// 水墨画的留白里其实有观者脑补的「气」。给空气塞一层极稀疏的淡墨微粒
    /// （墨点 + 一点点更亮的纸尘），镜头一动就能感到空间纵深。
    ///
    /// 为什么不用 prefab：全场景 0 个 ParticleSystem，Main.unity 二进制不能手改 ——
    /// 代码建，挂在与玩家同级的常驻物体上。
    ///
    /// 实现要点：模拟空间 = World，发射器每帧贴到玩家头顶 ——
    /// 已发射的粒子留在世界坐标不动（不会跟着镜头飘），只有「生成位置」跟着人走。
    /// </summary>
    public class AmbientInkMotes : MonoBehaviour
    {
        [Header("引用（空 = 自动找 PlayerHealth 的位置）")]
        public Transform player;

        [Header("密度")]
        public float spawnRate = 9f;          // 每秒墨点数（要稀，多了变沙尘暴）
        public float dustRate = 3f;           // 纸尘（更亮更小）

        [Header("范围")]
        public float radius = 14f;            // 发射盒半宽
        public float height = 7f;             // 发射盒高（玩家头顶起）

        private Transform _follow;
        private ParticleSystem _ink;
        private ParticleSystem _dust;

        private void Awake()
        {
            var go = new GameObject("[AmbientMotes]");
            go.transform.SetParent(transform, false);
            _ink = BuildLayer(go.transform, "Ink", spawnRate, 0.025f, 0.09f,
                new Color(0.10f, 0.11f, 0.13f, 0.34f), 5f, 10f);
            _dust = BuildLayer(go.transform, "Dust", dustRate, 0.012f, 0.035f,
                new Color(0.62f, 0.60f, 0.55f, 0.30f), 4f, 8f);
        }

        private void Update()
        {
            if (_follow == null)
            {
                var ph = player != null ? player : GameObject.FindWithTag("Player")?.transform;
                if (ph == null)
                {
                    var run = FindObjectOfType<InkWash.Roguelike.RunManager>();
                    if (run != null && run.playerHealth != null) ph = run.playerHealth.transform;
                }
                _follow = ph;
            }
            if (_follow != null)
            {
                var p = _follow.position;
                transform.position = new Vector3(p.x, p.y + 1.0f, p.z);
            }
        }

        private ParticleSystem BuildLayer(Transform parent, string name, float rate,
            float sizeMin, float sizeMax, Color color, float lifeMin, float lifeMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // 粒子留在世界
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.22f);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = color;
            main.gravityModifier = -0.015f;   // 极轻微上浮（墨在空气中"悬"）
            main.maxParticles = 220;
            main.loop = true;

            var em = ps.emission;
            em.rateOverTime = rate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(radius * 2f, height, radius * 2f);
            shape.position = new Vector3(0f, height * 0.5f, 0f);

            // 慢速旋转漂移
            var rot = ps.rotationOverLifetime;
            rot.z = new ParticleSystem.MinMaxCurve(-25f * Mathf.Deg2Rad, 25f * Mathf.Deg2Rad);

            // 淡入淡出（别闪现闪灭）
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                        new GradientAlphaKey(0.9f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            // 渲染：Sprites/Default 在 URP 下可渲染且支持顶点色/粒子色；
            // 贴图用程序化径向衰减圆点（没有软边贴图，whiteTexture 会是硬方块）
            var r = ps.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Sprites/Default");
            if (sh != null) r.material = new Material(sh) { mainTexture = InkDotTex };
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortingFudge = 10;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            return ps;
        }

        private static Texture2D _dot;
        private static Texture2D InkDotTex
        {
            get
            {
                if (_dot != null) return _dot;
                int n = 32;
                _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "InkMoteDot" };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;   // 0 中心 → 1 边
                        float a = Mathf.SmoothStep(1f, 0.2f, d);
                        _dot.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                _dot.Apply();
                return _dot;
            }
        }
    }
}
