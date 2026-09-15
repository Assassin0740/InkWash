using UnityEngine;
using UnityEngine.Rendering;

namespace InkWash.Rendering
{
    /// <summary>
    /// 水墨场景的全局光照配置（环境光的单一所有权）。
    ///
    /// 为什么放在**运行时**而不是场景里：
    ///   ① `Main.unity` 是 **二进制**序列化（EditorSettings.m_SerializationMode = 2），
    ///      手改不可行，只能用编辑器 API。
    ///   ② 更重要的是：环境光是"画面取向"的一部分，应该和 shader 一起进版本控制、
    ///      能被 review、能被探针临时改掉做对照实验。埋在二进制场景里这三件事都做不到。
    ///   ③ 上一轮"去灰"就是把环境光推到了 sky 0.885 —— 因为它只存在于一次探针运行的
    ///      副作用里，没人能在 git 里看见它，于是它一路留到了"画面被压平"。
    ///
    /// ★ 本文件与 shader 里 `lit = lambert*0.80 + ambient*0.40` 是**一件事的两半**：
    ///   环境光权重降到 0.40 的前提是环境光本身也不该太亮。改一边必须同时看另一边。
    ///   「纸白由**受光**给，不由环境光平摊给」—— 环境光在这里只负责"让背光面
    ///   落在浓墨而不是焦墨"，不负责提供亮度。
    /// </summary>
    public static class InkLighting
    {
        /// <summary>环境光三档（Trilight）+ 强度。数值来源见 MEMORY/Docs 的水墨整改计划 §3.1。</summary>
        public struct Profile
        {
            public float sky;
            public float equator;
            public float ground;
            public float intensity;

            public static Profile Ink => new Profile
            {
                // 0.52 / 0.42 / 0.26：
                //   朝上的面（地面）拿 sky  ≈ 0.52 → 配合主光推到清墨/淡墨（承担留白）
                //   水平面（墙）  拿 equator ≈ 0.42 → 落在浓墨~重墨（承担中间调）
                //   朝下的面       拿 ground  ≈ 0.26 → 压到浓墨（最深，但不至于纯黑）
                sky = 0.52f,
                equator = 0.42f,
                ground = 0.26f,
                intensity = 1.0f,
            };
        }

        /// <summary>上一次 Apply 之前的设置，供对照实验还原。</summary>
        public struct Snapshot
        {
            public AmbientMode mode;
            public Color sky, equator, ground;
            public float intensity;
        }

        public static Profile Current = Profile.Ink;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Apply();
        }

        public static Snapshot Capture()
        {
            return new Snapshot
            {
                mode = RenderSettings.ambientMode,
                sky = RenderSettings.ambientSkyColor,
                equator = RenderSettings.ambientEquatorColor,
                ground = RenderSettings.ambientGroundColor,
                intensity = RenderSettings.ambientIntensity,
            };
        }

        public static void Restore(Snapshot s)
        {
            RenderSettings.ambientMode = s.mode;
            RenderSettings.ambientSkyColor = s.sky;
            RenderSettings.ambientEquatorColor = s.equator;
            RenderSettings.ambientGroundColor = s.ground;
            RenderSettings.ambientIntensity = s.intensity;
        }

        public static void Apply() { Apply(Current); }

        public static void Apply(Profile p)
        {
            Current = p;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(p.sky, p.sky * 1.005f, p.sky * 1.0f, 1f);
            RenderSettings.ambientEquatorColor = new Color(p.equator, p.equator * 1.005f, p.equator * 1.02f, 1f);
            RenderSettings.ambientGroundColor = new Color(p.ground, p.ground * 1.005f, p.ground * 1.04f, 1f);
            RenderSettings.ambientIntensity = p.intensity;
        }

        public static string Describe()
        {
            return "环境光 Trilight sky=" + F(RenderSettings.ambientSkyColor)
                 + " equator=" + F(RenderSettings.ambientEquatorColor)
                 + " ground=" + F(RenderSettings.ambientGroundColor)
                 + " intensity=" + RenderSettings.ambientIntensity.ToString("0.###");
        }

        static string F(Color c) { return c.r.ToString("0.###"); }
    }
}
