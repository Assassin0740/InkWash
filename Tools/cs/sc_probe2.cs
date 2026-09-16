using System;
using UnityEngine;

// sc_probe2：直接生成一个敌人，打印它的世界位置 / 包围盒 / 缩放
public class sc_probe2 : MonoBehaviour
{
    void Start()
    {
        Shader.SetGlobalFloat("_Dummy", 1f);

        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab");
        if (pf == null) { Debug.Log("sc_probe2 ✗ 预制体"); return; }

        var go = Instantiate(pf, Vector3.zero, Quaternion.identity);
        go.name = "PROBE_Enemy";
        Debug.Log("[sc_probe2] 实例位置=" + go.transform.position + " 缩放=" + go.transform.lossyScale);

        var rends = go.GetComponentsInChildren<Renderer>(true);
        Debug.Log("[sc_probe2] 渲染器数=" + rends.Length);
        int i = 0;
        foreach (var r in rends)
        {
            i++;
            if (i <= 4)
                Debug.Log(string.Format("[sc_probe2]   [{0}] {1} {2} enabled={3} activeOK={4} 世界中心={5} 包围盒尺寸={6}",
                    i, r.GetType().Name, r.name, r.enabled, r.gameObject.activeInHierarchy,
                    r.bounds.center, r.bounds.size));
        }

        // 龙的
        var pf2 = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        var go2 = Instantiate(pf2, Vector3.zero, Quaternion.identity);
        go2.name = "PROBE_Dragon";
        var rends2 = go2.GetComponentsInChildren<Renderer>(true);
        Debug.Log("[sc_probe2] 龙 渲染器数=" + rends2.Length + " 缩放=" + go2.transform.lossyScale);
        i = 0;
        foreach (var r in rends2)
        {
            i++; if (i > 4) break;
            Debug.Log(string.Format("[sc_probe2]   龙[{0}] {1} {2} enabled={3} 世界中心={4} 尺寸={5}",
                i, r.GetType().Name, r.name, r.enabled, r.bounds.center, r.bounds.size));
        }

        // 相机与玩家位置
        var cam = Camera.main;
        Debug.Log("[sc_probe2] 相机=" + (cam != null ? cam.transform.position.ToString() : "<null>"));
        var pt = Type.GetType("InkWash.Combat.PlayerRef, Assembly-CSharp");
        if (pt != null)
        {
            var fi = pt.GetField("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var tr = fi != null ? fi.GetValue(null) as Transform : null;
            if (tr != null) Debug.Log("[sc_probe2] 玩家=" + tr.position);
        }
    }
}

var g = new GameObject("sc_probe2");
g.AddComponent<sc_probe2>();
return "SC_PROBE2_STARTED";
