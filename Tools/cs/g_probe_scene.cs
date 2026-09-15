// g-1：场景体检 —— 清单里所有 Renderer / Canvas，并从相机对屏幕若干点做射线（定位"地上那条带子"）
using System;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

var sb = new StringBuilder();

string PathOf(Transform t)
{
    var s = t.name;
    var p = t.parent;
    while (p != null) { s = p.name + "/" + s; p = p.parent; }
    return s;
}

// ---------- 相机 ----------
var cam = Camera.main;
if (cam == null)
{
    var cams = UnityEngine.Object.FindObjectsOfType<Camera>();
    if (cams.Length > 0) cam = cams[0];
}
sb.AppendLine("=== 相机 ===");
if (cam == null) sb.AppendLine("  未找到相机");
else sb.AppendLine("  " + PathOf(cam.transform) + "  pos=" + cam.transform.position.ToString("F2")
                    + "  rot=" + cam.transform.eulerAngles.ToString("F1")
                    + "  fov=" + cam.fieldOfView + "  near=" + cam.nearClipPlane + "  far=" + cam.farClipPlane
                    + "  mask=" + cam.cullingMask);

// ---------- 所有 Renderer ----------
sb.AppendLine();
sb.AppendLine("=== Renderer 清单 ===");
var rends = UnityEngine.Object.FindObjectsOfType<Renderer>();
Array.Sort(rends, (a, b) => string.Compare(PathOf(a.transform), PathOf(b.transform), StringComparison.Ordinal));
foreach (var r in rends)
{
    var b = r.bounds;
    var m = r.sharedMaterial;
    sb.AppendLine("  " + PathOf(r.transform)
        + "\n      mat=" + (m == null ? "null" : m.name) + "  shader=" + (m == null ? "-" : m.shader.name)
        + "  layer=" + LayerMask.LayerToName(r.gameObject.layer)
        + "  active=" + r.gameObject.activeInHierarchy
        + "\n      bounds c=" + b.center.ToString("F2") + " s=" + b.size.ToString("F2"));
}

// ---------- Canvas 清单（"地上带子"若是 UI 就会在这里）----------
sb.AppendLine();
sb.AppendLine("=== Canvas 清单 ===");
var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
foreach (var c in canvases)
{
    sb.AppendLine("  " + PathOf(c.transform) + "  renderMode=" + c.renderMode
        + "  sortingOrder=" + c.sortingOrder + "  active=" + c.gameObject.activeInHierarchy);
    foreach (Transform ch in c.transform)
        sb.AppendLine("      - " + ch.name + "  active=" + ch.gameObject.activeInHierarchy
            + "  size=" + (ch as RectTransform)?.sizeDelta.ToString("F0"));
}

// ---------- 光照与雾 ----------
sb.AppendLine();
sb.AppendLine("=== 光照 / 雾 ===");
var lights = UnityEngine.Object.FindObjectsOfType<Light>();
foreach (var l in lights)
    sb.AppendLine("  " + l.type + " " + PathOf(l.transform) + "  rot=" + l.transform.eulerAngles.ToString("F1")
        + "  intensity=" + l.intensity + "  shadows=" + l.shadows + "  strength=" + l.shadowStrength);
sb.AppendLine("  fog=" + RenderSettings.fog + " mode=" + RenderSettings.fogMode + " density=" + RenderSettings.fogDensity
    + " color=" + RenderSettings.fogColor.ToString("F2"));

// ---------- 屏幕射线 ----------
sb.AppendLine();
sb.AppendLine("=== 屏幕射线（归一化坐标 → 命中物体）===");
if (cam != null)
{
    float[] xs = { 0.08f, 0.30f, 0.55f, 0.80f, 0.95f };
    float[] ys = { 0.30f, 0.55f, 0.75f, 0.90f };
    foreach (var ny in ys)
    {
        var line = new StringBuilder("  y=" + ny.ToString("F2") + " : ");
        foreach (var nx in xs)
        {
            var ray = cam.ScreenPointToRay(new Vector3(nx * Screen.width, ny * Screen.height, 0f));
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 400f))
                line.Append("[" + nx.ToString("F2") + "]" + hit.collider.name + "@" + hit.distance.ToString("F1") + "m ");
            else
                line.Append("[" + nx.ToString("F2") + "](空) ");
        }
        sb.AppendLine(line.ToString());
    }
    sb.AppendLine("  Screen=" + Screen.width + "x" + Screen.height);
}

return sb.ToString();
