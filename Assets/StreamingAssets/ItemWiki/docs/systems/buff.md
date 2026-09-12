# Buff 系统

## 系统定位

负责中毒、出血、临时增温、周期伤害/恢复等具有持续时间、叠加规则和生命周期的角色状态。

## 当前机制

- Buff 内容由 JSON 定义，Manifest 统一发现。
- Buff ID 同时承担注册、运行时、存档和内容引用，属于稳定业务 ID。
- `BuffManager` 管理角色当前 BuffInstance。
- Effect 的 `typeId` 在定义构建阶段映射到缓存 Handler，运行 Tick 不做反射或字符串查找。
- `durationSeconds=null` 表示永久状态。
- Tick 型 Buff 必须有正 Tick 间隔。

## 数据来源

- `Assets/StreamingAssets/GameConfig/Buffs/buff-manifest.json`
- `Assets/StreamingAssets/GameConfig/Buffs/*.json`
- 实现：`Assets/5_Scripts/5-3_GamePlay/Entities/Buff/`

当前内容分包包含属性修饰、伤害减免、食物效果、周期伤害、周期恢复、周期资源变化、生存状态等。

## 设计边界

- “当前正在水里/当前允许某操作”这类环境事实不使用 Buff 表达。
- 潮湿、感染、中毒、出血等具有角色持续状态语义的效果才进入 BuffManager。
- 伤害模块只发布结算结果，具体命中附加 Buff 通过独立处理器接入。
- 玩家正式重生时统一清空全部 Buff，不能只清 UI。

## 出血

当前正式出血使用互斥等级：`出血1 / 出血2 / 出血3`。更高等级替换低等级，更低等级只续期当前高等级。

## 修改时联动

- 新 effect type：同步 Definition 校验、Dispatcher、参数约束。
- Buff ID：涉及存档兼容语义。
- 命中状态：战斗系统。
- 温度 Buff：生存/环境系统。

## 对应 Skill

`.agents/skills/flatworld-buff/SKILL.md`
