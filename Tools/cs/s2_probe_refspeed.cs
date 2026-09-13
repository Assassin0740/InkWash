// 运行时探针 v6：丈量 UAL1 各移动片段的「原生地面速度」与「原始浮空高度」。
//
// 为什么需要它：
//   PlayerController 的步伐同步是 播放倍率 = 实际速度 / 片段原生速度(refSpeed)。
//   refSpeed 与片段真实速度不符 → 脚打滑 → 表现为「两条腿在原地倒腾」。
//
// 前五版走的所有弯路，供以后别再踩：
//   v1 世界坐标差分「最低顶点」        → 腾空期最低点跳到摆动腿，被污染
//   v2 踝骨前后位移 ÷ 周期            → 少了着地占比这个因子，且踝关节随脚掌滚动
//   v3 只留「离最低值 3cm 以内」的帧   → 慢跑身体起伏 25cm，这个窗口只覆盖"身体最低那一瞬"
//   v4 锁定单个鞋底顶点              → 脚掌滚动时该点只在一瞬间贴地
//   v5 改到局部空间                  → 修掉了根位移污染（实测各片段根位移都是 0），但判据仍错
//
// v6 的正确判据 —— 不看高度，看**竖直速度**：
//   脚踩在地上的时候，它的世界 y 是不动的（哪怕身体在上下起伏）。
//   所以「该帧最低点的 |Δy/Δt| ≈ 0」就是真正的着地；
//   这时它的水平速度就是片段的原生地面速度。
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

sb.AppendLine("地面 Y = " + groundY.ToString("F3"));
sb.AppendLine();
sb.AppendLine("片段          时长   原始最低  着地帧占比  原生地面速度(着地中位)    1.0×步/秒  单步位移");
sb.AppendLine("--------------------------------------------------------------------------------------");

var wanted = new string[] { "Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop", "Sprint_Loop" };
foreach (var name in wanted)
{
    UnityEngine.AnimationClip c;
    if (!map.TryGetValue(name, out c))
    {
        sb.AppendLine(string.Format("{0,-13} (缺该片段)", name));
        continue;
    }

    int N = 240;
    float len = c.length;
    var lx = new float[N + 1];
    var wz = new float[N + 1];
    var wx = new float[N + 1];
    var ly = new float[N + 1];
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
        lx[i] = lp.x; ly[i] = lp.y;
        wx[i] = bp.x; wz[i] = bp.z; wy[i] = lo - groundY;
        side[i] = lp.x < 0f ? -1 : 1;
        if (wy[i] < minH) minH = wy[i];
    }

    float dt = len / N;
    var spd = new System.Collections.Generic.List<float>();
    for (int i = 0; i + 1 <= N; i++)
    {
        if (side[i] != side[i + 1]) continue;                      // 换脚，跳过
        float vy = Mathf.Abs((wy[i + 1] - wy[i])) / dt;
        if (vy > 0.35f) continue;                                  // 在动，就是在空中
        float dx = wx[i + 1] - wx[i], dz = wz[i + 1] - wz[i];
        spd.Add(Mathf.Sqrt(dx * dx + dz * dz) / dt);
    }
    spd.Sort();
    float med = spd.Count > 0 ? spd[spd.Count / 2] : 0f;
    float p25 = spd.Count > 0 ? spd[spd.Count / 4] : 0f;
    float p75 = spd.Count > 0 ? spd[(spd.Count * 3) / 4] : 0f;
    float stepLen = spd.Count > 0 && (2f / len) > 0 ? med / (2f / len) : 0f;

    sb.AppendLine(string.Format("{0,-13} {1,5:F3}s {2,9:F3} {3,11:F0}%  {4,8:F2} m/s (p25 {5:F2}/p75 {6:F2})  {7,7:F2}  {8,6:F2}m",
        name, len, minH, 100f * spd.Count / N, med, p25, p75, 2f / len, stepLen));
}

if (footIk != null) footIk.enabled = ikWas;
UnityEngine.Object.Destroy(bake);

sb.AppendLine();
sb.AppendLine("判读：");
sb.AppendLine("  · 原生地面速度 = 该片段 1.0× 播放时应当匹配的角色速度，即 refSpeed 正确取值");
sb.AppendLine("  · 单步位移 = 速度 ÷ 步频，用来做常识校验：正常人走路 0.7~0.8m、慢跑 1.0~1.3m、冲刺 1.6~2.0m");
sb.AppendLine("  · 原始最低 = 整段动画脚底离地最小值，FootIK 静态基准偏移取它的负值");
return sb.ToString();
