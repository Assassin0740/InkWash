// S1-2：建「水墨禅院」白盒场景并保存为 Assets/_Project/Scenes/Main.unity
var sb = new System.Text.StringBuilder();

// ---------- 0) 确保当前场景干净，避免弹保存对话框卡住桥 ----------
var active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
sb.AppendLine("当前场景: " + active.path + "  isDirty=" + active.isDirty);
if (active.isDirty) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
    UnityEditor.SceneManagement.NewSceneMode.Single);
sb.AppendLine("已新建空场景: " + scene.name);
sb.AppendLine();

// ---------- 1) 材质（URP 必须显式给 Lit，否则 CreatePrimitive 拿到 Built-in 粉红材质） ----------
string matDir = "Assets/_Project/Art/Materials";
if (!UnityEditor.AssetDatabase.IsValidFolder(matDir))
    UnityEditor.AssetDatabase.CreateFolder("Assets/_Project/Art", "Materials");

var litShader = UnityEngine.Shader.Find("Universal Render Pipeline/Lit");
if (litShader == null) { sb.AppendLine("!! 找不到 URP/Lit Shader"); return sb.ToString(); }

System.Func<string, UnityEngine.Color, UnityEngine.Material> makeMat =
    (name, col) =>
    {
        string path = matDir + "/" + name + ".mat";
        var m = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
        if (m == null)
        {
            m = new UnityEngine.Material(litShader);
            UnityEditor.AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", col);
        m.SetFloat("_Smoothness", 0.05f);   // 哑光：水墨要的是吸光纸面，不是塑料高光
        m.SetFloat("_Metallic", 0f);
        UnityEditor.EditorUtility.SetDirty(m);
        return m;
    };

var mGround = makeMat("M_Whitebox_Ground", new UnityEngine.Color(0.72f, 0.71f, 0.68f));
var mWall = makeMat("M_Whitebox_Wall", new UnityEngine.Color(0.60f, 0.58f, 0.55f));
var mPlatform = makeMat("M_Whitebox_Platform", new UnityEngine.Color(0.80f, 0.79f, 0.76f));
var mPillar = makeMat("M_Whitebox_Pillar", new UnityEngine.Color(0.55f, 0.53f, 0.50f));
UnityEditor.AssetDatabase.SaveAssets();
sb.AppendLine("材质已就绪: Ground / Wall / Platform / Pillar");
sb.AppendLine();

// ---------- 2) 工具方法 ----------
System.Func<string, UnityEngine.Vector3, UnityEngine.Vector3, UnityEngine.Material, int, UnityEngine.GameObject> box =
    (name, pos, scale, mat, layer) =>
    {
        var go = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial = mat;
        go.layer = layer;
        return go;
    };

int L_GROUND = UnityEngine.LayerMask.NameToLayer("Ground");
int L_WALL = UnityEngine.LayerMask.NameToLayer("Wall");

// ---------- 3) 环境 ----------
var envRoot = new UnityEngine.GameObject("Environment");

var ground = box("Ground", new UnityEngine.Vector3(0f, -0.25f, 0f),
    new UnityEngine.Vector3(44f, 0.5f, 44f), mGround, L_GROUND);
ground.transform.SetParent(envRoot.transform);

// 比试台（中央高台）：给移动速度一个视觉参照
var platform = box("Arena", new UnityEngine.Vector3(0f, 0.15f, 0f),
    new UnityEngine.Vector3(10f, 0.3f, 10f), mPlatform, L_GROUND);
platform.transform.SetParent(envRoot.transform);

// 四面院墙（禅院围合）
float H = 4f, T = 0.6f, half = 22f;
box("Wall_N", new UnityEngine.Vector3(0f, H / 2f, half), new UnityEngine.Vector3(half * 2f + T, H, T), mWall, L_WALL).transform.SetParent(envRoot.transform);
box("Wall_S", new UnityEngine.Vector3(0f, H / 2f, -half), new UnityEngine.Vector3(half * 2f + T, H, T), mWall, L_WALL).transform.SetParent(envRoot.transform);
box("Wall_E", new UnityEngine.Vector3(half, H / 2f, 0f), new UnityEngine.Vector3(T, H, half * 2f + T), mWall, L_WALL).transform.SetParent(envRoot.transform);
box("Wall_W", new UnityEngine.Vector3(-half, H / 2f, 0f), new UnityEngine.Vector3(T, H, half * 2f + T), mWall, L_WALL).transform.SetParent(envRoot.transform);

// 四角立柱：打破空旷、提供远近参照
var pillars = new UnityEngine.GameObject("Pillars");
pillars.transform.SetParent(envRoot.transform);
float[][] corners = { new float[]{14f,14f}, new float[]{14f,-14f}, new float[]{-14f,14f}, new float[]{-14f,-14f} };
for (int i = 0; i < corners.Length; i++)
{
    var p = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cylinder);
    p.name = "Pillar_" + i;
    p.transform.position = new UnityEngine.Vector3(corners[i][0], 3f, corners[i][1]);
    p.transform.localScale = new UnityEngine.Vector3(0.8f, 3f, 0.8f);
    p.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial = mPillar;
    p.layer = L_WALL;
    p.transform.SetParent(pillars.transform);
}
sb.AppendLine("环境: Ground / Arena / 4 面院墙 / 4 根立柱");
sb.AppendLine();

