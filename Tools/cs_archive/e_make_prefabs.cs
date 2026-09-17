// e_make_prefabs.cs —— 生成敌人预制体 + 墨弹预制体，并给 Player.prefab 补上受击/判定组件（编辑态）
//   尺寸不靠"看宣传图"：逐顶点 BakeMesh 量出模型原生身高，再按目标身高反算容器缩放
//   （SkinnedMeshRenderer.bounds 会把 Q 版模型量成 2.35 m，这个坑本项目踩过）。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using InkWash.Combat;
using InkWash.Enemies;
using InkWash.Player;

IEnumerator Body()
{
    var sb = new StringBuilder();
    string projRoot = Path.GetDirectoryName(Application.dataPath);
    var prefabDir = "Assets/_Project/Prefabs/Enemies";
    Directory.CreateDirectory(Path.Combine(projRoot, prefabDir));
    AssetDatabase.Refresh();
    yield return null; yield return null;

    // ---------- 原生身高：逐顶点量 ----------
    float MeasureHeight(GameObject root)
    {
        var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var mesh = new Mesh();
        var buf = new List<Vector3>(8192);
        float mn = float.MaxValue, mx = float.MinValue;
        foreach (var s in smrs)
        {
            if (s == null || s.sharedMesh == null) continue;
            s.BakeMesh(mesh); mesh.GetVertices(buf);
            var m = s.transform.localToWorldMatrix;
            for (int i = 0; i < buf.Count; i++)
            {
                float y = m.m10 * buf[i].x + m.m11 * buf[i].y + m.m12 * buf[i].z + m.m13;
                if (y < mn) mn = y; if (y > mx) mx = y;
            }
        }
        if (mx < mn) return 0f;
        return mx - mn;
    }

    // ---------- 造一个敌人预制体 ----------
    GameObject BuildEnemy(string modelFbx, string prefabName, string ctrlPath,
                          float targetHeight, bool isElite, bool isRanged,
                          System.Action<GameObject> extra)
    {
        var root = new GameObject(prefabName);
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(modelFbx);
        if (fbx == null) { sb.AppendLine("  [ERR] 模型加载失败 " + modelFbx); return null; }

        var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
        model.name = "Visual";
        // FBX 自带的 Animator 会跟根上的抢骨骼 —— 本项目换主角时踩过，直接删
        foreach (var a in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a, true);

        float native = MeasureHeight(model);
        float scale = native > 0.001f ? targetHeight / native : 1f;
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * scale;

        // 模型原点是否在脚底？量一次世界最低点
        float baseY = 0f;
        {
            var smrs = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var mesh = new Mesh(); var buf = new List<Vector3>(8192);
            float mn = float.MaxValue;
            foreach (var s in smrs)
            {
                if (s == null || s.sharedMesh == null) continue;
                s.BakeMesh(mesh); mesh.GetVertices(buf);
                var m = s.transform.localToWorldMatrix;
                for (int i = 0; i < buf.Count; i++)
                {
                    float y = m.m10 * buf[i].x + m.m11 * buf[i].y + m.m12 * buf[i].z + m.m13;
                    if (y < mn) mn = y;
                }
            }
            if (mn < float.MaxValue) baseY = mn;
        }
        if (Mathf.Abs(baseY) > 0.01f) model.transform.localPosition = new Vector3(0f, -baseY, 0f);

        // ---- 根组件 ----
        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = Mathf.Max(0.3f, targetHeight * 0.19f);
        agent.height = targetHeight;
        agent.baseOffset = 0f;
        agent.speed = 3.4f;
        agent.acceleration = 14f;
        agent.angularSpeed = 720f;
        agent.stoppingDistance = 0f;
        agent.autoBraking = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

        var col = root.AddComponent<CapsuleCollider>();
        col.height = targetHeight;
        col.radius = Mathf.Max(0.28f, targetHeight * 0.19f);
        col.center = new Vector3(0f, targetHeight * 0.5f, 0f);

        var anim = root.AddComponent<Animator>();
        anim.applyRootMotion = false;
        anim.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ctrlPath);
        anim.avatar = AssetDatabase.LoadAllAssetsAtPath(modelFbx).OfType<Avatar>().FirstOrDefault();

        var hitbox = root.AddComponent<Hitbox>();
        hitbox.owner = root;
        hitbox.ownerFaction = Faction.Enemy;
        hitbox.radius = isElite ? 1.35f : 0.55f;
        hitbox.pointA = new Vector3(0f, targetHeight * 0.5f, targetHeight * 0.15f);
        hitbox.pointB = new Vector3(0f, targetHeight * 0.5f, targetHeight * (isElite ? 0.85f : 0.75f));
        hitbox.damage = isElite ? 26f : (isRanged ? 0f : 12f);   // 远程的伤害在墨弹上
        hitbox.knockback = isElite ? 7.5f : 4f;
        hitbox.hitStun = isElite ? 0.45f : 0.3f;
        hitbox.hitStop = isElite ? 0.09f : 0.05f;
        hitbox.oncePerTargetInWindow = true;

        if (extra != null) extra(root);
        return root;
    }

    // ---------- ① 墨徒（近战）----------
    sb.AppendLine("========== 生成敌人预制体 ==========");
    var moTu = BuildEnemy(
        "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Minion.fbx",
        "Enemy_MoTu", "Assets/_Project/Animations/Enemies/EnemyMelee.controller",
        1.75f, false, false, root =>
        {
            var e = root.AddComponent<EnemyMelee>();
            e.enemyName = "墨徒";
            e.maxHealth = 42f;
            e.walkSpeed = 1.6f; e.chaseSpeed = 3.6f;
            e.sightRange = 17f; e.loseSightRange = 26f; e.sightAngleDeg = 220f;
            e.attackRange = 2.1f; e.attackCooldown = 1.35f;
            e.attackWindup = 0.34f; e.attackActive = 0.16f; e.attackRecover = 0.42f;
            e.destroyAfterDeath = 2.4f;
        });
    sb.AppendLine("  墨徒  身高=1.75m  " + (moTu != null ? "OK" : "失败"));

    // ---------- ② 墨偶（远程）----------
    var moOu = BuildEnemy(
        "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Mage.fbx",
        "Enemy_MoOu", "Assets/_Project/Animations/Enemies/EnemyRanged.controller",
        1.70f, false, true, root =>
        {
            var e = root.AddComponent<EnemyRanged>();
            e.enemyName = "墨偶";
            e.maxHealth = 30f;
            e.walkSpeed = 1.4f; e.chaseSpeed = 2.9f;
            e.sightRange = 20f; e.loseSightRange = 28f; e.sightAngleDeg = 250f;
            e.attackRange = 10.5f; e.attackCooldown = 1.9f;
            e.attackWindup = 0.42f; e.attackActive = 0.14f; e.attackRecover = 0.5f;
            e.keepDistance = 7.5f; e.retreatDistance = 4.2f; e.retreatStep = 4.5f;
            e.projectileDamage = 9f; e.projectileSpeed = 13f;
            e.destroyAfterDeath = 2.4f;
            // 枪口：胸口高度前方
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(root.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 1.25f, 0.45f);
            e.muzzle = muzzle.transform;
        });
    sb.AppendLine("  墨偶  身高=1.70m  " + (moOu != null ? "OK" : "失败"));

    // ---------- ③ 墨魇（精英）----------
    var moYan = BuildEnemy(
        "Assets/ThirdParty/KayKit/Skeletons/Characters/Skeleton_Golem.fbx",
        "Enemy_MoYan", "Assets/_Project/Animations/Enemies/EnemyElite.controller",
        2.55f, true, false, root =>
        {
            var e = root.AddComponent<EnemyElite>();
            e.enemyName = "墨魇";
            e.maxHealth = 260f;
            e.walkSpeed = 1.5f; e.chaseSpeed = 3.0f;
            e.sightRange = 22f; e.loseSightRange = 32f; e.sightAngleDeg = 260f;
            e.attackRange = 3.4f; e.attackCooldown = 1.5f;
            e.attackWindup = 0.62f; e.attackActive = 0.26f; e.attackRecover = 0.72f;
            e.chargeSpeed = 9.0f; e.chargeMaxTime = 0.6f;
            e.parryWindow = 0.42f; e.parryDamageScale = 0.35f; e.parryStunScale = 3.2f;
            e.destroyAfterDeath = 3.5f;
        });
    sb.AppendLine("  墨魇  身高=2.55m  " + (moYan != null ? "OK" : "失败"));

    // ---------- 保存敌人预制体 ----------
    foreach (var pair in new (GameObject, string)[] {
        (moTu, prefabDir + "/Enemy_MoTu.prefab"),
        (moOu, prefabDir + "/Enemy_MoOu.prefab"),
        (moYan, prefabDir + "/Enemy_MoYan.prefab") })
    {
        if (pair.Item1 == null) continue;
        PrefabUtility.SaveAsPrefabAsset(pair.Item1, pair.Item2);
        Object.DestroyImmediate(pair.Item1);
        sb.AppendLine("  已保存 " + pair.Item2);
    }

    // ---------- ④ 墨弹预制体 ----------
    {
        AssetDatabase.DeleteAsset("Assets/_Project/Art/Materials/M_Ink.mat");
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.name = "M_Ink";
        mat.SetColor("_BaseColor", new Color(0.05f, 0.055f, 0.075f, 1f));
        mat.SetFloat("_Smoothness", 0.62f);
        mat.SetFloat("_Metallic", 0.0f);
        AssetDatabase.CreateAsset(mat, "Assets/_Project/Art/Materials/M_Ink.mat");

        var bolt = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bolt.name = "Ink_Projectile";
        Object.DestroyImmediate(bolt.GetComponent<Collider>());   // 判定用扫掠查询，不用碰撞体
        bolt.transform.localScale = Vector3.one * 0.34f;
        bolt.GetComponent<MeshRenderer>().sharedMaterial = mat;

        var trail = bolt.AddComponent<TrailRenderer>();
        trail.time = 0.22f;
        trail.startWidth = 0.26f;
        trail.endWidth = 0.0f;
        trail.material = mat;
        trail.numCapVertices = 2;

        var proj = bolt.AddComponent<InkProjectile>();
        proj.ownerFaction = Faction.Enemy;
        proj.speed = 13f; proj.damage = 9f; proj.radius = 0.34f; proj.lifetime = 5f;

        PrefabUtility.SaveAsPrefabAsset(bolt, prefabDir + "/Ink_Projectile.prefab");
        Object.DestroyImmediate(bolt);
        sb.AppendLine("  已保存 " + prefabDir + "/Ink_Projectile.prefab");
    }

    // 把墨弹挂回墨偶的 muzzle
    {
        var path = prefabDir + "/Enemy_MoOu.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        var e = root.GetComponent<EnemyRanged>();
        if (e != null && e.projectilePrefab == null)
        {
            e.projectilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabDir + "/Ink_Projectile.prefab");
            var mz = root.transform.Find("Muzzle");
            if (mz != null) e.muzzle = mz;
        }
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        sb.AppendLine("  墨偶已挂上墨弹预制体");
    }

    // ---------- ⑤ Player.prefab 补组件 ----------
    {
        string pPath = "Assets/_Project/Prefabs/Player/Player.prefab";
        var root = PrefabUtility.LoadPrefabContents(pPath);
        var pc = root.GetComponent<PlayerController>();
        var vfx = root.GetComponent<InkWash.Effects.SwordVfx>();

        var ph = root.GetComponent<PlayerHealth>();
        if (ph == null) ph = root.AddComponent<PlayerHealth>();
        ph.controller = pc;
        ph.maxHealth = 120f;
        ph.invincibleAfterHit = 0.65f;

        var hbGo = root.transform.Find("SwordHitbox");
        if (hbGo == null)
        {
            var g = new GameObject("SwordHitbox");
            g.transform.SetParent(root.transform, false);
            hbGo = g.transform;
        }
        var hb = hbGo.GetComponent<Hitbox>();
        if (hb == null) hb = hbGo.gameObject.AddComponent<Hitbox>();
        hb.owner = root;
        hb.ownerFaction = Faction.Player;
        hb.radius = 0.42f;
        hb.pointA = Vector3.zero;
        hb.pointB = new Vector3(0f, 0f, 1f);
        hb.damage = 16f; hb.knockback = 2.6f; hb.hitStun = 0.26f; hb.hitStop = 0.055f;
        hb.oncePerTargetInWindow = true;

        var psh = hbGo.GetComponent<PlayerSwordHitbox>();
        if (psh == null) psh = hbGo.gameObject.AddComponent<PlayerSwordHitbox>();
        psh.player = pc;
        psh.vfx = vfx;

        PrefabUtility.SaveAsPrefabAsset(root, pPath);
        PrefabUtility.UnloadPrefabContents(root);
        sb.AppendLine("  已给 Player.prefab 补上 PlayerHealth + SwordHitbox");
    }

    AssetDatabase.SaveAssets();
    AssetDatabase.Refresh();

    File.WriteAllText(Path.Combine(projRoot, "Tools/reports/e_make_prefabs.txt"), sb.ToString());
    Debug.Log("[e_make_prefabs] done");
    yield return null;
}

return Body();
