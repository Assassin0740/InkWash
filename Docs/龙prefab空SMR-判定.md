# 【已作废】龙 prefab「空 SMR」判定

> ⚠️ **本文的结论被 `Docs/龙prefab重建回退-判定.md` 推翻，仅留作方法论留痕，不要据此行动。**
>
> 当时（同日早些时候）的两条归因：
> - 「`SMR=1` = 导入陈旧」（并预测 `Ctrl+R` 后应回到 2）
> - 「`有网格=0` = `LoadAssetAtPath` 跨文件只解析一层」
>
> **实际复核结果（`Ctrl+R` 之后、资产确认已导入）：四种读法（`LoadAssetAtPath` / 单数 / `LoadPrefabContents` / `InstantiatePrefab`）全部得到 `SMR=1`、`mesh=null`。**
> 导入陈旧那条**被证伪**（而且是反方向：刷新改变不了任何东西）。
>
> 真因见新文档：`d31bc8e` 那次 WIP 重建把 SMR 的 `m_Mesh` 写成了**全工程不存在的 fileID** ⇒ 网格引用悬空；并顺带把 2 个 SMR 合并成 1 个、在 MoLong 上加了"材质缩成 1 个 null"的覆盖。已 `git checkout b540f26 --` 回退两个 prefab。

## 唯一还成立的方法论（这部分值得保留）

1. **「读到 1 个而文件里有 2 个」这类数量对不上，比「读到 null」信息量大** —— null 可能是读取语义，**数量变化只能用解析失败解释**。
2. **纯声明/读取类的归因，必须配一条可证伪预测**（当时的预测是「`Ctrl+R` 后 `SMR` 从 1 → 2」）。预测被证伪时，**要敢把整条归因一起丢掉**，而不是补一句"可能还有别的原因"。
3. **`m_Mesh` 这类引用只要 fileID 不存在就静默变 null**；判据是 `AssetDatabase.TryGetGUIDAndLocalFileIdentifier` **必须命中**（同一 fbx 的材质 id 被精确匹配上，即证明这把尺子可信）。
4. **「有网格」≠「看得见」**：`sharedMesh != null` 只证明引用有效，真判据要实渲一次量非背景像素。
