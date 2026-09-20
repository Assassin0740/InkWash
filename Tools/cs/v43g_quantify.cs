// v43g: quantify yaw/translation profile + fixed front camera sheets
using UnityEngine;
using UnityEditor;
using System.Text;

var sb = new System.Text.StringBuilder();
var player = GameObject.Find("Player");
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
float[] fracs = new float[] { 0f, 0.15f, 0.30f, 0.45f, 0.58f, 0.72f, 0.86f, 1.0f };
int[] yawAt = new int[] { 0, 2, 4, 6 }; // fracs indices for yaw report

var camGo = new GameObject("v43g_cam");
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
    AnimationClip clip = null;
    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
        { var c = o as AnimationClip; if (c != null && !c.name.StartsWith("__preview")) { clip = c; break; } }
    if (clip == null) { sb.AppendLine(names[p] + " NO CLIP"); continue; }

    // pass 1: yaw + planar offset profile at fine steps
    var yaws = new float[21]; var offs = new float[21];
    Vector3 p0 = Vector3.zero; float yaw0 = 0f; float minYaw = 1e9f, maxYaw = -1e9f;
    for (int i = 0; i <= 20; i++)
    {
        float t = clip.length * i / 20f;
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(player, clip, t);
        AnimationMode.EndSampling();
        Vector3 hp = hips.position;
        Vector3 fw3 = hips.TransformDirection(Vector3.forward);
        float y = Mathf.Atan2(fw3.x, fw3.z) * Mathf.Rad2Deg;
        if (i == 0) { p0 = new Vector3(hp.x, 0, hp.z); yaw0 = y; }
        yaws[i] = Mathf.DeltaAngle(yaw0, y);
        offs[i] = Vector3.Distance(new Vector3(hp.x, 0, hp.z), p0);
        if (yaws[i] < minYaw) minYaw = yaws[i];
        if (yaws[i] > maxYaw) maxYaw = yaws[i];
    }
    float yawSpan = maxYaw - minYaw;
    float maxOff = 0f; foreach (var o in offs) maxOff = Mathf.Max(maxOff, o);

    // pass 2: fixed camera at t=0 facing front
    AnimationMode.BeginSampling();
    AnimationMode.SampleAnimationClip(player, clip, 0f);
    AnimationMode.EndSampling();
    Vector3 hip0 = hips.position;
    Vector3 f0 = hips.TransformDirection(Vector3.forward); f0.y = 0; f0.Normalize();
    var lookC = hip0 + Vector3.up * 0.25f;
    cam.transform.position = lookC + f0 * 2.4f + Quaternion.AngleAxis(30f, Vector3.up) * f0 * 1.0f + Vector3.up * 0.5f;
    cam.transform.LookAt(lookC);

    var fillColor = new Color32(40, 40, 44, 255);
    var px = new Color32[fw * cols * fh * 2];
    for (int i = 0; i < px.Length; i++) px[i] = fillColor;
    sheet.SetPixels32(px);
    for (int f = 0; f < fracs.Length; f++)
    {
        float t = fracs[f] * clip.length;
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(player, clip, t);
        AnimationMode.EndSampling();
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        int cx = (f % cols) * fw, cy = (1 - f / cols) * fh;
        sheet.ReadPixels(new Rect(0, 0, fw, fh), cx, cy);
        RenderTexture.active = null;
    }
    sheet.Apply();
    string tag = "q" + (p + 1);
    string fn = "Assets/../Tools/screenshots/v43_fix_" + tag + ".png";
    System.IO.File.WriteAllBytes(fn, sheet.EncodeToPNG());
    sb.AppendLine(tag + " " + names[p].Replace("sword and shield ", "") +
        " len=" + clip.length.ToString("F2") + "s | yawSpan=" + yawSpan.ToString("F0") + "deg | planarOff<=" + maxOff.ToString("F2") + "m");
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
