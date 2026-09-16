// a54_combat_showcase.cs —— 分段展示每一类战斗（用户要的"很多的战斗"）
//
// ★ 设计反思：a52 一直在追"让自动玩家打赢"，那是本末倒置。
//   用户要的是**看到战斗内容**：不同的怪怎么打、龙的四招长什么样、招式命中什么表现。
//   所以这里改成分段演示（每个主题一段，各自独立摆位），而不是"一场混战打到死"。
//
// 五段：
//   ① 三段连招 —— 玩家对木桩，展示 Atk1→2→3 完整连击
//   ② 敌群围攻 —— 3 只墨兵同时进攻，展示被围与受击
//   ③ 重击 K   —— 展示重击起手
//   ④ 冲刺     —— 展示 Dash 低身前冲与无敌帧
//   ⑤ 墨龙四招 —— 甩尾 / 撕咬 / 龙息 / 盘旋，逐招强制触发 + 读脊骨偏移
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
var shotDir = Path.Combine(root, "Tools/screenshots/showcase54");
Directory.CreateDirectory(shotDir);

var sb = new StringBuilder();
sb.AppendLine("========== a54 分主题战斗展示 ==========");

foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.name.StartsWith("A54_") || g.name.StartsWith("A52_") || g.name.StartsWith("HoverTest"))
        UnityEngine.Object.DestroyImmediate(g);

var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
ground.name = "A54_Ground";
ground.transform.position = new Vector3(0f, -0.6f, 0f);
ground.transform.localScale = new Vector3(140f, 1f, 140f);

GameObject player = null;
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.CompareTag("Player")) { player = g; break; }

if (player == null) { Debug.LogError("[a54] ★ 找不到玩家"); }
else
{
    var ph = player.GetComponent<PlayerHealth>();
    if (ph != null) ph.ResetHealth();
    sb.AppendLine("玩家 = " + player.name + "  HP = " + (ph != null ? ph.EffectiveMaxHealth.ToString() : "?"));
}

var cam = Camera.main != null ? Camera.main : UnityEngine.Object.FindObjectOfType<Camera>();
if (cam != null)
{
    var rig = cam.gameObject.GetComponent<ShowcaseCam>();
    if (rig == null) rig = cam.gameObject.AddComponent<ShowcaseCam>();
    rig.player = player != null ? player.transform : null;
    sb.AppendLine("ShowcaseCam 已挂");
}

var ws = UnityEngine.Object.FindObjectOfType<WaveSpawner>();
if (ws != null) { ws.enabled = false; sb.AppendLine("WaveSpawner 已临时禁用"); }

var go = new GameObject("A54_Director");
var dir = go.AddComponent<ShowcaseDirector>();
dir.player = player;
dir.shotDir = shotDir;
dir.reportPath = Path.Combine(root, "Tools/reports/a54_combat_showcase.txt");
dir.gruntPrefab = "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoGuai.prefab";
dir.dragonPrefab = "Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab";
sb.AppendLine("ShowcaseDirector 已挂");

File.WriteAllText(Path.Combine(root, "Tools/reports/a54_launch.txt"), sb.ToString());
Debug.Log("[a54]\n" + sb.ToString());

// ==================================================================

public class ShowcaseCam : MonoBehaviour
{
    public Transform player;
    public Transform focusOverride;
    /// <summary>额外要框进来的目标（例如龙）——相机会取"玩家 + 它"的中点。</summary>
    public Transform secondTarget;
    public float height = 9.0f, back = 13.0f;

    private void LateUpdate()
    {
        // ★ 取景要点：龙长 6 m、玩家身高 2 m，把镜头钉在龙身上会把玩家挤出画面，
        //   钉在玩家身上又只剩龙的爪子（a54 第一版两种都试过，各废了一半图）。
        //   取两者的**中点**才装得下这场对决。
        Vector3 c;
        if (focusOverride != null) c = focusOverride.position;
        else if (player != null && secondTarget != null) c = (player.position + secondTarget.position) * 0.5f;
        else if (player != null) c = player.position;
        else c = transform.position;

        Vector3 want = c + new Vector3(0f, height, -back);
        transform.position = Vector3.Lerp(transform.position, want, 5f * Time.deltaTime);
        var look = Quaternion.LookRotation((c - transform.position).normalized, Vector3.up);
        transform.rotation = Quaternion.Lerp(transform.rotation, look, 5f * Time.deltaTime);
    }
}

public class ShowcaseDirector : MonoBehaviour
{
    public GameObject player;
    public string shotDir;
    public string reportPath;
    public string gruntPrefab;
    public string dragonPrefab;

