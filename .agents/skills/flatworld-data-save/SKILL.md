---
name: flatworld-data-save
description: "Use when: 定位或修改 FlatWorld 的数据模型、MemoryPack 存档、自动保存、区块差量、玩家数据、星球数据、Addressables 或 JSON 配置。关键词：SaveDataMgr、GameSaveData、ItemData、ModuleData、MapSave、PlanetData。"
---

# FlatWorld 数据与存档

## 入口

- 磁盘与快照：`Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.cs`
- 根数据：`World/Map/Data/GameSaveData.cs` 及 `GameSaveData.*.cs`
- Item/Module：`Assets/5_Scripts/5-1_Data/{ItemData/ItemData,ModData/ModuleData}.cs`
- 地图/星球：`World/Map/Data/{MapSave,PlanetData,EcologyWorldSaveData}.cs`
- JSON：`Assets/StreamingAssets/GameConfig/`；Addressables：`Assets/AddressableAssetsData/`

## 核心不变量

- MemoryPack 字段只追加；派生类型同步 Union/版本。明确不兼容时拒绝读取，绝不静默覆盖用户文件。
- `SerializableTimeData` 的时间 Profile、限时边界和月相字段只能追加；旧存档缺失这些字段时由 `TimeData.EnsureTimeSystemDefaults()` 补齐默认值。
- 正式存档只写 `Application.persistentDataPath/Saves/LocalSaveData/`，并使用临时文件/原子替换；失败不得伪装为成功恢复。
- `ItemSpecialDataJsonStore` 按命名空间更新并保留未知根属性；教程、任务、维度、出生点不得互相覆盖或改 `Data_Player` 布局。
- Item/Recipe JSON 是唯一内容真源；Manifest 不自动扫描目录。移动资源还要核对 Address、标签和运行时字典键。
- Actor JSON 位于 `GameConfig/Actors`，使用独立 Manifest；外壳/Sprite/Animator 的 `flatworld.actor.*` 地址由 GUID 跟随移动，`sourcePrefab` 仅供编辑器。
- Sprite 地址只有在源图是 Multiple 切片时才追加 `[子资源名]`；单 Sprite 图必须使用主资源地址。Unity 2022.3 + Addressables 1.22.3 Fast Mode 遇到无效子资源地址可能在 `AssetDatabaseProvider.LoadAssetSubObject` 抛空引用，编辑器加载路径需先做资源存在性和子资源名称校验。
- Tile 栈只通过 `Data_TileMap` API 读写；区块差量保留基线、ChangedItems、删除 GUID 与确定性 ID 语义。
- 新版 WorldModel 的格子建筑写入 `ChunkTerrainData.BlockingTileId`，不会进入 `MapSave.items`；必须按“确定性生成基线 → RuntimeTileDeltas 差量 → 表现绑定”的顺序持久化和恢复。
- 新版 WorldModel 的动态建筑 Item 不属于旧 `Chunk.RunTimeItems`；建筑存档要复用区块 `ChangedItems` 差量，按 `Mod_Building` 角色清理旧记录并在区块数据就绪后实例化恢复。
- 自动/手动保存可分帧采集，但后台只处理不可变快照；旧任务不得覆盖更新的手动/退出保存。
- 地表 `WorldKey=PlanetId`；非地表用 `PlanetId__dimension__DimensionId`。`TopologyMode` 维持兼容默认 `Infinite=0`，新增世界字段追加在后。
- 任务 `flatworld.quests` 等未来版本必须拒绝写回；未知 MOD 记录应保留。
- 玩家创建 JSON 位于 `StreamingAssets/GameConfig/Players`，不进入 MemoryPack 存档；只在无存档创建阶段注入，并在模块加载前同步到 `Data_Player.ModuleDataDic`；已有玩家存档始终优先于模板。

## 工作流与验证

1. 先确定权威数据、持久化位置、版本与迁移策略，再修改模型。
2. 用临时路径做序列化往返，验证旧/新数据和失败路径；不得触碰真实玩家存档。
3. 联动：生命周期→Core，Item/Module→Item，Chunk 差量→Map，协议快照→Networking，内容 Def→对应领域 Skill。
4. 默认做静态诊断、编译和 Console；需要时运行 `DataSave.Smoke`。测试入口：`Assets/GameTest/DataSave/DataSaveSmokeTests.cs`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
