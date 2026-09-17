using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;

// dg_smr_diff.cs —— 不再猜假设：把「能渲染的 MoGu」与「不能渲染的 MoShan/MoGuai」的
// SkinnedMeshRenderer 全部序列化字段 dump 出来，逐项 diff，让差异自己冒出来。
//
// 已排除（别再重复）：取景(含大余量/固定6m)、尺度、材质属性/贴图/renderQueue、材质互换、
//   网格索引(idx>0)、可见性、layer/cullingMask、视锥剔除设置、色空间。
public class dg_smr_diff : MonoBehaviour
{
    readonly StringBuilder _sb = new StringBuilder();
    readonly List<GameObject> _spawned = new List<GameObject>();
    int _phase, _wait;
    readonly Dictionary<string, string>[] _maps = new Dictionary<string, string>[3];
    static readonly string[] Names = { "MoGu", "MoShan", "MoGuai" };
    static readonly string[] Paths =
    {
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGu.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoShan.prefab",
        "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab"
    };
    const string ReportPath = "D:/Unity Project/InkWash/Tools/reports/dg_smr_diff.txt";

    void Awake()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go != gameObject && go.name.StartsWith("dg_")) Destroy(go);
        _sb.AppendLine("===== dg_smr_diff =====");
        _sb.AppendLine("口径：每个 prefab 取『renderer.bounds 最大的那个 SMR』，dump 全部序列化字段后 diff。");
        _sb.AppendLine();
    }

    void Start() { _phase = 0; _wait = 2; }

    void Update()
    {
        if (_wait > 0) { _wait--; return; }

        if (_phase == 0)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Paths[i]);
                if (pf == null) { _sb.AppendLine("### " + Names[i] + " prefab 加载失败"); _maps[i] = new Dictionary<string, string>(); continue; }
                var inst = Instantiate(pf, new Vector3(0f, 300f + i * 60f, 0f), Quaternion.identity);
                inst.name = "dg_diff_" + Names[i];
                foreach (var ag in inst.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) ag.enabled = false;
                _spawned.Add(inst);
            }
            _phase = 1; _wait = 3;
            return;
        }

        if (_phase == 1)
        {
            for (int i = 0; i < Names.Length; i++)
                _maps[i] = DumpBiggest(Names[i], _spawned[i]);
            _phase = 2; _wait = 1;
            return;
        }

        // phase 2：以 MoGu 为基准 diff
        var baseM = _maps[0];
        for (int i = 1; i < Names.Length; i++)
        {
            _sb.AppendLine("### MoGu  vs  " + Names[i] + "   （只列不同的字段）");
            var m = _maps[i];
            int diff = 0;
            foreach (var kv in baseM)
            {
                string other;
                if (!m.TryGetValue(kv.Key, out other)) { _sb.AppendLine("    [仅 MoGu 有] " + kv.Key + " = " + kv.Value); diff++; continue; }
                if (other != kv.Value)
                {
                    _sb.AppendLine("    " + kv.Key.PadRight(42) + "  MoGu = " + kv.Value);
                    _sb.AppendLine("    " + "".PadRight(42) + "  " + Names[i] + " = " + other);
                    diff++;
                }
            }
            foreach (var kv in m)
                if (!baseM.ContainsKey(kv.Key)) { _sb.AppendLine("    [仅 " + Names[i] + " 有] " + kv.Key + " = " + kv.Value); diff++; }
            _sb.AppendLine("    ---- 差异字段数 = " + diff);
            _sb.AppendLine();
        }

        // 关键枚举直接印（SerializedObject 里可能显示为 int，这里印人类可读值）
        for (int i = 0; i < Names.Length; i++)
        {
            var smr = Biggest(_spawned[i]);
            if (smr == null) continue;
            _sb.AppendLine("[" + Names[i] + "] quality=" + smr.quality
                          + "  updateWhenOffscreen=" + smr.updateWhenOffscreen
                          + "  forceMatrixRecal=" + smr.forceMatrixRecalculationPerRender
                          + "  enabled=" + smr.enabled
                          + "  bones=" + (smr.bones == null ? -1 : smr.bones.Length)
                          + "  rootBone=" + (smr.rootBone == null ? "<null>" : smr.rootBone.name)
                          + "  localBounds=" + smr.localBounds.size.ToString("F3"));
        }

        Finish();
    }

    SkinnedMeshRenderer Biggest(GameObject go)
    {
        SkinnedMeshRenderer best = null; float bestV = -1f;
        foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            float v = s.bounds.size.x * s.bounds.size.y * s.bounds.size.z;
            if (v > bestV) { bestV = v; best = s; }
        }
        return best;
    }

    Dictionary<string, string> DumpBiggest(string name, GameObject go)
    {
        var map = new Dictionary<string, string>();
        var smr = Biggest(go);
        _sb.AppendLine("### " + name + "   SMR 总数=" + go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length
                      + "   选中=" + (smr == null ? "<none>" : smr.name));
        if (smr == null) return map;

        var so = new SerializedObject(smr);
        var it = so.GetIterator();
        bool enter = true;
        while (it.NextVisible(enter))
        {
            enter = false;
            string path = it.propertyPath;
            string val;
            try
            {
                switch (it.propertyType)
                {
                    case SerializedPropertyType.ObjectReference:
                        val = it.objectReferenceValue == null ? "null" : (it.objectReferenceValue.name + " (fileID=" + it.objectReferenceInstanceIDValue + ")");
                        break;
                    case SerializedPropertyType.Integer: val = it.longValue.ToString(); break;
                    case SerializedPropertyType.Boolean: val = it.boolValue.ToString(); break;
                    case SerializedPropertyType.Float: val = it.floatValue.ToString("F6"); break;
                    case SerializedPropertyType.String: val = it.stringValue; break;
                    case SerializedPropertyType.Vector3: val = it.vector3Value.ToString("F4"); break;
                    case SerializedPropertyType.Vector4: val = it.vector4Value.ToString("F4"); break;
                    case SerializedPropertyType.Bounds: val = it.boundsValue.center.ToString("F4") + " / " + it.boundsValue.size.ToString("F4"); break;
                    case SerializedPropertyType.Color: val = it.colorValue.ToString("F4"); break;
                    case SerializedPropertyType.Rect: val = it.rectValue.ToString(); break;
                    case SerializedPropertyType.ArraySize: val = it.intValue.ToString(); break;
                    case SerializedPropertyType.Enum: val = it.enumValueIndex.ToString(); break;
                    default: val = "(" + it.propertyType + ")"; break;
                }
            }
            catch (Exception e) { val = "<读异常:" + e.Message + ">"; }
            map[path] = val;
            _sb.AppendLine("    " + path.PadRight(40) + " = " + val);
        }
        _sb.AppendLine();
        return map;
    }

    void Finish()
    {
        string s = _sb.ToString();
        Debug.Log(s);
        try { File.WriteAllText(ReportPath, s); } catch { }
        foreach (var g in _spawned) if (g != null) Destroy(g);
        enabled = false;
    }

    void OnDestroy() { foreach (var g in _spawned) if (g != null) Destroy(g); }
}

var g = new GameObject("dg_smr_diff");
g.AddComponent<dg_smr_diff>();
return "DG_SMR_DIFF_STARTED";
