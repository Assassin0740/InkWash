# 龙的"空 SMR"判定：是导入陈旧，不是读取方式的锅

> 现象（目标机器报）：`Z_Enemy_MoLong.prefab` 在**编辑模式**读出来 `SMR=1、无网格、材质 null`；
> 同一个 prefab 在 **Play** 里实测 `mesh=dragon / bones=180 / 渲染像素 99.9%`。
> 本轮结论：**不是资产缺陷**；"假阴性"这个定性方向对，但**机制不是"variant 不 materialize 继承子树"**。

---

## 一、铁证四条（全部在源机器复现，不依赖 Unity）

### 1. 这个 prefab 里**根本没有自带数据的 SMR**

`Z_Enemy_MoLong.prefab` 只有 **16 992 字节 / 11 个块**，块类型分布：

```
GameObject(1)  Transform(4)  MonoBehaviour(114)  SMR(137)  PrefabInstance(1001)
   2 个           4 个           1 个               2 个        2 个
```

它的两个 SMR **都是 `stripped` 块**——块头带 `stripped` 标记，正文只有
`m_CorrespondingSourceObject` + `m_PrefabInstance` 两行，
**`m_Mesh` / `m_Materials` / `m_Bones` / `m_RootBone` / `m_IsActive` / `m_Enabled` 字段一概不存在**。

> ⇒ **单看这个文件，"读出来是空的"是必然结果**，不是 Unity 的 bug，也不是读法写错了。

### 2. 空 SMR 的源可以精确追出，而且源是完整的

| MoLong 里的 stripped 块 | 源（`m_CorrespondingSourceObject`） | 源的形态 |
|---|---|---|
| `SMR #7880465578712287935` | `Z_Dragon.prefab` #`6533441441231540133` | **inline 完整 SMR**：`m_Mesh → chinese_dragon.fbx`、182 个字段（180 根 bones）、2 个材质（含 `M_Ink_Boss_Dragon.mat`） |
| `SMR #6920316179837779111` | `Enemy_MoYan.prefab` #`6374850387060490837` | stripped → 再往上 `KayKit/Skeletons/Characters/Skeleton_Golem.fbx` |

两个 fileID **完全对得上**（`Z_Dragon.prefab` 里那个 SMR 的 ID 就是 `6533441441231540133`）。
**资产本身没问题。**

### 3. "stripped 读不到"这个假设**被否掉了**

7 个敌人 prefab 的结构对照：

| prefab | GameObject | SMR | 结构 | SMR 的 mesh 来源 |
|---|---|---|---|---|
| `Z_Enemy_MoShan` | 43 | 14 | **全 inline** | `mountain_orge.fbx` |
| `Z_Enemy_MoGuai` | 111 | 10 | **全 inline** | `low-poly_orc.fbx` |
| `Z_Enemy_MoGu` | 91 | 3 | **全 inline** | `cursed_undead_soldier_rig.fbx` |
| `Enemy_MoYan` | 1 | 1 | **stripped** | `KayKit/Skeleton_Golem.fbx` |
| `Enemy_MoTu` | 1 | 1 | **stripped** | `KayKit/Skeleton_Minion.fbx` |
| `Enemy_MoOu` | 2 | 1 | **stripped** | `KayKit/Skeleton_Mage.fbx` |
| **`Z_Enemy_MoLong`** | **2** | **2** | **全 stripped** | **`Z_Dragon.prefab`**（+ `Enemy_MoYan`） |

`Enemy_MoYan / MoTu / MoOu` **也是 stripped 结构、那边读得到**
⇒ stripped 本身不是障碍。

**龙唯一的两个差别**：
- 它的 SMR 源是**另一个 `.prefab`**（`Z_Dragon.prefab`），其他敌人的源是 **`.fbx`**；
- 它是唯一的**两层嵌套**（MoLong → Enemy_MoYan → KayKit）。

### 4. 本机实测：**Library 落后资产 13.7 小时**

```
Z_Dragon.prefab / Z_Enemy_MoLong.prefab / EnemyDragon.cs   09-17 14:44:48
Library/ArtifactDB                                          09-17 01:01:58
Library/SourceAssetDB                                       09-17 01:02:36
```

本机是"工作区已换成新版（180 骨重建）、Unity 还没开过"的干净复现态 ⇒ **导入产物确实跟不上资产**。

