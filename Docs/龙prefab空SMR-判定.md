# 龙的"空 SMR"判定：三个观测，两个原因

> 现象（目标机器报，探针 `Tools/cs/chk_enemy_vis.cs`）：
> `Z_Enemy_MoLong.prefab` 在**编辑模式**读出 `SMR=1、有网格=0、材质=null`；
> 同一个 prefab 在 **Play** 里实测 `mesh=dragon / bones=180 / 渲染像素 99.9%`。

## 结论：不是资产缺陷，但也不是单一原因

| 观测 | 真正的原因 | 属哪一类 |
|---|---|---|
| **SMR=1**（文件里明明有 **2** 个 SMR 块） | 龙的直接源 `Z_Dragon.prefab` 是刚重建的新版，**导入产物没跟上** ⇒ 新 fileID 在旧产物里查不到 ⇒ 该 SMR 那一支**整支没能展开** | **导入陈旧** |
| **有网格=0 / 材质=null** | 剩下那个 SMR 的源是 `Enemy_MoYan.prefab` 里的**占位**（两层链），`LoadAssetAtPath` 不递归解析 ⇒ `sharedMesh = null` | **读取方式**（跨文件只解析一层） |
| **Play 正常** | 运行时实例化会**完整递归展开**，两个 SMR 都实例化、mesh 都解析到 | — |

- "**variant prefab 不 materialize 继承子树**"这个说法**不成立** —— 它不是 variant，是两个普通 `PrefabInstance`。
- 但"**编辑模式读数不可信**"这个判断**是对的**，只是原因有两个，缺一不可。
- **资产本身完全正常**（下面第一节第 2 条）。

---

## 一、铁证（全部在源机器复现，不依赖 Unity）

### 1. 这个 prefab 里**根本没有自带数据的 SMR**

`Z_Enemy_MoLong.prefab` 只有 **16 992 字节 / 11 个块**：

```
GameObject(1)  Transform(4)  MonoBehaviour(114)  SMR(137)  PrefabInstance(1001)
   2 个           4 个           1 个               2 个        2 个
```

两个 SMR **都是 `stripped` 块** —— 块头带 `stripped` 标记，正文只有
`m_CorrespondingSourceObject` + `m_PrefabInstance`，
**`m_Mesh` / `m_Materials` / `m_Bones` / `m_RootBone` / `m_IsActive` / `m_Enabled` 字段一概不存在**。

> ⇒ 单看这个文件，"读出来是空的"是必然结果；**它的值全在源里**。

### 2. 空 SMR 的源可以精确追出，而且源是完整的

| MoLong 里的 stripped 块 | 源（`m_CorrespondingSourceObject`） | 源的形态 |
|---|---|---|
| `SMR #7880465578712287935` | `Z_Dragon.prefab` #`6533441441231540133` | **inline 完整 SMR**：`m_Mesh → chinese_dragon.fbx`、182 个字段（180 根 bones）、2 个材质（含 `M_Ink_Boss_Dragon.mat`） |
| `SMR #6920316179837779111` | `Enemy_MoYan.prefab` #`6374850387060490837` | **也是 stripped 占位** → 再往上 `KayKit/Skeletons/Characters/Skeleton_Golem.fbx` |

两个 fileID **完全对得上**（`Z_Dragon.prefab` 里那个 SMR 的 ID 就是 `6533441441231540133`）。

> ⇒ **一条链是「占位 → 实体」（一层），另一条是「占位 → 占位 → 实体」（两层）。**
> 这个差别就是"为什么只有龙读不到"的一半答案。

### 3. "stripped 读不到"这个假设**被否掉了**

7 个敌人 prefab 的结构对照：

| prefab | GameObject | SMR | 结构 | SMR 占位的源 | 链长 |
|---|---|---|---|---|---|
| `Z_Enemy_MoShan` | 43 | 14 | **全 inline** | —（自带 mesh） | 0 |
| `Z_Enemy_MoGuai` | 111 | 10 | **全 inline** | — | 0 |
| `Z_Enemy_MoGu` | 91 | 3 | **全 inline** | — | 0 |
| `Enemy_MoYan` | 1 | 1 | stripped | `KayKit/Skeleton_Golem.fbx`（**实体**） | 1 |
| `Enemy_MoTu` | 1 | 1 | stripped | `KayKit/Skeleton_Minion.fbx`（**实体**） | 1 |
| `Enemy_MoOu` | 2 | 1 | stripped | `KayKit/Skeleton_Mage.fbx`（**实体**） | 1 |
| **`Z_Enemy_MoLong`** | **2** | **2** | 全 stripped | `Z_Dragon.prefab`（实体）/ `Enemy_MoYan.prefab`（**占位**） | **1 / 2** |

