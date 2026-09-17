// 探针：确认「网格最低点」到底落在哪个渲染器上 —— 是鞋底，还是手里的刀？
var sb = new System.Text.StringBuilder();
var player = GameObject.Find("Player");
if (player == null) return "[ERR] 找不到 Player，先进入 Play";
var anim = player.GetComponentInChildren<Animator>(true);
var smrs = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
var bake = new UnityEngine.Mesh();
bake.MarkDynamic();

float groundY = player.transform.position.y;
var hits = Physics.RaycastAll(player.transform.position + Vector3.up * 3f, Vector3.down, 10f);
float best = float.MinValue;
foreach (var h in hits)
{
    if (h.collider.transform.IsChildOf(player.transform)) continue;
    if (h.point.y > best) best = h.point.y;
}
if (best > float.MinValue) groundY = best;

sb.AppendLine("地面 Y = " + groundY.ToString("F3"));
sb.AppendLine();
sb.AppendLine("=== 所有 SkinnedMeshRenderer ===");
for (int k = 0; k < smrs.Length; k++)
{
    sb.AppendLine(string.Format("  [{0}] {1}", k, smrs[k].name));
    sb.AppendLine(string.Format("      路径 = {0}", GetPath(smrs[k].transform, player.transform)));
}

// 非蒙皮的渲染器也列一下（刀可能是普通 MeshRenderer）
sb.AppendLine();
sb.AppendLine("=== 所有 MeshRenderer（非蒙皮）===");
var mrs = player.GetComponentsInChildren<MeshRenderer>(true);
foreach (var mr in mrs)
{
    sb.AppendLine("  " + GetPath(mr.transform, player.transform)
        + "  enabled=" + mr.enabled
        + "  有MeshFilter=" + (mr.GetComponent<MeshFilter>() != null));
}

const string Fbx = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
var objs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(Fbx);
var map = new System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>();
foreach (var o in objs)
{
    var c = o as UnityEngine.AnimationClip;
    if (c == null) continue;
    string key = c.name;
    int bar = key.LastIndexOf('|');
    if (bar >= 0 && bar + 1 < key.Length) key = key.Substring(bar + 1);
    map[key] = c;
}

sb.AppendLine();
sb.AppendLine("=== 各姿势下每个渲染器自身的最低点（相对地面）===");
sb.AppendLine("片段         nt    " + string.Join("  ", System.Array.ConvertAll(smrs, r => Pad(r.name, 12))));
string[] clips = { "Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop", "Sprint_Loop" };
foreach (var cn in clips)
{
    UnityEngine.AnimationClip c;
    if (!map.TryGetValue(cn, out c)) continue;
    for (int s = 0; s <= 4; s++)
    {
        float nt = s / 4f;
        c.SampleAnimation(player, c.length * nt);
        var vals = new string[smrs.Length];
        for (int k = 0; k < smrs.Length; k++)
        {
            smrs[k].BakeMesh(bake);
            var verts = bake.vertices;
            var m = smrs[k].transform.localToWorldMatrix;
            float lo = float.MaxValue;
            for (int v = 0; v < verts.Length; v++)
            {
                float wy = m.MultiplyPoint3x4(verts[v]).y;
                if (wy < lo) lo = wy;
            }
            vals[k] = (lo - groundY).ToString("F3");
        }
        sb.AppendLine(string.Format("{0,-12} {1:F2}  {2}", cn, nt, string.Join("  ", System.Array.ConvertAll(vals, v => Pad(v, 12)))));
    }
}

UnityEngine.Object.Destroy(bake);
return sb.ToString();

string Pad(string s, int n) { return s.Length >= n ? s.Substring(0, n) : s + new string(' ', n - s.Length); }
string GetPath(Transform t, Transform root)
{
    string p = t.name;
    while (t.parent != null && t != root) { t = t.parent; p = t.name + "/" + p; }
    return p;
}