// ---------- 4) 光照 ----------
var lightRoot = new UnityEngine.GameObject("Lighting");

var sunGo = new UnityEngine.GameObject("Directional Light (Sun)");
var sun = sunGo.AddComponent<UnityEngine.Light>();
sun.type = UnityEngine.LightType.Directional;
sun.color = new UnityEngine.Color(1f, 0.97f, 0.92f);
sun.intensity = 1.15f;
sun.shadows = UnityEngine.LightShadows.Soft;
sun.shadowStrength = 0.55f;          // 水墨不要硬投影
sunGo.transform.rotation = UnityEngine.Quaternion.Euler(48f, -35f, 0f);
sunGo.transform.SetParent(lightRoot.transform);

var fillGo = new UnityEngine.GameObject("Fill Light (Sky)");
var fill = fillGo.AddComponent<UnityEngine.Light>();
fill.type = UnityEngine.LightType.Directional;
fill.color = new UnityEngine.Color(0.75f, 0.80f, 0.92f);
fill.intensity = 0.28f;              // 冷色补光，把暗部从死黑里拉回来
fill.shadows = UnityEngine.LightShadows.None;
fillGo.transform.rotation = UnityEngine.Quaternion.Euler(22f, 150f, 0f);
fillGo.transform.SetParent(lightRoot.transform);

// 环境光 + 雾（水墨的空间层次靠雾做远近浓淡）
UnityEngine.RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
UnityEngine.RenderSettings.ambientSkyColor = new UnityEngine.Color(0.62f, 0.63f, 0.62f);
UnityEngine.RenderSettings.ambientEquatorColor = new UnityEngine.Color(0.50f, 0.50f, 0.49f);
UnityEngine.RenderSettings.ambientGroundColor = new UnityEngine.Color(0.30f, 0.30f, 0.30f);
UnityEngine.RenderSettings.fog = true;
UnityEngine.RenderSettings.fogMode = UnityEngine.FogMode.ExponentialSquared;
UnityEngine.RenderSettings.fogColor = new UnityEngine.Color(0.90f, 0.90f, 0.88f);
UnityEngine.RenderSettings.fogDensity = 0.012f;
sb.AppendLine("光照: 主光(软阴影,强度1.15) + 补光(0.28) + Trilight 环境光 + 指数雾");
sb.AppendLine();

// ---------- 5) 相机 ----------
var camGo = new UnityEngine.GameObject("Main Camera");
camGo.tag = "MainCamera";
var cam = camGo.AddComponent<UnityEngine.Camera>();
cam.fieldOfView = 45f;
cam.nearClipPlane = 0.1f;
cam.farClipPlane = 300f;
camGo.AddComponent<UnityEngine.AudioListener>();
camGo.transform.position = new UnityEngine.Vector3(0f, 9f, -13f);
camGo.transform.rotation = UnityEngine.Quaternion.Euler(32f, 0f, 0f);
// URP 必须挂 UniversalAdditionalCameraData，否则相机走 Built-in 路径
camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
camGo.transform.SetParent(null);
sb.AppendLine("相机: Main Camera (FOV 45, 俯角 32°, 已挂 URP CameraData)");

// ---------- 6) 保存场景 ----------
string scenePath = "Assets/_Project/Scenes/Main.unity";
System.IO.Directory.CreateDirectory("Assets/_Project/Scenes");
bool ok = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
sb.AppendLine();
sb.AppendLine("保存场景: " + scenePath + "  -> " + ok);

// 加进 Build Settings（否则打包后没有场景）
var list = new System.Collections.Generic.List<UnityEditor.EditorBuildSettingsScene>(UnityEditor.EditorBuildSettings.scenes);
bool exists = false;
foreach (var s in list) if (s.path == scenePath) exists = true;
if (!exists) { list.Insert(0, new UnityEditor.EditorBuildSettingsScene(scenePath, true)); UnityEditor.EditorBuildSettings.scenes = list.ToArray(); }
sb.AppendLine("Build Settings 场景数: " + UnityEditor.EditorBuildSettings.scenes.Length);

UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.AssetDatabase.Refresh();

// ---------- 7) 复查场景层级 ----------
sb.AppendLine();
sb.AppendLine("=== 场景层级复查 ===");
foreach (var root in scene.GetRootGameObjects())
{
    sb.AppendLine("   " + root.name);
    foreach (UnityEngine.Transform c in root.transform)
        sb.AppendLine("      - " + c.name + "  pos=" + c.position.ToString("F1") + "  layer=" + UnityEngine.LayerMask.LayerToName(c.gameObject.layer));
}

return sb.ToString();
