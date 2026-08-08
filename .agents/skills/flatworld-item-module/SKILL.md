---
name: flatworld-item-module
description: "Use when: 定位或修改 FlatWorld 的 Item/Module 组合架构、实体创建销毁、对象池、模块加载保存、Tick 调度、运行时注册、空间索引或网络模块序列化。关键词：ItemMgr、Item、Module、ItemMods、ItemMaker。"
argument-hint: "Item、Module、Tick 或对象池问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld Item / Module 系统定位

> 最后核对：2026-08-07。绝大多数玩法最终挂接到 Item 的 Module。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Entities/Item/Item.cs`：Item 生命周期、模块加载保存与局部调度缓存。
2. `Assets/5_Scripts/5-3_GamePlay/Entities/Item/Module.cs`：模块基类、`TickMode`、`FixedTickInterval`。
3. `Assets/5_Scripts/5-3_GamePlay/Entities/Item/ItemMods.cs`：按名称/ID 的模块索引与调度失效。
4. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/ItemMgr*.cs`：`ItemMgr.cs` 保留公共 API 与 Unity 生命周期；Spawning、Perception、Players、RandomDrop partial 分别承载实例生成销毁、感知空间索引、玩家加载和随机掉落。

## 核心链路

```text
ItemMaker / ItemMgr 实例化
→ Item.itemData
→ Item.ItemMods
→ Module.ModuleInit + Load
→ ItemMgr 分级调度 Item
→ Item.Tick 调度每个 Module.ModUpdate
→ Save / Despawn / Pool Reuse
```

## 关键文件与资源

- 抽象实体：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/Item.cs`。
- 通用实体：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/GameItem.cs`。
- 玩家实体：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/Player.cs`。
- 创建入口：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/ItemMaker.cs`。
- 数据基类：`Assets/5_Scripts/5-1_Data/ItemData/ItemData.cs`。
- 地图 ItemData：`Assets/5_Scripts/5-1_Data/ItemData/Data_TileMap.cs`；格子地形栈：`Assets/5_Scripts/5-1_Data/TileData/TileStackCell.cs`。
- 联网序列化：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/ItemNetworkStateSerialization.cs`。
- 远端模块边界：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/IRemoteNetworkModule.cs`。
- Item Prefab：`Assets/2_Prefabs/Item/`。
- Module Prefab：`Assets/2_Prefabs/Module/`。
- 本体物品入口：`Assets/StreamingAssets/GameConfig/Items/item-manifest.json`；定义按最终解析出的 `shellPrefab` 放在 `Items/shells/*.json`，分包 `id/path`、`shellPrefab` 和模板 Prefab 根名称统一使用 `Axe/Prop/Dagger/Pickaxe/Spear/Stick/Seed`。`ItemDefinitionCatalogLoader` 只加载 Manifest 显式启用的包，先全局合并再解析跨文件 `parent`，是物品静态配置的唯一真源。

## 调度约束

- `Module.TickMode` 可为 `EveryFrame`、`FixedInterval`、`Disabled`。
- 未声明策略的旧模块默认 `EveryFrame`；未覆盖 `ModUpdate` 的模块可自动休眠。
- `ItemMgr` 使用 EveryFrame、Fast(0.05s)、Normal(0.1s)、Slow(0.25s) 的 8 桶错帧调度。
- `ItemMods` 增删模块、对象池复用和模块配置变化必须使调度缓存失效。
- FixedInterval 模块接收真实累计 `deltaTime`，不要假设每次调用等于固定间隔。
- `Data_TileMap` 仍是 `ItemData` 联合序列化成员；地形栈必须通过 `TileStackCell`/`TileStackView` API 访问，禁止恢复逐格 `List<TileData>` 或暴露可变列表。
- 物品环境初始化只保留温度、摄氏温度、降水、高度、光照五张网格；生产速度的环境系数读取降水，不再读取湿度。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-08：编辑器 Addressables Play Mode 必须使用 `BuildScriptFastMode`（`AddressableAssetSettings.m_ActivePlayerDataBuilderIndex = 0`），确保 `GameRes` 直接加载当前 Item/Module Prefab；打包模式会读取旧 Bundle，表现为外壳缺少 ItemData 或内嵌模块无法解析。
- 2026-08-07：物品分包 `id/path`、Manifest `shellPrefab`、模板 Prefab 文件名和根对象名统一为 `Axe/Prop/Dagger/Pickaxe/Spear/Stick/Seed`；具体物品 `ItemData.IDName` 继续保留 `Axe_Stone/Bone/...`，避免破坏存档、配方与玩法引用。
- 2026-08-07：`ItemMgr` 按公共生命周期、实例生成销毁、感知空间索引、玩家加载和随机掉落拆为五个 partial 文件；仅调整物理组织，公共签名与序列化字段保持不变。
- 2026-08-07：本体物品目录改为 `item-manifest.json` 显式聚合 7 个 `shells/*.json` 分包；分包按继承解析后的 `shellPrefab` 分类，所有启用包合并后再统一解析 `parent`，旧单文件 `items.json` 已移除。
- 2026-08-07：移除旧装备/防御/食物 Excel→Prefab 同步链；`StreamingAssets/GameConfig/Items` 的 Manifest 分包成为本体物品及模块参数的唯一编辑源，禁止恢复 Excel 双向同步。
- 2026-08-05：`Data_TileMap` 改为 MemoryPack 持久化 `TileStackCell[,]`，继续保持 ItemData 联合序列化身份；环境初始化收敛为五张网格，生产环境系数统一读取降水，`ItemModule.Smoke` 覆盖新栈往返与初始化契约。
- 2026-08-04：`Module.CanonicalModuleId` 统一模板、存档与运行时索引使用的模块 ID；`Item.ModuleLoad()` 会按旧 ID/Prefab 子物体名匹配后迁移为规范 ID，`GameRes` 为独立模块 Prefab 注册规范 ID 与旧序列化 ID 别名。`ItemDefinitionRuntime` 在实例化独立模块前也必须按规范 ID、旧 ID、子物体名和组件类型复用共享外壳中的模块，避免把内置的 `Mod_Weapon_AnimationAction` 误判为缺失。
- 2026-08-04：`Item.ModuleLoad()` 先按持久化 ID 匹配模块；旧实体 Prefab 的运行时模块若使用通用 ID，则回退按子物体名或组件类型匹配，避免将内嵌 AI/动画模块误判为缺失并错误实例化独立 Prefab。无法恢复的模块必须记录明确错误并跳过，禁止解引用空对象。
- 2026-08-03：`Item.Get_NewItemData()` 的 Prefab 模板提取只复制静态 Item/ModuleData，不执行 Item 或 Module 的 `Load/Save`；空模块 ID 会按模块物体名补齐，`GameRes` 以请求 ID 固化新数据，`ItemMgr` 在进入任何字典前拒绝空 `IDName`。
- 2026-07-30：农业模块边界收敛；`Mod_Seed` 的低频 Tick 仅迁移旧落地种子，`Mod_Grow` 低频 Tick 成为唯一作物成长与成熟状态机，`Mod_FarmlandSupply` 为休眠模块且仅响应物品使用事件；`Item.ModuleLoad()` 会先清理 Apple/AppleTree 的废弃农业模块数据再执行缺失模块自动修复。

