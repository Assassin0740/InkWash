// 打印 Kevin Iglesias Run FBX 里每个资源的**精确名字**（带字符码），
// 用来排查"片段键名对不上"的问题。
var sb = new System.Text.StringBuilder();
const string P = "Assets/ThirdParty/KevinIglesias/Animations/Male/Movement/Run/HumanM@Run01_Forward.fbx";

foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(P))
{
    if (o == null) { sb.AppendLine("(null asset)"); continue; }
    string n = o.name;
    var codes = new System.Text.StringBuilder();
    foreach (var ch in n) codes.Append(((int)ch).ToString() + " ");
    sb.AppendLine(o.GetType().Name + "  name=[" + n + "]  len=" + n.Length);
    sb.AppendLine("    codes: " + codes.ToString());
}
return sb.ToString();
