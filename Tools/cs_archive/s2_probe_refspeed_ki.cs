// 丈量移动片段的「原生地面速度」，用来标定 PlayerController 的 walkRefSpeed / runRefSpeed。
// 方法与 s2_probe_refspeed.cs v6 一致（判据：脚踩地时世界 y 不动，此时它的水平速度就是原生地面速度）。
// 本版把 **KI《Run01_Forward》** 也拉进来一起量 —— 换了跑步片段就必须重新标定，
// 否则 MotionSpeed = 实际速度/原生速度 会算错，脚打滑、步频发疯（观感就是"两条腿在原地倒腾/扭腰"）。
var sb = new System.Text.StringBuilder();

var player = GameObject.Find("Player");
if (player == null) return "[ERR] 找不到 Player，先进入 Play";
var anim = player.GetComponentInChildren<Animator>(true);
if (anim == null) return "[ERR] 无 Animator";

var smrs = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
var bake = new UnityEngine.Mesh();
bake.MarkDynamic();

var footIk = player.GetComponent<InkWash.Player.FootIK>();
bool ikWas = false;
if (footIk != null) { ikWas = footIk.enabled; footIk.enabled = false; }

float groundY = player.transform.position.y;
var hits = Physics.RaycastAll(player.transform.position + Vector3.up * 3f, Vector3.down, 10f);
float best = float.MinValue;
foreach (var h in hits)
{
    if (h.collider.transform.IsChildOf(player.transform)) continue;
    if (h.point.y > best) best = h.point.y;
}
if (best > float.MinValue) groundY = best;

const string FbxUAL1 = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/Unity/AnimationLibrary_Unity_Standard.fbx";
const string FbxKI = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";

var map = new System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>();
var keys = new System.Collections.Generic.List<string>();
System.Action<string> ingest = (path) =>
{
    foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
    {
        var c = o as UnityEngine.AnimationClip;
        if (c == null || c.name.StartsWith("__preview__")) continue;
        // 原名字 + 去掉 '|' 前缀的名字都登记，避免"片段名对不上"
        if (!map.ContainsKey(c.name)) { map[c.name] = c; keys.Add(c.name); }
        string k = c.name; int bar = k.LastIndexOf('|');
        if (bar >= 0 && bar + 1 < k.Length) k = k.Substring(bar + 1);
        if (!map.ContainsKey(k)) { map[k] = c; keys.Add(k); }
        else if (bar >= 0) map[k] = c;
    }
};
ingest(FbxUAL1);
ingest(FbxKI);

sb.AppendLine("地面 Y = " + groundY.ToString("F3"));
sb.AppendLine("可用键: " + string.Join(", ", keys.ToArray()));
sb.AppendLine();
sb.AppendLine("片段             时长    原始最低 着地占比  原生地面速度(着地中位)      步频/s   单步位移");
sb.AppendLine("----------------------------------------------------------------------------------------");

var wanted = new string[] { "Walk_Loop", "Jog_Fwd_Loop", "Sprint_Loop", "Run01_Forward" };
foreach (var name in wanted)
{
    UnityEngine.AnimationClip c;
    if (!map.TryGetValue(name, out c))
    {
        sb.AppendLine(string.Format("{0,-16} (缺该片段)", name));
        continue;
    }

    int N = 240;
    float len = c.length;
    var wx = new float[N + 1];
    var wz = new float[N + 1];
    var wy = new float[N + 1];
    var side = new int[N + 1];
    float minH = float.MaxValue;

    for (int i = 0; i <= N; i++)
    {
        c.SampleAnimation(player, len * i / N);

        float lo = float.MaxValue;
        Vector3 bp = Vector3.zero;
        for (int k = 0; k < smrs.Length; k++)
        {
            smrs[k].BakeMesh(bake);
            var verts = bake.vertices;
            var m = smrs[k].transform.localToWorldMatrix;
            for (int v = 0; v < verts.Length; v++)
            {
                var wp = m.MultiplyPoint3x4(verts[v]);
                if (wp.y < lo) { lo = wp.y; bp = wp; }
            }
        }
        var lp = player.transform.InverseTransformPoint(bp);
        wx[i] = bp.x; wz[i] = bp.z; wy[i] = lo - groundY;
        side[i] = lp.x < 0f ? -1 : 1;
        if (wy[i] < minH) minH = wy[i];
    }

    float dt = len / N;
    var spd = new System.Collections.Generic.List<float>();
    for (int i = 0; i + 1 <= N; i++)
    {
        if (side[i] != side[i + 1]) continue;
        float vy = Mathf.Abs((wy[i + 1] - wy[i])) / dt;
        if (vy > 0.35f) continue;
        float dx = wx[i + 1] - wx[i], dz = wz[i + 1] - wz[i];
        spd.Add(Mathf.Sqrt(dx * dx + dz * dz) / dt);
    }
    spd.Sort();
    float med = spd.Count > 0 ? spd[spd.Count / 2] : 0f;
    float stepLen = spd.Count > 0 && len > 0f ? med / (2f / len) : 0f;

    sb.AppendLine(string.Format("{0,-16} {1,5:F3}s {2,9:F3} {3,8:F0}%  {4,8:F2} m/s             {5,7:F2}  {6,6:F2}m",
        name, len, minH, 100f * spd.Count / N, med, len > 0f ? 2f / len : 0f, stepLen));
}

if (footIk != null) footIk.enabled = ikWas;
UnityEngine.Object.Destroy(bake);

sb.AppendLine();
sb.AppendLine("判读：");
sb.AppendLine("  · 原生地面速度 = 该片段 1.0× 播放时应匹配的角色速度，即 refSpeed 的正确取值");
sb.AppendLine("  · 单步位移 常识校验：走路 0.7~0.8m、慢跑 1.0~1.3m、冲刺 1.6~2.0m");
return sb.ToString();
