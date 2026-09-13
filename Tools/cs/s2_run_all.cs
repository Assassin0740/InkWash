// 触发 S2 全套验收（S1 回归 + S2 战斗）；桥会跨帧驱动协程直到结束。
// 惯例：报告落 Tools/reports/，S1 与 S2 各一份。
return InkWash.Utils.PlaytestHarness.RunAll();
