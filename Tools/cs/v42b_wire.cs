// v42b: Mixamo clip 接入 Player.controller
// ① Idle/Walk/Run 三循环 clip 设 LoopTime ②替换 Idle/Walk/Run/Atk1/Atk2/Atk3/Death
// ③新增 Hit 状态（sword and shield impact）+ Hit 触发器 + AnyState 进 / 出
using System.Collections;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

IEnumerator Body()
{
    var sb = new System.Text.StringBuilder();
    System.Func<string, AnimationClip> loadClip = fbxPath =>
    {
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            var c = a as AnimationClip;
            if (c != null && !c.name.Contains("preview")) return c;
        }
        return null;
    };

    // ① 循环 clip：LoopTime = true
    foreach (var f in new[] {
        "Assets/Mixamo/sword and shield idle.fbx",
        "Assets/Mixamo/sword and shield walk.fbx",
        "Assets/Mixamo/sword and shield run.fbx" })
    {
        var imp = AssetImporter.GetAtPath(f) as ModelImporter;
        if (imp == null) { sb.AppendLine("[v42b] no importer " + f); continue; }
        var clips = imp.defaultClipAnimations;
        bool changed = false;
        foreach (var c in clips) { if (!c.loopTime) { c.loopTime = true; changed = true; } }
        if (changed)
        {
            var cfg = new System.Collections.Generic.List<ModelImporterClipAnimation>(clips);
            imp.clipAnimations = cfg.ToArray();
            imp.SaveAndReimport();
        }
        sb.AppendLine("[v42b] loopTime set: " + f);
    }

    var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Project/Animations/Player.controller");
    if (ctrl == null) { sb.AppendLine("[v42b] controller NOT FOUND"); Debug.Log("[v42b]\n" + sb); yield break; }
    var sm = ctrl.layers[0].stateMachine;

    var idleClip = loadClip("Assets/Mixamo/sword and shield idle.fbx");
    var walkClip = loadClip("Assets/Mixamo/sword and shield walk.fbx");
    var runClip  = loadClip("Assets/Mixamo/sword and shield run.fbx");
    var atk1     = loadClip("Assets/Mixamo/sword and shield slash.fbx");
    var atk2     = loadClip("Assets/Mixamo/sword and shield slash (2).fbx");
    var atk3     = loadClip("Assets/Mixamo/sword and shield attack.fbx");
    var hitClip  = loadClip("Assets/Mixamo/sword and shield impact.fbx");
    var deathClip= loadClip("Assets/Mixamo/sword and shield death.fbx");
    var map = new System.Collections.Generic.Dictionary<string, AnimationClip> {
        { "Idle", idleClip }, { "Walk", walkClip }, { "Run", runClip },
        { "Atk1", atk1 }, { "Atk2", atk2 }, { "Atk3", atk3 }, { "Death", deathClip },
    };
    foreach (var st in sm.states)
    {
        if (map.TryGetValue(st.state.name, out var clip) && clip != null)
        {
            sb.AppendLine("[v42b] " + st.state.name + ": " + (st.state.motion ? st.state.motion.name : "null") + " -> " + clip.name);
            st.state.motion = clip;
        }
    }

    // ③ Hit 状态
    AnimatorState hitState = null;
    foreach (var st in sm.states) if (st.state.name == "Hit") hitState = st.state;
    if (hitState == null && hitClip != null)
    {
        hitState = sm.AddState("Hit", new Vector3(320, 120, 0));
        hitState.motion = hitClip;
        bool hasParam = false;
        foreach (var p in ctrl.parameters) if (p.name == "Hit") { hasParam = true; break; }
        if (!hasParam) ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

        var any = sm.AddAnyStateTransition(hitState);
        any.duration = 0.06f;
        any.hasExitTime = false;
        any.AddCondition(AnimatorConditionMode.If, 0, "Hit");

        // Hit 结束回 Idle（继续由 Speed 条件链回 Walk/Run）
        var back = hitState.AddTransition(sm.states[0].state); // Idle 在 states[0]
        back.hasExitTime = true;
        back.exitTime = 0.95f;
        back.duration = 0.12f;
        sb.AppendLine("[v42b] Hit state created (AnyState --Hit--> Hit --> Idle)");
    }
    else sb.AppendLine("[v42b] Hit state exists=" + (hitState != null) + " hitClip=" + (hitClip != null));

    AssetDatabase.SaveAssets();
    EditorUtility.SetDirty(ctrl);
    AssetDatabase.SaveAssets();
    Debug.Log("[v42b]\n" + sb);
    yield break;
}

return Body();
