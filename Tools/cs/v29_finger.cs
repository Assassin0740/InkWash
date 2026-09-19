// 临时探针（第二十九轮）：验证指骨卷曲在运行时真实发生。
// 读 EnemyDragon 私有 _fingerBones/_fingerDepth，采两帧之间的局部旋转角变化。
var d = Object.FindObjectOfType<EnemyDragon>();
if (d == null) return "no dragon";
var t = d.GetType();
var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var bones = t.GetField("_fingerBones", bf).GetValue(d) as System.Collections.Generic.List<Transform>;
var depths = t.GetField("_fingerDepth", bf).GetValue(d) as System.Collections.Generic.List<int>;
if (bones == null || bones.Count == 0) return "no fingers collected";
var sb = new System.Text.StringBuilder();
sb.Append("fingers=").Append(bones.Count).Append("; ");
// 采样前 6 根：当前局部旋转相对采集基准（localRotation 本身即被驱动后的值）的角度
// 这里连续两次调用（间隔由桥的两次 execute 自然产生）对不上相位，
// 所以直接输出每根当前欧拉角 + 深度，由外部两次执行做差。
for (int i = 0; i < bones.Count && i < 6; i++)
{
    var b = bones[i];
    if (b == null) { sb.Append("null;"); continue; }
    var e = b.localRotation.eulerAngles;
    sb.Append("d").Append(depths[i]).Append(":").Append(e.x.ToString("F1")).Append("; ");
}
sb.Append("limbs=");
var roots = t.GetField("_limbRoots", bf).GetValue(d) as System.Collections.Generic.List<Transform>;
sb.Append(roots == null ? 0 : roots.Count);
return sb.ToString();