## 易误判点

- 远程网络视觉副本不得注册进本地 Tick、AI 感知或本地存档索引。
- `Item.OnDestroy` 与主动 `PrepareForDespawn` 有防重复逻辑，不能在外部再次保存/销毁同一 Item。
- 新模块不仅要创建脚本，还要检查 Module Prefab、ModuleData、Addressables 标签和目标 Item Prefab 挂载。
- `Items/shells/` 不会被自动扫描；新增分包必须登记进 `item-manifest.json`。修改物品最终 `shellPrefab` 后，也必须把原始定义移到对应模板包，否则加载器会按 Manifest 的 `shellPrefab` 分类约束直接报错。
- 当前 7 个本体分包的 `id`、文件名、`shellPrefab` 与模板 Prefab 根名称必须保持一致；具体物品 ID 与模板身份是两套概念，禁止为了统一模板名而修改 Prefab 内的 `ItemData.IDName`。
- 旧 `Item/Mod_HealthPoints.cs` 已确认无代码或资源引用并删除；实体生命值不是通用 Item Module 扩展点，统一由战斗系统 `DamageReceiver` 管理。

## 高耦合联动

只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；单个玩法模块的内部数值调整只加载该玩法 Skill。

| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| Item 创建/加载/保存/Despawn、对象池复用或 ModuleData | `flatworld-data-save`；涉及远端状态时再加载 `flatworld-networking` | 注册与注销各一次、池化无旧订阅、序列化往返不丢模块 | `DataSave.Smoke`；联机时追加 `Networking.Smoke` |
| Module Tick 档位、累计 deltaTime 或调度缓存失效 | 只加载实际受影响模块所属 Skill，例如 `flatworld-buff`、`flatworld-inventory-crafting`、`flatworld-combat` | FixedInterval 语义不变，增删模块和复用后重新建缓存 | 对应玩法 Smoke |
| 玩家注册、创建/释放或 `SetProfileContext()` | `flatworld-core`、`flatworld-player-interaction`；涉及远程副本时再加载 `flatworld-networking` | 本地档案、玩家索引与远程副本隔离 | `Core.Smoke`、`PlayerInteraction.Smoke`；联机时追加 `Networking.Smoke` |
| 感知空间索引、移动同步或实体位置注册 | `flatworld-ai`、`flatworld-navigation` | AI 查询不读到池中/远程残留，导航位置与实体位置一致 | `AI.Smoke`、`Navigation.Smoke` |

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/ItemModule/ItemModuleSmokeTests.cs`；当前只保留 Manifest 分包聚合/外壳分类与模块规范 ID/数据重绑定两个关键契约。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；Item/Module 测试约定目录：`Assets/GameTest/ItemModule/`；场景目录：`Assets/GameTest/Scenes/ItemModule/`；冒烟分类：`ItemModule.Smoke`。
- 新增实体创建销毁、模块加载保存、Tick 调度、对象池或运行时注册行为时必须增加系统测试；修复 Bug 时先增加回归测试。Item 完整生命周期变化时同步更新冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；测试结束必须验证注册表、调度器、空间索引和对象池不存在残留引用。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category ItemModule.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

改变 Item/Module 生命周期、Tick 档位、池化规则、注册索引、Prefab 目录、物品 JSON 定义或网络边界后，必须更新本 Skill；若调整具体玩法模块，也同步更新该玩法 Skill 的近期变更。
