using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

// 建 Showcase 场景：一块空地 + 一个挂着 ActionShowcase 的宿主对象。
// 只建一次；已存在则直接打开并补齐缺失对象。
public class BuildShowcaseScene
{
    public static object Run()
    {
        const string path = "Assets/_Project/Scenes/Showcase.unity";
        Directory.CreateDirectory("Assets/_Project/Scenes");

        Scene scene;
        if (File.Exists(path))
        {
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }
        else
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // 清掉上一次的残留（幂等：重复跑不会叠对象）
        foreach (var name in new[] { "Showcase", "Showcase_Camera", "Showcase_KeyLight", "Showcase_FillLight", "Showcase_Ground" })
        {
            var old = GameObject.Find(name);
            if (old != null) Object.DestroyImmediate(old);
        }

        var host = new GameObject("Showcase");
        host.transform.position = Vector3.zero;
        var comp = host.AddComponent<InkWash.DebugTools.ActionShowcase>();
        comp.stageCenter = new Vector3(0f, 0f, 0f);
        comp.groundSize = 70f;
        comp.camDistance = 11f;
        comp.camHeight = 4.2f;

        // 场景里没有玩家 —— 主角动作需要它，这里放一个最小可用的玩家
        // （复用 Main 场景里的玩家预制体，保证动画器/材质都是真的）
        var existing = GameObject.Find("Player");
        if (existing == null)
        {
            string[] candidates = {
                "Assets/_Project/Prefabs/Player/Z_Player.prefab",
                "Assets/_Project/Prefabs/Player/Player.prefab",
                "Assets/_Project/Prefabs/Player.prefab",
            };
            GameObject pp = null;
            foreach (var c in candidates) { pp = AssetDatabase.LoadAssetAtPath<GameObject>(c); if (pp != null) break; }
            if (pp != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(pp);
                inst.name = "Player";
                inst.transform.position = new Vector3(0f, 0.05f, -3.5f);
                Debug.Log("[BuildShowcase] 已放入玩家：" + AssetDatabase.GetAssetPath(pp));
            }
            else Debug.LogWarning("[BuildShowcase] 没找到玩家预制体，主角动作将不可用");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, path);
        Debug.Log("[BuildShowcase] 场景已保存：" + path + "  根对象数=" + scene.rootCount);
        return path;
    }
}

var built = BuildShowcaseScene.Run();
return built;
