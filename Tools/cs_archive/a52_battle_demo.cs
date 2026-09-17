// a52_battle_demo.cs —— 程序驱动的多波次实战（用户要的"很多的战斗"）
//
// 目标：一场从第一波打到第四波·墨龙 BOSS 的完整实战，全程录屏 + 出对比图。
//
// 驱动链：
//   ① 重置试玩台（清残留 + 玩家回位 + 血量恢复）
//   ② 保底平地（防止掉出世界，a46 的教训）
//   ③ 挂 BattleCam 跟拍相机（俯视偏后，同时看见玩家与龙）
//   ④ 挂 AutoPlayer：程序控制玩家走位 + 攻击（走 PlayerController.SetInjectedMove 注入口）
//   ⑤ BattleProbe 逐帧记录 + 每 1 s 截图
//
// ★ Codely --runtime 禁阻塞 ⇒ 全部靠 MonoBehaviour 的 Update 驱动。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InkWash.Combat;
using InkWash.Enemies;
using InkWash.Player;
using UnityEditor;
using UnityEngine;

var root = Path.GetDirectoryName(Application.dataPath);
var shotDir = Path.Combine(root, "Tools/screenshots/battle52");
Directory.CreateDirectory(shotDir);

var sb = new StringBuilder();
sb.AppendLine("========== a52 多波次实战 ==========");

// ---- 清残留 ----
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.name.StartsWith("A52_") || g.name.StartsWith("HoverTest") || g.name.StartsWith("A49_")
        || g.name.StartsWith("A48_"))
        UnityEngine.Object.DestroyImmediate(g);

// ---- 保底平地 ----
var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
ground.name = "A52_Ground";
ground.transform.position = new Vector3(0f, -0.6f, 0f);
ground.transform.localScale = new Vector3(120f, 1f, 120f);

// ---- 玩家回位 + 回血 ----
GameObject player = null;
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.CompareTag("Player")) { player = g; break; }
if (player != null)
{
    var cc = player.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    player.transform.position = new Vector3(0f, 0.3f, -3f);
    if (cc != null) cc.enabled = true;
    sb.AppendLine("玩家回位 = " + player.transform.position.ToString("F2"));

    var ph = player.GetComponent<PlayerHealth>();
    if (ph != null) { ph.ResetHealth(); sb.AppendLine("玩家血量已满 = " + ph.EffectiveMaxHealth); }
}
else sb.AppendLine("★ 找不到玩家");

// ---- WaveSpawner ----
var ws = UnityEngine.Object.FindObjectOfType<WaveSpawner>();
if (ws != null)
{
    ws.Begin();
    sb.AppendLine("WaveSpawner 已 Begin，波数 = " + ws.WaveCount);
}
else sb.AppendLine("★ 找不到 WaveSpawner");

// ---- 相机 ----
var cam = Camera.main;
if (cam == null) cam = UnityEngine.Object.FindObjectOfType<Camera>();
if (cam != null)
{
    var rig = cam.gameObject.GetComponent<BattleCam>();
    if (rig == null) rig = cam.gameObject.AddComponent<BattleCam>();
    rig.player = player != null ? player.transform : null;
    sb.AppendLine("BattleCam 已挂，相机 = " + cam.name);
}

// ---- 探针 ----
var probeGo = new GameObject("A52_Probe");
var probe = probeGo.AddComponent<BattleProbe>();
probe.player = player != null ? player.transform : null;
probe.shotDir = shotDir;
probe.reportPath = Path.Combine(root, "Tools/reports/a52_battle_demo.txt");
probe.duration = 80f;
sb.AppendLine("BattleProbe 已挂，时长 = " + probe.duration + " s");

File.WriteAllText(Path.Combine(root, "Tools/reports/a52_launch.txt"), sb.ToString());
Debug.Log("[a52]\n" + sb.ToString());

// ==================================================================

