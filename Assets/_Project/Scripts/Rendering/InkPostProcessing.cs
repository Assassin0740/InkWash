using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InkWash.Rendering
{
    /// <summary>
    /// 全局水墨后处理（第三十八轮 A3）。
    ///
    /// 为什么用代码建：Main.unity 是二进制序列化不能手改，Volume 与相机的
    /// postProcessing 开关只能运行时装配（幂等，Boot 随便调多少次都只建一份）。
    ///
    /// 为什么用它：水墨三件套（墨线/宣纸/墨晕）都是 RendererFeature 全屏 Pass，
    /// 但它们不负责「画面的整体收束」—— Bloom 让浓墨微微泛光（宣纸上积墨的反光感）、
    /// Vignette 把视线收进画面中心（宣纸边缘受潮压暗的既有设计在 InkPaper 里，
    /// 这里的 Vignette 只做极轻的补刀）、ColorAdjustments 微降饱和让主角的蓝袍
    /// 往墨色靠一点。都调得很淡 —— 后处理的职责是「衬」，不是「抢」。
    /// </summary>
    public static class InkPostProcessing
    {
        private const string VolumeName = "InkVolume";

        /// <summary>Boot 幂等：已建（同场景）就跳过。RunManager.Awake 调用。</summary>
        public static void Boot()
        {
            if (GameObject.Find(VolumeName) != null) return;

            // ---- 相机开后处理 ----
            var cam = Camera.main;
            if (cam != null)
            {
                var camData = cam.GetComponent<UniversalAdditionalCameraData>();
                if (camData != null) camData.renderPostProcessing = true;
            }

            // ---- 全局 Volume ----
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "InkPostProfile";

            // Bloom：threshold 高、强度极低 —— 只有浓墨（低亮度反相后的暗部高亮？不，
            // 本项目画面整体是宣纸白，亮部 Bloom 会把纸色推爆；所以 threshold 抬高到
            // 只有最亮的高光才泛一点，实际作用是给飞白留一点"纸面反光"的呼吸。
            var bloom = Create<Bloom>(profile);
            bloom.threshold.overrideState = true; bloom.threshold.value = 1.05f;
            bloom.intensity.overrideState = true; bloom.intensity.value = 0.12f;
            bloom.scatter.overrideState = true;   bloom.scatter.value = 0.55f;
            bloom.clamp.overrideState = true;     bloom.clamp.value = 1.5f;

            // 色彩：微降饱和（主角蓝袍往墨色收）+ 极轻对比
            // ★ 用户历史反馈「发灰」——饱和只敢轻扣，亮度靠 postExposure 找回
            var color = Create<ColorAdjustments>(profile);
            color.postExposure.overrideState = true; color.postExposure.value = 0.06f;
            color.saturation.overrideState = true;   color.saturation.value = -5f;
            color.contrast.overrideState = true;     color.contrast.value = 2f;

            // 暗角：把视线收进画面（InkPaper 自带 vignette 0.18，这里只补极轻的一刀）
            var vig = Create<Vignette>(profile);
            vig.intensity.overrideState = true;   vig.intensity.value = 0.10f;
            vig.smoothness.overrideState = true;  vig.smoothness.value = 0.35f;

            // 色调映射：Neutral 保色彩不偏移（ACES 会把宣纸白往黄绿拽）
            var tone = Create<Tonemapping>(profile);
            tone.mode.overrideState = true; tone.mode.value = TonemappingMode.Neutral;

            var go = new GameObject(VolumeName);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.weight = 1f;
            vol.sharedProfile = profile;
        }

        private static T Create<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var c = ScriptableObject.CreateInstance<T>();
            c.name = typeof(T).Name;
            c.active = true;
            profile.components.Add(c);
            return c;
        }
    }
}
