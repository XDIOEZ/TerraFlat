# 存档与数据系统

## 系统定位

负责游戏运行态持久化、MemoryPack 存档、Item/Module 状态、玩家状态、PlanetData、Chunk 差量和内容 JSON 的边界。

## 当前原则

- 开发期存档只支持**当前格式版本**。
- MemoryPack 布局或关键差量结构变化时提升版本；旧版本直接抛 `SaveVersionIncompatibleException`，不做静默迁移或字段回退。
- 正式存档写入 `Application.persistentDataPath/Saves/LocalSaveData/`，使用临时文件/原子替换。
- JSON 是内容定义真源，MemoryPack 是运行时状态快照，两者不能互相替代。

## 数据层级

### 内容定义

`Assets/StreamingAssets/GameConfig/`

例如 Item、Recipe、Buff、Player 创建模板等。这些定义描述“新实例应该是什么”。

### Item / Module 状态

ItemData + ModuleData 保存实体运行时状态。跨系统的小型扩展状态优先使用 ItemSpecialData 的独立命名空间。

### 星球与区块状态

- PlanetData：时间、天气、星球级环境等。
- ChunkTerrainData / ChunkSaveRecord：Tile 差量、ChangedItems、农业、平台、污染等区块状态。

## WorldModel 差量

- Tile 建筑和建筑损伤进入 RuntimeTileDeltas。
- 动态建筑进入 ChangedItems。
- 农业进入 AgricultureCells。
- 水上支撑层进入 SupportCells。
- 污染使用稀疏 ContaminationCells。

## 保存生命周期

- `Save()` 只抓取持久化状态，不负责解绑事件或停止运行。
- 运行态资源释放统一在 `Unload()`。
- 自动保存可以分帧抓快照，但后台只能处理不可变快照；旧保存任务不能覆盖较新的手动/退出保存。

## 关键入口

- `Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.cs`
- `World/Map/Data/GameSaveData*.cs`
- `World/Map/Data/PlanetData.cs`
- `World/Map/Data/MapSave.cs`
- `Assets/5_Scripts/5-1_Data/ItemData/`
- `Assets/5_Scripts/5-1_Data/ModData/`

## 修改时联动

先确定“谁是权威状态”，再选择存储位置。不要因为一个字段需要保存就直接往 GameSaveData 根对象继续堆字段。

## 对应 Skill

`.agents/skills/flatworld-data-save/SKILL.md`