/// <summary>跟拍相机：拉远俯视，把玩家与最近敌人一起框进来。</summary>
public class BattleCam : MonoBehaviour
{
    public Transform player;
    private Transform _boss;
    private readonly List<EnemyBase> _buf = new List<EnemyBase>();

    private void LateUpdate()
    {
        if (_boss == null)
        {
            var ds = UnityEngine.Object.FindObjectsOfType<EnemyDragon>();
            if (ds.Length > 0 && ds[0] != null) _boss = ds[0].transform;
        }

        // ★ 视野要包住"玩家 + 最近的那群敌人"，只跟玩家会把敌人甩出画面
        //   （第一版就是这样：80 张截图里几乎看不到敌人在打）
        Vector3 center = player != null ? player.position : transform.position;
        int n = 0;
        _buf.Clear();
        foreach (var e in UnityEngine.Object.FindObjectsOfType<EnemyBase>())
        {
            if (e == null || e.Health <= 0f) continue;
            float d = player != null ? Vector3.Distance(player.position, e.transform.position) : 0f;
            if (player == null || d < 14f) { _buf.Add(e); n++; if (n >= 6) break; }
        }
        if (_buf.Count > 0)
        {
            Vector3 eCenter = Vector3.zero;
            // 取最近的 3 只求中点，避免被远处的怪把镜头拽跑
            int take = Mathf.Min(3, _buf.Count);
            for (int i = 0; i < take; i++) eCenter += _buf[i].transform.position;
            eCenter /= take;
            center = Vector3.Lerp(center, eCenter, 0.5f);
        }
        if (_boss != null) center = Vector3.Lerp(center, _boss.position, 0.45f);

        // 高一点、远一点：7 m 高 / 9 m 后退在打多目标时视野不够
        Vector3 want = center + new Vector3(0f, 9.5f, -13.5f);
        transform.position = Vector3.Lerp(transform.position, want, 4.5f * Time.deltaTime);
        var look = Quaternion.LookRotation((center - transform.position).normalized, Vector3.up);
        transform.rotation = Quaternion.Lerp(transform.rotation, look, 4.5f * Time.deltaTime);
    }
}

/// <summary>程序驱动的玩家：朝最近敌人走 + 靠近就砍 + 血少时后撤。</summary>
public class AutoPlayer : MonoBehaviour
{
    private PlayerController _pc;
    private PlayerHealth _ph;
    private Transform _t;
    private float _atkTimer;
    private float _dashTimer;
    private float _strafeSign = 1f;
    private float _strafeFlipTimer;

    // ★ 接敌距离。这里踩过一次坑：
    //   第一版取 2.6 m（第 3 段前冲距离），想的是"冲过去刚好够到"。
    //   但**攻击前冲是瞬时的、接敌位置是持续的** —— 玩家停在 2.6 m 处起手，
    //   前冲 1.2~2.6 m 只在攻击窗口那 0.19 s 内有效，窗口一过又弹回 2.6 m。
    //   结果就是"原地挥空、被敌人白打"，录像里表现为「剩 1 只怪卡 70 秒」。
    //   墨兵攻击距离是 2.1 m ⇒ 要把接敌距离压到**它打得到我、我也打得到它**的重叠区，
    //   靠 0.65 s 无敌帧换输出。取 1.6 m。
    private const float EngageDist = 1.6f;

    private void Start()
    {
        _pc = GetComponent<PlayerController>();
        _ph = GetComponent<PlayerHealth>();
        _t = transform;
        if (_pc != null) _pc.BeginInputOverride();
    }

