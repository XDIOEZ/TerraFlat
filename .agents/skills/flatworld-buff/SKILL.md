---
name: flatworld-buff
description: "Use when: 定位或修改 FlatWorld 的 Buff 定义、JSON 目录、运行时实例、叠加与持续时间、Tick 效果、效果处理器、Buff 存档、MOD Buff 注册或 BuffManager Prefab。关键词：BuffManager、BuffDefinition、BuffInstance、BuffEffectDispatcher、BuffCatalogLoader、buff-manifest.json。"
---

# FlatWorld Buff

## 入口

- 生命周期：`Assets/5_Scripts/5-3_GamePlay/Entities/Buff/{BuffManager,BuffInstance}.cs`
- 叠层与角色水体时钟：同目录 `BuffManager.Stacks.cs`；Wiki 展示/写回位于 `StreamingAssets/ItemWiki/{buffs.js,buff_validation.py,wiki_server.py}`。
- 定义链：同目录 `{BuffDefinition,BuffDefinitionDto,BuffDefinitionFactory}.cs`
- 效果映射：`BuffEffectDispatcher.cs`、`BuffEffectTypeIds`
- 内容：`Assets/StreamingAssets/GameConfig/Buffs/buff-manifest.json` 及其分包 JSON

## 不变量

- Buff ID 同时用于注册、运行时、存档和内容引用；重命名必须提供迁移映射。
- JSON schemaVersion 1 严格校验；重复 ID、未知 typeId/字段、非法枚举和非有限数值应在构建阶段失败。
- `durationSeconds: null` 表示永久；Tick Buff 的间隔必须大于 0；extend/refresh 只用于正持续时间。
- Handler 在定义构建时缓存，运行 Tick 不做反射或字符串查找。
- `add_stacks` 只改变单实例 `StackCount` 并续期，不重放 Start/Stop、不重置 Tick 相位；冷却/移速等登记型倍率仍只登记一次。按层伤害由效果 `scaleWithStacks` 显式声明，不能把整个效果集合一律乘层数。
- 层数必须追加在 BuffInstance 原有持久化字段后；恢复时按当前定义把缺失/越界层数归一到 `1..MaxStacks`。表现订阅 `BuffStacksChanged`，不能靠重建 Buff 或反复触发布局表达层数变化。
- 水体叠层时钟由每个 BuffManager 独享，不能放入共享 LiquidDefinition/WorldLiquidBehaviour；使用真实地形深度而不是漂浮后的视觉浸没深度。同帧跨水格保留计时，进入浅水只限制后续增长，不削掉已有层数；离水保留 Buff 按自身时长自然到期。
- 火焰施加先比较完整候选层数：同层潮湿阻止点燃，强火成功施加后才蒸发弱潮湿。燃烧期间重新浸水允许潮湿累计到灭火阈值，不能每次把新加的单层水立即删除而导致永久无法灭火。
- Wiki BUFF 页与 Item 共用内联编辑、文件指纹、备份及原子写回事务；BuffManifest 是可写目标白名单，校验器须与 BuffDefinitionFactory 同步。保存不代表正在运行的 GameRes 已热重载，页面必须说明生效边界；公开模式只读。
- 新效果需同时增加稳定 typeId、Dispatcher 注册和参数校验。
- `core:temperature_warming` 在 start/stop 按 Buff 实例登记、撤销临时增温，start 必须配置正 `value` 和 `upperLimit`；不要在 Tick 中反复加温，也不要在 Stop 固定减去配置值。受限增温和基础体温由 `Mod_Temperature` 分层结算，重复食用使用续期而不重复登记来源。
- 内容分包只决定归档；运行时语义仍由 `category`/effects 决定。
- “当前位于某环境、可执行某操作”以及只在环境内生效的减速等被动影响，不使用可清除 Buff；只有潮湿、感染、中毒等角色状态进入 BuffManager。
- 玩家正式重生视为新的角色生命周期，必须在 `Mod_PlayerDeathState.CompleteRespawnState` 的统一重生收口调用 `BuffManager.ClearAllBuffs()`；同世界重生与跨维度重生都走这一规则，不能只清 UI 或只清部分 Buff 类别。
- Buff 的只读调试表现可从 `BuffManager.ActiveBuffs` 读取 `BuffInstance.Definition.DisplayName` 与剩余时间；表现层不得通过显示逻辑修改、续期或移除 Buff。
- 出血状态固定使用互斥等级 `出血1/出血2/出血3`；统一通过 `BuffManager.ApplyBleedingTier` 应用，更低等级只续期当前更高等级，更高等级替换低等级。历史 `失血/流血/出血` 只作为存档迁移别名存在，不能重新作为内容或玩法 ID 使用。

## 原生 AIECS 状态边界

- Native Buff 由当前 GameRes 定义冷编译，实例以定义索引、到期时间、下次 Tick 和 Credit 保存在 DynamicBuffer；不得把托管 Handler 放进 Burst。只支持完整效果集合的定义，未知效果/阶段/标签条件必须显式报告，不能只迁移同一 Buff 中的伤害而漏掉其它效果。
- 每个 Buff 的周期命中独立保留来源 Credit，禁止把同实体的多个 Buff 合并后使用最后一个来源作击杀者；按模拟时间推进，过期前已到期的 Tick 仍结算。输出容量按实际周期命中数量扩张，不能用固定最大 Buff 数静默丢事件。
- 当前原生能力是周期真实伤害、水量变化、出血互斥等级与 refresh/extend/ignore/add_stacks；常量伤害与按层伤害分别编译，命中上下文的完整层数一次传递。温度、完整潮湿/环境状态、营养完整玩法、自定义 Handler 仍需通过能力阶段迁移。玩家继续由旧 BuffManager 管理，外部代理不重复 Tick 玩家 Buff。

## 工作流与验证

1. 数值/组合只改 JSON；schema、叠加、生命周期才改 C#。
2. 存档字段或 ID 变化联动 `flatworld-data-save`；MOD 定义联动 `flatworld-modding`；伤害语义联动 `flatworld-combat`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