    private PlayerController _pc;
    private PlayerHealth _ph;
    private readonly List<string> _log = new List<string>();
    private readonly List<string> _shots = new List<string>();
    private int _shotIdx;
    private EnemyDragon _dg;
    private readonly List<EnemyBase> _spawned = new List<EnemyBase>();

    private void Start()
    {
        _pc = player != null ? player.GetComponent<PlayerController>() : null;
        _ph = player != null ? player.GetComponent<PlayerHealth>() : null;
        if (_pc != null) _pc.BeginInputOverride();
        StartCoroutine(RunAll());
    }

    private IEnumerator RunAll()
    {
        yield return new WaitForSeconds(0.5f);
        yield return SegmentCombo();
        yield return SegmentGank();
        yield return SegmentHeavy();
        yield return SegmentDash();
        yield return SegmentDragon();
        Finish();
    }

    // ---------------- 工具 ----------------

    private void ResetPlayer(Vector3 pos)
    {
        if (player == null) return;
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.transform.position = pos;
        if (cc != null) cc.enabled = true;
        if (_ph != null) _ph.ResetHealth();
        if (_pc != null) { _pc.ResetToLocomotion(); _pc.SetInjectedMove(Vector2.zero, false); }
    }

    private IEnumerator Shot(string tag)
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
        var path = Path.Combine(shotDir, string.Format("{0:D2}_{1}.png", _shotIdx++, tag));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        _shots.Add(Path.GetFileName(path));
        Destroy(tex);
        rt.Release();
        Destroy(rt);
    }

    private GameObject Spawn(string prefabPath, Vector3 pos)
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (p == null) { _log.Add("  ★ 找不到 prefab: " + prefabPath); return null; }
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(p);
        inst.name = "A54_" + p.name;
        inst.transform.position = pos;
        var eb = inst.GetComponent<EnemyBase>();
        if (eb != null) _spawned.Add(eb);
        return inst;
    }

    private void KillSpawned()
    {
        foreach (var e in _spawned) if (e != null) Destroy(e.gameObject);
        _spawned.Clear();
    }

    // ---------------- ① 三段连招 ----------------

    private IEnumerator SegmentCombo()
    {
        _log.Add("");
        _log.Add("① 三段连招（Atk1 → Atk2 → Atk3）");
        ResetPlayer(new Vector3(0f, 0.3f, -1.5f));
        Spawn(gruntPrefab, new Vector3(0f, 0.3f, 0.7f));
        yield return new WaitForSeconds(0.7f);

        if (_pc == null) { _log.Add("  ★ 没有 PlayerController"); yield break; }

        var tags = new List<string>();
        float maxSpeed = 0f;
        for (int i = 0; i < 3; i++)
        {
            _pc.RequestInjectedAttack();
            float t = 0f;
            while (t < 0.42f)
            {
                t += Time.deltaTime;
                maxSpeed = Mathf.Max(maxSpeed, _pc.DesiredVelocity.magnitude);
                yield return null;
            }
            tags.Add(_pc.PhaseTag);
            if (i == 1) yield return Shot("1_combo");
        }
        _log.Add("  三段各自的状态快照 = " + string.Join("  →  ", tags));
        _log.Add("  期间最大期望速度 = " + maxSpeed.ToString("F2") + " m/s（攻击前冲峰值 ~11）");

        KillSpawned();
        yield return new WaitForSeconds(0.4f);
    }

    // ---------------- ② 敌群围攻 ----------------

    private IEnumerator SegmentGank()
    {
        _log.Add("");
        _log.Add("② 敌群围攻（3 只墨兵）");
        ResetPlayer(new Vector3(0f, 0.3f, -2f));

        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.PI * 2f / 3f;
            Spawn(gruntPrefab, new Vector3(Mathf.Sin(a) * 4.5f, 0.3f, Mathf.Cos(a) * 4.5f + 1f));
        }
        yield return new WaitForSeconds(0.5f);

        float hp0 = _ph != null ? _ph.Health : 0f;
        int counter0 = _ph != null ? _ph.DamageTakenCount : 0;
        int blocked0 = _ph != null ? _ph.DamageBlockedByIFrameCount : 0;

        float t = 0f;
        bool shot = false;
        while (t < 7f)
        {
            t += Time.deltaTime;
            if (_pc != null && t > 1.0f) _pc.RequestInjectedAttack();
            if (!shot && t > 3.5f) { shot = true; yield return Shot("2_gank"); }
            yield return null;
        }

        float hp1 = _ph != null ? _ph.Health : 0f;
        _log.Add("  7 s 内：HP " + hp0.ToString("F0") + " → " + hp1.ToString("F0")
                 + "（掉 " + (hp0 - hp1).ToString("F0") + "）");
        _log.Add("  命中玩家次数 = " + (_ph != null ? _ph.DamageTakenCount - counter0 : -1)
                 + "   被无敌帧挡下 = " + (_ph != null ? _ph.DamageBlockedByIFrameCount - blocked0 : -1));

        KillSpawned();
        yield return new WaitForSeconds(0.4f);
    }

    // ---------------- ③ 重击 ----------------

    private IEnumerator SegmentHeavy()
    {
        _log.Add("");
        _log.Add("③ 重击（K 键 / RequestInjectedHeavyAttack）");
        ResetPlayer(new Vector3(0f, 0.3f, -1.8f));
        Spawn(gruntPrefab, new Vector3(0f, 0.3f, 0.4f));
        yield return new WaitForSeconds(0.6f);

        if (_pc != null)
        {
            _pc.RequestInjectedHeavyAttack();
            float t = 0f;
            string tag = "-";
            bool shot = false;
            while (t < 0.9f)
            {
                t += Time.deltaTime;
                tag = _pc.PhaseTag;
                if (!shot && t > 0.25f) { shot = true; yield return Shot("3_heavy"); }
                yield return null;
            }
            _log.Add("  重击过程中最后状态 = " + tag + "（Attack/3 表示走的是第三段）");
        }

        KillSpawned();
        yield return new WaitForSeconds(0.4f);
    }

    // ---------------- ④ 冲刺 ----------------

    private IEnumerator SegmentDash()
    {
        _log.Add("");
        _log.Add("④ 冲刺（Dash 低身前冲 + 无敌帧）");
        ResetPlayer(new Vector3(0f, 0.3f, -5f));

        if (_pc != null)
        {
            Vector3 p0 = player.transform.position;
            _pc.SetInjectedMove(new Vector2(0f, 1f), false);
            _pc.RequestInjectedDash();
            float t = 0f;
            bool shot = false;
            bool sawInvincible = false;
            while (t < 0.7f)
            {
                t += Time.deltaTime;
                if (_pc.IsInvincible) sawInvincible = true;
                if (!shot && t > 0.15f) { shot = true; yield return Shot("4_dash"); }
                yield return null;
            }
            Vector3 p1 = player.transform.position;
            _log.Add("  冲刺位移 = " + Vector3.Distance(p0, p1).ToString("F2") + " m"
                     + "   过程状态 = " + _pc.PhaseTag
                     + "   期间出现无敌帧 = " + sawInvincible);
        }
        yield return new WaitForSeconds(0.3f);
    }

    // ---------------- ⑤ 墨龙四招 ----------------

    private IEnumerator SegmentDragon()
    {
        _log.Add("");
        _log.Add("⑤ 墨龙四招（逐招强制触发）");

        // 布局：龙在 z=4（头朝 +Z），玩家在 z=10 —— 距离 6 m，在 biteMaxRange 7 m 内。
        // 龙的长轴是 X（6.04 m）、头朝 +Z，所以玩家必须站在它的 +Z 侧，
        // 否则 `CanSeePlayer()` 会否掉一切（第一版就是栽在这）。
        var inst = Spawn(dragonPrefab, new Vector3(0f, 0.3f, 4f));
        _dg = inst != null ? inst.GetComponent<EnemyDragon>() : null;
        if (_dg == null) { _log.Add("  ★ 龙上没有 EnemyDragon"); yield break; }

        ResetPlayer(new Vector3(0f, 0.3f, 10f));

        // ★ 相机用一个**静态机位**：从两者中点的侧上方看下去。
        //   不要用"跟随 + 取中点"的写法 —— 龙与玩家都在游戏里移动，
        //   相机的 Lerp 追不上，拍出来要么空镜、要么只剩一角（试过两版都废）。
        //   演示段里双方位置本来就是固定的，静态机位最稳、构图也最好控制。
        var camRig = Camera.main != null ? Camera.main.GetComponent<ShowcaseCam>() : null;
        if (camRig != null)
        {
            camRig.enabled = false;
            var cam = Camera.main;
            // ★ 机位不是试出来的，是**算出来的**（见 Tools/cs/a56_cam_calc.cs）：
            //   龙的包围盒 5.87 × 2.59 × 6.04，加上 6 m 外的玩家，联合包围盒
            //   外接球半径 6.62 m；相机 fov 48° / 16:9 ⇒ 竖直半角 24°、水平 38.4°，
            //   装下外接球需要 17.26 m（含 6% 余量）。沿"后上偏右"方向拉这个距离，
            //   自检 8 个包围盒角**全部落在视口内**。
            //   前两版（11 m 侧移 / 17 m 垂直俯拍）都是先猜后拍，结果一个拍到脖子特写、
            //   一个拍到地板，白跑两轮。
            cam.transform.position = new Vector3(10.246f, 12.103f, -3.871f);
            cam.transform.LookAt(new Vector3(0.754f, 1.402f, 5.794f));
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 120f);
        }
        yield return new WaitForSeconds(1.2f);

        _log.Add("  龙 HP = " + _dg.Health.ToString("F0")
                 + "   SpineLinks = " + _dg.SpineLinksFound
                 + "   龙位置 = " + inst.transform.position.ToString("F1")
                 + "   玩家位置 = " + (player != null ? player.transform.position.ToString("F1") : "?"));

        foreach (var atk in new[] { "TailSweep", "Bite", "Breath", "HoverOrbit" })
        {
            if (_dg == null) break;

            // 先复位，再指定下一招，最后强制推进状态机（不依赖 AI 判定）
            _dg.EndAttackForTest();
            yield return null;
            _dg.ForceNextAttackForTest(atk);
            _dg.ForceEnterAttackForTest();

            float t = 0f;
            float maxSpine = 0f;
            bool shot = false, sawAirborne = false;
            var seen = new HashSet<string>();
            // ★ 墨弹要**逐帧累加峰值**，不能"结束时减开始时"。
            //   踩过的坑：a54 第一版只在招式前后各数一次，结果龙息报"新增墨弹=0" ——
            //   而 a55 逐帧追踪证明它其实吐了 3 颗。原因是弹会飞出去、命中或超时销毁，
            //   3.0 s 之后场上早就一颗不剩了。用"见过的最多颗数"当指标才不会被销毁抹平。
            int projPeak = 0;
            int projSeen = 0, projPrev = CountProjectiles();

            while (t < 3.0f)
            {
                t += Time.deltaTime;
                if (_dg == null) break;
                string an = _dg.CurrentAttackName;
                if (!string.IsNullOrEmpty(an) && an != "-") seen.Add(an);
                maxSpine = Mathf.Max(maxSpine, _dg.MaxSpineOffsetDeg);
                if (_dg.Airborne) sawAirborne = true;

                int pc = CountProjectiles();
                if (pc > projPrev) projSeen += pc - projPrev;
                projPrev = pc;
                projPeak = Mathf.Max(projPeak, pc);

                if (!shot && t > 0.55f) { shot = true; yield return Shot("5_" + atk); }
                yield return null;
            }

            _log.Add(string.Format("  ── {0,-11} 招式名=[{1}]  最大脊骨偏移={2,6:F1}°  升空={3}  墨弹(新生={4}/同屏峰值={5})",
                atk, string.Join(",", seen), maxSpine, sawAirborne ? "是" : "否", projSeen, projPeak));

            yield return new WaitForSeconds(0.4f);
        }

        KillSpawned();
        if (camRig != null) { camRig.focusOverride = null; camRig.secondTarget = null; camRig.enabled = true; }
        yield return new WaitForSeconds(0.3f);
    }

    private int CountProjectiles()
    {
        // 用类型直接查，不做字符串匹配 —— 本项目已经因为"按名字找东西"
        // 静默失效过好几次（改名后不报错、只是找不到）。
        return UnityEngine.Object.FindObjectsOfType<InkProjectile>().Length;
    }

    // ---------------- 收尾 ----------------

    private void Finish()
    {
        if (_pc != null) _pc.EndInputOverride();
        var camRig = Camera.main != null ? Camera.main.GetComponent<ShowcaseCam>() : null;
        if (camRig != null) Destroy(camRig);

        var sb = new StringBuilder();
        sb.AppendLine("========== a54 分主题战斗展示报告 ==========");
        foreach (var l in _log) sb.AppendLine(l);
        sb.AppendLine();
        sb.AppendLine("---- 截图 ----");
        sb.AppendLine("  共 " + _shots.Count + " 张 → " + shotDir);
        foreach (var s in _shots) sb.AppendLine("    " + s);

        File.WriteAllText(reportPath, sb.ToString());
        Debug.Log("[a54]\n" + sb.ToString());
        Destroy(gameObject);
    }
}
