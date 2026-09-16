// a55_breath_diag.cs —— 龙息为什么不吐弹
//
// a54 读数：四招的「招式名 / 脊骨偏移 / 升空」全绿，唯独 TailSweep/Bite/Breath 三招
// 「新增墨弹 = 0」。升空那招验证了 `ForceEnterAttackForTest` 是有效的，
// 所以问题在 `SpawnBreath` 这条路径本身，不在状态机。
//
// `TickAttack` 的判定触发条件：
//     float prev = _stateTime - Time.deltaTime;
//     if (prev < hitAt && t >= hitAt) PerformHit();   // hitAt = attackWindup
// 逐帧盯着 `_stateTime / attackWindup / breathProjectilePrefab / 场上墨弹数`，
// 看是"判定帧没跨过"还是"prefab 为空"。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InkWash.Enemies;
using UnityEditor;
using UnityEngine;

var root = Path.GetDirectoryName(Application.dataPath);

foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.name.StartsWith("A55_") || g.name.StartsWith("A54_") || g.name.StartsWith("HoverTest"))
        UnityEngine.Object.DestroyImmediate(g);

var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
ground.name = "A55_Ground";
ground.transform.position = new Vector3(0f, -0.6f, 0f);
ground.transform.localScale = new Vector3(140f, 1f, 140f);

GameObject player = null;
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.CompareTag("Player")) { player = g; break; }
if (player != null)
{
    var cc = player.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    player.transform.position = new Vector3(0f, 0.3f, 11f);
    if (cc != null) cc.enabled = true;
}

var ws = UnityEngine.Object.FindObjectOfType<WaveSpawner>();
if (ws != null) ws.enabled = false;

var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
var dg = p != null ? PrefabUtility.InstantiatePrefab(p) as GameObject : null;
if (dg != null) dg.transform.position = new Vector3(0f, 0.3f, 6f);

var go = new GameObject("A55_Probe");
var probe = go.AddComponent<BreathProbe>();
probe.dragon = dg;
probe.reportPath = Path.Combine(root, "Tools/reports/a55_breath_diag.txt");
Debug.Log("[a55] 探针已挂, dragon=" + (dg != null));

public class BreathProbe : MonoBehaviour
{
    public GameObject dragon;
    public string reportPath;

    private EnemyDragon _d;
    private float _t;
    private readonly List<string> _rows = new List<string>();
    private int _phase;   // 0=等 Awake, 1=准备, 2=观察中
    private float _phaseT;
    private bool _forced;
    private int _lastProjCount;
    private int _spawnSeen;
    private float _lastAttackNameChangeT;

    private void Start()
    {
        var t = typeof(EnemyDragon);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        _d = dragon != null ? dragon.GetComponent<EnemyDragon>() : null;
    }

    private void Update()
    {
        _t += Time.deltaTime;
        _phaseT += Time.deltaTime;

        if (_d == null) { if (_t > 12f) Finish(); return; }

        // 场上墨弹数
        int proj = UnityEngine.Object.FindObjectsOfType<InkProjectile>().Length;
        if (proj > _lastProjCount) _spawnSeen += proj - _lastProjCount;
        _lastProjCount = proj;

        // 反射读私有字段
        var t = typeof(InkWash.Enemies.EnemyBase);
        var fState = t.GetField("_state", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var fStateTime = t.GetField("_stateTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var fCd = t.GetField("_cooldownTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var fWind = t.GetField("attackWindup", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var fAct = t.GetField("attackActive", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        string st = fState != null ? fState.GetValue(_d).ToString() : "?";
        string stt = fStateTime != null ? ((float)fStateTime.GetValue(_d)).ToString("F2") : "?";
        string cd = fCd != null ? ((float)fCd.GetValue(_d)).ToString("F2") : "?";
        string wind = fWind != null ? fWind.GetValue(_d).ToString() : "?";
        string act = fAct != null ? fAct.GetValue(_d).ToString() : "?";

        // 反射读 prerfab
        var fPrefab = typeof(EnemyDragon).GetField("breathProjectilePrefab",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        string prefabName = "-";
        if (fPrefab != null)
        {
            var v = fPrefab.GetValue(_d) as GameObject;
            prefabName = v != null ? v.name : "★NULL";
        }

        if (_t > 1.5f && !_forced)
        {
            _forced = true;
            _rows.Add("=== 触发前状态 ===");
            _rows.Add("  breathProjectilePrefab = " + prefabName);
            _rows.Add("  SpineLinks = " + _d.SpineLinksFound);
            _d.ForceNextAttackForTest("Breath");
            _d.ForceEnterAttackForTest();
            _rows.Add("  已 ForceNextAttackForTest(Breath) + ForceEnterAttackForTest()");
            _rows.Add("");
            _rows.Add("=== 逐帧追踪（t 相对触发时刻）===");
        }

        if (_forced && _rows.Count < 90)
        {
            _rows.Add(string.Format("  +{0:F3}s  state={1,-10} stateTime={2}  windup={3} active={4}  cd={5}  attack={6}  proj={7}",
                _phaseT, st, stt, wind, act, cd, _d.CurrentAttackName, proj));
        }

        if (_t > 7f) Finish();
    }

    private void Finish()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== a55 龙息吐弹诊断 ==========");
        sb.AppendLine("累计看到的新生墨弹 = " + _spawnSeen);
        sb.AppendLine();
        foreach (var r in _rows) sb.AppendLine(r);
        File.WriteAllText(reportPath, sb.ToString());
        Debug.Log("[a55]\n" + sb.ToString());
        Destroy(gameObject);
    }
}
