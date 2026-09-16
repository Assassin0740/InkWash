// a28_reimport.cs —— 强制 Unity 丢弃内存缓存、从磁盘重新导入 Enemy_MoYan
//
// 背景：我用 git checkout 把 Enemy_MoYan.prefab 还原成干净版本（磁盘上 0 个 drgon_），
//   但 a27 的防御闸门仍然读到 272 个 drgon_ 节点 —— 说明 **Unity 的内存缓存没失效**。
//
//   ★ 这正是本项目硬规矩 8 的另一个面：
//     "改资产后必须 refresh，否则静默不生效" —— 反过来同样成立：
//     **外部（git）改了资产后，也必须强制重新导入，否则 Unity 继续用旧的**。
//     而且它**不会报错**，只会让你以为"还原没成功"。
//
// 做法：AssetDatabase.ImportAsset(path, ForceUpdate | ForceSynchronousImport)
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

var sb = new StringBuilder();
string root = Path.GetDirectoryName(Application.dataPath);

const string target = "Assets/_Project/Prefabs/Enemies/Enemy_MoYan.prefab";
sb.AppendLine("========== a28 强制重新导入 ==========");
sb.AppendLine("目标 = " + target);

sb.AppendLine("导入前 drgon_* 节点数 = " + CountDrgon());

AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

sb.AppendLine("导入后 drgon_* 节点数 = " + CountDrgon());
sb.AppendLine();
sb.AppendLine("（0 = 干净；非 0 = 缓存仍未失效，需要重启 Unity）");

File.WriteAllText(Path.Combine(root, "Tools/reports/a28_reimport.txt"), sb.ToString());
Debug.Log("[a28]\n" + sb.ToString());

int CountDrgon()
{
    var p = AssetDatabase.LoadAssetAtPath<GameObject>(target);
    if (p == null) return -1;
    int n = 0;
    foreach (var t in p.GetComponentsInChildren<Transform>(true))
        if (t.name.StartsWith("drgon_")) n++;
    return n;
}
