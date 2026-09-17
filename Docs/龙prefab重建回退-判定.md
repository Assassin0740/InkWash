# 龙 prefab「空 SMR」判定 → 回退一次 WIP 重建

> 2026-09-17。起因：目标机器报告 `Z_Enemy_MoLong.prefab` 在编辑模式读出 `SMR=1 / 无网格 / 材质 null`，而"Play 里 mesh=dragon / bones=180 / 像素 99.9%"。
> 结论：**不是导入陈旧，不是读取假阴性，也不是合并丢改动 —— 是一次没做完的重建把网格引用写坏了。已回退。**

## 一、结论（一句话）

`d31bc8e`（`wip(dragon): 用原生 FBX(180骨)重建 Z_Dragon 视觉子树`）给 `Z_Dragon.prefab` **自造了一套 180 骨骨架**（骨名 `drgon_1..52` + `drgon_dup0..147`），同时把 SMR 的 `m_Mesh` 写成了一个**全工程不存在的 fileID**，并把原来的 2 个 SMR 合并成 1 个。结果：**龙的网格引用悬空 ⇒ 无论编辑模式还是 Play 都渲染 0 像素**。

## 二、证据链（全部可复现）

### 1. 合并完整性：一个都没丢
- `HEAD == origin/main`，0 ahead / 0 behind，工作区干净
- 三个文件最后一次改动都在 HEAD 祖先链上，且**之后无人再动**（`git log <commit>..HEAD -- <file>` 为空、`git diff <commit> -- <file>` 为空）
- 9 个标记逐个在位，数值也一致：`_baseRel`×17 + `CaptureBaseRel`×5 / `enablePerformanceCycle`×6 / `bodyBobAmp`×3 / `restLift`×2 / `BeginRoar`×2 / `limbSwingDeg = 14f` / `headLeadGain = 0.03f` / `Quaternion.AngleAxis(yaw, Vector3.up)`
- `Z_Enemy_MoLong.prefab` 把 C# 默认值全序列化进去了（`limbSwingDeg: 14` / `headLeadGain: 0.03` / `enablePerformanceCycle: 1` / `restLift: 0.35` / `bodyBobAmp: 1.1` / `straightenSpine: 1`）—— 恰好避开了"只改 C# 默认值不生效"那个坑

### 2. 网格引用悬空（`Tools/reports/dg_prefab_forensic.txt`、`dg_mesh_hunt.txt`）
`Z_Dragon.prefab` 的 SMR 是 **inline**（字段在文件里，不是占位）：

```
m_Mesh: {fileID: -3325053396650211315, guid: c68a78a74a299104a8f51cee5029c146, type: 3}
```

- 该 guid 反查 = `Assets/ThirdParty/Ziyuan/_fbx/chinese_dragon.fbx`
- **同一个 guid 的材质**（`-1725437989455171953` = `MI_b09_00_drg_eye`）**解析成功** ⇒ 不是"整个文件没导入"，guid/type/方法都对
- 而全工程 **284 个 Model / 569 个 Mesh 里没有任何一个** localId 等于 `-3325053396650211315`
- 该 fbx 现存 2 个 Mesh：`Object_281`(7161509005421682173, 28082v) / `Object_282`(7238157132297919992, 804v)

★ 方法自证：材质 id 被精确匹配上，说明 `TryGetGUIDAndLocalFileIdentifier` 这条尺子是可信的。

### 3. Play 复核：真的渲染不出来（`Tools/reports/dg_play_verify.txt`）
| | 重建后 | 回退后 |
|---|---|---|
| MoLong SMR | 1（无网格） | **2（都有网格）** |
| `bones` | 180 | **273** |
| `renderer.bounds` | 4.737×3.219×6.307 | **5.871×2.588×6.044** |
| 非背景像素（Play 实渲） | **0.00% / 0.00%** | **9.43% / 9.14%** |
| `_spine` | 24 节（`drgon_dup2…`） | **24 节（`drgon_03→…→drgon_00`）** |
| `_modelRoot` / `_baseRel` | `Visual` / 24 | `Visual` / 24 |

回退后的 `5.871×2.588×6.044` 与 `drgon_03→…→drgon_00` **正是搬迁前记录的龙真值**，两条独立指标同时对上。

### 4. 三次提交对照 —— 重建前完全正常
| 提交 | SMR 块数 | `m_Mesh` | 骨名 |
|---|---|---|---|
| `68ca3be` 导入素材 | **2** | `…82173` / `…19992` | `drgon_0xx` |
| `622e8a2` 新怪接入 | **2** | 同上（**至今仍有效**） | `drgon_0xx` |
| `b540f26` 调参写入 prefab | **2** | 同上 | `drgon_0xx` |
| `d31bc8e` **重建** | **1** | `-3325053396650211315` ← **不存在** | `drgon_dup*` |

### 5. `drgon_dup*` 是自造的
全工程（含 5 个 fbx 的二进制字节、glTF 原文）**只有 `Z_Dragon.prefab` 一个文件含 `drgon_dup`**。原始 `chinese_dragon.fbx` 的骨名是 `drgon_0xx`（补零，286 处），glTF 也没有 dup。⇒ 这套骨名是重建那一步造出来的（本意应是**造一条命名规整的 24 节脊柱**，让 `ResolveSpine` 必然走对，解掉 `dg_chain.cs` 里"采到的未必是脊柱"的隐患 —— 这个意图本身是好的）。

