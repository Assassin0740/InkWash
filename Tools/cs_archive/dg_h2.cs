using System;
using System.Reflection;
using System.Text;
using UnityEngine;

// dg_h2：自己生成一条龙并持续量高度，不依赖演示场的自动切条。
public class dg_h2 : MonoBehaviour
{
    GameObject _dragon;
    Component _comp;
    Type _ty;
    int _step; float _t;
    readonly StringBuilder _sb = new StringBuilder();

    void Start()
    {
        var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (pf == null) { Debug.Log("dg_h2 ✗ 预制体没找到"); enabled = false; return; }

        _dragon = Instantiate(pf, new Vector3(0f, 0.05f, 0f), Quaternion.identity);
        _dragon.name = "dg_h2_Dragon";
        // 玩家放远一点，让龙进 Chase
        var player = GameObject.Find("Player");
        if (player != null) player.transform.position = new Vector3(0f, 0.05f, 8f);

        _ty = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { var t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (t != null) { _ty = t; break; } }
        if (_ty == null) { Debug.Log("dg_h2 ✗ 没有 EnemyDragon 类型"); enabled = false; return; }
        _comp = _dragon.GetComponentInChildren(_ty, true);

        _sb.AppendLine("t(s) | tf.y | Visual.lp.y | root.lp.y | curLift | 首节Y | 尾节Y | airborne");
    }

    void Update()
    {
        _t += Time.deltaTime;
        if (_t < 1.0f) return;
        if (_t > 14.0f) { Flush(); enabled = false; return; }
        if (_comp == null) return;
        if (_step++ % 20 != 0) return;

        var fRootF = _ty.GetField("_modelRoot", BindingFlags.NonPublic | BindingFlags.Instance);
        var fBase = _ty.GetField("_modelRootBaseLocalPos", BindingFlags.NonPublic | BindingFlags.Instance);
        var fLift = _ty.GetField("_currentLift", BindingFlags.NonPublic | BindingFlags.Instance);
        var fSpine = _ty.GetField("_spine", BindingFlags.NonPublic | BindingFlags.Instance);
        var fAir = _ty.GetField("_airborne", BindingFlags.NonPublic | BindingFlags.Instance);
        var fForceAtk = _ty.GetMethod("ForceEnterAttackForTest", BindingFlags.Public | BindingFlags.Instance);

        // 4 s 时强制开一招，看俯冲高度
        if (_t > 4.0f && _t < 4.1f && fForceAtk != null)
        {
            try { fForceAtk.Invoke(_comp, null); } catch { }
            _sb.AppendLine(">>> 4.0s 强制开招");
        }

        var root = fRootF.GetValue(_comp) as Transform;
        Vector3 b = (Vector3)fBase.GetValue(_comp);
        float lift = fLift != null ? (float)fLift.GetValue(_comp) : -1f;
        bool air = fAir != null && (bool)fAir.GetValue(_comp);

        float y0 = 0f, yN = 0f;
        var raw = fSpine.GetValue(_comp) as System.Collections.IList;
        if (raw != null && raw.Count > 0)
        {
            y0 = (raw[0] as Transform).position.y;
            yN = (raw[raw.Count - 1] as Transform).position.y;
        }

        _sb.AppendLine(string.Format("{0,5:F2} | {1,6:F2} | {2,9:F2} | {3,9:F2} | {4,7:F2} | {5,6:F2} | {6,6:F2} | {7}",
            _t, _dragon.transform.position.y,
            root != null ? root.localPosition.y : -1f, b.y, lift, y0, yN, air));
    }

    void Flush()
    {
        Debug.Log(_sb.ToString());
        try { System.IO.File.WriteAllText("D:/Unity Project/InkWash/Tools/reports/dg_h2.txt", _sb.ToString()); } catch { }
    }

    void OnDestroy() { if (_dragon != null) Destroy(_dragon); }
}

var g = new GameObject("dg_h2");
g.AddComponent<dg_h2>();
return "DG_H2_STARTED";
