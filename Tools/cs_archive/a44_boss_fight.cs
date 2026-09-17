// a44_boss_fight.cs —— 墨龙实战验收（非阻塞版）
//
// ⚠ 上一版被 Codely 拦了：`Thread.Sleep` 在 exec_runtime_script 里禁止
//   （脚本跑在 Unity 主线程，sleep 会冻编辑器）。
//   正解：挂一个 **MonoBehaviour 探针**，让它在自己的 Update 里逐帧采样，
//        采样完成后写报告 + 自毁。exec_cs 立刻返回，不阻塞。
//
// 验收目标（用户要「很多的战斗」⇒ 必须证明龙**会打**）：
//   ① 四招都触发过（甩尾/撕咬/龙息/盘旋）
//   ② 每招触发时**脊骨确实在动**（不是只改了状态名）
//   ③ 墨弹真的生成过（龙息有判定）
//   ④ 盘旋时真的升空（Y 升高）
//   ⑤ 玩家真的掉血（攻击有伤害）
//   ⑥ 全程抓异常
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InkWash.Enemies;
using UnityEditor;
using UnityEngine;

var root = Path.GetDirectoryName(Application.dataPath);
var existing = UnityEngine.Object.FindObjectOfType<DragonProbe>();
if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

// 找玩家
GameObject player = null;
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.CompareTag("Player")) { player = g; break; }
if (player == null)
{
    Debug.LogError("[a44] 找不到 Player");
    return;
}

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
if (prefab == null) { Debug.LogError("[a44] 读不到 Z_Enemy_MoLong.prefab"); return; }

Vector3 spawn = player.transform.position + player.transform.forward * 9f;
spawn.y = player.transform.position.y;

// ★ 必须落到 NavMesh 上，否则 NavMeshAgent 建不起来
//   （桥会 Warn: `Failed to create agent because it is not close enough to the NavMesh`），
//   龙会自由落体 + 状态机走不动，整个验收就是假的。
UnityEngine.AI.NavMeshHit navHit;
if (UnityEngine.AI.NavMesh.SamplePosition(spawn, out navHit, 8f, UnityEngine.AI.NavMesh.AllAreas))
{
    spawn = navHit.position;
    Debug.Log("[a44] 生成点已吸附到 NavMesh: " + spawn.ToString("F2"));
}
else Debug.LogWarning("[a44] 生成点附近 8 m 内没有 NavMesh，agent 可能建不起来");

var go = (GameObject)UnityEngine.Object.Instantiate(prefab, spawn,
    Quaternion.LookRotation((player.transform.position - spawn).normalized, Vector3.up));
go.name = "Z_Enemy_MoLong_Test";

var probe = go.AddComponent<DragonProbe>();
probe.player = player;
probe.reportPath = Path.Combine(root, "Tools/reports/a44_boss_fight.txt");
probe.duration = 30f;
Debug.Log("[a44] 探针已挂上，龙已刷出：spawn=" + spawn.ToString("F2"));

// ============ 探针（挂在龙身上，逐帧采样） ============
public class DragonProbe : MonoBehaviour
{
    public GameObject player;
    public string reportPath;
    public float duration = 30f;

    private EnemyDragon _dragon;
    private InkWash.Player.PlayerHealth _php;
    private List<Transform> _chain = new List<Transform>();
    private List<Quaternion> _baseRot = new List<Quaternion>();
    private readonly Dictionary<string, int> _frames = new Dictionary<string, int>();
    private readonly Dictionary<string, float> _maxMotion = new Dictionary<string, float>();
    private int _totalFrames, _movingFrames, _breathSpawns;
    private float _maxY = float.MinValue, _minY = float.MaxValue;
    private float _startHp, _startTime;
    private bool _done;

