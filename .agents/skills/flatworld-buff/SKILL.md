---
name: flatworld-buff
description: "Use when: 定位或修改 FlatWorld 的 Buff 定义、JSON 目录、运行时实例、叠加与持续时间、Tick 效果、效果处理器、Buff 存档、MOD Buff 注册或 BuffManager Prefab。关键词：BuffManager、BuffDefinition、BuffInstance、BuffEffectDispatcher、BuffCatalogLoader、buff-manifest.json。"
---

# FlatWorld Buff 定位

> 最后核对：2026-08-11。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Entities/Buff/BuffManager.cs`：添加、查询、叠加、移除、事件、固定间隔 Tick 与 MemoryPack 存档入口。
2. `Assets/5_Scripts/5-3_GamePlay/Entities/Buff/BuffDefinition.cs`、`BuffDefinitionDto.cs`、`BuffDefinitionFactory.cs`：运行时定义、JSON DTO、严格校验与效果处理器缓存。
3. `Assets/5_Scripts/5-3_GamePlay/Entities/Buff/BuffInstance.cs`：持续时间、Start/Tick/Stop、过期和可持久化运行时状态。
4. `Assets/5_Scripts/5-3_GamePlay/Entities/Buff/BuffEffectDispatcher.cs`：`typeId` 到 C# 效果函数的唯一映射。
5. `Assets/5_Scripts/5-3_GamePlay/Entities/Buff/BuffCatalogLoader.cs` 与 `Assets/StreamingAssets/GameConfig/Buffs/buff-manifest.json`：本体 Buff 分包加载入口与清单。

主链路：`buff-manifest.json -> 启用的 Buff 分包 / MOD Def -> BuffDefinitionFactory -> GameRes.RegisterBuff -> BuffManager.AddBuff -> BuffInstance -> BuffEffectDispatcher`。

角色状态表现链路：`BuffManager.BuffAdded/BuffRemoved/BuffDurationChanged -> ActorStatusVisualEffectController -> 独立 SpriteRenderer 序列 / 状态光晕 / VisualEffectManager 池化粒子`。控制器装配在玩家与 AI 共用的 Animator 模块 Prefab；它不参与伤害 Tick，也不改变 Buff 的权威生命周期。

玩家状态 HUD 链路：`BuffManager.BuffAdded/BuffRemoved/BuffDurationChanged/BuffCountdownChanged -> PlayerBuffStatusHUD -> BuffStatusRowView`。HUD 只读取本地玩家的 `ActiveBuffs`；增删事件刷新条目结构，显式时长与整秒倒计时事件只刷新文本，不创建、修改或持久化 Buff。

## 按任务定位

- 修改本体 Buff 数值或组合：编辑 `Assets/StreamingAssets/GameConfig/Buffs/` 下由 `buff-manifest.json` 声明的功能模型分包；`periodic_damage.json` 保存周期伤害主模型，`periodic_recovery.json` 保存周期生命恢复，`periodic_resource_change.json` 保存周期资源变化，`attribute_modifiers.json` 保存成对 Start/Stop 属性倍率。复合 Buff 按主要执行模型只归档一次，附加效果仍保留在同一定义的 `effects` 中。
- 分包 ID/文件名只负责内容编辑与未来 Excel 工作表分组，不参与运行时行为；`category` 仍是饮水延时等规则使用的运行时语义，禁止用文件分类替代 `blood_loss` 等类别。
- 新增功能模型分包必须登记稳定 ID、相对路径和启用状态，不要恢复已淘汰的 Buff ScriptableObject 路径。
- 修改字段、类别或叠加规则：同步检查 `BuffDefinitionDto`、`BuffDefinition`、`BuffDefinitionFactory` 和现有 JSON。
- 新增效果类型：在 `BuffEffectTypeIds` 定义稳定 ID，在 `BuffEffectDispatcher` 注册处理器，并在 `BuffDefinitionFactory.ValidateEffectParameters()` 增加参数校验。
- 修改添加、移除、叠加、饮水延时或事件：编辑 `BuffManager`。
- GM 对限时 Buff 的时长覆盖：先经 `BuffManager.AddBuff()` 创建/叠加，再调用 `TrySetBuffDuration(buffId, seconds)`；永久 Buff 必须保持 JSON 的永久语义，拒绝运行时覆盖。
- 修改持续时间、补帧 Tick、Start/Stop 或存档字段：编辑 `BuffInstance` 与 `BuffManagerSaveData`。
- 修改本体加载或全局注册：检查 `BuffCatalogLoader`、`GameRes.BuffDefinitions`、`GameRes.RegisterBuff()` 和 `GameRes.GetBuffDefinition()`。
- 修改 MOD Buff：检查 `ModRuntimeManager.ProcessBuffDefinitions()`、内容 Def 的 `buffs` 数组与本地化键，并同时使用 `flatworld-modding`。
- 修改模块装配：检查 `Assets/2_Prefabs/Module/Manager/Module_BuffManager.prefab`，并同时使用 `flatworld-item-module`。

`ModuleObserverBase.cs` 与 `ColdWeaponStaminaObserver.cs` 虽位于 Buff 目录，但属于旧模块观察者/武器精力逻辑，不是 JSON Buff 生命周期；修改它们时同时使用 `flatworld-combat` 或 `flatworld-item-module`。

## Excel 往返契约

- 使用 `Buffs` 表保存一行一个 Buff 的公共字段：`packageId`、`id`、显示文本、`category`、持续时间、Tick 间隔、叠加方式与饮水延时。
- 使用 `Effects` 表保存一行一个效果：`buffId`、`effectIndex`、`phase`、`typeId`、`targetId`、`requiredTag`、`value`；通过 `buffId` 回连 `Buffs`，按 `effectIndex` 恢复 JSON 数组顺序。
- `packageId` 必须对应 `buff-manifest.json` 中的功能模型分包 ID；导入时按它写回目标 JSON，禁止根据 `category` 猜测文件归属。
- 禁止把任意数量的效果展开为 `effect1/effect2/...` 固定列，也不要引入模板继承或覆盖语义；每个 Buff JSON 定义保持自包含，确保 JSON 与表格能够无损往返。

## 高耦合联动

只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；单个 Buff 的显示名、描述或不改变效果类型的数值调整不要扩散检查。

| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| Buff ID、JSON schema、定义注册、叠加策略或可持久化字段 | `flatworld-data-save`；涉及 MOD Def 时再加载 `flatworld-modding` | 旧 ID/存档可恢复、注册冲突明确、MOD 与本体共用校验 | `DataSave.Smoke`；MOD 时追加 `Modding.Smoke` |
| `BuffManager` 模块生命周期、Tick、Save/Load 或模块 Prefab | `flatworld-item-module` | Module 调度、对象池复用和序列化后无重复事件/旧效果 | `ItemModule.Smoke` |
| Heal、TrueDamage、MaxHealthPercentTrueDamage 处理器或接收者解析 | `flatworld-combat` | 继续调用 `DamageReceiver` 权威入口，不绕过难度和死亡边界 | `Combat.Smoke` |
| MoveSpeed、Stamina、Nutrition、FoodConsumeSpeed 或 TemperatureCooling 处理器 | 只加载实际处理器对应的 `flatworld-player-interaction`、`flatworld-inventory-crafting` 或 `flatworld-environment` | Start/Stop 成对恢复，目标模块与数据更新事件正确 | 对应领域 Smoke |
| `AddBuff/RemoveBuff/HasBuff`、过期/移除事件或 Tile 驱动应用 | `flatworld-ai`；涉及水体/地块时再加载 `flatworld-dimension` | `AI_Ghost` 与 Tile 行为不留下永久 Buff，Stop/Removed 只执行一次 | `AI.Smoke`；Tile 时追加 `Dimension.TileEffects` |

## 边界约束

- Buff ID 同时是全局注册键、运行时字典键、存档恢复键和内容引用键；禁止直接重命名。确需迁移时提供旧 ID 映射并增加旧存档回归测试。
- JSON 使用严格 schemaVersion 1；未知字段、重复 ID、未知 `typeId`、无效枚举或非有限数值必须在构建阶段失败，禁止静默忽略。
- `durationSeconds: null` 表示永久 Buff；`extend_duration` 与 `refresh_duration` 只能用于正持续时间 Buff；含 Tick 效果时 `tickIntervalSeconds` 必须大于 0。
- 效果处理器必须在定义构建时缓存，运行时 Tick 不做字符串查找或反射。
- 成对的 Start/Stop 乘法效果必须互为倒数，并验证添加、移除、过期和存档恢复后属性能回到原值。
- `RemoveBuff()`、`ClearAllBuffs()` 与自然过期必须让 Stop 效果和 `BuffRemoved` 各执行一次；遍历期间继续使用快照列表，禁止直接修改活动字典。
- MemoryPack 只持久化 `DefinitionId`、剩余时间和 Tick 累计；Unity 对象与定义引用在加载时通过 `GameRes` 恢复。缺失定义应跳过并报告，不得留下半初始化实例。
- 保留 `BuffManager` 的模块 Tick 调度与 `BuffInstance` 的单次更新 Tick 上限，避免长帧造成无限补算。
- `BuffCountdownChanged` 只在存在订阅者且限时 Buff 的向上取整显示秒数变化时触发；永久 Buff、同一显示秒内的 0.1 秒 Tick 和没有 HUD 的 AI 不产生表现事件开销。
- MOD Buff 与本体 Buff 共用 `GameRes.RegisterBuff()` 的冲突检测；不要绕过注册入口覆盖已有 ID。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-11：本体 Buff JSON 从 `combat/environment/survival/movement` 业务来源分包改为 `periodic_damage/periodic_recovery/periodic_resource_change/attribute_modifiers` 功能模型分包；13 个 Buff 的稳定 ID、schemaVersion 1、数值、效果与运行时 `category` 均保持不变，分包层仅服务内容编辑和未来 Excel 分组。
- 2026-08-10：`PlayerBuffStatusHUD` 移除 `0.25s` 兜底轮询；`BuffManager` 新增仅在显示整秒跨界时触发的 `BuffCountdownChanged`，HUD 的时长文本与条目结构分别按事件刷新，不改变 Buff 权威生命周期。
- 2026-08-09：新增 `PlayerBuffStatusHUD`/`BuffStatusRowView`，从本地玩家 `BuffManager.ActiveBuffs` 只读展示活动 Buff，使用 `BuffAdded`、`BuffRemoved` 与 `BuffDurationChanged` 刷新；图标暂用 UI 占位符，不改变 Buff 生命周期。
- 2026-08-09：`光耀` 接入 `ActorStatusVisualEffectController` 的低强度状态光晕，复用圆形 Sprite 做轻微呼吸发光；仍由 `BuffAdded`、`BuffRemoved`、续期事件和 `0.2s` 兜底校验驱动，不改变幽灵真实伤害 Tick。
- 2026-08-09：`ActorStatusVisualEffectController` 新增复合 Buff 粒子映射；`出血|流血|失血` 共用 `BloodDropStatusEffect`，由 `BuffAdded`、`BuffRemoved`、续期事件和 `0.2s` 兜底校验驱动，持续表现通过 `VisualEffectManager` 对象池启停。
- 2026-08-09：新增可复用 `ActorStatusVisualEffectController`，由 `BuffManager` 生命周期事件驱动、每 `0.2s` 兜底同步存档恢复；`燃烧` 映射到 `CreatureBurning_Sheet` 八帧火焰，已装配到玩家/AI 动画模块。移除、过期和对象复用都会隐藏火焰，且附属精灵不受角色水体/受击 MPB 影响。
- 2026-08-08：F4 GM Buff 分发改为动态扫描已加载场景中激活的 `BuffManager`，通过带索引的滚动列表选择目标；保留确认、取消、清除和限时覆盖，运行时不再绑定 `GameController.LeftClick`。
- 2026-08-08：新版 `TileEffectReceiver` 通过 `ChunkTerrainData` 恢复河流/海洋的 `Tile_Water` 行为；进入水体添加 `水体减速`、`潮湿`，离开时成对移除并恢复移动倍率，旧 `Map` 仅作兼容回退。
- 2026-08-05：本体 combat 分包新增稳定 ID `燃烧`：持续 5 秒、每秒 1 点真实伤害、重复施加刷新持续时间；真实单机黄金路径会在玩家移动阶段施加并跨 Tick 验证伤害，随后移除 Buff、恢复生命。
- 2026-08-04：`BuffManager` 的规范模块 ID 固定为 `BuffManager`，并兼容旧存档/Prefab 中的 `Buff模块`；模板提取和模块自动修复不得再把旧 ID 写回运行时索引。

## 修改后自动测试

- Buff 冒烟测试：`Assets/GameTest/Buff/BuffSmokeTests.cs`；分类：`Buff.Smoke`。
- GM Buff 目标解析与时长覆盖：`Assets/GameTest/Buff/GmBuffTargetingTests.cs`；分类：`Buff.GM`。
- 水体地块驱动 Buff 回归：`Assets/GameTest/Dimension/DimensionTileEffectTests.cs`；分类：`Dimension.TileEffects`。
- 先按 `flatworld-test-automation` 的触发门槛判断；普通局部表现事件清理只做静态诊断、相关程序集编译和 Console 检查，达到系统级门槛或用户明确要求时才执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Buff.Smoke`。
- 仅按“高耦合联动”表命中项追加分类。
- 新行为先增加确定性回归测试；测试失败时优先修复生产代码，禁止删除测试或弱化断言。
- 只有 Buff 图标、粒子、界面布局或最终画面观感发生变化时才做定向截图。

## 修改后维护本 Skill

新增或移动 Buff 字段、效果类型、类别、叠加模式、JSON 路径、模块 Prefab、注册入口、存档结构或关键消费者后，更新本 Skill；单次测试结果只在任务总结中报告。
