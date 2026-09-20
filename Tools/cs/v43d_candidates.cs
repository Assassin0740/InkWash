// v43d: refresh, then sample all attack-candidate clips with tracking camera
using UnityEngine;
using UnityEditor;
using System.Text;

AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

var sb = new System.Text.StringBuilder();
var player = GameObject.Find("Player");
if (player == null) return "no Player";
var animator = player.GetComponentInChildren<Animator>();
var hips = animator.GetBoneTransform(HumanBodyBones.Hips);

var names = new string[] {
    "sword and shield slash.fbx",
    "sword and shield slash (2).fbx",
    "sword and shield slash (3).fbx",
    "sword and shield slash (4).fbx",
    "sword and shield slash (5).fbx",
    "sword and shield attack.fbx",
    "sword and shield attack (2).fbx",
    "sword and shield attack (3).fbx",
    "sword and shield attack (4).fbx",
    "sword and shield kick.fbx"
};

int fw = 320, fh = 240, cols = 4;
float[] fracs = new float[] { 0f, 0.12f, 0.25f, 0.38f, 0.50f, 0.62f, 0.78f, 1.0f };

var camGo = new GameObject("v43d_cam");
var cam = camGo.AddComponent<Camera>();
cam.backgroundColor = new Color(0.25f, 0.28f, 0.3f, 1f);
cam.clearFlags = CameraClearFlags.Color;
cam.cullingMask = ~0;
cam.fieldOfView = 45;

var rt = new RenderTexture(fw, fh, 24);
var sheet = new Texture2D(fw * cols, fh * 2, TextureFormat.RGB24, false);

try
{
AnimationMode.StartAnimationMode();

for (int p = 0; p < names.Length; p++)
{
    string path = "Assets/Mixamo/" + names[p];
    var all = AssetDatabase.LoadAllAssetsAtPath(path);
    AnimationClip clip = null;
    foreach (var o in all) { var c = o as AnimationClip; if (c != null && !c.name.StartsWith("__preview")) { clip = c; break; } }
    if (clip == null) { sb.AppendLine(names[p] + " NO CLIP"); continue; }

    // rig check
    var humanRig = false;
    var imp = AssetImporter.GetAtPath(path) as ModelImporter;
    if (imp != null && (int)imp.animationType == 3) humanRig = true; // 3 = Humanoid (团结引擎枚举无此名)

    var fillColor = new Color32(40, 40, 44, 255);
    var px = new Color32[fw * cols * fh * 2];
    for (int i = 0; i < px.Length; i++) px[i] = fillColor;
    sheet.SetPixels32(px);

    Vector3 firstHips = Vector3.zero; float maxDev = 0f; float maxHipsY = 0f;
    for (int f = 0; f < fracs.Length; f++)
    {
        float t = fracs[f] * clip.length;
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(player, clip, t);
        AnimationMode.EndSampling();

        var h = hips != null ? hips.position : player.transform.position;
        if (f == 0) firstHips = h;
        maxDev = Mathf.Max(maxDev, Vector3.Distance(new Vector3(h.x, 0, h.z), new Vector3(firstHips.x, 0, firstHips.z)));
        maxHipsY = Mathf.Max(maxHipsY, h.y - firstHips.y);

        Vector3 look = h + Vector3.up * 0.25f;
        cam.transform.position = look + new Vector3(0f, 0.6f, -2.2f);
        cam.transform.LookAt(look);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        int cx = (f % cols) * fw, cy = (1 - f / cols) * fh;
        sheet.ReadPixels(new Rect(0, 0, fw, fh), cx, cy);
        RenderTexture.active = null;
    }
    sheet.Apply();
    string tag = "c" + (p + 1);
    string fn = "Assets/../Tools/screenshots/v43_cand_" + tag + ".png";
    System.IO.File.WriteAllBytes(fn, sheet.EncodeToPNG());
    sb.AppendLine(tag + " " + names[p] + " len=" + clip.length.ToString("F2") + "s dev=" + maxDev.ToString("F2") + "m hipsY+=" + maxHipsY.ToString("F2") + "m human=" + humanRig);
}
}
finally
{
    AnimationMode.StopAnimationMode();
    Object.DestroyImmediate(camGo);
    Object.DestroyImmediate(rt);
    Object.DestroyImmediate(sheet);
}
return sb.ToString();
