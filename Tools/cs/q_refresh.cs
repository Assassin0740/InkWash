// q_refresh.cs —— 强制 AssetDatabase 重新导入（手改 .prefab / .asset / .mat / .shader 之后必须）
//
// 为什么必须显式刷：**手改磁盘上的 YAML，Unity 不会立刻知道**。
// 上一轮的教训：改完 prefab 直接读 `SerializedObject`，读到的还是旧值 30
// —— 因为资产没重新导入，内存里还是旧序列化结果（新字段则一律按 C# 默认值返回，
//    所以「新字段读到默认值」根本不能证明 YAML 生效了，得看**被改动的那个旧字段**）。
using UnityEditor;

AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
AssetDatabase.ImportAsset("Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab",
                          ImportAssetOptions.ForceUpdate);
AssetDatabase.SaveAssets();
return "REFRESHED";
