// v43b: sample attack clips at normalized times, compose contact sheets per clip
using UnityEngine;
using UnityEditor;
using System.Text;

var sb = new System.Text.StringBuilder();

var player = GameObject.Find("Player");
if (player == null) return "no Player";
var clipPaths = new string[] {
    "Assets/Mixamo/sword and shield slash.fbx",
    "Assets/Mixamo/sword and shield slash (2).fbx",
    "Assets/Mixamo/sword and shield attack.fbx"
};
var tag = new string[] { "atk1", "atk2", "atk3" };

var rend = player.GetComponentInChildren<SkinnedMeshRenderer>();
if (rend == null) return "no skinned mesh";
Vector3 center = rend.bounds.center;

int fw = 320, fh = 240, cols = 4;
float[] fracs = new float[] { 0f, 0.15f, 0.30f, 0.45f, 0.60f, 0.75f, 0.90f, 1.0f };

var camGo = new GameObject("v43b_cam");
var cam = camGo.AddComponent<Camera>();
cam.transform.position = center + new Vector3(0f, 1.2f, -2.6f);
cam.transform.LookAt(center + Vector3.up * 0.8f);
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
    string fn = "Assets/../Tools/screenshots/v43_contact_" + tag[p] + ".png";
    System.IO.File.WriteAllBytes(fn, sheet.EncodeToPNG());
    sb.AppendLine(tag[p] + " len=" + clip.length.ToString("F3") + " -> " + fn);
}

Object.DestroyImmediate(camGo);
Object.DestroyImmediate(rt);
Object.DestroyImmediate(sheet);
}
finally
{
    AnimationMode.StopAnimationMode();
}
return sb.ToString();
