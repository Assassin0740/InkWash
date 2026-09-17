// S1-5：装配玩家（两层结构：Player 控制器根 + Visual 视觉子节点）
// 为什么分两层：
//   1. 模型网格底部未必与根节点对齐（实测低 0.102m），需要单独偏移视觉层；
//   2. 若直接偏移带动画的那一层，AnimationClip 可能每帧覆写其局部位置 → 抖动。
//      Visual 作为"动画够不到"的外层，是唯一安全的偏移点。
//   3. 后续换模型（Mixamo → 自研）只需替换 Visual，控制逻辑零改动。
var sb = new System.Text.StringBuilder();

const string Fbx = "Assets/ThirdParty/Quaternius/UniversalBaseCharacters/Base Characters/Unity/Superhero_Male_FullBody.fbx";
const string CtrlPath = "Assets/_Project/Animations/Player.controller";
int L_PLAYER = UnityEngine.LayerMask.NameToLayer("Player");

var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (scene.name != "Main") { sb.AppendLine("!! 不在 Main 场景，中止"); return sb.ToString(); }

var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(Fbx);
if (model == null) { sb.AppendLine("!! 模型加载失败"); return sb.ToString(); }

UnityEngine.Avatar avatar = null;
foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(Fbx))
{ var a = o as UnityEngine.Avatar; if (a != null) { avatar = a; break; } }
if (avatar == null) { sb.AppendLine("!! 没找到 Avatar，中止"); return sb.ToString(); }

var ctrl = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(CtrlPath);
if (ctrl == null) { sb.AppendLine("!! Controller 未找到，中止"); return sb.ToString(); }

// ---------- 1) 清旧 ----------
var old = UnityEngine.GameObject.Find("Player");
if (old != null) { UnityEngine.Object.DestroyImmediate(old); sb.AppendLine("已清除旧 Player"); }

// ---------- 2) 控制器根 ----------
var player = new UnityEngine.GameObject("Player");
player.transform.position = new UnityEngine.Vector3(0f, 0f, -3f);
player.transform.rotation = UnityEngine.Quaternion.identity;
player.tag = "Player";
player.layer = L_PLAYER;

// ---------- 3) 视觉层 ----------
var visual = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(model, player.transform);
visual.name = "Visual";
visual.transform.localPosition = UnityEngine.Vector3.zero;
visual.transform.localRotation = UnityEngine.Quaternion.identity;
visual.transform.localScale = UnityEngine.Vector3.one;
foreach (var t in visual.GetComponentsInChildren<UnityEngine.Transform>(true))
    t.gameObject.layer = L_PLAYER;

// ---------- 4) 量取网格实际包围盒（相对根节点） ----------
var rends = visual.GetComponentsInChildren<UnityEngine.Renderer>(true);
var bnd = new UnityEngine.Bounds();
bool first = true;
foreach (var r in rends)
{
    if (first) { bnd = r.bounds; first = false; }
    else bnd.Encapsulate(r.bounds);
}
float bottomOffset = bnd.min.y - player.transform.position.y;   // 网格底部相对根节点的偏移
float meshHeight = bnd.size.y;
sb.AppendLine("网格包围盒: 高=" + meshHeight.ToString("F3") + " " +
    "底偏移=" + bottomOffset.ToString("F3") + " " +
    "宽=" + bnd.size.x.ToString("F3") + " 渲染器=" + rends.Length);

// ---------- 5) 对齐：网格底部 -> 根节点 y=0 ----------
visual.transform.localPosition = new UnityEngine.Vector3(0f, -bottomOffset, 0f);
sb.AppendLine("Visual 局部 Y 偏移 = " + (-bottomOffset).ToString("F3") + "（脚底对齐根节点）");

// 复查对齐结果
float after = float.MaxValue;
foreach (var r in visual.GetComponentsInChildren<UnityEngine.Renderer>(true))
    after = UnityEngine.Mathf.Min(after, r.bounds.min.y);
sb.AppendLine("复查：对齐后网格底部世界 Y = " + after.ToString("F3") + "（应≈0，留一点余量为正常）");

// ---------- 6) Animator ----------
var anim = player.AddComponent<UnityEngine.Animator>();
anim.avatar = avatar;
anim.runtimeAnimatorController = ctrl;
anim.applyRootMotion = false;       // 位移由 PlayerController 全权接管
anim.updateMode = UnityEngine.AnimatorUpdateMode.Normal;
anim.cullingMode = UnityEngine.AnimatorCullingMode.AlwaysAnimate;

// ---------- 7) CharacterController ----------
var cc = player.AddComponent<UnityEngine.CharacterController>();
cc.height = UnityEngine.Mathf.Round(meshHeight * 100f) / 100f;
cc.radius = 0.32f;
cc.center = new UnityEngine.Vector3(0f, cc.height * 0.5f, 0f);
cc.slopeLimit = 50f;
cc.stepOffset = 0.35f;
cc.skinWidth = 0.03f;
cc.minMoveDistance = 0f;            // 必须为 0，否则低速移动被吞
sb.AppendLine("CharacterController: h=" + cc.height + " r=" + cc.radius + " center=" + cc.center.ToString("F2"));

// ---------- 8) PlayerController ----------
var ctl = player.AddComponent<InkWash.Player.PlayerController>();
ctl.animator = anim;
var camGo = UnityEngine.GameObject.Find("Main Camera");
ctl.cameraTransform = camGo != null ? camGo.transform : null;
sb.AppendLine("PlayerController 已挂，cameraTransform=" + (ctl.cameraTransform != null ? ctl.cameraTransform.name : "null"));

// ---------- 9) Prefab ----------
System.IO.Directory.CreateDirectory("Assets/_Project/Prefabs/Player");
string prefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";
UnityEditor.AssetDatabase.DeleteAsset(prefabPath);
UnityEditor.PrefabUtility.SaveAsPrefabAssetAndConnect(player, prefabPath, UnityEditor.InteractionMode.AutomatedAction);
sb.AppendLine();
sb.AppendLine("Prefab: " + prefabPath);

UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
UnityEditor.AssetDatabase.SaveAssets();

// ---------- 10) 结构复查 ----------
sb.AppendLine();
sb.AppendLine("=== Player 层级 ===");
System.Action<UnityEngine.Transform, int> dump = null;
dump = (t, d) =>
{
    sb.AppendLine(new string(' ', d * 3) + "- " + t.name + "   layer=" + UnityEngine.LayerMask.LayerToName(t.gameObject.layer));
    if (d > 3) return;
    foreach (UnityEngine.Transform c in t) dump(c, d + 1);
};
dump(player.transform, 0);

return sb.ToString();
