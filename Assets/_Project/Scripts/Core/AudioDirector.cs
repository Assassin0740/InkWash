using UnityEngine;
using InkWash.Player;

namespace InkWash.Core
{
    /// <summary>
    /// 音频调度：把玩法事件接到 <see cref="GameAudio"/> 上。
    ///
    /// 为什么单独放一个组件、而不是写进 PlayerController：
    ///   角色控制器只负责"发生了什么"（挥砍了、冲刺了、命中了），
    ///   至于要不要响、响什么、多大声，是音频层的事。
    ///   两者用事件解耦，将来静音调试、换音源都不用碰玩法代码。
    ///
    /// 用法：挂在场景里任意常驻对象上（本项目挂在 Player 上），留空 player 会自动找。
    /// </summary>
    [DisallowMultipleComponent]
    public class AudioDirector : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("玩家控制器，留空自动找场景里的")]
        public PlayerController player;

        [Header("开场音乐")]
        public bool playMusicOnStart = true;

        [Tooltip("曲目名，见 GameAudio.Bgm 的常量")]
        public string musicClip = GameAudio.Bgm.ThemeMain;

        [Range(0f, 1f)] public float musicVolume = 0.45f;

        [Header("战斗音效")]
        public bool enableSwingSfx = true;

        [Range(0f, 1f)] public float swingVolume = 0.65f;

        [Range(0f, 1f)] public float dashVolume = 0.55f;

        private void Awake()
        {
            if (player == null) player = FindObjectOfType<PlayerController>();
        }

        private void OnEnable()
        {
            if (player == null) return;
            player.SwingStarted += OnSwingStarted;
            player.DashStarted += OnDashStarted;
        }

        private void OnDisable()
        {
            if (player == null) return;
            player.SwingStarted -= OnSwingStarted;
            player.DashStarted -= OnDashStarted;
        }

        private void Start()
        {
            if (playMusicOnStart) PlayMainTheme();
        }

        /// <summary>开局 / 回主菜单时调。</summary>
        public void PlayMainTheme()
        {
            GameAudio.MusicVolume = musicVolume;
            GameAudio.PlayMusic(musicClip, true, musicVolume);
        }

        private void OnSwingStarted(int step)
        {
            if (!enableSwingSfx) return;

            // 三段给不同音源 + 不同音高：第 1 段轻快、第 2 段略沉、第 3 段换重击音源并压低音高。
            switch (step)
            {
                case 1:
                    GameAudio.PlaySfxVaried(GameAudio.Sfx.SwingA, swingVolume, 1.06f);
                    break;
                case 2:
                    GameAudio.PlaySfxVaried(GameAudio.Sfx.SwingB, swingVolume, 0.98f);
                    break;
                default:
                    GameAudio.PlaySfxVaried(GameAudio.Sfx.SwingHeavy, swingVolume, 0.90f, 0.05f);
                    break;
            }
        }

        private void OnDashStarted()
        {
            GameAudio.PlaySfxVaried(GameAudio.Sfx.Dash, dashVolume, 1.0f, 0.10f);
        }

        // 说明：PlayerController.HitMoment 现在不接音效。
        // 场景里还没有敌人，命中时刻打击只发生在空气里，配打击音会"凭空出声"。
        // 等敌人做出来（S3）之后，在这里订阅 HitMoment 并根据是否真的打到目标，
        // 分别播 GameAudio.Sfx.HitBlade / HitFlesh。
    }
}
