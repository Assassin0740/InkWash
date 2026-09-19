// v38_vfx_audit.cs —— 第三十八轮视觉盘点：粒子/材质/后处理/特效脚本挂载现状
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

var sb = new System.Text.StringBuilder();

// ① 场景内所有 ParticleSystem（含未激活）
var psAll = Object.FindObjectsOfType<ParticleSystem>(true);
sb.AppendLine("== ParticleSystem 总数: " + psAll.Length);
foreach (var g in psAll.GroupBy(p => p.gameObject.scene.IsValid() ? "scene" : "other"))
{
    foreach (var p in psAll)
    {
        var go = p.gameObject;
        var main = p.main;
        bool activeInHierarchy = go.activeInHierarchy;
        sb.AppendLine(string.Format("PS '{0}' active={1} playing={2} maxParticles={3} startColor={4} dur={5:F1}s renderer={6}",
            GetPath(go), activeInHierarchy, p.isPlaying, main.maxParticles,
            main.startColor.color, main.duration, p.GetComponent<ParticleSystemRenderer>()?.sharedMaterial?.name ?? "null"));
    }
}

// ② 特效脚本挂载
sb.AppendLine("== VFX 脚本挂载:");
foreach (var t in new System.Type[] {
    typeof(InkWash.Effects.InkHitVfx), typeof(InkWash.Effects.SwordVfx), typeof(InkWash.Effects.DragonStormVfx) })
{
    var arr = Object.FindObjectsOfType(t, true);
    sb.AppendLine(t.Name + " x" + arr.Length + (arr.Length > 0 ? " @ " + GetPath(((Component)arr[0]).gameObject) : ""));
}

// ③ 场景 Renderer 材质分布（shader 名计数）
var renderers = Object.FindObjectsOfType<Renderer>(true);
var shaderCount = new System.Collections.Generic.Dictionary<string, int>();
foreach (var r in renderers)
    foreach (var m in r.sharedMaterials)
    {
        if (m == null) { shaderCount["<null mat>"] = shaderCount.TryGetValue("<null mat>", out var n0) ? n0 + 1 : 1; continue; }
        var sn = m.shader != null ? m.shader.name : "<null shader>";
        shaderCount[sn] = shaderCount.TryGetValue(sn, out var n) ? n + 1 : 1;
    }
sb.AppendLine("== 材质 shader 分布（共 " + renderers.Length + " 个 Renderer）:");
foreach (var kv in shaderCount.OrderByDescending(k => k.Value))
    sb.AppendLine("  " + kv.Value + "x " + kv.Key);

// ④ 后处理 Volume
var volumes = Object.FindObjectsOfType<Volume>(true);
sb.AppendLine("== Volume x" + volumes.Length);
foreach (var v in volumes)
{
    string prof = v.sharedProfile != null ? v.sharedProfile.name : "null";
    string effects = "";
    if (v.profile != null || v.sharedProfile != null)
    {
        var p = v.sharedProfile;
        if (p != null)
            foreach (var comp in p.components) effects += comp.GetType().Name + " ";
    }
    sb.AppendLine("Volume '" + GetPath(v.gameObject) + "' profile=" + prof + " isGlobal=" + v.isGlobal + " effects=[" + effects + "]");
}

// ⑤ 天空/相机
var cams = Object.FindObjectsOfType<Camera>(true);
foreach (var c in cams)
    sb.AppendLine(string.Format("Camera '{0}' bg={1} clearFlags={2} postEnabled={3} volumeMask={4}",
        GetPath(c.gameObject), c.backgroundColor, c.clearFlags,
        c.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>()?.renderPostProcessing, c.cullingMask));

// ⑥ 关键水墨物体检查（天空、地面、门、HUD 装饰）
foreach (var nm in new string[] { "InkSky", "InkSurface", "Ground", "Floor", "Sky" })
{
    var go = GameObject.Find(nm);
    if (go != null)
    {
        var rr = go.GetComponentsInChildren<Renderer>(true);
        sb.AppendLine("Obj '" + nm + "' @ " + GetPath(go) + " renderers=" + rr.Length +
            (rr.Length > 0 ? " shader=" + rr[0].sharedMaterial?.shader?.name : ""));
    }
}

Debug.Log("[v38audit]\n" + sb.ToString());
return sb.ToString();

string GetPath(GameObject go)
{
    var path = go.name;
    var t = go.transform;
    while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
    return path;
}
