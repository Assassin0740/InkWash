// a57_dragon_reel.cs —— 龙的招式演示（专供录屏）
//
// 教训：a54 我在"相机取景"上白跑了四轮（跟随取中点 / 11 m 侧移 / 17 m 俯拍 / 算出来的机位），
// 根因是一直**先拍后看**，而且 `ShowcaseCam` 的 LateUpdate 会覆盖我手设的机位。
// 这次的做法：
//   ① 完全不挂跟随相机 —— 直接主相机静态机位，谁也别抢
//   ② 机位用 a56 算出来的那组数（外接球半径 6.62 m ⇒ 距离 17.26 m，8/8 角在视野内）
//   ③ 招式之间**不移动相机**，让 10 s 的视频里四招连贯演完
//
// 时间线（共 13 s）：
//   0.0 ~ 2.0  龙入场、站立（能从侧面看清整条中国龙）
//   2.0 ~ 4.5  甩尾
//   4.5 ~ 7.0  撕咬
//   7.0 ~ 9.5  龙息
//   9.5 ~ 13.0 盘旋（升空）
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using InkWash.Enemies;
using UnityEditor;
using UnityEngine;

var root = Path.GetDirectoryName(Application.dataPath);

foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.name.StartsWith("A5") || g.name.StartsWith("HoverTest"))
        UnityEngine.Object.DestroyImmediate(g);

var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
ground.name = "A57_Ground";
ground.transform.position = new Vector3(0f, -0.6f, 0f);
ground.transform.localScale = new Vector3(180f, 1f, 180f);

GameObject player = null;
foreach (var g in UnityEngine.Object.FindObjectsOfType<GameObject>())
    if (g.CompareTag("Player")) { player = g; break; }
if (player != null)
{
    var cc = player.GetComponent<CharacterController>();
    if (cc != null) cc.enabled = false;
    player.transform.position = new Vector3(0f, 0.3f, 10f);
    if (cc != null) cc.enabled = true;
}

var ws = UnityEngine.Object.FindObjectOfType<WaveSpawner>();
if (ws != null) ws.enabled = false;

// ★ 先关掉一切会抢相机的组件（a54 栽在这上面：手设了机位，跟随脚本每帧又改回去）
var cam = Camera.main != null ? Camera.main : UnityEngine.Object.FindObjectOfType<Camera>();
if (cam != null)
{
    // 用名字销毁，避免跨脚本引用（Codely 的每个 .cs 是独立编译单元，
    // 引用 a54 里定义的 ShowcaseCam 类型会编译失败）
    foreach (var mb in cam.GetComponents<MonoBehaviour>())
    {
        if (mb == null) continue;
        string tn = mb.GetType().Name;
        if (tn.Contains("ShowcaseCam") || tn.Contains("Brain") || tn.Contains("Follow"))
        {
            UnityEngine.Object.DestroyImmediate(mb);
            Debug.Log("[a57] 已移除抢相机的组件: " + tn);
        }
    }
    // a56 算出的机位
    cam.transform.position = new Vector3(10.246f, 12.103f, -3.871f);
    cam.transform.LookAt(new Vector3(0.754f, 1.402f, 5.794f));
    cam.farClipPlane = Mathf.Max(cam.farClipPlane, 150f);
    cam.fieldOfView = 48f;
    Debug.Log("[a57] 相机已钉死: pos=" + cam.transform.position.ToString("F2")
              + " fov=" + cam.fieldOfView + " cam.name=" + cam.name);
}

var p = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
var dg = p != null ? PrefabUtility.InstantiatePrefab(p) as GameObject : null;
if (dg != null) dg.transform.position = new Vector3(0f, 0.3f, 4f);

var go = new GameObject("A57_Reel");
var reel = go.AddComponent<DragonReel>();
reel.dragon = dg;
reel.reportPath = Path.Combine(root, "Tools/reports/a57_dragon_reel.txt");
Debug.Log("[a57] 已挂, dragon=" + (dg != null));

public class DragonReel : MonoBehaviour
{
    public GameObject dragon;
    public string reportPath;

    private EnemyDragon _d;
    private readonly List<string> _log = new List<string>();

    private void Start()
    {
        _d = dragon != null ? dragon.GetComponent<EnemyDragon>() : null;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        yield return new WaitForSeconds(1.5f);
        if (_d == null) { Finish(); yield break; }

        _log.Add("龙 = " + dragon.name + "  HP=" + _d.Health.ToString("F0")
                 + "  SpineLinks=" + _d.SpineLinksFound);

        // 入场观察段（不攻击，让人看清龙的造型）
        _log.Add("[0.0~2.0] 站位观察");
        yield return new WaitForSeconds(2.0f);

        yield return DoAttack("TailSweep", 2.5f, "甩尾");
        yield return DoAttack("Bite", 2.5f, "撕咬");
        yield return DoAttack("Breath", 2.5f, "龙息");
        yield return DoAttack("HoverOrbit", 3.5f, "盘旋");

        Finish();
    }

    private IEnumerator DoAttack(string name, float dur, string cn)
    {
        _log.Add("── " + name + "（" + cn + "）持续 " + dur + " s");

        float maxSpine = 0f;
        bool sawAir = false;
        int projSeen = 0, projPrev = CountProj();
        int projPeak = 0;
        var seen = new HashSet<string>();

        _d.EndAttackForTest();
        yield return null;
        _d.ForceNextAttackForTest(name);
        _d.ForceEnterAttackForTest();

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            if (_d == null) break;
            string an = _d.CurrentAttackName;
            if (!string.IsNullOrEmpty(an) && an != "-") seen.Add(an);
            maxSpine = Mathf.Max(maxSpine, _d.MaxSpineOffsetDeg);
            if (_d.Airborne) sawAir = true;
            int pc = CountProj();
            if (pc > projPrev) projSeen += pc - projPrev;
            projPrev = pc;
            projPeak = Mathf.Max(projPeak, pc);
            yield return null;
        }

        _log.Add(string.Format("     招式名=[{0}]  最大脊骨偏移={1:F1}°  升空={2}  墨弹(新生={3}/峰值={4})",
            string.Join(",", seen), maxSpine, sawAir ? "是" : "否", projSeen, projPeak));

        // 招与招之间留一点收招时间
        _d.EndAttackForTest();
        yield return new WaitForSeconds(0.35f);
    }

    private int CountProj() { return UnityEngine.Object.FindObjectsOfType<InkProjectile>().Length; }

    private void Finish()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== a57 龙的招式演示（录屏配套） ==========");
        foreach (var l in _log) sb.AppendLine(l);
        File.WriteAllText(reportPath, sb.ToString());
        Debug.Log("[a57]\n" + sb.ToString());
        Destroy(gameObject);
    }
}
