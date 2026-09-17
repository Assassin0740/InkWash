using System;
using System.Text;
using UnityEngine;

// sc_probe3：为什么敌人看不见？查材质 / 层 / 渲染器开关 / 包围盒
public class sc_probe3 : MonoBehaviour
{
    void Start()
    {
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab");
        var go = Instantiate(pf, new Vector3(0f, 0.05f, 0f), Quaternion.identity);
        go.name = "PROBE3_Enemy";

        var sb = new StringBuilder();
        sb.AppendLine("[sc_probe3] ===== 敌人可见性排查 =====");
        sb.AppendLine("[sc_probe3] layer=" + go.layer + " (" + LayerMask.LayerToName(go.layer) + ")");
        sb.AppendLine("[sc_probe3] activeInHierarchy=" + go.activeInHierarchy);

        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            sb.AppendLine(string.Format("[sc_probe3] {0} '{1}' layer={2} enabled={3} active={4} matCount={5}",
                r.GetType().Name, r.name, r.gameObject.layer, r.enabled,
                r.gameObject.activeInHierarchy, r.sharedMaterials.Length));
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) { sb.AppendLine("[sc_probe3]    <null material>"); continue; }
                sb.AppendLine(string.Format("[sc_probe3]    mat='{0}' shader='{1}' shaderExists={2}",
                    m.name, m.shader != null ? m.shader.name : "<null>",
                    m.shader != null && m.shader.name != "Hidden/InternalErrorShader"));
            }
        }

        var cam = Camera.main;
        sb.AppendLine("[sc_probe3] 相机 cullingMask=0x" + cam.cullingMask.ToString("X8") + " pos=" + cam.transform.position);
        sb.AppendLine("[sc_probe3] 相机能看见 layer " + go.layer + " = " + ((cam.cullingMask & (1 << go.layer)) != 0));
        sb.AppendLine("[sc_probe3] quality shadows / 距离=" + Vector3.Distance(cam.transform.position, go.transform.position));
        Debug.Log(sb.ToString());
    }
}

var g = new GameObject("sc_probe3");
g.AddComponent<sc_probe3>();
return "SC_PROBE3_STARTED";
