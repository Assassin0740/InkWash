// 探针：当前运行期里 AudioKit 到底初始化了没有 + 场景里的刀光订阅者是否存在。
// 目的：定位"SwingStarted 多播被 AudioDirector 的 NRE 打断 → SwordVfx 收不到事件"这条链路。
var sb = new System.Text.StringBuilder();
sb.AppendLine("isPlaying = " + UnityEngine.Application.isPlaying);
sb.AppendLine("frameCount = " + UnityEngine.Time.frameCount);
sb.AppendLine("time = " + UnityEngine.Time.time.ToString("F2"));

try
{
    var s = QFramework.AudioKit.Settings;
    sb.AppendLine("AudioKit.Settings = " + (s == null ? "NULL" : s.GetType().Name));
    if (s != null)
    {
        sb.AppendLine("  IsSoundOn  = " + (s.IsSoundOn == null ? "NULL" : s.IsSoundOn.Value.ToString()));
        sb.AppendLine("  IsMusicOn  = " + (s.IsMusicOn == null ? "NULL" : s.IsMusicOn.Value.ToString()));
    }
    var pool = QFramework.AudioKit.Config;
    sb.AppendLine("AudioKit.Config = " + (pool == null ? "NULL" : pool.GetType().Name));
    if (pool != null)
        sb.AppendLine("  AudioLoaderPool = " + (pool.AudioLoaderPool == null ? "NULL" : pool.AudioLoaderPool.GetType().Name));
}
catch (System.Exception e)
{
    sb.AppendLine("AudioKit 访问异常: " + e.GetType().Name + " : " + e.Message);
}

var pgo = UnityEngine.GameObject.Find("Player");
sb.AppendLine("Player = " + (pgo == null ? "NULL" : "OK"));
if (pgo != null)
{
    var pctl = pgo.GetComponent<InkWash.Player.PlayerController>();
    sb.AppendLine("PlayerController = " + (pctl == null ? "NULL" : "OK"));
    var vfx = UnityEngine.Object.FindObjectOfType<InkWash.Effects.SwordVfx>();
    sb.AppendLine("SwordVfx = " + (vfx == null ? "NULL" : vfx.name));
    if (vfx != null)
    {
        sb.AppendLine("  HasBladeAnchor = " + vfx.HasBladeAnchor);
        sb.AppendLine("  SwingCount = " + vfx.SwingCount);
        sb.AppendLine("  player 字段 = " + (vfx.player == null ? "NULL" : vfx.player.name));
    }
    var ad = UnityEngine.Object.FindObjectOfType<InkWash.Core.AudioDirector>();
    sb.AppendLine("AudioDirector = " + (ad == null ? "NULL" : ad.name));
}
return sb.ToString();
