// 只读探针：① 检查墨怪各 SMR 的 bones 是否有重名（接骨错位的经典成因）
//            ② 实例化出图：整体三视角 + 逐件单渲，定位「斧子/棒子」分别是哪块网格
var sb = new System.Text.StringBuilder();

string P = "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab";
var src = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(P);
if (src == null) return "prefab NULL";

// ── ① bones 重名检测 ──
var smrs0 = src.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true);
sb.AppendLine("=== bones 重名检测（同一 SMR 内出现同名骨骼 = 接骨歧义）===");
int badTotal = 0;
foreach (var s in smrs0)
{
    var seen = new System.Collections.Generic.Dictionary<string, int>();
    for (int i = 0; i < s.bones.Length; i++)
    {
        string nm = s.bones[i] == null ? "<NULL>" : s.bones[i].name;
        if (!seen.ContainsKey(nm)) seen[nm] = 0;
        seen[nm]++;
    }
    var dup = new System.Collections.Generic.List<string>();
    foreach (var kv in seen) if (kv.Value > 1) dup.Add(kv.Key + "×" + kv.Value);
    if (dup.Count > 0)
    {
        badTotal++;
        sb.AppendLine("  ⚠ " + s.name + " 重名: " + string.Join(", ", dup.ToArray()));
    }
}
sb.AppendLine(badTotal == 0 ? "  （无重名）" : "  ⚠ 有重名的 SMR 数 = " + badTotal);
sb.AppendLine();

// ── ② 出图 ──
var inst = UnityEngine.Object.Instantiate(src);
inst.transform.position = new UnityEngine.Vector3(2000f, 0f, 2000f);
inst.transform.rotation = UnityEngine.Quaternion.identity;
inst.name = "PROBE_GUAI";

var rends = inst.GetComponentsInChildren<UnityEngine.Renderer>(true);
int n = rends.Length;
var bAll = rends[0].bounds;
for (int i = 1; i < n; i++) bAll.Encapsulate(rends[i].bounds);

sb.AppendLine("渲染器数 = " + n);
sb.AppendLine("整体世界 bounds = " + bAll.center.ToString("F3") + "  size=" + bAll.size.ToString("F3"));
sb.AppendLine();
for (int i = 0; i < n; i++)
    sb.AppendLine("  " + rends[i].name + "  [" + rends[i].GetType().Name + "]  world="
        + rends[i].bounds.center.ToString("F2") + " / " + rends[i].bounds.size.ToString("F2"));

var camGO = new UnityEngine.GameObject("PROBE_CAM");
var cam = camGO.AddComponent<UnityEngine.Camera>();
cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
cam.backgroundColor = new UnityEngine.Color(0.86f, 0.88f, 0.91f, 1f);
cam.fieldOfView = 40f;
cam.nearClipPlane = 0.05f;
cam.farClipPlane = 9000f;
float rad = bAll.extents.magnitude;
float dist = rad / UnityEngine.Mathf.Sin(cam.fieldOfView * 0.5f * UnityEngine.Mathf.Deg2Rad) * 1.08f;

int W = 960, H = 900;
var rt = new UnityEngine.RenderTexture(W, H, 24);
cam.targetTexture = rt;

void Snap(string tag)
{
    cam.Render();
    var prev = UnityEngine.RenderTexture.active;
    UnityEngine.RenderTexture.active = rt;
    var tex = new UnityEngine.Texture2D(W, H, UnityEngine.TextureFormat.RGB24, false);
    tex.ReadPixels(new UnityEngine.Rect(0, 0, W, H), 0, 0);
    tex.Apply();
    System.IO.File.WriteAllBytes("Tools/screenshots/enemies/QG_" + tag + ".png", tex.EncodeToPNG());
    UnityEngine.RenderTexture.active = prev;
    UnityEngine.Object.DestroyImmediate(tex);
}

var dirs = new UnityEngine.Vector3[] {
    new UnityEngine.Vector3(0f, 0.20f, 1f),
    new UnityEngine.Vector3(1f, 0.20f, 0f),
    new UnityEngine.Vector3(0.7f, 0.15f, 0.7f),
};
var dtag = new string[] { "f", "r", "q" };

for (int d = 0; d < dirs.Length; d++)
{
    cam.transform.position = bAll.center + dirs[d].normalized * dist;
    cam.transform.LookAt(bAll.center);
    Snap("all_" + dtag[d]);
}

// 逐件单渲
for (int i = 0; i < n; i++)
{
    for (int j = 0; j < n; j++) rends[j].enabled = (j == i);
    cam.transform.position = bAll.center + dirs[0].normalized * dist;
    cam.transform.LookAt(bAll.center);
    Snap("only_" + rends[i].name.Replace(" ", "_").Replace("/", "_"));
}
for (int j = 0; j < n; j++) rends[j].enabled = true;

cam.targetTexture = null;
UnityEngine.Object.DestroyImmediate(rt);
UnityEngine.Object.DestroyImmediate(camGO);
UnityEngine.Object.DestroyImmediate(inst);

System.IO.File.WriteAllText("Tools/reports/q_gua_shot.txt", sb.ToString());
return "q_gua_shot done: rends=" + n + " dupSMR=" + badTotal;
