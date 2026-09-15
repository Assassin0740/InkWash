// s5_run.cs —— Sprint 5（Roguelike 循环 + 水墨特效）验收入口：跑 PlaytestHarness.S5RoguelikeFlow()。
// 报告落到 Tools/reports/S5_latest.txt（另留时间戳副本）。
//
// 注意：`--runtime` 跑完**不会自动退出 Play**。第二次跑之前先 `stop`，
// 否则会落在同一个还活着的会话里（静态字段半初始化 + 残留敌人 ⇒ 假故障）。
//
// 本流程会跑完一整局（清 3 间房），因此比 S3/S4 久 —— 单段超时给足。
using System.Collections;
using UnityEngine;
using InkWash.Utils;

IEnumerator Body()
{
    Debug.Log("[s5_run] 开始 Sprint 5 验收");
    yield return PlaytestHarness.S5RoguelikeFlow();
    Debug.Log("[s5_run] done");
    yield return null;
}
return Body();
