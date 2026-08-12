---
name: flatworld-item-module
description: "Use when: 定位或修改 FlatWorld 的 Item/Module 组合架构、实体创建销毁、对象池、模块加载保存、Tick 调度、运行时注册、空间索引或网络模块序列化。关键词：ItemMgr、Item、Module、ItemMods、ItemMaker。"
---

# FlatWorld Item / Module

## 入口

- 生命周期：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/{Item,Module,ItemMods,ItemMaker}.cs`
- 管理器：`Core/Manager/ItemMgr*.cs`（Spawning/Perception/Players/RandomDrop partial）
- 数据：`Assets/5_Scripts/5-1_Data/{ItemData/ItemData,ModData/ModuleData}.cs`
- 本体定义：`Assets/StreamingAssets/GameConfig/Items/item-manifest.json`
- Actor 定义复用 Item/Module 实例化与池化：`Entities/AI/Definitions/ActorDefinitionCatalogLoader.cs`

## 主链与不变量

`ItemMaker/ItemMgr → ItemData → ItemMods → ModuleInit/Load → ItemMgr 分级 Tick → Save/Despawn/Pool`

- Module 明确选择 EveryFrame、FixedInterval 或 Disabled；增删模块、配置变化和池复用必须使调度缓存失效。
- 注册/注销、保存/销毁各执行一次；`PrepareForDespawn` 与 `OnDestroy` 不得被外部重复调用。
- 远程网络副本不进入本地 Tick、感知和存档索引。
- 新模块同时检查脚本、ModuleData、模块/Item Prefab、Addressables 与 JSON 定义。
- Manifest 是唯一发现入口；包的最终 `shellPrefab` 必须与声明一致。
- `RuntimeItemDefinition.IsActor` 只表示复用通用管线；Actor 还必须登记到 `GameRes.ActorDefinitions` 且外壳包含 `IAIActor`。
- 堆叠身份统一由 `ItemData` 判定，空与 null 特殊数据按现有规范处理。

## 验证

- 检查加载→Tick→保存→Despawn→复用后无旧状态、订阅、空间索引或调度残留。
- 生命周期/ModuleData 联动 Data Skill；网络状态联动 Networking；具体玩法只加载其领域 Skill。
- 默认不主动跑测试；需要时运行 `ItemModule.Smoke`。入口：`Assets/GameTest/ItemModule/ItemModuleSmokeTests.cs`。

生命周期、调度、池化、注册、JSON/Prefab 或网络边界变化时更新本 Skill；近期变更最多 5 条。

## 近期变更

- 2026-08-12：RuntimeItemDefinition 增加 Animator/Actor 元数据，Actor 通过同一实例化、模块参数、对象池和存档链创建。
