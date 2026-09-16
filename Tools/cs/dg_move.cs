using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// 龙的移动诊断：逐帧打印状态机 / 高度 / 距离 / 位移 / timeScale。
// 目的：判"零位移"到底是 ①没进 Chase ②PlayerRef 失效 ③timeScale=0 ④位移被丢。
public class DgMoveProbe : MonoBehaviour
{
    private static readonly StringBuilder _sb = new StringBuilder();
    private static string _path;
    private static Type _ty;
    private static Component _dragon;
    private static float _t0;
    private static float _next;
    private static int _n;
    private static Vector3 _p0;
    private static bool _hasP0;

    void Start()
    {
        _path = "D:/Unity Project/InkWash/Tools/reports/dg_move.txt";
        _ty = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = null;
            try { t = asm.GetType("InkWash.Enemies.EnemyDragon"); } catch { }
            if (t != null) { _ty = t; break; }
        }
        _t0 = Time.time;
        _next = 0f;
        _n = 0;
        _sb.AppendLine("=== DgMoveProbe ===");
        _sb.AppendLine("dragonType = " + (_ty != null ? _ty.FullName : "<null>"));
        _sb.AppendLine("timeScale  time  dt  | state  airborne lift  dist  pos  | swimDir  | posDelta/frame");
    }

    void Update()
    {
        if (Time.time - _t0 < _next) return;
        _next += 0.4f;
        _n++;
        if (_n > 32) { Flush(); Destroy(gameObject); return; }

        if (_dragon == null && _ty != null)
        {
            var all = UnityEngine.Object.FindObjectsOfType(_ty);
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i] as Component;
                if (c != null && c.gameObject.name.StartsWith("Showcase_Actor_")) { _dragon = c; break; }
            }
        }

        if (_dragon == null)
        {
            _sb.AppendLine(string.Format("{0,6:F2} {1,6:F2} {2,7:F3} | <找不到龙演员>", Time.timeScale, Time.time, Time.deltaTime));
            return;
        }

        Vector3 pos = _dragon.transform.position;
        float delta = 0f;
        if (_hasP0) delta = Vector3.Distance(pos, _p0);
        _p0 = pos; _hasP0 = true;

        _sb.AppendLine(string.Format(
            "{0,6:F2} {1,6:F2} {2,7:F3} | {3,-12} {4,-6} {5,5:F2} {6,6:F2} {7} | {8} | {9:F4}",
            Time.timeScale, Time.time, Time.deltaTime,
            Field("_state"), Field("_airborne"), Field("_currentLift"), Field("_distanceToPlayer"),
            string.Format("({0:F1},{1:F1},{2:F1})", pos.x, pos.y, pos.z),
            Field("aerialLoop"),
            delta));
    }

    private static object _lastObj;
    private static string Field(string name)
    {
        if (_dragon == null || _ty == null) return "-";
        FieldInfo f = _ty.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) return "<无" + name + ">";
        object v = null;
        try { v = f.GetValue(_dragon); } catch (Exception e) { return "ERR:" + e.Message; }
        if (v is float fv) return fv.ToString("F2");
        if (v is bool bv) return bv ? "True" : "False";
        return v != null ? v.ToString() : "<null>";
    }

    private static void Flush()
    {
        try { File.WriteAllText(_path, _sb.ToString()); }
        catch (Exception e) { Debug.LogError("[DgMove] 写报告失败 " + e.Message); }
    }
}

var probeGo = new GameObject("DgMoveProbe");
probeGo.AddComponent<DgMoveProbe>();
return "DG_MOVE_STARTED";
