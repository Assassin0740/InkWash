// s4_demo_a.cs —— M4 录像证据 ①：四阶段逐层叠加（白模 → 量化 → ＋墨线 → ＋宣纸）。
//
// 时间驱动（不是帧数驱动）：录制会把帧率压到 20~35，按帧数等会把段长放大好几倍。
// 面板全程可见：一段片子里同时证明「四层可分别开关」与「E6 参数面板可用」。
using System.Collections;
using UnityEngine;
using InkWash.Combat;
using InkWash.Player;
using InkWash.UI;

IEnumerator Body()
{
    var ph = Object.FindObjectOfType<PlayerHealth>();
    if (ph == null) { Debug.LogError("[s4_demo_a] 找不到 PlayerHealth"); yield break; }
    var go = ph.gameObject;
    var ctl = go.GetComponent<PlayerController>();
    var stance = go.GetComponentInChildren<CombatStance>(true);
    var panel = Object.FindObjectOfType<InkStylePanel>();

    if (ctl != null) ctl.ResetToLocomotion();
    yield return null;

    var cc = go.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    go.transform.position = new Vector3(0f, 0.45f, -2f);
    go.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
    if (cc != null) cc.enabled = true;
    if (stance != null) { stance.combatExitDelay = 99999f; stance.ForceStance(true); }
    if (panel != null) panel.visible = true;

    ctl.BeginInputOverride();
    { float t = Time.time; while (Time.time - t < 1.0f) yield return null; }

    // 四阶段 × 3.5 s，横向慢走（来回摆，免得走出取景）
    for (int s = 0; s < 4; s++)
    {
        if (panel != null) panel.ApplyStage(s);
        ph.ResetHealth();
        float t0 = Time.time, flip = 0f, dir = 1f;
        while (Time.time - t0 < 3.5f)
        {
            if (Time.time - flip > 1.1f) { flip = Time.time; dir = -dir; }
            ctl.SetInjectedMove(new Vector2(dir, 0f), false);
            yield return null;
        }
    }

    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.EndInputOverride();
    if (stance != null) stance.combatExitDelay = 6f;
    Debug.Log("[s4_demo_a] done");
    yield return null;
}
return Body();
