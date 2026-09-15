// r_scene.cs —— 只读探针：把场景的光照与"白盒"几何现状读出来
//
// 为什么需要它：用户反馈"水墨风发灰、像普通积木玩偶"。
// 在改任何数值之前先要看清楚**谁在决定画面的中间调**：
//   · RenderSettings 的环境光（SH）—— 水墨 shader 里 ambient 那一项
//   · 主光的颜色 / 强度 / 角度 —— 决定量化墨阶落在第几阶
//   · 场景里的墙地柱用的是不是 URP/Lit 白盒材质（那就是"积木"的来源）
// 只读，不写任何资产。
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    var scene = SceneManager.GetActiveScene();
    sb.AppendLine("场景 = " + scene.name + "   isDirty=" + scene.isDirty + "   根对象=" + scene.rootCount);
    sb.AppendLine();

    // ---------------- 环境光照 ----------------
    sb.AppendLine("===== RenderSettings（环境光） =====");
    sb.AppendLine("  ambientMode        = " + RenderSettings.ambientMode);
    sb.AppendLine("  ambientIntensity   = " + RenderSettings.ambientIntensity);
    sb.AppendLine("  ambientSkyColor    = " + C(RenderSettings.ambientSkyColor));
    sb.AppendLine("  ambientEquatorColor= " + C(RenderSettings.ambientEquatorColor));
    sb.AppendLine("  ambientGroundColor = " + C(RenderSettings.ambientGroundColor));
    sb.AppendLine("  fog                = " + RenderSettings.fog + "  color=" + C(RenderSettings.fogColor)
                  + " mode=" + RenderSettings.fogMode);
    sb.AppendLine("  skybox             = " + (RenderSettings.skybox != null ? RenderSettings.skybox.name : "(无)"));
    sb.AppendLine("  reflectionIntensity= " + RenderSettings.reflectionIntensity);
    sb.AppendLine();

    // ---------------- 灯光 ----------------
    sb.AppendLine("===== 灯光 =====");
    var lights = UnityEngine.Object.FindObjectsOfType<Light>(true);
    sb.AppendLine("  数量 = " + lights.Length);
    foreach (var l in lights)
    {
        sb.AppendLine("  · [" + l.type + "] " + PathOf(l.gameObject)
                      + "  color=" + C(l.color)
                      + "  intensity=" + F(l.intensity)
                      + "  bounce=" + F(l.bounceIntensity)
                      + "  shadows=" + l.shadows
                      + "  shadowStrength=" + F(l.shadowStrength)
                      + "  lightmapping=" + l.lightmapBakeType
                      + "  euler=" + V3(l.transform.eulerAngles)
                      + "  active=" + l.gameObject.activeInHierarchy);
    }
    sb.AppendLine();

    // ---------------- 渲染器 / 材质 ----------------
    sb.AppendLine("===== 场景渲染器使用的材质 =====");
    var rends = UnityEngine.Object.FindObjectsOfType<Renderer>(true);
    var byMat = new System.Collections.Generic.Dictionary<string, int>();
    var order = new System.Collections.Generic.List<string>();
    foreach (var r in rends)
    {
        var mats = r.sharedMaterials;
        if (mats == null) continue;
        foreach (var m in mats)
        {
            if (m == null) { continue; }
            string key = m.name + "  <" + (m.shader != null ? m.shader.name : "无Shader") + ">";
            if (!byMat.ContainsKey(key)) { byMat[key] = 0; order.Add(key); }
            byMat[key]++;
        }
    }
    order.Sort();
    foreach (var k in order) sb.AppendLine("  " + byMat[k].ToString().PadLeft(4) + " × " + k);
    sb.AppendLine();

    // ---------------- 白盒材质明细 ----------------
    sb.AppendLine("===== 白盒材质明细（BaseColor） =====");
    foreach (var path in AssetDatabase.FindAssets("M_Whitebox t:Material"))
    {
        string p = AssetDatabase.GUIDToAssetPath(path);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (mat == null) continue;
        string bc = mat.HasProperty("_BaseColor") ? C(mat.GetColor("_BaseColor")) : "(无 _BaseColor)";
        string sh = mat.shader != null ? mat.shader.name : "(无)";
        sb.AppendLine("  " + Path.GetFileName(p) + "  shader=" + sh + "  base=" + bc
                      + "  队列=" + mat.renderQueue
                      + "  关键字=" + string.Join(",", mat.shaderKeywords));
    }
    sb.AppendLine();

    // ---------------- 场景对象树（只看有渲染器的根，方便定位是哪些物体） ----------------
    sb.AppendLine("===== 用了白盒材质的对象（最多 40 个） =====");
    int n = 0;
    foreach (var r in rends)
    {
        bool white = false;
        string names = "";
        foreach (var m in r.sharedMaterials)
        {
            if (m == null) continue;
            if (m.name.StartsWith("M_Whitebox")) { white = true; names += m.name + " "; }
        }
        if (!white) continue;
        sb.AppendLine("  " + PathOf(r.gameObject) + "  边界中心=" + V3(r.bounds.center)
                      + "  尺寸=" + V3(r.bounds.size) + "  → " + names);
        if (++n >= 40) { sb.AppendLine("  …（截断）"); break; }
    }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/r_scene.txt"), sb.ToString());
    Debug.Log("[r_scene] done");
    yield return null;
}

static string C(Color c) { return "(" + F(c.r) + ", " + F(c.g) + ", " + F(c.b) + ", " + F(c.a) + ")"; }
static string V3(Vector3 v) { return "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")"; }
static string F(float f) { return f.ToString("0.###"); }
static string PathOf(GameObject go)
{
    string s = go.name;
    var t = go.transform.parent;
    while (t != null) { s = t.name + "/" + s; t = t.parent; }
    return s;
}

return Body();