    private void Update()
    {
        if (_pc == null) return;

        // 阵亡就不动了（复活是另一套系统，不在本轮范围）
        bool alive = _ph == null || _ph.Health > 0f;

        EnemyBase best = null; float bestD = float.MaxValue;
        foreach (var e in UnityEngine.Object.FindObjectsOfType<EnemyBase>())
        {
            if (e == null || e.Health <= 0f) continue;
            float d = Vector3.Distance(_t.position, e.transform.position);
            if (d < bestD) { bestD = d; best = e; }
        }
        if (!alive || best == null) { _pc.SetInjectedMove(Vector2.zero, false); return; }

        Vector3 toE = best.transform.position - _t.position;
        toE.y = 0f;
        float dist = toE.magnitude;
        if (dist > 1e-4f) toE /= dist;

        // ★★ 注入的是「相机相对输入」，不是世界方向。
        //   `ResolveMoveDirection` 的定义是 `fwd * input.y + right * input.x`，
        //   其中 fwd/right 来自 `cameraTransform`。而 BattleCam 是俯视偏后的
        //   （y+9.5 / z-13.5），它的 forward 指向斜下方前 —— 直接把**世界方向**
        //   当作 input 喂进去会整体转 ~55°，角色会朝敌人**侧后方**走。
        //   这正是「贴身挥剑打不中」的根因：方向歪了，朝向就歪了，
        //   `ResolveAttackDir` 又回落到 `transform.forward`，于是剑砍在空气里。
        //   所以必须把世界方向**反解**成相机空间输入。
        Vector2 intent = WorldDirToCameraInput(toE);
        Vector2 strafe = WorldDirToCameraInput(new Vector3(-toE.z, 0f, toE.x));

        // ---- 行为策略（这才是"会玩的玩家"和"站着挨打"的区别）----
        //   血量 < 35% ⇒ 侧向绕圈（不是直线后撤）
        //     踩过的坑：最初写的是"垂直后退"，结果是**僵局** —— 敌人追、玩家退，
        //     两者距离永远维持在敌人攻击距离之外、玩家攻击距离之内，谁都不掉血，
        //     录像里表现为「剩 1 只怪卡 70 秒」。侧向绕圈能拉开角度、脱离包夹，
        //     也让自己一直处在"能反击"的距离。
        //   中距离 ⇒ 冲锋接敌
        //   贴身 ⇒ 停下连砍
        float hpRatio = _ph != null ? _ph.Health / Mathf.Max(1f, _ph.EffectiveMaxHealth) : 1f;

        // 绕圈方向每 2.5 s 换一次，避免一直贴着一个方向撞墙
        _strafeFlipTimer -= Time.deltaTime;
        if (_strafeFlipTimer <= 0f) { _strafeFlipTimer = 2.5f; _strafeSign = -_strafeSign; }

        if (hpRatio < 0.35f && dist < 4.5f)
        {
            // 侧向绕圈：沿"到敌人的方向的垂线"走
            _pc.SetInjectedMove(strafe * (_strafeSign >= 0f ? 1f : -1f), true);
        }
        else if (dist > EngageDist)
        {
            _pc.SetInjectedMove(intent, true);
            _dashTimer -= Time.deltaTime;
            if (dist > 4.0f && _dashTimer <= 0f) { _dashTimer = 1.6f; _pc.RequestInjectedDash(); }
        }
        else
        {
            // ★ 贴身时"停位移"但**不能停朝向**：`SetInjectedMove(zero)` 会让
            //   `ResolveMoveDirection` 直接返回零向量，`ApplyRotation` 随之提前返回，
            //   玩家就保持冲过来那一瞬间的朝向 —— 而攻击前冲又是沿"当前朝向"走的，
            //   于是经常侧身/背对着敌人挥剑。
            //   给一个极小的朝向意图：足够驱动转向，位移小到看不出来
            //   （0.05 × 4 m/s = 20 cm/s，且被 `MoveTowards` 的加减速吃掉大半）。
            _pc.SetInjectedMove(intent * 0.05f, false);
            _atkTimer -= Time.deltaTime;
            if (_atkTimer <= 0f)
            {
                // 连招三段最短节奏约 0.3 s/段。0.7 s 的输出只有真实玩家的一半，
                // 会被 3 只墨兵围死（墨兵 42 HP / 12 伤害 / 2.27 s 周期）。
                _atkTimer = 0.34f;
                _pc.RequestInjectedAttack();
            }
        }
    }

