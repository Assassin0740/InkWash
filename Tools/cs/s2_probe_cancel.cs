// 运行期诊断：单次攻击后，逐帧记录 (状态, normalizedTime, 取消窗口, 段位, 阶段)。
// 只在关键量变化时打印，避免上百行噪声。
var player = GameObject.Find("Player");
if (player == null) return "[ERR] 找不到 Player";
var ctl = player.GetComponent<InkWash.Player.PlayerController>();
var anim = player.GetComponent<Animator>();
if (ctl == null || anim == null) return "[ERR] 缺 PlayerController / Animator";

var sb = new System.Text.StringBuilder();
string[] names = { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };

sb.AppendLine("comboRecCancelStart = " + string.Join(",", System.Array.ConvertAll(ctl.comboRecCancelStart, v => v.ToString("F2"))));
sb.AppendLine("attackInputBuffer = " + ctl.attackInputBuffer);

yield return null;
ctl.RequestInjectedAttack();
float t0 = Time.time;
string lastKey = "";
int frames = 0;
while (Time.time - t0 < 2.6f)
{
    frames++;
    var st = anim.GetCurrentAnimatorStateInfo(0);
    string nm = "Other";
    for (int i = 0; i < names.Length; i++) if (st.IsName(names[i])) { nm = names[i]; break; }
    string key = nm + "|" + ctl.IsCancelWindowOpen + "|" + ctl.ComboStep + "|" + ctl.Phase;
    if (key != lastKey)
    {
        lastKey = key;
        sb.AppendLine(string.Format("t={0:F3}  状态={1,-8} nt={2:F3}  cancel={3,-5} 段位={4} 阶段={5}",
            Time.time - t0, nm, st.normalizedTime, ctl.IsCancelWindowOpen, ctl.ComboStep, ctl.Phase));
    }
    yield return null;
}
sb.AppendLine("(2.6s 结束, " + frames + " 帧)");
return sb.ToString();