    void Start()
    {
        _dragon = GetComponent<EnemyDragon>();
        if (player != null) _php = player.GetComponent<InkWash.Player.PlayerHealth>();
        _startHp = _php != null ? _php.Health : 0f;
        _startTime = Time.time;

        var smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr != null && smr.rootBone != null)
        {
            var cur = smr.rootBone;
            int g0 = 0;
            while (cur != null && cur.childCount == 1 && cur.name == "_rootJoint" && g0++ < 5) cur = cur.GetChild(0);
            int guard = 0;
            while (cur != null && _chain.Count < 80 && guard++ < 300)
            {
                if (cur.GetComponent<SkinnedMeshRenderer>() != null) break;
                _chain.Add(cur);
                Transform nxt = null;
                for (int i = 0; i < cur.childCount; i++)
                {
                    var c = cur.GetChild(i);
                    if (c.GetComponent<SkinnedMeshRenderer>() != null) continue;
                    nxt = c; break;
                }
                cur = nxt;
            }
            foreach (var t in _chain) _baseRot.Add(t.localRotation);
        }
    }

    void Update()
    {
        if (_done || _dragon == null) return;
        if (!_dragon.gameObject.activeInHierarchy) return;

        _totalFrames++;
        var name = _dragon.CurrentAttackName ?? "-";

        float motion = 0f;
        for (int i = 0; i < _chain.Count; i++)
            motion += Quaternion.Angle(_chain[i].localRotation, _baseRot[i]);

        if (motion > 0.5f)
        {
            _movingFrames++;
            if (!_frames.ContainsKey(name)) { _frames[name] = 0; _maxMotion[name] = 0f; }
            _frames[name]++;
            _maxMotion[name] = Mathf.Max(_maxMotion[name], motion);
        }

        float y = transform.position.y;
        _maxY = Mathf.Max(_maxY, y); _minY = Mathf.Min(_minY, y);

        foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
            if (g != null && g.name != null && g.name.StartsWith("DragonBreath_")) _breathSpawns++;

        if (Time.time - _startTime >= duration) Finish();
    }

    void Finish()
    {
        _done = true;
        var sb = new StringBuilder();
        sb.AppendLine("========== a44 墨龙实战验收 ==========");
        sb.AppendLine("时长 = " + duration + " s   采样帧 = " + _totalFrames);
        sb.AppendLine("脊骨链节数 = " + _chain.Count);
        sb.AppendLine();
        sb.AppendLine("---- ① 四招触发（只统计「该招 + 骨骼确实在动」的帧）----");
        int hit = 0;
        foreach (var k in new[] { "甩尾", "撕咬", "龙息", "盘旋" })
        {
            int c = _frames.ContainsKey(k) ? _frames[k] : 0;
            float m = _maxMotion.ContainsKey(k) ? _maxMotion[k] : 0f;
            bool ok = c > 0 && m > 1f;
            if (ok) hit++;
            sb.AppendLine("  " + k + " : " + c + " 帧   最大骨骼偏移 = " + m.ToString("F1") + "°   " + (ok ? "✓" : "★"));
        }
        sb.AppendLine("  ⇒ 触发 " + hit + " / 4");
        sb.AppendLine("  （出现过的状态名: " + string.Join(", ", new List<string>(_frames.Keys).ToArray()) + "）");
        var rep = new List<string>();
        foreach (var kv in _frames) rep.Add(kv.Key + "=" + kv.Value);
        sb.AppendLine("  （全部状态名帧数: " + string.Join(", ", rep.ToArray()) + "）");
        sb.AppendLine();
        sb.AppendLine("---- ② 计数与判定 ----");
        sb.AppendLine("  有骨骼位移的帧 = " + _movingFrames + " / " + _totalFrames
                      + "  (" + (100f * _movingFrames / Mathf.Max(1, _totalFrames)).ToString("F0") + "%)");
        sb.AppendLine("  甩尾=" + _dragon.TailSweepCount + " 撕咬=" + _dragon.BiteCount
                      + " 龙息=" + _dragon.BreathCount + " 盘旋=" + _dragon.HoverCount);
        sb.AppendLine("  SpineLinksFound = " + _dragon.SpineLinksFound);
        sb.AppendLine();
        sb.AppendLine("---- ③ 龙息墨弹 ----");
        sb.AppendLine("  检测到 DragonBreath_* 的累计计数 = " + _breathSpawns
                      + (_breathSpawns > 0 ? "  ✓ 有生成" : "  ★ 从未生成"));
        sb.AppendLine();
        sb.AppendLine("---- ④ 盘旋升空 ----");
        sb.AppendLine("  Y 范围 [" + _minY.ToString("F2") + ", " + _maxY.ToString("F2") + "]  最大升幅 = " + (_maxY - _minY).ToString("F2") + " m");
        sb.AppendLine("  最后 Airborne = " + _dragon.Airborne);
        sb.AppendLine();
        sb.AppendLine("---- ⑤ 伤害 ----");
        float endHp = _php != null ? _php.Health : 0f;
        sb.AppendLine("  玩家 HP " + _startHp.ToString("F1") + " → " + endHp.ToString("F1")
                      + "   掉血 = " + (_startHp - endHp).ToString("F1") + ((_startHp - endHp) > 0.01f ? "  ✓" : "  ★"));
        sb.AppendLine("  龙 HP = " + _dragon.Health.ToString("F1") + "/" + _dragon.maxHealth);

        File.WriteAllText(reportPath, sb.ToString());
        Debug.Log("[a44]\n" + sb.ToString());
        UnityEngine.Object.Destroy(gameObject);
    }
}
