// 演示（紧凑版）：侧向步行 → 侧向疾跑 → 向前疾跑 → 冲刺。
// 配合 exec_cs.py --record 录 MP4 用；总长约 11.4s，控制在桥的 15s 单次录制上限内。
// 侧向机位是为了把「上半身遮罩叠加」与「步频/位移匹配」同时拍进画面。
return InkWash.Utils.PlaytestHarness.DemoLocomotionCompact();
