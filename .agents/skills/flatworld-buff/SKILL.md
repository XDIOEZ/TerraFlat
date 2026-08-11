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

## 按任务定位
- 修改本体 Buff 数值或组合：编辑 `Assets/StreamingAssets/GameConfig/Buffs/` 下由 `buff-manifest.json` 声明的功能模型分包；`periodic_damage.json` 保存周期伤害主模型，`periodic_recovery.json` 保存周期生命恢复，`periodic_resource_change.json` 保存周期资源变化，`attribute_modifiers.json` 保存成对 Start/Stop 属性倍率。复合 Buff 按主要执行模型只归档一次，附加效果仍保留在同一定义的 `effects` 中。
- 分包 ID/文件名只负责内容编辑与未来 Excel 工作表分组，不参与运行时行为；`category` 仍是饮水延时等规则使用的运行时语义，禁止用文件分类替代 `blood_loss` 等类别。
- 新增功能模型分包必须登记稳定 ID、相对路径和启用状态，不要恢复已淘汰的 Buff ScriptableObject 路径。
- 修改字段、类别或叠加规则：同步检查 `BuffDefinitionDto`、`BuffDefinition`、`BuffDefinitionFactory` 和现有 JSON。
- 新增效果类型：在 `BuffEffectTypeIds` 定义稳定 ID，在 `BuffEffectDispatcher` 注册处理器，并在 `BuffDefinitionFactory.ValidateEffectParameters()` 增加参数校验。
- 修改添加、移除、叠加、饮水延时或事件：编辑 `BuffManager`。

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

## 边界约束
- Buff ID 同时是全局注册键、运行时字典键、存档恢复键和内容引用键；禁止直接重命名。确需迁移时提供旧 ID 映射并增加旧存档回归测试。
- JSON 使用严格 schemaVersion 1；未知字段、重复 ID、未知 `typeId`、无效枚举或非有限数值必须在构建阶段失败，禁止静默忽略。
- `durationSeconds: null` 表示永久 Buff；`extend_duration` 与 `refresh_duration` 只能用于正持续时间 Buff；含 Tick 效果时 `tickIntervalSeconds` 必须大于 0。
- 效果处理器必须在定义构建时缓存，运行时 Tick 不做字符串查找或反射。

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-12：新增永久能力 Buff“位于干净的淡水中/位于脏的淡水中”，由 `Tile_Water` 按河流与湖泊/地下水授予并在离水时移除；Buff 本身不补水，长按交互饮水由 `Mod_InteractSender.FreshWaterDrinking` 消费该能力。
- 2026-08-12：新增“感染”Buff，持续 30 秒、每秒造成 1 点真实伤害；`ActorBuffLightController` 同时观察感染状态，通过 `ActorRenderColorEffect` 为玩家与共用动画模块的角色启用 0.18 强度绿色染色，移除或到期后恢复。
- 2026-08-12：新增 `core:water_consume_speed_multiplier`，允许 Buff 单独调整水分消耗；奔跑 Buff 用 0.5/2.0 成对倍率将奔跑期间水分消耗减半，不改变其他营养消耗。
- 2026-08-12：新增 `ActorBuffLightController`，观察 `BuffManager` 的添加/移除事件；“光耀”生效时在角色子层级启用暖黄色 Point Light2D，移除时关闭，不改动 Buff 数据与生命周期。
- 2026-08-11：`PlayerBuffStatusHUD` 宽度缩减为 160，面板高度改为按标题区与实际 Buff 条目总高度动态计算；条目变化后强制重建布局，不再保留空白列表区。

## 修改后自动测试
- Buff 冒烟测试：`Assets/GameTest/Buff/BuffSmokeTests.cs`；分类：`Buff.Smoke`。
- GM Buff 目标解析与时长覆盖：`Assets/GameTest/Buff/GmBuffTargetingTests.cs`；分类：`Buff.GM`。
- 水体地块驱动 Buff 回归：`Assets/GameTest/Dimension/DimensionTileEffectTests.cs`；分类：`Dimension.TileEffects`。
- 先按 `flatworld-test-automation` 的触发门槛判断；普通局部表现事件清理只做静态诊断、相关程序集编译和 Console 检查，达到系统级门槛或用户明确要求时才执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Buff.Smoke`。
- 仅按“高耦合联动”表命中项追加分类。
- 新行为先增加确定性回归测试；测试失败时优先修复生产代码，禁止删除测试或弱化断言。

## 修改后维护本 Skill
新增或移动 Buff 字段、效果类型、类别、叠加模式、JSON 路径、模块 Prefab、注册入口、存档结构或关键消费者后，更新本 Skill；单次测试结果只在任务总结中报告。
