// a34_dragon_render.cs —— 用**画面**判定龙的真实尺寸与朝向（不再靠包围盒推理）
//
// 为什么要走到这一步：包围盒（无论哪种口径）给出的 Y 高 5.87~6.8 m，
//   对一条体长 7 m 的龙来说明显不合理 —— 原因很可能是 275 根骨里包含大量
//   **鳍/须/翼的散开骨骼**，它们的 bind pose 张得很大，把 AABB 撑起来了。
//   AABB 是轴对齐的，它衡量的是"最远的两个点"，**不能代表"看起来多大"**。
//
//   ⇒ 唯一能定案的证据是**渲染一张图**。用它回答三个问题：
//       ① 龙在画面里占多大（相对已知尺寸的参照物）
//       ② 头朝哪个方向（判定 rot.y=270 是否正确）
//       ③ 它趴/浮在什么高度（判定是否穿地）
//
// 做法：临时场景 + 正交/透视相机 + 已知大小的参照方块 + 渲染到 PNG。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);
string shotDir = Path.Combine(root, "Tools/screenshots/dragon");
Directory.CreateDirectory(shotDir);

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab");
sb.AppendLine("========== a34 渲染判定 ==========");
if (prefab == null) { sb.AppendLine("★ 读不到"); File.WriteAllText(Path.Combine(root, "Tools/reports/a34_dragon_render.txt"), sb.ToString()); Debug.LogError(sb.ToString()); return; }

// 新建一个临时场景（当前场景是 Main，不要污染它）
var tmpScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

// 地面参照：10×10 的平板 + 一个 2 m 高的立方体（当比例尺）
var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
ground.name = "Ref_Ground";
ground.transform.position = new Vector3(0f, -0.05f, 0f);
ground.transform.localScale = new Vector3(20f, 0.1f, 20f);

var refCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
refCube.name = "Ref_2m_Player";
refCube.transform.position = new Vector3(3.5f, 1.0f, -3f);
refCube.transform.localScale = new Vector3(0.6f, 2.0f, 0.6f);   // 2 m 高 = 主角身高

// 龙
var dragon = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
dragon.transform.position = Vector3.zero;
dragon.transform.rotation = Quaternion.identity;

// 光
var lightGo = new GameObject("Sun");
var lt = lightGo.AddComponent<Light>();
lt.type = LightType.Directional;
lt.intensity = 1.2f;
lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

// 相机（透视，看龙全貌）
var camGo = new GameObject("Cam");
var cam = camGo.AddComponent<Camera>();
cam.clearFlags = CameraClearFlags.SolidColor;
cam.backgroundColor = new Color(0.92f, 0.92f, 0.9f, 1f);

int shotW = 900, shotH = 700;

void Shoot(string name, Vector3 camPos, Vector3 lookAt, float fov = 50f)
{
    camGo.transform.position = camPos;
    camGo.transform.LookAt(lookAt);
    cam.fieldOfView = fov;
    var rt = new RenderTexture(shotW, shotH, 24, RenderTextureFormat.ARGB32);
    rt.antiAliasing = 4;
    cam.targetTexture = rt;
    cam.Render();

    RenderTexture.active = rt;
    var tex = new Texture2D(shotW, shotH, TextureFormat.RGB24, false);
    tex.ReadPixels(new Rect(0, 0, shotW, shotH), 0, 0);
    tex.Apply();
    RenderTexture.active = null;
    cam.targetTexture = null;

    string p = Path.Combine(shotDir, name + ".png");
    File.WriteAllBytes(p, tex.EncodeToPNG());
    UnityEngine.Object.DestroyImmediate(tex);
    rt.Release();
    UnityEngine.Object.DestroyImmediate(rt);
    sb.AppendLine("  已渲染 " + p);
}

// 先量一下龙的实际范围（供相机定位）
var (bmn, bmx) = MeshBounds(dragon);
sb.AppendLine("网格 AABB: X[" + bmn.x.ToString("F2") + "," + bmx.x.ToString("F2") + "]"
              + " Y[" + bmn.y.ToString("F2") + "," + bmx.y.ToString("F2") + "]"
              + " Z[" + bmn.z.ToString("F2") + "," + bmx.z.ToString("F2") + "]");

// 三视图
Shoot("V1_侧视_左", new Vector3(-14f, 5f, 0f), new Vector3(0f, 2.5f, 0f));
Shoot("V2_侧视_右", new Vector3(14f, 5f, 0f), new Vector3(0f, 2.5f, 0f));
Shoot("V3_正视", new Vector3(0f, 5f, 16f), new Vector3(0f, 2.5f, 0f));
Shoot("V4_俯视", new Vector3(0f, 18f, 0.01f), new Vector3(0f, 0f, 0f), 60f);
Shoot("V5_近景头", new Vector3(-5f, 3f, 5f), new Vector3(0f, 2f, 2f), 45f);

// 顺带报一下"如果把 Y 压到合理高度"需要多少
sb.AppendLine();
sb.AppendLine("Y 高 = " + (bmx.y - bmn.y).ToString("F3") + " m（体长 7 m 的龙，这个高偏大 ⇒ 大概率是鳍/须散开撑的）");

// 还原场景
EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity", OpenSceneMode.Single);

File.WriteAllText(Path.Combine(root, "Tools/reports/a34_dragon_render.txt"), sb.ToString());
Debug.Log("[a34]\n" + sb.ToString());

(Vector3 mn, Vector3 mx) MeshBounds(GameObject go)
{
    var mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
    var mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
    bool any = false;
    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
    { var bb = smr.bounds; mn = Vector3.Min(mn, bb.min); mx = Vector3.Max(mx, bb.max); any = true; }
    foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
    {
        if (mr.GetComponent<SkinnedMeshRenderer>() != null) continue;
        var bb = mr.bounds; mn = Vector3.Min(mn, bb.min); mx = Vector3.Max(mx, bb.max); any = true;
    }
    return any ? (mn, mx) : (Vector3.zero, Vector3.zero);
}
