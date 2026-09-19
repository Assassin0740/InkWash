using UnityEngine;

namespace InkWash.Audio
{
    /// <summary>
    /// 全局音频管理（第三十七轮）：BGM 循环 + 池化 SFX 播放。
    ///
    /// ★ 素材来源：Assets/_Project/Audio/Resources 下已备好一套 CC0 素材
    ///   （挥砍 Swing_A/B/Heavy、命中 Hit_Blade/Hit_Flesh、冲刺 Dash、
    ///   拔刀 Draw、脚步 Footstep、按钮 UI_Click、中国风 BGM）——
    ///   此前只放了没接线，本类是最后一块拼图。
    /// ★ 用 Resources.Load 而不是 Inspector 引用：调用点遍布
    ///   PlayerController/PlayerSwordHitbox/PlayerHitFeedback/RunPresentation，
    ///   静态入口 + 约定路径最省接线成本；素材在 Resources 目录下按名可寻。
    /// ★ 池化 6 个 AudioSource 轮转 PlayOneShot：挥砍/命中经常同帧连发，
    ///   单 AudioSource 的 PlayOneShot 会被截断，PlayClipAtPoint 会狂建物体。
    /// </summary>
    public static class AudioManager
    {
        private const string BgmPath = "BGM/et11lx-chinese-ancient-style-music-love-etlx-247345";

        private static GameObject _host;
        private static AudioSource _bgm;
        private static AudioSource[] _pool;
        private static int _poolIdx;
        private static System.Collections.Generic.Dictionary<string, AudioClip> _cache
            = new System.Collections.Generic.Dictionary<string, AudioClip>();

        /// <summary>确保宿主存在（懒创建；第一次 Play/BGM 时自动建）。</summary>
        private static void Ensure()
        {
            if (_host != null) return;
            _host = new GameObject("AudioManager");
            Object.DontDestroyOnLoad(_host);
            _pool = new AudioSource[6];
            for (int i = 0; i < _pool.Length; i++)
            {
                var src = _host.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;          // UI/动作音走 2D（第三人称镜头拉太远会忽大忽小）
                _pool[i] = src;
            }
            _bgm = _host.AddComponent<AudioSource>();
            _bgm.loop = true;
            _bgm.playOnAwake = false;
            _bgm.spatialBlend = 0f;
        }

        /// <summary>开始放 BGM（幂等；已在放则不动）。音量刻意压低，给音效让位。</summary>
        public static void PlayBgm()
        {
            Ensure();
            if (_bgm.isPlaying) return;
            var clip = Load(BgmPath);
            if (clip == null) return;
            _bgm.clip = clip;
            _bgm.volume = 0.28f;
            _bgm.Play();
        }

        /// <summary>播一个音效（PlayOneShot 同源可叠）。clip 缺失时静默跳过（表现层不炸玩法）。</summary>
        public static void Play(string name, float volume = 1f, float pitchMin = 0.94f, float pitchMax = 1.06f)
        {
            Ensure();
            var clip = Load("SFX/" + name);
            if (clip == null) return;
            var src = _pool[_poolIdx];
            _poolIdx = (_poolIdx + 1) % _pool.Length;
            src.pitch = Random.Range(pitchMin, pitchMax);   // 每次微调音高 —— 连击不像是同一块磁带在循环
            src.PlayOneShot(clip, volume);
        }

        private static AudioClip Load(string path)
        {
            if (_cache.TryGetValue(path, out var c)) return c;
            c = Resources.Load<AudioClip>(path);
            _cache[path] = c;                               // 缺失也缓存，避免每刀都去扫 Resources
            if (c == null) Debug.LogWarning("[AudioManager] 缺音效素材: " + path);
            return c;
        }
    }
}
