# FlatWorld 种植作物运行态与 Mono 耦合问题记录

## 1. 问题定位

本文只记录当前种植作物运行态与 Unity `MonoBehaviour` 的耦合位置，不预设改造方案。

玩家种植作物的存档数据最终以 `ItemData` 写入 `ChunkSaveRecord.AgricultureCells`，但运行时作物的持有、恢复、快照采集和销毁清理都由 `ChunkAgricultureRenderer` 组件协调。农业表现组件因此同时承担地块视觉、作物实体生命周期和存档采集职责。

## 2. 当前关联链

- 耕地进度、水分和肥力保存在 `ChunkTerrainData` 的农业环境层。
- `ChunkAgricultureRenderer` 使用 `Dictionary<Vector2Int, Item>` 持有当前区块的种植作物实例。
- 组件绑定区块时，从 `SaveDataMgr.GetAgricultureCells` 读取作物快照，创建并加载 `Item`；作物作为组件对象层级下的 Unity 实体运行。
- 组件采集状态时调用每个 `Item.Save()`，再将 `Item` 传给 `SaveDataMgr.RecordCultivatedCrop`；存档管理器从 `Item.itemData` 克隆出 `AgricultureCellSaveData.Crop`。
- 作物销毁时，组件通过 `OnItemDestroy` 回调清除对应格子的作物快照。
- `Mod_Plantable` 通过目标 `ChunkView` 找到 `ChunkAgricultureRenderer`，并让它注册新作物、立即采集状态。`ChunkView` 的自动保存和解绑流程也会调用该组件的 `CaptureState`。

## 3. 问题所在

- 持久化字段是 `ItemData`，本身不是 Unity 对象；但运行时作物状态的采集入口要求先取得 `Item` Mono 实例，并由实例执行模块保存。
- `SaveDataMgr.RecordCultivatedCrop` 接收 `Item`，存档边界直接依赖游戏实体组件，而非只接收已采集的数据快照。
- `ChunkAgricultureRenderer` 将耕地渐显表现与作物实例集合、作物恢复、状态保存及销毁回调放在同一组件中。
- 作物实体的创建、回收和状态采集与 `ChunkView` 的表现绑定、解绑流程相连；目前没有独立于这些 Unity 对象的区块作物运行态容器。

## 4. 后续待明确

- 种植作物运行时状态的权威持有者与 `ChunkRuntime`、`ChunkView`、`ItemData` 之间的边界尚未明确。
- 作物实例离开表现绑定时的生命周期，以及其模块状态何时进入区块快照，尚未形成独立于表现组件的规则。
- `SaveDataMgr` 的农业接口目前仍以 Unity `Item` 作为作物快照输入，数据边界需要后续规划。
