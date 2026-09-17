// ws_diag.cs —— 「WaveSpawner 一只都不刷」的定位探针
//
// 背景：F2 性能基线里，战斗档等 8 秒拿到 0 只怪，读数是
//   `波次 -1/4　累计生成 0　生成失败 0`
// 这一组数很有信息量，必须按它来分叉，不能直接下结论：
//   · waves=4（数组非空）⇒ 不是"没配波次"
//   · CurrentWave=-1 ⇒ **NextWave() 从未执行过**（它第一件事就是 _currentWave++）
//   · Spawned=0 且 Failed=0 ⇒ **压根没走到 SpawnOne**
//     （若走到了，"成功"和"失败"必居其一，两者都是 0 说明入口就没进）
// 三条合起来只指向一个地方：`Begin()` 没被调用过。
//
// 而 Begin() 只有两条来路 —— `Start()` 里的 autoStart，和 `RunManager` 开局。
// RunManager.Start() 是 `if (autoStartRun) StartRun(); else SetState(MainMenu);`
// 而 autoStartRun 是 **public 序列化字段**：C# 写 default true，场景里存的是别的值
// 就会覆盖（本项目硬规矩 #6）。若进 Play 停在主菜单，波次本来就是不开始的 ——
// 那不是 bug，是设计；我上一轮的探针直接进 Play 打战斗，等于在主菜单里找怪。
//
// ★ 但查下去发现**叠着一个真 bug**：场景里 `WaveSpawner.enabled` 也是 false，
//   而全仓 grep 显示**生产代码里没有任何一处会把它打开**。被禁用的 MonoBehaviour
//   不执行 Update()，于是 `Begin()` 把 `_running/CurrentWave` 都设好了、
//   `State` 也变成 Playing，`_waveTimer` 却永远停在初值 —— 玩家点「开始一局」后
//   一只怪都不来、控制台干干净净。来源：做演示场时为免刷怪把它关掉，这个调试态
//   被一起存进 Main.unity 并提交（41f1c9a，binary 场景，在 165 个文件的提交里看不出来）。
//   已修（场景数据 + Begin() 自愈两层），本探针的段 C / 段 F 分别验证这两层。
//
// ★ 所以本探针**分段取证**，先证明"没开局"，再证明"开局后能刷"，
//   最后才去碰 PickSpawnPosition。顺序错了会把"没开局"误判成"寻路坏了"。
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using InkWash.Enemies;
using InkWash.Roguelike;

public class A91_WsDiag : MonoBehaviour
{
    StringBuilder sb = new StringBuilder();

