using System;
using System.IO;
using System.Reflection;
using UnityEngine;

// d16：定点出图 —— 在"盘旋 / 俯冲 / 拉起"三个相位各截一张，
//      用固定机位（侧视 + 稍俯）保证三张可比。
public class d16_probe : MonoBehaviour
{
    Type _t; Component _d; Transform _modelRoot;
    float _el; int _shot; bool _ready;
    Camera _cam;
    string _dir;
    PropertyInfo _pPhase;
    MethodInfo _mForce;
    float _lastPhaseT;

    void Start()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        { _t = asm.GetType("InkWash.Enemies.EnemyDragon"); if (_t != null) break; }
        if (_t == null) { Debug.Log("d16 ✗ 类型"); return; }

        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
        if (prefab == null) { Debug.Log("d16 ✗ 预制体"); return; }

        var pt = FindType("InkWash.Combat.PlayerRef");
        Vector3 pp = Vector3.zero;
        if (pt != null)
        {
            var pm = pt.GetProperty("Position", BindingFlags.Public | BindingFlags.Static);
            if (pm != null) pp = (Vector3)pm.GetValue(null);
        }

        var go = Instantiate(prefab, pp + new Vector3(7f, 0.05f, 5f), Quaternion.identity);
        go.name = "D16_MoLong";
        _d = go.GetComponentInChildren(_t, true);
        if (_d == null) { Debug.Log("d16 ✗ 组件"); return; }

        var fLoop = _t.GetField("aerialLoop");
        if (fLoop != null) fLoop.SetValue(_d, true);

        _pPhase = _t.GetProperty("DivePhaseName", BindingFlags.Public | BindingFlags.Instance);
        _mForce = _t.GetMethod("ForceNextAttackForTest", BindingFlags.Public | BindingFlags.Instance);

        _dir = "D:/Unity Project/InkWash/Tools/screenshots/d16";
        Directory.CreateDirectory(_dir);

        _cam = Camera.main;
        if (_cam != null)
        {
            // 固定机位：站在玩家侧后方，抬高到龙的高度附近
            _cam.transform.position = pp + new Vector3(-10f, 6.5f, -11f);
            _cam.transform.LookAt(pp + new Vector3(2f, 3.6f, 0.5f));
            Debug.Log("d16 机位 @ " + _cam.transform.position);
        }
        _ready = true;
        Debug.Log("d16 就位，开始相位出图");
    }

    void Update()
    {
        if (!_ready || _d == null || _cam == null) return;
        _el += Time.unscaledDeltaTime;

        string ph = _pPhase != null ? (_pPhase.GetValue(_d, null) as string) : "-";

        // 相机锁定龙（保证构图里有龙）
        var smr = _d.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr != null && _modelRoot == null) _modelRoot = FindRoot(smr.transform, _d.transform);
        if (_modelRoot != null)
            _cam.transform.LookAt(_modelRoot.position + Vector3.up * 1.2f);

        // 每 2.5 s 一张，直到 6 张（覆盖至少两个完整俯冲循环）
        if (_el - _lastPhaseT > 2.5f && _shot < 6)
        {
            _lastPhaseT = _el;
            _shot++;
            string fn = string.Format("{0}/d16_{1}_{2:00}.png", _dir, ph, _shot);
            StartCoroutine(Shot(fn, ph));
        }

        if (_shot >= 6 && _el > 3f)
        {
            Debug.Log("d16 出图完成");
            enabled = false;
        }
    }

    System.Collections.IEnumerator Shot(string path, string ph)
    {
        yield return new WaitForEndOfFrame();
        int w = Screen.width, h = Screen.height;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        _cam.targetTexture = prev;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Destroy(tex); Destroy(rt);
        Debug.Log("d16 出图 " + Path.GetFileName(path) + " (phase=" + ph + ")");
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

var g16 = new GameObject("D16_Probe");
g16.AddComponent<d16_probe>();
return "D16_STARTED";