`Enemy_MoYan / MoTu / MoOu` **也是 stripped 结构、那边读得到**
⇒ **stripped 本身不是障碍；"源是不是另一层占位"才是。**

### 4. 本机实测：**Library 落后资产 13.7 小时**

```
Z_Dragon.prefab / Z_Enemy_MoLong.prefab / EnemyDragon.cs   09-17 14:44:48
Library/ArtifactDB                                          09-17 01:01:58
Library/SourceAssetDB                                       09-17 01:02:36
```

本机是"工作区已换成新版（180 骨重建）、Unity 还没开过"的干净复现态。
`Tools/check_import_freshness.py` 的输出更精确：**1285 个资产里 8 个晚于 Library**，
正好是上轮拉下来那批（`Main.unity`、`WaveSpawner.cs`、`PlaytestHarness.cs`、
`EnemyDragon.cs`、**`Z_Dragon.prefab`**、`Z_Enemy_MoLong.prefab`、`Player.prefab`、
`PlayerHitFeedback.cs`）；而 **`Enemy_MoYan.prefab` 的 mtime 是 09-16 11:22（早于 Library，已导入）**。

---

## 二、`SMR=1` 是怎么来的（关键推理）

目标机器用的探针 `Tools/cs/chk_enemy_vis.cs`：

```csharp
var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
var smrs = p.GetComponentsInChildren<SkinnedMeshRenderer>(true);   // 带 true，含 inactive
foreach (var s in smrs) if (s.sharedMesh != null) withMesh++;
// 打印：SMR=smrs.Length   有网格=withMesh   材质=smrs[0].sharedMaterial
```

**这个读法本身没错**（带 `true`，判据是"至少 1 个 SMR 有网格"）。但它在 MoLong 上拿到 `SMR=1` ——
**文件里明明有 2 个 SMR 块，少了一个**。推理：

1. 少掉的那一个**没能展开** ⇒ 直接原因只能是**它的源解析失败**。
2. 两个占位的源：一个是 `Z_Dragon.prefab` 的**实体**（有 mesh），一个是 `Enemy_MoYan.prefab` 的**占位**。
3. 观测是 `有网格=0` ⇒ **出现的那 1 个 SMR，mesh 是 null** ⇒
   它不可能是龙那支（龙支的源是实体、有 mesh） ⇒ **出现的是山怪支，消失的是龙支**。
4. 龙支的源 = `Z_Dragon.prefab#6533441441231540133`。这个 fileID 是**新版重建时新生成的**；
   若 Library 里那份 `Z_Dragon.prefab` 还是旧的，这个 ID 就**不存在** ⇒ 解析失败 ⇒ 整支不展开。 ✅

> **⇒ `SMR=1` 是"源的导入产物陈旧"的直接指纹。**
> 如果只是读取方式的问题，`SMR` 应该仍是 2（两个占位都在），只是 mesh 全为 null。

**那为什么出现的那一个（山怪支）mesh 也是 null？**

它的源 `Enemy_MoYan.prefab#6374850387060490837` **本身又是占位**（数据还要再往上一层到 KayKit fbx）。
`AssetDatabase.LoadAssetAtPath` 展开嵌套实例时**不递归第二层** ⇒ 拿到的是占位 ⇒ `sharedMesh = null`。

对比 `Enemy_MoYan / MoTu / MoOu` **自己**读得到，正因为它们的占位**直接指向 FBX 实体**（一层就够）。

> **⇒ `有网格=0` 是"跨文件引用只解析一层"的指纹。**

**Play 里为什么正常**：运行时实例化是**完整递归展开**的，两层都展开、两个 SMR 都有 mesh。
这也解释了为什么"6 个敌人 OK、龙 ❌"—— 前者的链长是 1，后者是 2。

---

## 三、判据与验证

### 一锤定音（目标机器执行）

在 Unity 里 **`Assets → Refresh`（Ctrl+R）** 或重开工程，等导入完成，**用同一个探针再读一次**：

