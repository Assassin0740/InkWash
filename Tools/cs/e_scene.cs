// e_scene.cs —— 场景布置（编辑态）：NavMesh 烘焙 + 波次生成器 + 石门 + 房间控制器
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

    // ---------- ① NavMeshSurface ----------
    var navGo = GameObject.Find("NavMesh");
    if (navGo == null) navGo = new GameObject("NavMesh");
    var surface = navGo.GetComponent<NavMeshSurface>();
    if (surface == null) surface = navGo.AddComponent<NavMeshSurface>();
    surface.collectObjects = CollectObjects.All;
    surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
    surface.layerMask = ~0;
    surface.agentTypeID = 0;
    // ★ 体素尺寸必须显式收紧：默认体素 ≈ agentRadius/3 ≈ 0.167 m，
    //   而导航网格只在**体素边界**上生成 ⇒ 网格高度会整体落在最近的上方体素面，
    //   实测地面被抬到 y=0.17（真实地面 y=0）—— 敌人会明显浮空。
    //   这属于"默认值看起来没问题、但和本工程的地面厚度不匹配"的隐性偏差。
    surface.overrideVoxelSize = true;
    surface.voxelSize = 0.05f;
    surface.overrideTileSize = false;
    surface.defaultArea = 0;
    surface.BuildNavMesh();

    var tri = NavMesh.CalculateTriangulation();
    sb.AppendLine(string.Format("NavMesh 烘焙：顶点 {0} / 三角 {1}", tri.vertices.Length, tri.indices.Length / 3));
    if (tri.vertices.Length > 0)
    {
        float mnY = tri.vertices.Min(v => v.y), mxY = tri.vertices.Max(v => v.y);
        float mnX = tri.vertices.Min(v => v.x), mxX = tri.vertices.Max(v => v.x);
        float mnZ = tri.vertices.Min(v => v.z), mxZ = tri.vertices.Max(v => v.z);
        sb.AppendLine(string.Format("  覆盖范围 X[{0:F1},{1:F1}]  Y[{2:F2},{3:F2}]  Z[{4:F1},{5:F1}]",
            mnX, mxX, mnY, mxY, mnZ, mxZ));
    }

    // 采样验证：几个关键点是否落在导航网格上
    sb.AppendLine("  采样验证（关键点是否可走）：");
    foreach (var p in new[] { new Vector3(0, 0.5f, 0), new Vector3(6, 0.5f, 0), new Vector3(0, 0.5f, 6), new Vector3(-8, 0.5f, -8) })
    {
        NavMeshHit h;
        bool ok = NavMesh.SamplePosition(p, out h, 3f, NavMesh.AllAreas);
        sb.AppendLine(string.Format("    {0} → {1} {2}", p, ok ? "可走" : "**不可走**", ok ? h.position.ToString("F2") : ""));
    }

    // ---------- ② 石门 ----------
    var gatesRoot = GameObject.Find("Gates");
    if (gatesRoot == null) gatesRoot = new GameObject("Gates");
    var wallMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Whitebox_Wall.mat");
    var gateList = new List<Transform>();
    var specs = new (string name, Vector3 pos, Vector3 size)[]
    {
        ("Gate_N", new Vector3(0f, 1.55f, 5.0f),  new Vector3(7.5f, 2.5f, 0.45f)),
        ("Gate_S", new Vector3(0f, 1.55f, -5.0f), new Vector3(7.5f, 2.5f, 0.45f)),
        ("Gate_E", new Vector3(5.0f, 1.55f, 0f),  new Vector3(0.45f, 2.5f, 7.5f)),
        ("Gate_W", new Vector3(-5.0f, 1.55f, 0f), new Vector3(0.45f, 2.5f, 7.5f)),
    };
    foreach (var s in specs)
    {
        var g = GameObject.Find(s.name);
        if (g == null)
        {
            g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = s.name;
            g.transform.SetParent(gatesRoot.transform, true);
            if (wallMat != null) g.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
        }
        g.transform.position = s.pos;
        g.transform.localScale = s.size;
        gateList.Add(g.transform);
    }
    sb.AppendLine("石门 " + gateList.Count + " 扇已就位（清空后下沉 3.2 m）");

    // ---------- ③ 波次生成器 + 房间控制器 ----------
    var enemiesRoot = GameObject.Find("Enemies");
    if (enemiesRoot == null) enemiesRoot = new GameObject("Enemies");

    var spawner = enemiesRoot.GetComponent<WaveSpawner>();
    if (spawner == null) spawner = enemiesRoot.AddComponent<WaveSpawner>();
    var moTu = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Enemy_MoTu.prefab");
    var moOu = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Enemy_MoOu.prefab");
    var moYan = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Enemy_MoYan.prefab");

    spawner.spawnPoints = new Transform[0];
    spawner.spawnCenter = new Vector3(0f, 0.3f, 0f);
    spawner.spawnRadius = 9.5f;
    spawner.minDistanceToPlayer = 5.5f;
    spawner.spawnInterval = 0.45f;
    spawner.autoStart = false;               // 由验收脚本/房间控制器显式 Begin()
    spawner.waves = new[]
    {
        new Wave { label = "第一波·墨徒", delayBefore = 1.2f,
            entries = new[]{ new EnemySpawnEntry { prefab = moTu, count = 3 } } },
        new Wave { label = "第二波·墨徒+墨偶", delayBefore = 2.0f,
            entries = new[]{ new EnemySpawnEntry { prefab = moTu, count = 3 },
                             new EnemySpawnEntry { prefab = moOu, count = 2 } } },
        new Wave { label = "第三波·墨魇", delayBefore = 2.5f,
            entries = new[]{ new EnemySpawnEntry { prefab = moTu, count = 3 },
                             new EnemySpawnEntry { prefab = moYan, count = 1 } } },
    };

    var room = enemiesRoot.GetComponent<RoomController>();
    if (room == null) room = enemiesRoot.AddComponent<RoomController>();
    room.spawner = spawner;
    room.gates = gateList.ToArray();
    room.gateOpenDepth = 3.2f;
    room.gateOpenDuration = 1.1f;

    sb.AppendLine("波次配置：" + spawner.waves.Length + " 波（"
        + string.Join(" / ", spawner.waves.Select(w => w.label + "×" + w.entries.Sum(e => e.count))) + "）");

    // ---------- ④ 玩家位置与朝向 ----------
    var player = GameObject.Find("Player");
    if (player != null)
    {
        sb.AppendLine(string.Format("玩家位置 {0}  旋转 {1}", player.transform.position.ToString("F2"), player.transform.eulerAngles.ToString("F0")));
        var ph = player.GetComponent<InkWash.Player.PlayerHealth>();
        sb.AppendLine("玩家已挂 PlayerHealth = " + (ph != null));
        var sh = player.transform.Find("SwordHitbox");
        sb.AppendLine("玩家已有 SwordHitbox = " + (sh != null));
    }

    // ---------- ⑤ 保存 ----------
    EditorSceneManager.MarkSceneDirty(scene);
    bool saved = EditorSceneManager.SaveScene(scene, scene.path);
    AssetDatabase.SaveAssets();
    sb.AppendLine("场景保存 = " + saved);

    // 体素误差复核：把几个点的"导航网格高度 vs 真实地面高度"直接打出来
    sb.AppendLine();
    sb.AppendLine("体素误差复核（导航网格 Y − 真实表面 Y）：");
    var ray = Physics.RaycastAll(Vector3.zero + Vector3.up * 5f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
    float groundY = 0f;
    foreach (var h in ray) if (h.collider.name == "Ground") groundY = h.point.y;
    sb.AppendLine(string.Format("  地面真实 Y = {0:F3}", groundY));
    foreach (var p in new[] { new Vector3(0, 0.5f, 0), new Vector3(6, 0.5f, 0), new Vector3(0, 0.5f, 6) })
    {
        NavMeshHit h;
        if (NavMesh.SamplePosition(p, out h, 3f, NavMesh.AllAreas))
            sb.AppendLine(string.Format("    {0} → 网格 Y={1:F3}（相对地面 {2:+0.000;-0.000}）", p, h.position.y, h.position.y - groundY));
    }

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/e_scene.txt"), sb.ToString());
    Debug.Log("[e_scene] done");
    yield return null;
}

return Body();
