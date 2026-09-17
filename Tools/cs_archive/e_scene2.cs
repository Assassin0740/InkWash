// e_scene2.cs —— 修「房间装不下出生圈」这个关卡布局缺陷（编辑态）
//
// 背景：原本石门围出来的是 10×10 的房间（±5），但 WaveSpawner 的出生圈半径是 9.5 ——
// 也就是说**敌人生成在石门外面**。门挡着 Linecast，敌人永远看不见玩家，整波原地站着；
// 而控制台一条报错都没有（NavMeshAgent 只是不移动，不抛异常）。这类"沉默的关卡矛盾"
// 靠读代码很难发现，是靠逐帧诊断（s3_diag_ai）把 Linecast 命中的碰撞体名打出来才定位到的。
//
// 修法：把房间放大到 20×20（±10），并给四扇门打 NavMeshModifier(ignoreFromBuild)，
// 保证"门"永远不参与导航烘焙（门是会下沉的活动障碍，烘进去就会"开了门也走不出去"）。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using InkWash.Enemies;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    yield return null; yield return null;

    var scene = SceneManager.GetActiveScene();
    sb.AppendLine("场景 = " + scene.name + "   path=" + scene.path);
    sb.AppendLine("===== e_scene2：房间尺寸 + 出生点 =====");

    // ---------- ① 四扇石门推出去：±5 → ±10，长 7.5 → 20.9（角上互相压住，不留缝）----------
    var gatesRoot = GameObject.Find("Gates");
    var gateList = new List<Transform>();
    var specs = new (string name, Vector3 pos, Vector3 size)[]
    {
        ("Gate_N", new Vector3(0f, 1.55f, 10f),  new Vector3(20.9f, 2.5f, 0.45f)),
        ("Gate_S", new Vector3(0f, 1.55f, -10f), new Vector3(20.9f, 2.5f, 0.45f)),
        ("Gate_E", new Vector3(10f, 1.55f, 0f),  new Vector3(0.45f, 2.5f, 20.9f)),
        ("Gate_W", new Vector3(-10f, 1.55f, 0f), new Vector3(0.45f, 2.5f, 20.9f)),
    };
    foreach (var s in specs)
    {
        var g = GameObject.Find(s.name);
        if (g == null) { sb.AppendLine("  ** 找不到 " + s.name + "（跳过）"); continue; }
        Vector3 old = g.transform.position;
        g.transform.position = s.pos;
        g.transform.localScale = s.size;

        // 门不能参与导航烘焙：它们是"清空后下沉"的活动障碍。
        // 若被烘进网格，门开了网格上的洞还在 ⇒ 玩家"看着门开了却撞墙"。
        var mod = g.GetComponent<NavMeshModifier>();
        if (mod == null) mod = g.AddComponent<NavMeshModifier>();
        mod.ignoreFromBuild = true;

        gateList.Add(g.transform);
        sb.AppendLine("  " + s.name + "  " + F(old) + " → " + F(s.pos) + "   尺寸 " + F(s.size));
    }
    sb.AppendLine("  房间内净尺寸 ≈ 19.1 × 19.1（门厚 0.45）");

    // ---------- ② 重新烘焙导航网格（门已被排除）----------
    var navGo = GameObject.Find("NavMesh");
    var surface = navGo != null ? navGo.GetComponent<NavMeshSurface>() : null;
    if (surface == null) { sb.AppendLine("** 找不到 NavMeshSurface"); }
    else
    {
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = ~0;
        surface.agentTypeID = 0;
        // 默认体素 ≈ agentRadius/3 ≈ 0.167 m，而网格只在体素边界生成 ⇒ 地面整体被抬到 y=0.17。
        // 必须显式收紧到 0.05。
        surface.overrideVoxelSize = true;
        surface.voxelSize = 0.05f;
        surface.overrideTileSize = false;
        surface.defaultArea = 0;
        surface.BuildNavMesh();

        var tri = NavMesh.CalculateTriangulation();
        sb.AppendLine(string.Format("NavMesh 重烘：顶点 {0} / 三角 {1}", tri.vertices.Length, tri.indices.Length / 3));
        if (tri.vertices.Length > 0)
        {
            sb.AppendLine(string.Format("  覆盖 X[{0:F1},{1:F1}]  Y[{2:F2},{3:F2}]  Z[{4:F1},{5:F1}]",
                tri.vertices.Min(v => v.x), tri.vertices.Max(v => v.x),
                tri.vertices.Min(v => v.y), tri.vertices.Max(v => v.y),
                tri.vertices.Min(v => v.z), tri.vertices.Max(v => v.z)));
        }

        // 体素误差复核：导航网格 Y − 真实表面 Y
        sb.AppendLine("  体素误差复核（导航网格 Y − 真实表面 Y）：");
        foreach (var probe in new[] { new Vector3(7.5f, 0.5f, 0f), new Vector3(0f, 0.5f, 7.5f), new Vector3(0f, 0.5f, 0f) })
        {
            NavMeshHit nh; RaycastHit gh;
            bool onNav = NavMesh.SamplePosition(probe, out nh, 3f, NavMesh.AllAreas);
            bool onGround = Physics.Raycast(probe + Vector3.up * 3f, Vector3.down, out gh, 10f, ~0, QueryTriggerInteraction.Ignore);
            sb.AppendLine(string.Format("    {0} → 网格Y={1}  真值Y={2}  误差={3}",
                F(probe), onNav ? nh.position.y.ToString("F3") : "无",
                onGround ? gh.point.y.ToString("F3") : "无",
                (onNav && onGround) ? (nh.position.y - gh.point.y).ToString("F3") : "-"));
        }
    }

    // ---------- ③ 出生圈必须落在房间里 ----------
    var spawner = Object.FindObjectOfType<WaveSpawner>();
    if (spawner == null) sb.AppendLine("** 找不到 WaveSpawner");
    else
    {
        spawner.spawnPoints = new Transform[0];
        spawner.spawnCenter = new Vector3(0f, 0f, 0f);   // 圆心 = 房间中心，**不是玩家**
        spawner.spawnRadius = 7.5f;                      // 7.5 + 采样容差 < 内净尺寸 9.55
        spawner.minDistanceToPlayer = 4.5f;
        spawner.spawnAttempts = 24;
        EditorUtility.SetDirty(spawner);
        sb.AppendLine();
        sb.AppendLine("WaveSpawner：spawnCenter=" + F(spawner.spawnCenter)
                      + "  spawnRadius=" + spawner.spawnRadius
                      + "  minDistanceToPlayer=" + spawner.minDistanceToPlayer
                      + "  spawnAttempts=" + spawner.spawnAttempts
                      + "  波次=" + spawner.WaveCount);
        sb.AppendLine("  校验：出生圈最远点半径 7.5 → 距门内表面 "
                      + (9.55f - spawner.spawnRadius).ToString("F2") + " m（应为正）");
    }

    // ---------- ④ 把 RoomController 的门列表刷新一遍 ----------
    var room = Object.FindObjectOfType<RoomController>();
    if (room != null && gateList.Count > 0)
    {
        room.gates = gateList.ToArray();
        room.gateOpenDepth = 3.2f;
        room.gateOpenDuration = 1.1f;
        EditorUtility.SetDirty(room);
        sb.AppendLine("RoomController.gates 已刷新为 " + gateList.Count + " 扇");
    }

    // ---------- ⑤ 保存 ----------
    bool saved = EditorSceneManager.SaveScene(scene, scene.path);
    sb.AppendLine();
    sb.AppendLine("场景保存 = " + saved);
    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/e_scene2.txt"), sb.ToString());
    Debug.Log("[e_scene2] done");
    yield return null;
}

string F(Vector3 v) => "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2") + ")";

return Body();