| 若看到 | 说明 |
|---|---|
| **`SMR` 从 1 变回 2** | **导入陈旧实锤**（龙的 SMR 回来了） |
| `有网格` 变 1（龙支有 mesh、山怪支仍 null） | 正常 —— 剩下的 null 是"只解析一层"的语义，不是缺陷 |
| **`SMR` 仍为 1** 或仍全 null | 才需要查读取方式（用下面的 C / D） |

### 让验收工具不再低报（建议改 `chk_enemy_vis.cs`）

`LoadAssetAtPath` 对**拼装式（跨文件两层以上）**的 prefab **天然会低报** ——
这个探针以后每次跑都会给"拼装式"敌人报假阴性。两种改法任选：

```csharp
// 改法 1（推荐）：强制完整展开后再判
var contents = PrefabUtility.LoadPrefabContents(path);
var smrs = contents.GetComponentsInChildren<SkinnedMeshRenderer>(true);
// ... 判完
PrefabUtility.UnloadPrefabContents(contents);

// 改法 2：本体为空时，改用「源」判
var src = PrefabUtility.GetCorrespondingObjectFromSource(s);
if (s.sharedMesh == null && src is SkinnedMeshRenderer s2 && s2.sharedMesh != null) withMesh++;
```

四种读法的对照探针已备好：`Tools/cs/molong_smr_probe.cs`（编辑模式跑，**四种读法 + 导入新鲜度自检**）：

| 读法 | API |
|---|---|
| A / A2 | `AssetDatabase.LoadAssetAtPath` + `GetComponentsInChildren<T>(true / 不带)` |
| B | `GetComponentInChildren<T>(true)` |
| C | `PrefabUtility.LoadPrefabContents` → 遍历 → `UnloadPrefabContents`（**强制展开**） |
| D | `PrefabUtility.InstantiatePrefab` → 遍历 → `DestroyImmediate`（实例化，会报 isDirty） |

每列都打印 `localMesh` / `mats` / `bones` **以及 `源SMR:`（`GetCorrespondingObjectFromSource` 的结果）**。
**"源SMR"列解析不出来 = 导入或链路问题；本体为空而源有值 = 读取方式问题。**

```bash
python Tools/exec_cs.py cs/molong_smr_probe.cs     # 编辑模式，不加 --runtime
python Tools/exec_cs.py cs/chk_enemy_vis.cs        # 同一次会话里对照原探针
```

---

## 四、顺带查明：龙是**拼装 prefab**（"地面怪心智"的物证）

```
Z_Enemy_MoLong.prefab
 ├─ 根（inline，挂 EnemyDragon.cs）
 ├─ Muzzle（inline GameObject）
 ├─ PrefabInstance → Z_Dragon.prefab        ← 龙本体（180 骨 + 水墨材质）
 └─ PrefabInstance → Enemy_MoYan.prefab     ← ★ 山怪那整套
```

第二个实例带来的东西（从它的 modification 列表可见）：
`Hitbox.cs`、`EnemyElite.cs`、`InkMaterialSwap.cs`、`Animator`、`MeshFilter`，
以及 **CapsuleCollider 参数被逐个改过**（`m_Height` / `m_Radius` / `m_Center.y` / `m_Direction`）
和战斗参数（`damage` / `radius` / `hitStop` / `pointA` / `pointB` / `knockback`）。

> 龙不是独立写的，是把 `Enemy_MoYan` 整个拖过来改造的 ——
> 与既有结论（`EnemyBase.CanSeePlayer()` 的视锥是地面怪口径、Boss 必须覆写
> `TickIdle` / `TickChase`）互为印证。

材质覆盖分两处：龙支盖 `litMaterials` / `m_Materials`（target = `InkMaterialSwap.cs` 与那个 SMR），
山怪支盖 `inkMaterials`（target = 另一个 `InkMaterialSwap.cs`）
⇒ **prefab 里材质为空可能是设计如此**，运行期由 `InkMaterialSwap` 填水墨材质 —— 待探针确认。

---

## 五、工具

| 工具 | 用途 |
|---|---|
| `Tools/check_import_freshness.py` | 资产 mtime vs `Library/ArtifactDB` ⇒ 哪些资产还没被 Unity 吃进去 |
| `Tools/enemy_prefab_audit.py` | 判 SMR 是 **inline 自带数据**还是 **stripped 占位**，并把占位链一路追到实体 |
| `Tools/cs/molong_smr_probe.cs` | 四种读法 + 导入新鲜度，编辑模式一次跑完 |
