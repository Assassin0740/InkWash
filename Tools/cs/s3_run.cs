// 触发 S3 敌人验收（M3：有对手）——NavMesh 追击 / 波次生成 / 全清开门。
// 桥会跨帧驱动返回的协程直到结束；报告落 Tools/reports/S3_latest.txt。
// 注意：全程关掉 HitStop（时间缩放会让所有"等待 N 秒"的判据失真）。
return InkWash.Utils.PlaytestHarness.S3EnemyFlow();
