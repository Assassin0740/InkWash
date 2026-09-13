using UnityEngine;
using QFramework;

namespace InkWash.Core
{
    /// <summary>
    /// 音频门面：把曲目名收成编译期常量，转发给 QFramework 的 AudioKit。
    ///
    /// 为什么还要写这一层（而不在业务代码里直接调 AudioKit）：
    ///   1. **消灭字符串魔法值**。曲目名写错在运行时才报，收成常量后编译期就能发现。
    ///   2. **收口第三方依赖**。全工程只有这个文件 using QFramework 的音频 API，
    ///      将来换音频后端（比如要做 3D 定位音、或换 FMOD）只改这一个文件。
    ///   3. 它**不含任何播放逻辑** —— 播放、AudioSource 池、音量总线、静音开关、
    ///      PlayerPrefs 持久化全在 AudioKit 里。这里只有"名字表 + 几行转发"。
    ///
    /// 资源约定：音频必须放在某个 `Resources` 目录下，
    /// 因为本项目的加载器走 `Resources.Load&lt;AudioClip&gt;(名字)`。
    /// 本项目放在 `Assets/_Project/Audio/Resources/`。
    /// 注意：AudioKit 自带加载器确实是 Resources 加载器，但 QFramework 的 `SupportOldQF`
    /// 模块会在运行时把它换成 **ResKit** 加载器（那套要预先注册资源地址），导致
    /// 明明 `Resources.Load` 能取到、AudioKit 却报 `Failed to Create Res`。
    /// 处理办法**不是**改第三方源码，而是由 <see cref="AudioKitBootstrap"/> 显式换回来。
    ///
    /// 已知限制：AudioKit 是**纯 2D** 音频（底层的 AudioSourceProxy 是 internal，
    /// 拿不到 AudioSource，也无法设置 spatialBlend）。BGM / UI / 角色自身音效不受影响；
    /// 若将来要做"远处敌人的脚步声"这类定位音，需要另做一层 3D 音效池。
    ///
    /// 已知限制：AudioKit 默认的"准备片段"模式是**异步**的（PrepareClipByLoaderAsync），
    /// 首次播放某个片段会多等 1~2 帧。连击音效是 0.5s 级的短音，这点延迟听不出来；
    /// 若将来要严格对齐打击帧，需要预先预热（先静音播一次）或改用同步模式。
    /// </summary>
    public static class GameAudio
    {
        /// <summary>背景音乐。文件名刻意保留 Pixabay 原始下载名 —— 曲目 ID（247345）是授权溯源的关键线索。</summary>
        public static class Bgm
        {
            public const string ThemeMain = "BGM/et11lx-chinese-ancient-style-music-love-etlx-247345";
        }

        /// <summary>音效。文件名已改成语义名，来源映射见 Docs/素材来源与授权清单.md。</summary>
        public static class Sfx
        {
            /// <summary>第 1 段挥砍（轻快）← Kenney RPG-Audio/knifeSlice</summary>
            public const string SwingA = "SFX/Swing_A";

            /// <summary>第 2 段挥砍 ← Kenney RPG-Audio/knifeSlice2</summary>
            public const string SwingB = "SFX/Swing_B";

            /// <summary>第 3 段挥砍（重）← Kenney RPG-Audio/chop</summary>
            public const string SwingHeavy = "SFX/Swing_Heavy";

            /// <summary>冲刺 ← Kenney RPG-Audio/cloth1</summary>
            public const string Dash = "SFX/Dash";

            /// <summary>拔刀 ← Kenney RPG-Audio/drawKnife1</summary>
            public const string Draw = "SFX/Draw";

            /// <summary>刀砍在硬物上 ← Kenney Impact-Sounds/impactMetal_light_000（等有敌人再用）</summary>
            public const string HitBlade = "SFX/Hit_Blade";

            /// <summary>刀砍在软目标上 ← Kenney Impact-Sounds/impactSoft_medium_000（等有敌人再用）</summary>
            public const string HitFlesh = "SFX/Hit_Flesh";

            /// <summary>脚步 ← Kenney Impact-Sounds/footstep_concrete_000</summary>
            public const string Footstep = "SFX/Footstep";

            /// <summary>UI 点击 ← Kenney UI-Audio/click1</summary>
            public const string UiClick = "SFX/UI_Click";
        }

        // ------------------------------------------------------------------
        // 音乐
        // ------------------------------------------------------------------

        public static void PlayMusic(string clipName, bool loop = true, float volume = 1f)
        {
            if (string.IsNullOrEmpty(clipName)) return;
            if (ReadySettings() == null) return;      // 音频没就绪 → 安静跳过，绝不抛异常
            AudioKitBootstrap.EnsureResourcesLoader();
            AudioKit.PlayMusic(clipName, loop, null, null, Mathf.Clamp01(volume));
        }

