---
name: flatworld-item-module
description: "Use when: 定位或修改 FlatWorld 的 Item/Module 组合架构、实体创建销毁、对象池、模块加载保存、Tick 调度、运行时注册、空间索引或网络模块序列化。关键词：ItemMgr、Item、Module、ItemMods、ItemMaker。"
---

# FlatWorld Item / Module

## 入口

- 生命周期：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/{Item,Module,ItemMods,ItemMaker}.cs`
- 管理器：`Entities/Item/Management/ItemMgr*.cs`（Spawning/Perception/Players/RandomDrop partial）
- 数据：`Assets/5_Scripts/5-1_Data/{ItemData/ItemData,ModData/ModuleData}.cs`
- 本体定义：`Assets/StreamingAssets/GameConfig/Items/item-manifest.json`
- 模板化物品编辑：`Assets/Editor/FlatWorld/ContentTools/ContentWorkshop/`
- Actor 定义复用 Item/Module 实例化与池化：`Entities/AI/Definitions/ActorDefinitionCatalogLoader.cs`

## 主链与不变量

`ItemMaker/ItemMgr → ItemData → ItemMods → ModuleInit/Load → ItemMgr 分级 Tick → Save/Despawn/Pool`

- Module 明确选择 EveryFrame、FixedInterval 或 Disabled；增删模块、配置变化和池复用必须使调度缓存失效。
- 注册/注销、保存/销毁各执行一次；`PrepareForDespawn` 与 `OnDestroy` 不得被外部重复调用。
- 远程网络副本不进入本地 Tick、感知和存档索引。
- 新模块同时检查脚本、ModuleData、模块/Item Prefab、Addressables 与 JSON 定义。
- Item 与 Actor 的 `modules.*.parameters` 共用 `ModuleJsonConfigurator` 严格契约；删除或改名可配置字段后必须同步现行 JSON，并运行“FlatWorld/内容配置/校验全部本体内容”，禁止等到具体实例生成时才发现漂移。
- 运行时生成模块参数时，`Vector2/Vector3` 必须显式写成 `x/y/z` 的 `JObject`；禁止 `JToken.FromObject(UnityEngine.Vector*)`，否则 Json.NET 会遍历 `normalized` 等计算属性并形成自引用。
- JSON 定义实体的战利品以 `LootPrefabName` 稳定 ID 为权威；Prefab 不再保存冗余 `LootPrefab` 对象引用，避免资源重建后残留旧 FileID 并触发 PPtr 类型转换错误。
- Manifest 是唯一发现入口；包的最终 `shellPrefab` 必须与声明一致。
- 内容工坊创建物品时只写继承差异：父定义和参考模块必须来自启用分包，Sprite 先生成稳定 Addressables 地址，JSON 写入前校验继承、重复 ID、文件指纹与分包外壳边界。
- `RuntimeItemDefinition.IsActor` 只表示复用通用管线；Actor 还必须登记到 `GameRes.ActorDefinitions` 且外壳包含 `IAIActor`。
- 堆叠身份统一由 `ItemData` 判定，空与 null 特殊数据按现有规范处理。
- 模块 Prefab 的 `ModuleData.Name/ID` 可能未序列化；进入 `ItemMods`、`ModuleInit` 或网络更新前必须统一建立非空身份，禁止直接把空值写入字典。
- `ItemPicker` 不能只依赖 `OnTriggerEnter2D`：掉落/飞行或联机预约可能让物品先以不可拾取状态进入范围，状态恢复后应补偿检查，并限制为一次性请求以避免部分入包或网络请求重复执行。
- 掉落拾取时序由 `Mod_Droping` 的轨迹状态决定：必须先移除掉落模块，再把 `CanBePickedUp` 设为 true；拾取器不能只信任这个数据标志。

## 验证

- 检查加载→Tick→保存→Despawn→复用后无旧状态、订阅、空间索引或调度残留。
- 生命周期/ModuleData 联动 Data Skill；网络状态联动 Networking；具体玩法只加载其领域 Skill。
- 默认不主动跑测试；需要时运行 `ItemModule.Smoke`。入口：`Assets/GameTest/ItemModule/ItemModuleSmokeTests.cs`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
