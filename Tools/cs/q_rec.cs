// q_rec.cs —— 录制前的场景预备（把主角放到场地中央，让波次怪自然生成进镜头）
using System;
using System.Text;
using UnityEngine;

public class QRec
{
    public static string Run()
    {
        var sb = new StringBuilder();
        var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
        if (pc == null) return "no player";
        var cc = pc.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        pc.transform.position = new Vector3(0f, 0.05f, -1.5f);
        pc.transform.rotation = Quaternion.identity;
        if (cc != null) cc.enabled = true;

        var a = pc.GetComponentInChildren<Animator>();
        sb.AppendLine("player=" + pc.name + " phase=" + pc.Phase
                    + " dash=(" + pc.GetComponent<InkWash.Player.PlayerController>().GetType().GetField("dashSpeed",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public).GetValue(pc) + ")");
        sb.AppendLine("dashDuration=" + pc.GetType().GetField("dashDuration",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public).GetValue(pc));
        sb.AppendLine("animator=" + (a != null ? a.name : "null"));
        return sb.ToString();
    }
}

return QRec.Run();