### 6. 只修 mesh 引用不够
把 mesh 指到 `Object_281` 后 Play 出图：**几何塌缩成一小团**（俯视图中只有约 0.7 m 的一坨，而包围盒报 6 m）。原因：**重建后骨架 180 骨，而网格 `bindposes = 273`**，数量不等 ⇒ 蒙皮错位。

### 7. MoLong 侧另有两处重建留下的痕迹
- 显式加了覆盖 `m_Materials.Array.size = 1` 且 `data[0] = null` ⇒ Play 里读到 `mats=1[null]`（**不是"只解析一层"**，是重建加的覆盖）
- 新增了一条指向**旧 fileID**（`2216177751835293821`）的覆盖，而重建后的 `Z_Dragon.prefab` 里已无此 id ⇒ 悬空修改

## 三、回退方案与验证

```bash
git checkout b540f26 -- \
  Assets/_Project/Prefabs/Ziyuan/Z_Dragon.prefab \
  Assets/_Project/Prefabs/Enemies/Z_Enemy_MoLong.prefab
```

选 `b540f26` 当目标而不是 `622e8a2`：`Z_Dragon.prefab` 在 622e8a2 之后只有 `d31bc8e` 动过（两者相同），而 `Z_Enemy_MoLong.prefab` 的全部实质改动来自更早的 `41f1c9a`（运动重做）/`a822ae3`（§3 容器语义轴）/`b540f26`（调参显式写入 prefab）—— 即 `b540f26` 就是**重建前最后一版**。

回退后：
- `Z_Dragon` / `MoLong` 均 `SMR=2 有mesh=2`；`bones=273`；`rootBone=_rootJoint`
- Play 实渲 9.43% / 9.14%（之前 0.00%）
- 出图肉眼确认：蛇形墨龙、黑色墨线外轮廓、灰墨躯干
- `SpineLinksFound=24`、`_modelRoot=Visual`、`_baseRel.Count=24` 全部完好
- **只有 `_health: 620` 没跟着回来**（那是重建那次加的，重建前是 0）⬜ 待用户定

备份：`Tools/backup/{Z_Dragon,Z_Enemy_MoLong}.prefab.bak_20260917` + `Z_Dragon.prefab.meta.bak_20260917`

## 四、附带发现

1. **`chk_enemy_vis.cs` 的判据有盲区**：它只查 `sharedMesh != null`，而"有网格"≠"看得见"。真判据要**实渲一次量非背景像素**。
2. **该判据会误报**：本轮实渲发现 **`Z_Enemy_MoShan`（14/14 网格）与 `Z_Enemy_MoGuai`（10/10 网格）渲染 0 像素**，而 `chk_enemy_vis` 判它们 ✅。已排除：取景、材质、被禁用、视锥剔除字段（与能正常渲染的 `MoGu` 同值）。**根因未定**，⬜ 待查。
3. 三个 Ziyuan 敌人的 SMR 都是 `m_RootBone: 0` + `m_Bones` 全 0（MoShan 26 / MoGuai 97 / MoGu 80 条全零）⇒ 靠 bindpose 渲染。
4. **度量自身的三个人为陷阱**（本轮全踩过一遍）：
   - `Camera` 组件 `enabled=false` 时 `Camera.Render()` **只清屏不画模型**（同一段代码给出 0.00% 的假阴性，而 `dg_look` 出图正常）
   - **一刀切禁用 MonoBehaviour** 会让 `InkMaterialSwap` 不跑 ⇒ 运行时要靠它贴的材质全是 null ⇒ 假"不可见"
   - 取景直接 union 所有 `Renderer.bounds` 会被**离群包围盒**拖偏（`MoGuai` 的 mesh.bounds 是 1269×1964×566 m）⇒ 相机被拉到极远，画面全空
5. 回退时 `AssetDatabase.ImportAsset(..., ForceUpdate)` 会告警 `Build asset version error ... modification time`（SourceAssetDB 与磁盘 mtime 不一致），但导入结果正确，非致命。

## 五、工具

| 工具 | 用途 |
|---|---|
| `Tools/cs/dg_prefab_forensic.cs` | 一次问清 fbx 子资产 / SMR 原始字段 / 源链逐跳 / MoLong 层级 / 场景实例 / NavMesh |
| `Tools/cs/dg_mesh_hunt.cs` | 全工程扫 Mesh 的 localId 与名字（定位悬空引用属于哪个文件） |
| `Tools/cs/dg_play_verify.cs` | Play 里实例化龙，量非背景像素 + 出 PNG + 读 `ResolveSpine` 运行真值 |
| `Tools/cs/dg_fix_mesh.cs` | 就地改 SMR 的 mesh/材质（场景实例 + `ApplyPrefabInstance`，最小 diff） |
| `Tools/cs/dg_enemy_shots.cs` | 7 个敌人的可见性截图集（v2 已修三个人为陷阱） |
| `Tools/cs/dg_enemy_wide.cs` | 冻结时间 + 固定大范围取景，分辨"没画"与"跑偏" |
| `Tools/tmp/dump_smr.py` | 拆 prefab 文本，列每个 SMR 的归属物体 / `m_Mesh` / `m_RootBone` / 材质 / `m_Bones` 条数 |

报告：`Tools/reports/dg_*.txt`；图：`Tools/screenshots/enemies/*.png`、`Tools/screenshots/dragon/*.png`
