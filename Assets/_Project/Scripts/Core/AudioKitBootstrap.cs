using UnityEngine;
using QFramework;

namespace InkWash.Core
{
    /// <summary>
    /// AudioKit 的接线修补：把音频加载器固定为「Resources 加载器」。
    ///
    /// 为什么需要这个类（这是接入 QFramework 时踩到的真实坑）：
    ///   QFramework ToolKits 里的 `SupportOldQF` 模块带了一个
    ///   `AudioKitWithResKitInit`，它在 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`
    ///   里把 `AudioKit.Config.AudioLoaderPool` 换成了 `ResKitAudioLoaderPool`。
    ///   ResKit 是「资源地址 + AssetBundle」体系，需要预先注册资源地址才找得到东西；
    ///   本项目没有（也不打算）为 10 个音频去搭一套 ResKit 注册流程，
    ///   于是运行时每次都报：
    ///       Failed to Create Res. Not Find By ResSearchKeys:AssetName:sfx/dash
    ///   注意日志里的 `sfx/dash` 是**被转成小写**的 —— 那是 ResKit 的地址规范化，
    ///   而不是我们的常量写错了。同一份路径用 `Resources.Load` 是能取到的。
    ///
    /// 处理方式：
    ///   **不改第三方源码**（改了下次重装就丢），而是在我们自己的代码里显式把加载器改回来。
    ///   AudioKit 自带的 `DefaultAudioLoaderPool` 走的就是 `Resources.Load<AudioClip>(名字)`，
    ///   与 `GameAudio` 里常量给出的路径约定（相对 Resources 目录）完全一致。
    ///
    /// 何时该删掉这个类：
    ///   等真要做资源热更 / 打进 AssetBundle / 上 Addressables，就该反过来用 ResKit，
    ///   在架构启动时调 `ResKit.Init()` 并注册地址，然后把这里的替换去掉。
    /// </summary>
    public static class AudioKitBootstrap
    {
        private static bool _patched;

        /// <summary>
        /// 兜底：即便初始化相位的先后顺序有变，也在场景加载后补一次。
        /// 取 AfterSceneLoad 是为了**晚于** SupportOldQF 的 BeforeSceneLoad。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoPatch() { EnsureResourcesLoader(); }

        /// <summary>
        /// 关掉「域重载」时静态字段会跨 Play 会话残留，必须复位。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() { _patched = false; }

        /// <summary>
        /// 保证 AudioKit 用的是 Resources 加载器。幂等，可反复调用。
        /// 之所以让 GameAudio 每次播之前都调一次，是为了**不依赖初始化相位的先后**
        /// （相位顺序在 Unity 版本间变动过，靠它等于把正确性押在未定义行为上）。
        /// </summary>
        public static void EnsureResourcesLoader()
        {
            if (_patched) return;
            _patched = true;

            IAudioLoaderPool pool = AudioKit.Config.AudioLoaderPool;
            if (pool is DefaultAudioLoaderPool) return;

            Debug.Log("[InkWash] AudioKit 加载器被 " + pool.GetType().Name
                + " 占用（QFramework 的 SupportOldQF 自动换的），已换回 DefaultAudioLoaderPool（Resources.Load）。");
            AudioKit.Config.AudioLoaderPool = new DefaultAudioLoaderPool();
        }
    }
}
