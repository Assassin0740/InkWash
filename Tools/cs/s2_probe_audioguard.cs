// 回归测试：人为把 AudioKit 设置模型打成"半初始化"（IsSoundOn = null），
// 验证 GameAudio 的守卫能自愈、且绝不再抛异常。
// 背景：这个 NRE 在多播里抛出会静默打断后面的刀光订阅者，是最难查的一类故障。
var sb = new System.Text.StringBuilder();
var model = QFramework.AudioKit.Settings;
sb.AppendLine("修复前状态: IsSoundOn = " + (model.IsSoundOn == null ? "null" : model.IsSoundOn.Value.ToString()));

var fld = model.GetType().GetField("<IsSoundOn>k__BackingField",
    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
if (fld == null) { sb.AppendLine("找不到 IsSoundOn 的 backing field，测试无法进行"); return sb.ToString(); }

fld.SetValue(model, null);
sb.AppendLine("已人为置空 IsSoundOn -> " + (model.IsSoundOn == null ? "null（模拟半初始化）" : "非null，置空失败"));

try
{
    bool e = InkWash.Core.GameAudio.SfxEnabled;
    sb.AppendLine("[OK] GameAudio.SfxEnabled 未抛异常，返回 " + e);
}
catch (System.Exception ex)
{
    sb.AppendLine("[FAIL] GameAudio.SfxEnabled 仍然抛异常: " + ex.GetType().Name + " " + ex.Message);
}

sb.AppendLine("自愈检查: IsSoundOn = " + (model.IsSoundOn == null ? "仍为 null（未自愈）" : "已重建 -> " + model.IsSoundOn.Value));

try { InkWash.Core.GameAudio.PlaySfx("SFX/Swing_A", 0f, 1f); sb.AppendLine("[OK] PlaySfx 未抛异常"); }
catch (System.Exception ex) { sb.AppendLine("[FAIL] PlaySfx 抛异常: " + ex.GetType().Name); }

try { InkWash.Core.GameAudio.StopAllSfx(); sb.AppendLine("[OK] StopAllSfx 未抛异常"); }
catch (System.Exception ex) { sb.AppendLine("[FAIL] StopAllSfx 抛异常: " + ex.GetType().Name); }

try
{
    InkWash.Core.GameAudio.MusicEnabled = true;
    InkWash.Core.GameAudio.SfxEnabled = true;
    InkWash.Core.GameAudio.SfxVolume = 1f;
    sb.AppendLine("[OK] 开关/音量写入未抛异常");
}
catch (System.Exception ex) { sb.AppendLine("[FAIL] 开关写入抛异常: " + ex.GetType().Name); }

return sb.ToString();
