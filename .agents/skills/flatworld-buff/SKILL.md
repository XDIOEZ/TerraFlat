---
name: flatworld-buff
description: "Use when: 定位或修改 FlatWorld 的 Buff 定义、JSON 目录、运行时实例、叠加与持续时间、Tick 效果、效果处理器、Buff 存档、MOD Buff 注册或 BuffManager Prefab。关键词：BuffManager、BuffDefinition、BuffInstance、BuffEffectDispatcher、BuffCatalogLoader、buff-manifest.json。"
---

# FlatWorld Buff 定位

> 最后核对：2026-08-03。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Buff/BuffManager.cs`：添加、查询、叠加、移除、事件、固定间隔 Tick 与 MemoryPack 存档入口。
2. `Assets/5_Scripts/5-3_GamePlay/Buff/BuffDefinition.cs`、`BuffDefinitionDto.cs`、`BuffDefinitionFactory.cs`：运行时定义、JSON DTO、严格校验与效果处理器缓存。
3. `Assets/5_Scripts/5-3_GamePlay/Buff/BuffInstance.cs`：持续时间、Start/Tick/Stop、过期和可持久化运行时状态。
4. `Assets/5_Scripts/5-3_GamePlay/Buff/BuffEffectDispatcher.cs`：`typeId` 到 C# 效果函数的唯一映射。
5. `Assets/5_Scripts/5-3_GamePlay/Buff/BuffCatalogLoader.cs` 与 `Assets/StreamingAssets/GameConfig/Buffs/buff-manifest.json`：本体 Buff 分包加载入口与清单。

主链路：`buff-manifest.json -> 启用的 Buff 分包 / MOD Def -> BuffDefinitionFactory -> GameRes.RegisterBuff -> BuffManager.AddBuff -> BuffInstance -> BuffEffectDispatcher`。

## 按任务定位

- 修改本体 Buff 数值或组合：编辑 `Assets/StreamingAssets/GameConfig/Buffs/` 下由 `buff-manifest.json` 声明的业务分包；新增分包必须登记稳定 ID、相对路径和启用状态，不要恢复已淘汰的 Buff ScriptableObject 路径。
- 修改字段、类别或叠加规则：同步检查 `BuffDefinitionDto`、`BuffDefinition`、`BuffDefinitionFactory` 和现有 JSON。
- 新增效果类型：在 `BuffEffectTypeIds` 定义稳定 ID，在 `BuffEffectDispatcher` 注册处理器，并在 `BuffDefinitionFactory.ValidateEffectParameters()` 增加参数校验。
- 修改添加、移除、叠加、饮水延时或事件：编辑 `BuffManager`。
- 修改持续时间、补帧 Tick、Start/Stop 或存档字段：编辑 `BuffInstance` 与 `BuffManagerSaveData`。
- 修改本体加载或全局注册：检查 `BuffCatalogLoader`、`GameRes.BuffDefinitions`、`GameRes.RegisterBuff()` 和 `GameRes.GetBuffDefinition()`。
- 修改 MOD Buff：检查 `ModRuntimeManager.ProcessBuffDefinitions()`、内容 Def 的 `buffs` 数组与本地化键，并同时使用 `flatworld-modding`。
- 修改模块装配：检查 `Assets/2_Prefabs/Module/Manager/Module_BuffManager.prefab`，并同时使用 `flatworld-item-module`。

`ModuleObserverBase.cs` 与 `ColdWeaponStaminaObserver.cs` 虽位于 Buff 目录，但属于旧模块观察者/武器精力逻辑，不是 JSON Buff 生命周期；修改它们时同时使用 `flatworld-combat` 或 `flatworld-item-module`。

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
- MOD Buff 与本体 Buff 共用 `GameRes.RegisterBuff()` 的冲突检测；不要绕过注册入口覆盖已有 ID。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-03：本体 Buff 从单个 `buffs.json` 拆为 `buff-manifest.json + environment/combat/survival/movement` 四个分包；运行时先聚合并检查跨包重复 ID，再统一注册。StreamingAssets 文本读取接入 Android/WebGL 的 `UnityWebRequest` 协程路径。

## 修改后自动测试

- Buff 冒烟测试：`Assets/GameTest/Buff/BuffSmokeTests.cs`；分类：`Buff.Smoke`。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Buff.Smoke`；无需视觉模型、截图或测试工具卡片。
- 仅按“高耦合联动”表命中项追加分类。
- 新行为先增加确定性回归测试；测试失败时优先修复生产代码，禁止删除测试或弱化断言。
- 只有 Buff 图标、粒子、界面布局或最终画面观感发生变化时才做定向截图。

## 修改后维护本 Skill

新增或移动 Buff 字段、效果类型、类别、叠加模式、JSON 路径、模块 Prefab、注册入口、存档结构或关键消费者后，更新本 Skill；单次测试结果只在任务总结中报告。
