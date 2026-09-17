// s4_run.cs —— Sprint 4（水墨风）验收入口：跑 PlaytestHarness.S4InkFlow()。
// 报告落到 Tools/reports/S4_latest.txt，四阶段对照图落到 Tools/screenshots/s4/。
//
// 注意：`--runtime` 跑完**不会自动退出 Play**。第二次跑之前先 `stop`，
// 否则会落在同一个还活着的会话里（静态字段半初始化 + 残留敌人 ⇒ 假故障）。
using System.Collections;
using UnityEngine;
using InkWash.Utils;

IEnumerator Body()
{
    Debug.Log("[s4_run] 开始 Sprint 4 验收");
    yield return PlaytestHarness.S4InkFlow();
    Debug.Log("[s4_run] done");
    yield return null;
}
return Body();
