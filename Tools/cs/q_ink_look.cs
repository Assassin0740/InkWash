// q_ink_look.cs —— 水墨观感 A/B：固定机位，只变「固有色的掺入量」（_InkDensity），出三张图。
//
// 为什么必须在运行时 A/B 而不是拍脑袋写进材质：
//   `_InkDensity` 决定**留白多少**。太小（0.15 掺入）时贴图几乎不参与，脸会洗成一片白
//   —— 在游戏距离下看着像"没画脸"；太大又退回普通贴图着色、失去水墨味。
//   这是**只用眼睛能判**的量，先出图再定值。
//
// 另外顺手把「墨阶数」也拍一版，用来确认 2/4/6 阶在人物身上各自的观感。
using System.Collections;
using UnityEngine;

string OUT = "Tools/screenshots/s4/look";

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    string projRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
    string dir = System.IO.Path.Combine(projRoot, OUT);
    System.IO.Directory.CreateDirectory(dir);

    var ctl = Object.FindObjectOfType<InkWash.Player.PlayerController>();
    if (ctl == null) { Debug.LogError("[q_ink_look] 找不到 PlayerController"); yield break; }
    var panel = Object.FindObjectOfType<InkWash.UI.InkStylePanel>();
    if (panel == null) { Debug.LogError("[q_ink_look] 找不到 InkStylePanel"); yield break; }

    panel.visible = false;
    panel.ApplyStage(3);                       // 全效果：量化 + 墨线 + 宣纸
    ctl.BeginInputOverride();
    ctl.SetInjectedMove(Vector2.zero, false);
    yield return null; yield return null;

    var tr = ctl.transform;
    var mainCam = Camera.main;
    var camGo = new GameObject("InkLookCam");
    var cam = camGo.AddComponent<Camera>();
    cam.CopyFrom(mainCam);
    cam.tag = "Untagged";
    cam.enabled = false;                        // 只手动 Render，不参与常规渲染
    cam.fieldOfView = 32f;

    // 半身 3/4 机位（游戏里最常看到的取景）
    // ★ 注意 `+tr.forward`：角色朝 forward，站在 **-forward** 侧拍出来是**背面**
    //   —— 而这一组 A/B 恰恰要看脸，所以必须从正面偏 33° 的方向拍。
    Vector3 chest = tr.position + Vector3.up * 1.32f;
    Vector3 off = (tr.forward * 0.84f + tr.right * 0.54f).normalized;
    Vector3 camPos = chest + off * 2.30f + Vector3.up * 0.12f;
    cam.transform.position = camPos;
    cam.transform.rotation = Quaternion.LookRotation(chest - camPos, Vector3.up);

    const int CW = 720, CH = 960;

    // 把属性写进**运行时**材质实例（面板 ApplyStage 之后 sharedMaterials 就是那批实例）
    System.Action<string, float> setAll = (prop, val) =>
    {
        foreach (var sw in Object.FindObjectsOfType<InkWash.UI.InkMaterialSwap>())
        {
            if (sw == null || sw.target == null) continue;
            foreach (var m in sw.target.sharedMaterials)
                if (m != null && m.HasProperty(prop)) m.SetFloat(prop, val);
        }
    };

    System.Action<string, string> shoot = (fileName, tag) =>
    {
        var rt = RenderTexture.GetTemporary(CW, CH, 24);
        cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var snap = new Texture2D(CW, CH, TextureFormat.RGB24, false);
        snap.ReadPixels(new Rect(0, 0, CW, CH), 0, 0); snap.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, fileName), snap.EncodeToPNG());
        Object.Destroy(snap);
        sb.AppendLine("  " + fileName + "   " + tag);
    };

    // ---- A 组：留白量（_InkDensity）----
    float[] dens = { 0.90f, 0.78f, 0.66f, 0.52f };
    sb.AppendLine("A 组 · 留白量 _InkDensity（越大越白；掺入量 = 1 − density）");
    foreach (var d in dens)
    {
        setAll("_InkDensity", d);
        yield return null; yield return null;
        shoot("A_density_" + d.ToString("0.00") + ".png",
              "掺入 " + (1f - d).ToString("0.00"));
    }

    // ---- B 组：墨阶数（_Bands）----
    setAll("_InkDensity", 0.78f);
    float[] bands = { 2f, 3f, 4f, 6f };
    sb.AppendLine("B 组 · 墨阶数 _Bands");
    foreach (var b in bands)
    {
        setAll("_Bands", b);
        yield return null; yield return null;
        shoot("B_bands_" + b.ToString("0") + ".png", b.ToString("0") + " 阶");
    }

    ctl.SetInjectedMove(Vector2.zero, false);
    ctl.EndInputOverride();
    Object.Destroy(camGo);
    Debug.Log(sb.ToString());
    Debug.Log("[q_ink_look] done");
    yield return null;
}
return Body();
