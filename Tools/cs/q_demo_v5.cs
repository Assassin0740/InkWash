// q_demo_v5.cs —— 第八轮（四条实机反馈收口）的演示录屏
//
// 相对 q_demo_v4 的变化（都对应本轮的四条诉求）：
//   ① 「人物外轮廓减淡」：_OutlineWidth 0.038 → 0.012（屏幕 13.7 px → 5 px）★
//   ② 「人物颜色丰富一点」：墨彩注入 _ChromaKeep 0.45 → 0.60（净贡献 0.043）★
//   ③ 「地上奇怪现象 / 墙壁很奇怪」：量化器真关闭 + 中央高台加轮廓 + 墙降噪
//   ④ 「有地图素材吗」：8 条瓦檐 + 20 个自然道具（清点证实库里本就没有建筑件）
//
//  0.0~1.6s  非战斗待机（远景：瓦檐 / 自然道具 / 干净院墙）
//  1.6~2.0s  拔剑
//  2.0~4.2s  持剑待机 ★（看墨线粗细与衣料墨彩）
//  4.2~6.8s  走路
//  6.8~10.4s 跑步
// 10.4~11.4s 停下
// 11.4~14.6s 三段连击
// 14.6~16.4s 侧身 55°（看侧影墨线与瓦檐天际线）
//
// 时间驱动、不按帧数等（本项目硬规矩：录屏会让帧率掉到 20~35 fps，按帧数等段长会被放大数倍）
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("[q_demo_v5] 找不到 Player"); yield break; }
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    var anim = go.GetComponent<Animator>();
    var cc = go.GetComponent<CharacterController>();
    var cam = Camera.main;
    var rig = cam != null ? cam.GetComponent<InkWash.CameraRig.ThirdPersonCamera>() : null;
    var hp = go.GetComponent<InkWash.Player.PlayerHealth>();
    if (hp != null) { hp.maxHealth = 100000f; hp.ResetHealth(); }

    // ★ 起点必须落在**石门以内**（石门在 z=±10、是实心碰撞体）——
    //   验收 harness 的 StageAnchor 曾因落在南门外（z=-14）导致 8 项假失败。
    var startPos = new Vector3(0f, 0.05f, -7f);
    var startRot = Quaternion.identity;

    void Park(float yaw)
    {
        if (cc != null) cc.enabled = false;
        go.transform.position = startPos;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cc != null) cc.enabled = true;
    }

    sb.AppendLine("=== 演示录屏 q_demo_v5（第八轮：外轮廓 5px / 墨彩 0.60 / 干净院墙 / 瓦檐+道具）===");
    if (ctl != null) ctl.BeginInputOverride();
    float t;

    // 1) 非战斗待机（远景）
    Park(0f);
    if (stance != null) stance.ForceStance(false);
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    t = Time.time; while (Time.time - t < 1.6f) yield return null;
    sb.AppendLine(Seg("1 非战斗待机", stance, anim, ctl));

    // 2) 拔剑
    if (stance != null) stance.ForceStance(true);
    t = Time.time; while (Time.time - t < 0.4f) yield return null;

    // 3) 持剑待机 ★
    Park(0f);
    t = Time.time; while (Time.time - t < 2.2f) yield return null;
    sb.AppendLine(Seg("3 持剑待机 ★", stance, anim, ctl));

    // 4) 走路
    Park(0f);
    if (stance != null) stance.ForceStance(true);
    if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), false);
    t = Time.time; while (Time.time - t < 2.6f) yield return null;
    sb.AppendLine(Seg("4 走路", stance, anim, ctl));

    // 5) 跑步
    Park(0f);
    if (ctl != null) ctl.SetInjectedMove(new Vector2(0f, 1f), true);
    t = Time.time; while (Time.time - t < 3.6f) yield return null;
    sb.AppendLine(Seg("5 跑步", stance, anim, ctl));

    // 6) 停下
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    t = Time.time; while (Time.time - t < 1.0f) yield return null;
    sb.AppendLine(Seg("6 停回待机", stance, anim, ctl));

    // 7) 三段连击
    Park(0f);
    if (stance != null) stance.ForceStance(true);
    t = Time.time;
    while (Time.time - t < 3.2f)
    {
        float e = Time.time - t;
        if (e >= 0.15f && e < 0.16f) ctl.RequestInjectedAttack();
        if (e >= 0.72f && e < 0.73f) ctl.RequestInjectedAttack();
        if (e >= 1.35f && e < 1.36f) ctl.RequestInjectedAttack();
        yield return null;
    }
    sb.AppendLine(Seg("7 三段连击", stance, anim, ctl));

    // 8) 侧身 55°（看侧影墨线 + 瓦檐天际线）
    Park(55f);
    if (ctl != null) ctl.SetInjectedMove(Vector2.zero, false);
    t = Time.time; while (Time.time - t < 1.8f) yield return null;
    sb.AppendLine(Seg("8 侧身 55°", stance, anim, ctl));

    // 收尾
    if (ctl != null) { ctl.SetInjectedMove(Vector2.zero, false); ctl.EndInputOverride(); }
    if (stance != null) stance.ForceStance(false);
    if (rig != null) rig.SetMouseLookEnabled(true);

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/q_demo_v5.txt"), sb.ToString());
    Debug.Log("[q_demo_v5] done");
    yield return null;
}

string Seg(string name, InkWash.Player.CombatStance st, Animator an, InkWash.Player.PlayerController pc)
{
    string stt = "Other";
    var si = an.GetCurrentAnimatorStateInfo(0);
    string[] names = { "Idle", "Walk", "Run", "Dash", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };
    for (int i = 0; i < names.Length; i++) if (si.IsName(names[i])) { stt = names[i]; break; }
    return "  " + name.PadRight(16) + " 状态=" + stt.PadRight(8)
         + " 速度=" + (pc != null ? pc.CurrentSpeed.ToString("0.00") : "?")
         + " 战斗中=" + (st != null ? st.InCombat.ToString() : "?")
         + " Idle片段=" + (st != null ? st.CurrentIdleClipName : "?");
}

return Body();
