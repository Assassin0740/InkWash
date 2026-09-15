// FootIK 诊断：换片段后脚穿地，看是「探不到地面」还是「抬升量不够」
using System.Collections;
using System.Text;
using UnityEngine;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);

    yield return null; yield return null;

    var go = GameObject.Find("Player");
    if (go == null) { Debug.LogError("no Player"); yield break; }
    var anim = go.GetComponent<Animator>();
    var ik = go.GetComponent<InkWash.Player.FootIK>();
    var ctl = go.GetComponent<InkWash.Player.PlayerController>();
    var stance = go.GetComponentInChildren<InkWash.Player.CombatStance>(true);
    if (ctl != null) ctl.enabled = false;
    if (stance != null) stance.ForceStance(true);
    if (ik != null) ik.enabled = true;
    yield return null;

    sb.AppendLine("FootIK 配置：probeUp=" + ik.probeUp + "  probeDown=" + ik.probeDown
                  + "  maxLift=" + ik.maxLift + "  liftSmooth=" + ik.liftSmooth
                  + "  groundClearance=" + ik.groundClearance);

    string[] states = { "Idle", "Atk1", "Atk1Rec", "Atk2", "Atk2Rec", "Atk3" };
    foreach (var s in states)
    {
        sb.AppendLine();
        sb.AppendLine("════ " + s + " ════");
        sb.AppendLine("   nt     已生效偏移   网格最低点Y   基准Base   BodyLift   需抬Raw   有地面?   角色Y");
        for (int i = 0; i <= 10; i++)
        {
            float nt = i / 10f;
            anim.Play(s, 0, nt);
            anim.Update(0f);
            anim.Update(0f);
            // 让 IK 收敛几帧
            for (int k = 0; k < 8; k++) { anim.Update(1f / 60f); }

            sb.AppendLine("   " + nt.ToString("F2")
                + "   " + ik.AppliedBodyOffset.ToString("+0.000;-0.000").PadLeft(9)
                + "   " + (ik.LowestMeshY + ik.AppliedBodyOffset).ToString("+0.000;-0.000").PadLeft(11)
                + "   " + ik.BodyBase.ToString("+0.000;-0.000").PadLeft(9)
                + "   " + ik.BodyLift.ToString("+0.000;-0.000").PadLeft(9)
                + "   " + ik.RawPenetration.ToString("+0.000;-0.000").PadLeft(9)
                + "   " + (ik.HasGroundInfo ? "  有 " : " 无!!").PadLeft(8)
                + "   " + go.transform.position.y.ToString("+0.000;-0.000").PadLeft(8));
        }
    }

    if (ctl != null) ctl.enabled = true;
    System.IO.File.WriteAllText(System.IO.Path.Combine(projRoot, "Tools/reports/q_footdiag.txt"), sb.ToString());
    Debug.Log("[q_footdiag] done");
    yield return null;
}
return Body();
