// v43c: sample attack clips with camera tracking hips; report baked root translation
using UnityEngine;
using UnityEditor;
using System.Text;

var sb = new System.Text.StringBuilder();

var player = GameObject.Find("Player");
if (player == null) return "no Player";
var animator = player.GetComponentInChildren<Animator>();
sb.AppendLine("applyRootMotion=" + (animator != null ? animator.applyRootMotion.ToString() : "no-animator"));

var hips = animator != null ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
var clipPaths = new string[] {
    "Assets/Mixamo/sword and shield slash.fbx",
    "Assets/Mixamo/sword and shield slash (2).fbx",
    "Assets/Mixamo/sword and shield attack.fbx"
};
var tag = new string[] { "atk1", "atk2", "atk3" };

int fw = 320, fh = 240, cols = 4;
float[] fracs = new float[] { 0f, 0.12f, 0.25f, 0.38f, 0.50f, 0.62f, 0.78f, 1.0f };

var camGo = new GameObject("v43c_cam");
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

for (int p = 0; p < clipPaths.Length; p++)
{
    var all = AssetDatabase.LoadAllAssetsAtPath(clipPaths[p]);
    AnimationClip clip = null;
    foreach (var o in all) { var c = o as AnimationClip; if (c != null && !c.name.StartsWith("__preview")) { clip = c; break; } }
    if (clip == null) { sb.AppendLine("no clip: " + clipPaths[p]); continue; }

    var fillColor = new Color32(40, 40, 44, 255);
    var px = new Color32[fw * cols * fh * 2];
    for (int i = 0; i < px.Length; i++) px[i] = fillColor;
    sheet.SetPixels32(px);

    Vector3 firstHips = Vector3.zero; float maxDev = 0f;
    for (int f = 0; f < fracs.Length; f++)
    {
        float t = fracs[f] * clip.length;
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(player, clip, t);
        AnimationMode.EndSampling();

        var h = hips != null ? hips.position : player.transform.position;
        if (f == 0) firstHips = h;
        maxDev = Mathf.Max(maxDev, Vector3.Distance(new Vector3(h.x, 0, h.z), new Vector3(firstHips.x, 0, firstHips.z)));

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
    string fn = "Assets/../Tools/screenshots/v43_track_" + tag[p] + ".png";
    System.IO.File.WriteAllBytes(fn, sheet.EncodeToPNG());
    sb.AppendLine(tag[p] + " len=" + clip.length.ToString("F3") + " rootPlanarDev<=" + maxDev.ToString("F2") + "m -> " + fn);
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
