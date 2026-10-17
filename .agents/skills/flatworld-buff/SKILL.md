---
name: flatworld-buff
description: "FlatWorld Buff 定义、叠层、持续时间、周期效果、来源归属与 GameObject/Native 生命周期。"
---

# Buff

`Mod_BuffManager.HasBuff(definitionId)` 查询常规及独立来源实例，比较沿用 ActiveBuffs 的身份语义；`TryGetBuff`/续期/普通出血等级操作仍只取常规键，来源实例用 EnsureSourceBuff/RemoveSourceBuff 的归属接口。对应现场 Lua 在 Buff/gameplay.lua，覆盖同定义双来源、删一保留、删完消失与常规出血共存。

## 入口

- `Assets/5_Scripts/5-3_GamePlay/Entities/Buff/`：`Mod_BuffManager/BuffInstance` 生命周期；`Stacks/Rain` 水体/淋雨时钟；`BuffDefinition/Dto/Factory` 定义；`BuffEffectDispatcher/BuffEffectTypeIds` 效果注册。
- `Assets/StreamingAssets/GameConfig/Buffs/buff-manifest.json` 及分包：内容真源。
- `Assets/StreamingAssets/ItemWiki/{buffs.js,buff_validation.py,wiki_server.py}`：展示、校验、文件指纹/备份/原子写回。
- Buff HUD 通过 `BuffStatusRowView` 和 `FlatWorldUI` 翻译 `DisplayName`，语言切换由 `PlayerBuffStatusHUD` 刷新；定义 ID 不参与翻译。
- 可选 `iconAddress` 指向 `Generated/Buff/StatusIcons` 的像素 Sprite；`BuffCatalogLoader` 按地址去重预载，句柄归 GameRes 资源会话，HUD 只读 `Definition.Icon`。正式行的白色 `状态图标` 与灰底/层数徽标分开；缺图用名称首字，复用清空旧图。

## 定义与状态

- ID 用于注册、内容和保存。`schemaVersion:1` 严格拒绝重复 ID、未知 typeId/字段、非法枚举/非有限值；新效果由稳定 typeId、Dispatcher、参数校验组成。Handler 构建时缓存。
- 伤害 Buff 通过 JSON `deathCauseId` 声明原因；`BuffInstance.DamageOrigin` 保存施加时的攻击者/物品快照，真伤沿用统一扣血入口。接受的续期/叠层采用本次来源，Ignore 保留原来源；岩浆显式接管燃烧来源并保留 lava 原因，避免归给此前武器。
- `durationSeconds:null` 永久，周期间隔为正，extend/refresh 用正持续时间；分包只是归档，语义由 category/effects 定义。
- `add_stacks` 改单实例 StackCount 并续期，不重放 Start/Stop 或重置 Tick 相位；登记倍率仅一份，`scaleWithStacks` 显式按层。恢复层数归一到 `1..MaxStacks`，表现订 `BuffStacksChanged`。
- `core:attack_flat_modifier` 按来源修正攻击总值（最低 0，武器/投射物沿 Owner 读取），`core:move_speed_reduction` 用 `1-value×层数` 登记独立移速倍率；二者用 start/stop 成对配置，支持 `scaleWithStacks`，层变只更新这些登记效果，不重放其它 Start。
- 感染统一由目录控制 300 秒、最多 3 层，重新感染加层并刷新时长，到期整体结束；每层每 10 秒 1 点真伤、固定攻击 -1、移速 -25%。生肉仅判定并施加感染，不覆盖目录时长；玩家由 GO 管理器推进，Native 受体尚不支持登记型属性效果。
- 可清 Buff 表达潮湿/感染/中毒等角色状态；环境内被动能力/可执行动作由环境运行器负责。`ActiveBuffs` 是只读表现入口。
- 正式重生在 `Mod_PlayerDeathState.CompleteRespawnState` 收口 `ClearAllBuffs`，同世界/跨维度一致。
- `BodyTraumaBuffEffects` 在 Start/Stop 时按角色刷新攻速与混乱汇总；逐帧输入及武器读取只查角色索引，不扫描全体 Buff，最后一个来源移除时清掉该角色缓存。
- 出血互斥 `出血1/2/3`，`ApplyBleedingTier` 低等级续期高等级、高等级替换低等级。
- Wiki 写目标由 BuffManifest 白名单限定，校验与 Factory 同步；公开模式只读，文件保存与 GameRes 重载是两个阶段。

## 湿、火与温度

- 水体时钟属于每个 Manager，按真实液深累计；跨水格不断计时，浅水只限后续增长。`decayStacksOnExpiry` 每次到期减一层并重新计时，最后一层 Stop。
- Rain 在权威生物 Tick 中推进，即使无现存 Buff。`RainWetnessClock` 按当地雨量/reference 折算 interval，rainMaxStacks 只限淋雨新增；持续雨续期限但不暂停周期效果。雨停/死亡/回池/清状态清未满层进度，雨量切换保留；Client/抑制天气维度不施加。
- 点燃比较完整候选层数：同层湿阻火，强火成功后蒸发弱湿；燃烧中浸水仍能积到灭火阈值。岩浆真实接触清湿并维持 JSON 最高燃烧层，退出续 10 秒，边缘交互不算接触。
- 潮湿保留叠层、到期脱层和灭火规则，effects 为空；环境热传导统一每秒缩小温差 1%，临时增温与入水保护由各自接口结算。
- `core:temperature_warming` Start/Stop 按实例登记/撤增温，正 value/upperLimit，重复食用续期；基础与受限增温由 Mod_Temperature 分层。
- 低温冻伤永久、热射病 240 秒，由体温越界且共用缓冲耗尽后的来源键持有，安全即移除；均每 10 秒 true_damage=10。热射病持续危险不续期，到期仍危险再完整施加。派生温度 Buff 不保存，缓冲由体温模块独立保存，清 Buff 不补满。

## Native 边界

- GameRes 定义冷编译，DynamicBuffer 存定义索引、期限、下次 Tick、Credit，不存托管 Handler；只接完整支持的效果集合，未知阶段/条件显式报告。
- 各周期命中独立来源 Credit，模拟时间推进，过期前已到期 Tick 仍结算；输出按实际事件扩容。
- 当前支持周期真实伤害、水量、出血互斥、refresh/extend/ignore/add_stacks，常量/按层分别编译；完整温度/潮湿/营养/自定义 Handler 尚未迁移。玩家由 Mod_BuffManager 推进，代理不重复 Tick。