        public static void StopMusic() { if (ReadySettings() == null) return; AudioKit.StopMusic(); }
        public static void PauseMusic() { if (ReadySettings() == null) return; AudioKit.PauseMusic(); }
        public static void ResumeMusic() { if (ReadySettings() == null) return; AudioKit.ResumeMusic(); }

        // ------------------------------------------------------------------
        // 音效
        // ------------------------------------------------------------------

        /// <summary>播一个音效。<paramref name="pitch"/> 用来给同一段连击做出音高变化，避免三段一模一样。</summary>
        public static void PlaySfx(string clipName, float volume = 1f, float pitch = 1f)
        {
            if (string.IsNullOrEmpty(clipName)) return;
            if (!SfxEnabled) return;
            AudioKitBootstrap.EnsureResourcesLoader();
            AudioKit.PlaySound(clipName, false, null, Mathf.Clamp01(volume), Mathf.Max(0.01f, pitch));
        }

        public static void StopAllSfx() { if (ReadySettings() == null) return; AudioKit.StopAllSound(); }

        /// <summary>
        /// 带随机音高的音效，避免连续同一段连击听起来像复读机。
        /// </summary>
        public static void PlaySfxVaried(string clipName, float volume = 1f, float pitchBase = 1f,
            float pitchJitter = 0.08f)
        {
            PlaySfx(clipName, volume, pitchBase + Random.Range(-pitchJitter, pitchJitter));
        }

        // ------------------------------------------------------------------
        // 音量 / 开关（AudioKit 用 PlayerPrefs 持久化，重启后仍生效）
        // ------------------------------------------------------------------

        public static float MusicVolume
        {
            get { var s = ReadySettings(); return s == null ? 0f : s.MusicVolume.Value; }
            set { var s = ReadySettings(); if (s != null) s.MusicVolume.Value = Mathf.Clamp01(value); }
        }

        public static float SfxVolume
        {
            get { var s = ReadySettings(); return s == null ? 0f : s.SoundVolume.Value; }
            set { var s = ReadySettings(); if (s != null) s.SoundVolume.Value = Mathf.Clamp01(value); }
        }

        public static bool MusicEnabled
        {
            get { var s = ReadySettings(); return s != null && s.IsMusicOn.Value; }
            set { var s = ReadySettings(); if (s != null) s.IsMusicOn.Value = value; }
        }

        /// <summary>
        /// 音效总开关。**注意**：拿不到设置时返回 false（静默降级），而不是抛异常 ——
        /// 见 <see cref="ReadySettings"/> 里的说明：这个 getter 会在
        /// `PlayerController.SwingStarted` 的多播里被调用，抛异常会把后面的
        /// 刀光订阅者一起打断。
        /// </summary>
        public static bool SfxEnabled
        {
            get { var s = ReadySettings(); return s != null && s.IsSoundOn.Value; }
            set { var s = ReadySettings(); if (s != null) s.IsSoundOn.Value = value; }
        }

        // ------------------------------------------------------------------
        // 设置模型的安全访问
        // ------------------------------------------------------------------

        /// <summary>
        /// 取一个**已初始化**的设置模型；拿不到就返回 null，调用方静默降级。
        ///
        /// 为什么需要这层保护（真实踩过的坑）：
        ///   `AudioKit` 的 `Architecture` 只在
        ///   `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 里初始化一次。
        ///   编辑器里 Play 会话如果跨了脚本重载，静态字段会被重建成
        ///   「对象在、但 `OnInit` 没跑」的半初始化状态 —— 实测
        ///   `AudioKit.Settings.IsSoundOn == null`。此时直接读 `.Value`
        ///   会抛 NullReferenceException，而它是在 `PlayerController.SwingStarted`
        ///   **多播**里抛的：C# 多播遇到异常就中断，排在后面的订阅者
        ///   （`SwordVfx` 的刀光 / 拖尾）**一次都收不到事件**，
        ///   现象就是"攻击完全没有刀光"，而日志里只有一条和刀光无关的音频报错。
        ///   音频是表现层，不该有权把玩法链路带崩，所以这里：
        ///     ① 检测到未初始化就补跑一次 `OnInit`（幂等，重复调用只会重建几个
        ///        PlayerPrefs 属性，不会重复注册监听）；
        ///     ② 补不上就返回 null，让音频安静地不播。
        /// </summary>
        private static AudioKitSettingsModel ReadySettings()
        {
            AudioKitSettingsModel s;
            try { s = AudioKit.Settings; }
            catch (System.Exception) { return null; }   // Architecture 未初始化时连取都可能炸

            if (s == null) return null;

            if (s.IsSoundOn == null)
            {
                // 半初始化：OnInit 没跑。ICanInit 是 AbstractModel 显式实现的，要转接口调。
                if (s is ICanInit init) init.Init();
                if (s.IsSoundOn == null) return null;
            }
            return s;
        }
    }
}
