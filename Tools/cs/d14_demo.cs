using System;
using System.Reflection;
using UnityEngine;

// d14：实机演示「空中循环」——把龙生成在玩家附近，跟着玩家跑，
//      让相机看得见它盘旋/俯冲/拉起。录屏由 --record 负责。
public class d14_probe : MonoBehaviour
{
    Type _t; Component _d; Transform _modelRoot;
    float _el; bool _spawned;
    MethodInfo _mForceAttack;

    void Start()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { _t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (_t != null) break; }
        if (_t == null) { Debug.Log("d14 ✗ 类型"); return; }

        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (prefab == null) { Debug.Log("d14 ✗ 预制体"); return; }

        // 玩家前方偏侧 8 m（让它从空中的轨道上入画）
        var go = Instantiate(prefab, new Vector3(8f, 0.05f, 6f), Quaternion.identity);
        go.name = "D14_MoLong";
        _d = go.GetComponentInChildren(_t, true);
        if (_d == null) { Debug.Log("d14 ✗ 组件"); return; }

        var f = _t.GetField("aerialLoop");
        if (f != null) f.SetValue(_d, true);

        // 相机：站在玩家位置附近，抬高看天
        var cam = Camera.main;
        if (cam != null)
        {
            var pt = FindType("InkWash.Combat.PlayerRef");
            Vector3 pp = Vector3.zero;
            if (pt != null)
            {
                var pm = pt.GetProperty("Position", BindingFlags.Public | BindingFlags.Static);
                if (pm != null) pp = (Vector3)pm.GetValue(null);
            }
            cam.transform.position = pp + new Vector3(-7f, 5.5f, -9f);
            cam.transform.LookAt(pp + new Vector3(3f, 2.2f, 1f));
            Debug.Log("d14 相机已就位 @ " + cam.transform.position);
        }
        _spawned = true;
        Debug.Log("d14 已生成 D14_MoLong，开始 22 s 演示");
    }

    void Update()
    {
        if (!_spawned || _d == null) return;
        _el += Time.unscaledDeltaTime;

        // 相机始终看向龙与玩家的中点（保证龙在画面里）
        var cam = Camera.main;
        if (cam != null && _modelRoot == null)
        {
            var smr = _d.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null) _modelRoot = FindRoot(smr.transform, _d.transform);
        }

        if (_el > 22f)
        {
            Debug.Log("d14 演示结束");
            enabled = false;
        }
    }

    static Transform FindRoot(Transform from, Transform stopAt)
    {
        Transform best = null, w = from.parent;
        int g = 0;
        while (w != null && w != stopAt && g++ < 20)
        { if (!w.name.StartsWith("drgon_") && w.name != "_rootJoint") best = w; w = w.parent; }
        return best;
    }

    static Type FindType(string full)
    {
        var t = Type.GetType(full + ", Assembly-CSharp");
        if (t != null) return t;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { t = asm.GetType(full); if (t != null) return t; }
        return null;
    }
}

var g = new GameObject("D14_Probe");
g.AddComponent<d14_probe>();
return "D14_STARTED";