    /// <summary>把世界空间方向反解成"相机相对输入"（相机 forward 为 input.y+）。</summary>
    private Vector2 WorldDirToCameraInput(Vector3 worldDir)
    {
        if (worldDir.sqrMagnitude < 1e-6f) return Vector2.zero;
        var cam = Camera.main;
        Vector3 fwd, right;
        if (cam != null)
        {
            fwd = cam.transform.forward; right = cam.transform.right;
            fwd.y = 0f; right.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize(); right.Normalize();
        }
        else { fwd = Vector3.forward; right = Vector3.right; }

        worldDir.y = 0f;
        worldDir.Normalize();
        return new Vector2(Vector3.Dot(worldDir, right), Vector3.Dot(worldDir, fwd));
    }
}

// ==================================================================

public class BattleProbe : MonoBehaviour
{
    public Transform player;
    public string shotDir;
    public string reportPath;
    public float duration = 80f;

    private float _t;
    private float _shotTimer;
    private int _shotIdx;
    private readonly List<string> _timeline = new List<string>();
    private readonly List<string> _shots = new List<string>();
    private int _maxEnemies;
    private int _totalKilled;
    private int _seenBefore;
    private readonly Dictionary<string, int> _attackHist = new Dictionary<string, int>();
    private float _pMinHp = float.MaxValue, _pMaxHp;
    private float _dMinHp = float.MaxValue, _dMaxHp;
    private int _waveMax = -1;
    private bool _dragonSeen;
    private float _dragonHp0;
    private float _lastLog;
    private PlayerHealth _ph;

    private void Start()
    {
        if (player != null)
        {
            _ph = player.GetComponent<PlayerHealth>();
            if (player.GetComponent<AutoPlayer>() == null)
                player.gameObject.AddComponent<AutoPlayer>();
        }
        Debug.Log("[a52probe] Start: player=" + (player != null));
    }

    private void Update()
    {
        _t += Time.deltaTime;

        // 保底：玩家掉出世界就拉回（a46 的教训）
        if (player != null && player.position.y < -20f)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.position = new Vector3(0f, 0.4f, -3f);
            if (cc != null) cc.enabled = true;
            _timeline.Add(string.Format("  t={0:F1}  ★ 玩家掉出世界，已拉回", _t));
        }

        int n = 0;
        foreach (var e in UnityEngine.Object.FindObjectsOfType<EnemyBase>())
            if (e != null && e.Health > 0f) n++;
        _maxEnemies = Mathf.Max(_maxEnemies, n);
        if (n < _seenBefore) _totalKilled += (_seenBefore - n);
        _seenBefore = n;

        if (_ph != null) { _pMinHp = Mathf.Min(_pMinHp, _ph.Health); _pMaxHp = Mathf.Max(_pMaxHp, _ph.Health); }

        foreach (var d in UnityEngine.Object.FindObjectsOfType<EnemyDragon>())
        {
            if (d == null) continue;
            if (!_dragonSeen)
            {
                _dragonSeen = true;
                _dragonHp0 = d.Health;
                _timeline.Add(string.Format("  t={0:F1}  ★★ 墨龙登场 HP={1:F0} 位置={2}", _t, d.Health,
                    d.transform.position.ToString("F1")));
            }
            _dMinHp = Mathf.Min(_dMinHp, d.Health);
            _dMaxHp = Mathf.Max(_dMaxHp, d.Health);
            string an = d.CurrentAttackName;
            if (!string.IsNullOrEmpty(an) && an != "None")
                _attackHist[an] = (_attackHist.TryGetValue(an, out var c) ? c : 0) + 1;
        }