    string Rep()
    {
        return Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                            "Tools/reports/ws_diag.txt");
    }

    void W(string s) { sb.AppendLine(s); }

    static string S(object o) { return o == null ? "null" : o.ToString(); }

    /// <summary>反射读私有字段（含继承链）。Codely 下不能跨脚本引用工具类型，
    /// 但生产类型是已加载 Assembly 里的，正常用 typeof 即可。</summary>
    static object Fld(object o, string n)
    {
        if (o == null) return null;
        var t = o.GetType();
        while (t != null)
        {
            var f = t.GetField(n, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) return f.GetValue(o);
            t = t.BaseType;
        }
        return null;
    }

    void Start() { StartCoroutine(Run()); }

    IEnumerator Run()
    {
        W("===== ws_diag：WaveSpawner「一只都不刷」定位 =====");
        W("时间 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        W("场景 " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
          + "　timeScale=" + Time.timeScale + "　frame=" + Time.frameCount);

        // ------------------------------------------------------------------
        // A. 现场快照（不做任何干预 —— 干预会把要看的现象抹掉）
        // ------------------------------------------------------------------
        W("");
        W("---------- A. 现场快照（未干预）----------");

        var rm = RunManager.Instance;
        W("RunManager.Instance = " + (rm == null ? "null" : rm.gameObject.name));
        if (rm != null)
        {
            W("  enabled=" + rm.enabled + "　activeInHierarchy=" + rm.gameObject.activeInHierarchy);
            W("  ★ autoStartRun=" + rm.autoStartRun + "　State=" + rm.State
              + "　StateAge=" + rm.StateAge.ToString("0.00") + "s");
            W("  RoomIndex=" + rm.RoomIndex + "　roomsToClear=" + rm.roomsToClear);
            W("  rm.spawner = " + (rm.spawner == null ? "null" : rm.spawner.gameObject.name));
            W("  rm.room = " + (rm.room == null ? "null" : rm.room.gameObject.name));
        }

        var sps = UnityEngine.Object.FindObjectsOfType<WaveSpawner>();
        W("场景里 WaveSpawner 数量 = " + sps.Length);
        WaveSpawner sp = sps.Length > 0 ? sps[0] : null;
        if (sp != null)
        {
            W("  [" + sp.gameObject.name + "] enabled=" + sp.enabled
              + "　activeInHierarchy=" + sp.gameObject.activeInHierarchy
              + "　autoStart=" + sp.autoStart);
            W("  waves=" + sp.WaveCount + "　CurrentWave=" + sp.CurrentWave
              + "　Spawned=" + sp.SpawnedCount + "　SpawnFailed=" + sp.SpawnFailedCount
              + "　Alive=" + sp.AliveEnemies + "　AllCleared=" + sp.AllCleared);
            W("  反射 _running=" + S(Fld(sp, "_running"))
              + "　_waveTimer=" + S(Fld(sp, "_waveTimer"))
              + "　_spawnTimer=" + S(Fld(sp, "_spawnTimer"))
              + "　_spawnCursor=" + S(Fld(sp, "_spawnCursor"))
              + "　_pointCursor=" + S(Fld(sp, "_pointCursor")));
            W("  spawnCenter=" + sp.spawnCenter.ToString("F2") + "　spawnRadius=" + sp.spawnRadius
              + "　spawnPoints=" + (sp.spawnPoints == null ? "null" : sp.spawnPoints.Length.ToString()));
            for (int i = 0; i < sp.WaveCount; i++)
            {
                var w = sp.waves[i];
                int n = 0;
                string names = "";
                if (w != null && w.entries != null)
                    for (int j = 0; j < w.entries.Length; j++)
                    {
                        var e = w.entries[j];
                        if (e == null) continue;
                        n += Mathf.Max(0, e.count);
                        names += (e.prefab == null ? "<null-prefab>" : e.prefab.name) + "×" + e.count + " ";
                    }
                W("    wave[" + i + "] delay=" + (w == null ? "?" : w.delayBefore.ToString("0.0"))
                  + "　共 " + n + " 只　" + names);
            }
        }

        var tri = NavMesh.CalculateTriangulation();
        W("NavMesh：顶点 " + tri.vertices.Length + "　三角面 " + (tri.indices.Length / 3)
          + (tri.vertices.Length == 0 ? "　★ 未烘焙！" : ""));

        var pc = UnityEngine.Object.FindObjectOfType<InkWash.Player.PlayerController>();
        W("玩家 = " + (pc == null ? "null" : pc.gameObject.name + " @" + pc.transform.position.ToString("F2")));

        if (sp != null)
        {
            NavMeshHit hc;
            bool okC = NavMesh.SamplePosition(sp.spawnCenter, out hc, 5f, NavMesh.AllAreas);
            W("  SamplePosition(spawnCenter, 5m)=" + okC + (okC ? " → " + hc.position.ToString("F2") : ""));
            if (pc != null)
            {
                NavMeshHit hp;
                bool okP = NavMesh.SamplePosition(pc.transform.position, out hp, 5f, NavMesh.AllAreas);
                W("  SamplePosition(玩家, 5m)=" + okP + (okP ? " → " + hp.position.ToString("F2") : ""));
            }
        }

        // ------------------------------------------------------------------
        // B. 静置观察：证明"它不会自己启波"
        // ------------------------------------------------------------------
        W("");
        W("---------- B. 静置 3s（不干预）----------");
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 3f) yield return null;
        if (sp != null)
            W("  3s 后 State=" + (rm == null ? "?" : rm.State.ToString())
              + "　CurrentWave=" + sp.CurrentWave + "　Spawned=" + sp.SpawnedCount
              + "　Alive=" + sp.AliveEnemies + "　_running=" + S(Fld(sp, "_running")));

        // ------------------------------------------------------------------
        // C. 显式开局 —— 验证"开局后波次能正常刷"
        // ------------------------------------------------------------------
        W("");
        W("---------- C. 显式 RunManager.StartRun() 并观察 12s ----------");
        if (rm == null)
        {
            W("  RunManager 不在场景里 ⇒ 本段跳过（那 spawner 只能靠 autoStart 自启）");
        }
        else
        {
            try { rm.StartRun(); }
            catch (Exception ex) { W("  ★ StartRun() 抛异常: " + ex.Message); }
            W("  调用后立即 State=" + rm.State
              + "　_running=" + S(Fld(sp, "_running"))
              + "　CurrentWave=" + sp.CurrentWave
              + "　WaveTimer=" + sp.WaveTimer.ToString("0.00"));
            float t1 = Time.unscaledTime;
            float nextLog = 0f;
            while (Time.unscaledTime - t1 < 12f)
            {
                float el = Time.unscaledTime - t1;
                if (el >= nextLog)
                {
                    nextLog += 1.5f;
                    W("  t=" + el.ToString("0.0") + "s　State=" + rm.State
                      + "　Wave=" + sp.CurrentWave + "/" + sp.WaveCount
                      + "　Spawned=" + sp.SpawnedCount + "　Failed=" + sp.SpawnFailedCount
                      + "　Alive=" + sp.AliveEnemies + "　WaveTimer=" + sp.WaveTimer.ToString("0.00"));
                }
                yield return null;
            }
        }

        // ------------------------------------------------------------------
        // D. 兜底：显式 Begin()
        // ------------------------------------------------------------------
        if (sp != null && sp.SpawnedCount == 0)
        {
            W("");
            W("---------- D. 仍 0 只 ⇒ 手工 ResetForTest()+Begin() 复测 ----------");
            sp.enabled = true;
            sp.ResetForTest();
            sp.Begin();
            W("  调用后 _running=" + S(Fld(sp, "_running"))
              + "　CurrentWave=" + sp.CurrentWave
              + "　WaveTimer=" + sp.WaveTimer.ToString("0.00"));
            float t2 = Time.unscaledTime;
            while (Time.unscaledTime - t2 < 8f) yield return null;
            W("  8s 后 Spawned=" + sp.SpawnedCount + "　Failed=" + sp.SpawnFailedCount
              + "　Alive=" + sp.AliveEnemies + "　Wave=" + sp.CurrentWave + "/" + sp.WaveCount);
        }

        // ------------------------------------------------------------------
        // E. 还不行才查出生点求解（反射直调生产方法，不另写一套等价逻辑）
        // ------------------------------------------------------------------
        if (sp != null && sp.SpawnedCount == 0)
        {
            W("");
            W("---------- E. 逐点体检 PickSpawnPosition（反射直调生产方法）----------");
            var mi = typeof(WaveSpawner).GetMethod("PickSpawnPosition",
                        BindingFlags.Instance | BindingFlags.NonPublic);
            W("  方法找到 = " + (mi != null));
            if (mi != null)
            {
                int before = sp.SpawnFailedCount;
                for (int i = 0; i < 8; i++)
                {
                    string err = null;
                    object r = null;
                    try { r = mi.Invoke(sp, null); }
                    catch (Exception ex)
                    {
                        err = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    }
                    W("  第" + (i + 1) + "点 → " + (err != null ? "抛异常 " + err : S(r)));
                }
                W("  SpawnFailed " + before + " → " + sp.SpawnFailedCount);
            }
        }

        // ------------------------------------------------------------------
        // F. 验证 Begin() 的自愈：**故意**把组件关掉再 Begin，应当自动启用
        //
        //    与"修场景数据"是两条独立的防线，必须分别验证：
        //    · 修场景数据 ⇒ 让当下的工程能用
        //    · 加代码自愈 ⇒ 让**下一次**谁再把 enabled 关掉时不再静默卡死
        //    只验前者的话，这个 bug 迟早会被同样地再犯一次。
        // ------------------------------------------------------------------
        W("");
        W("---------- F. Begin() 自愈验证（故意关掉组件后调 Begin）----------");
        if (sp != null)
        {
            sp.ResetForTest();
            sp.enabled = false;
            W("  关掉后 enabled=" + sp.enabled + "（期望 False）");
            sp.Begin();
            W("  Begin() 后 enabled=" + sp.enabled + "（期望 True）"
              + "　CurrentWave=" + sp.CurrentWave
              + "　_running=" + S(Fld(sp, "_running")));
            float t3 = Time.unscaledTime;
            while (Time.unscaledTime - t3 < 5f) yield return null;
            W("  5s 后 Spawned=" + sp.SpawnedCount + "　Alive=" + sp.AliveEnemies + "（期望 >0）");
        }

        // ------------------------------------------------------------------
        // 结论
        // ------------------------------------------------------------------
        W("");
        W("---------- 结论 ----------");
        if (rm != null && !rm.autoStartRun)
            W("  · RunManager.autoStartRun=false ⇒ 进 Play 停在主菜单，波次不会自己开始。"
              + "要打战斗必须先 StartRun()（玩家走主菜单「开始一局」按钮）。");
        if (sp != null)
            W("  · WaveSpawner.enabled = " + sp.enabled
              + (sp.enabled ? "　正常" : "　★ 为 false ⇒ Update 不跑、波次永不推进（Begin 自愈可兜底，但场景值仍应修）"));
        if (sp != null)
            W("  · 最终：Spawned=" + sp.SpawnedCount + "　Alive=" + sp.AliveEnemies);

        File.WriteAllText(Rep(), sb.ToString(), new UTF8Encoding(false));
        Debug.Log("[ws_diag] 报告已写出: " + Rep());
    }
}

var host = new GameObject("A91_WsDiagHost");
host.AddComponent<A91_WsDiag>();
return "ws_diag 已挂载";
