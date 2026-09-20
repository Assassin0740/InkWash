// v43f: body-relative camera sampling for chosen candidates
using UnityEngine;
using UnityEditor;
using System.Text;

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
float[] fracs = new float[] { 0f, 0.15f, 0.30f, 0.45f, 0.58f, 0.72f, 0.86f, 1.0f };

var camGo = new GameObject("v43f_cam");
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

        Vector3 hp = hips.position;
        Vector3 fwd = hips.TransformDirection(Vector3.forward); fwd.y = 0;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        // camera at body-relative front-right diagonal
        Vector3 look = hp + Vector3.up * 0.2f;
        cam.transform.position = look + fwd * 2.0f + Vector3.right * 1.1f * (fwd.x != 0 ? 1 : 1) + Vector3.up * 0.5f;
        // use fwd rotated 30 deg for a 3/4 view
        Vector3 dir = Quaternion.AngleAxis(35f, Vector3.up) * fwd;
        cam.transform.position = look + dir * 2.2f + Vector3.up * 0.55f;
        cam.transform.LookAt(look);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        int cx = (f % cols) * fw, cy = (1 - f / cols) * fh;
        sheet.ReadPixels(new Rect(0, 0, fw, fh), cx, cy);
        RenderTexture.active = null;
    }
    sheet.Apply();
    string tag = "r" + (p + 1);
    string fn = "Assets/../Tools/screenshots/v43_rel_" + tag + ".png";
    System.IO.File.WriteAllBytes(fn, sheet.EncodeToPNG());
    sb.AppendLine(tag + " " + names[p] + " len=" + clip.length.ToString("F2"));
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