        if (_t - _lastLog > 2.0f)
        {
            _lastLog = _t;
            _timeline.Add(string.Format("  t={0,4:F0}s  敌={1}  累计击杀={2}  玩家HP={3}",
                _t, n, _totalKilled, _ph != null ? _ph.Health.ToString("F0") : "?"));
        }

        _shotTimer -= Time.deltaTime;
        if (_shotTimer <= 0f) { _shotTimer = 1.0f; StartCoroutine(Shot()); }

        if (_t >= duration) Finish();
    }

    private IEnumerator Shot()
    {
        var cam = Camera.main;
        if (cam == null) yield break;
        yield return new WaitForEndOfFrame();
        int w = 1280, h = 720;
        var rt = new RenderTexture(w, h, 24);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        cam.targetTexture = prev;
        RenderTexture.active = null;
        var path = Path.Combine(shotDir, string.Format("F{0:D3}_t{1:F0}.png", _shotIdx++, _t));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        _shots.Add(path);
        Destroy(tex);
        rt.Release();
        Destroy(rt);
    }

    private void Finish()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== a52 多波次实战报告 ==========");
        sb.AppendLine("时长 = " + _t.ToString("F1") + " s");
        sb.AppendLine("最高同屏敌人数 = " + _maxEnemies);
        sb.AppendLine("累计击杀 = " + _totalKilled);
        sb.AppendLine("玩家 HP 范围 [" + _pMinHp.ToString("F0") + ", " + _pMaxHp.ToString("F0") + "]"
                      + "   终值 = " + (_ph != null ? _ph.Health.ToString("F0") : "?")
                      + (_ph != null && _ph.Health <= 0f ? "  ★ 阵亡" : "  存活 ✓"));
        sb.AppendLine("墨龙登场 = " + _dragonSeen);
        if (_dragonSeen)
        {
            sb.AppendLine("  龙初始 HP = " + _dragonHp0.ToString("F0"));
            sb.AppendLine("  龙 HP 范围 [" + _dMinHp.ToString("F0") + ", " + _dMaxHp.ToString("F0") + "]");
        }
        sb.AppendLine();
        sb.AppendLine("---- 龙的招式使用分布（累计帧数） ----");
        if (_attackHist.Count == 0) sb.AppendLine("  （无）");
        else foreach (var kv in _attackHist) sb.AppendLine("  " + kv.Key + " : " + kv.Value);

        var ws = UnityEngine.Object.FindObjectOfType<WaveSpawner>();
        if (ws != null)
        {
            sb.AppendLine();
            sb.AppendLine("---- 波次进度 ----");
            sb.AppendLine("  当前波 = " + (ws.CurrentWave + 1) + " / " + ws.WaveCount);
            sb.AppendLine("  已开始波次 = " + ws.WaveStartedCount + "   已清空波次 = " + ws.WaveClearedCount);
            sb.AppendLine("  历史生成数 = " + ws.SpawnedCount + "   生成失败 = " + ws.SpawnFailedCount);
            sb.AppendLine("  全清 = " + ws.AllCleared);
            _waveMax = ws.CurrentWave;
        }
        sb.AppendLine();
        sb.AppendLine("---- 时间线 ----");
        foreach (var l in _timeline) sb.AppendLine(l);
        sb.AppendLine();
        sb.AppendLine("---- 截图 ----");
        sb.AppendLine("  共 " + _shots.Count + " 张 → " + shotDir);

        File.WriteAllText(reportPath, sb.ToString());
        Debug.Log("[a52]\n" + sb.ToString());
        var cam = Camera.main;
        if (cam != null) { var b = cam.GetComponent<BattleCam>(); if (b != null) Destroy(b); }
        if (player != null)
        {
            var ap = player.GetComponent<AutoPlayer>();
            if (ap != null) Destroy(ap);
            var pc = player.GetComponent<PlayerController>();
            if (pc != null) pc.EndInputOverride();
        }
        Destroy(gameObject);
    }
}
