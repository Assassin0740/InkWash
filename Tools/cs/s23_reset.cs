// s23_reset.cs —— 把探针可能留下的全局时间状态复位。
// ★ 为什么需要它：探针在 Run() 里钉 `Time.captureFramerate = 30`，只在最后的 Done() 里解。
//   一旦中途抛异常（协程被异常终止），Done() 永远不执行 ⇒ 编辑器就留在「deltaTime 恒 1/30」的
//   假帧率状态里，之后所有性能测量全部失真 **而控制台一片干净**（本节第 45 条同源）。
//   ⇒ 每次探针异常 / 中断之后，先跑这个再重新量。
using UnityEngine;
Time.captureFramerate = 0;
Time.timeScale = 1f;
return "S23_RESET captureFramerate=" + Time.captureFramerate + " timeScale=" + Time.timeScale;
