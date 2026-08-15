---
name: flatworld-buff
description: "Use when: 定位或修改 FlatWorld 的 Buff 定义、JSON 目录、运行时实例、叠加与持续时间、Tick 效果、效果处理器、Buff 存档、MOD Buff 注册或 BuffManager Prefab。关键词：BuffManager、BuffDefinition、BuffInstance、BuffEffectDispatcher、BuffCatalogLoader、buff-manifest.json。"
---

# FlatWorld Buff

## 入口

- 生命周期：`Assets/5_Scripts/5-3_GamePlay/Entities/Buff/{BuffManager,BuffInstance}.cs`
- 定义链：同目录 `{BuffDefinition,BuffDefinitionDto,BuffDefinitionFactory}.cs`
- 效果映射：`BuffEffectDispatcher.cs`、`BuffEffectTypeIds`
- 内容：`Assets/StreamingAssets/GameConfig/Buffs/buff-manifest.json` 及其分包 JSON

## 不变量

- Buff ID 同时用于注册、运行时、存档和内容引用；重命名必须提供迁移映射。
- JSON schemaVersion 1 严格校验；重复 ID、未知 typeId/字段、非法枚举和非有限数值应在构建阶段失败。
- `durationSeconds: null` 表示永久；Tick Buff 的间隔必须大于 0；extend/refresh 只用于正持续时间。
- Handler 在定义构建时缓存，运行 Tick 不做反射或字符串查找。
- 新效果需同时增加稳定 typeId、Dispatcher 注册和参数校验。
- 内容分包只决定归档；运行时语义仍由 `category`/effects 决定。
- “当前位于某环境、可执行某操作”以及只在环境内生效的减速等被动影响，不使用可清除 Buff；只有潮湿、感染、中毒等角色状态进入 BuffManager。
- Buff 的只读调试表现可从 `BuffManager.ActiveBuffs` 读取 `BuffInstance.Definition.DisplayName` 与剩余时间；表现层不得通过显示逻辑修改、续期或移除 Buff。

## 工作流与验证

1. 数值/组合只改 JSON；schema、叠加、生命周期才改 C#。
2. 存档字段或 ID 变化联动 `flatworld-data-save`；MOD 定义联动 `flatworld-modding`；伤害语义联动 `flatworld-combat`。
3. 默认不主动跑测试；需要时运行 `Buff.Smoke`，GM/水体按需追加 `Buff.GM`、`Dimension.TileEffects`。
4. 测试入口：`Assets/GameTest/Buff/`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