---

## 二、为什么"编辑模式空、Play 正常"

三条事实合起来只剩一种解释：

1. MoLong 里的 SMR 是**占位**，数据必须靠 Unity 解析到 `Z_Dragon.prefab`；
2. `Z_Dragon.prefab` **是刚重建的新版**（骨骼从零填充名换成原生 FBX 名，fileID 整批变了）；
3. 若 Library 里那份 `Z_Dragon.prefab` **还是旧的**，新 fileID `6533441441231540133` 就查不到
   ⇒ 占位解析不出源 ⇒ **`sharedMesh = null`、`sharedMaterials = null`** ✅ 与观测一致。

而 Play 里正常，说明**进 Play 之前导入已经完成**了（Play 模式本身会禁用资产自动导入，
所以读数与导入的先后顺序决定了看到哪一版）。

> **⇒ 最可能的根因：读取发生在导入完成之前，或导入被跳过。不是读取 API 的语义问题。**

---

## 三、一锤定音的判据

在目标机器的 Unity 里执行 **`Assets → Refresh`（Ctrl+R）** 或**重开工程**，然后用**同一种读法**再读一次 `Z_Enemy_MoLong.prefab`：

- **变正常（mesh=dragon / bones=180）** ⇒ **导入陈旧实锤**，结案；
- **仍然为空** ⇒ 才是读取方式问题，改用下面的 C / D 两种读法（探针已就位）。

已备好探针 `Tools/cs/molong_smr_probe.cs`（编辑模式跑，**四种读法对照 + 导入新鲜度自检**）：

| 读法 | API | 说明 |
|---|---|---|
| A / A2 | `AssetDatabase.LoadAssetAtPath` + `GetComponentsInChildren<T>(true/不带)` | 资产对象本身 |
| B | `GetComponentInChildren<T>(true)` | 单数版 |
| C | `PrefabUtility.LoadPrefabContents` → 遍历 → `UnloadPrefabContents` | **强制展开** |
| D | `PrefabUtility.InstantiatePrefab` → 遍历 → `DestroyImmediate` | 实例化（清理干净，会报 isDirty） |

每列都打印 `localMesh` / `mats` / `bones` **以及 `源SMR:`（`GetCorrespondingObjectFromSource` 的解析结果）**。
最后这一列是判据核心：**它若解析不出来，而磁盘上 `Z_Dragon.prefab#6533441441231540133` 数据完整
⇒ 就是导入陈旧**。

```bash
python Tools/exec_cs.py cs/molong_smr_probe.cs     # 编辑模式，不加 --runtime
```

---

## 四、顺带查明的一条工程事实：龙是**拼装 prefab**

`Z_Enemy_MoLong.prefab` 由 **两个 PrefabInstance** 拼成：

```
根（inline，挂 EnemyDragon.cs）
 ├─ Muzzle（inline GameObject）
 ├─ PrefabInstance → Z_Dragon.prefab          ← 龙本体（180 骨 + 水墨材质）
 └─ PrefabInstance → Enemy_MoYan.prefab       ← ★ 山怪那整套
```

第二个实例带来的东西（从它的 modification 列表可见）：
`Hitbox.cs`、`EnemyElite.cs`、`InkMaterialSwap.cs`、`Animator`、`MeshFilter`、
以及 **CapsuleCollider 参数被逐个改过**（`m_Height` / `m_Radius` / `m_Center.y` / `m_Direction`）
和战斗参数（`damage` / `radius` / `hitStop` / `pointA` / `pointB` / `knockback`）。

> 这就是「**Boss 沿用地面怪心智**」的物证：龙不是独立写的，是把 `Enemy_MoYan` 整个拖过来改造的。
> 与既有结论（`EnemyBase.CanSeePlayer()` 的视锥是地面怪口径、Boss 必须覆写 `TickIdle`/`TickChase`）互为印证。

材质覆盖也分两处：龙支盖 `litMaterials` / `m_Materials`（target = `InkMaterialSwap.cs` 与那个 SMR），
山怪支盖 `inkMaterials`（target = 另一个 `InkMaterialSwap.cs`）
⇒ **prefab 里材质为空可能是设计如此**，运行期由 `InkMaterialSwap` 填水墨材质。
这一条要等探针结果才能定，先记为待验证。
