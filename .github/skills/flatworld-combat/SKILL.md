---
name: flatworld-combat
description: "Use when: 定位或修改 FlatWorld 的伤害、生命值、身体部位、死亡、战利品、武器、防御、Buff、状态效果、技能定义与施放。关键词：DamageReceiver、IDamageSender、BuffManager、Mod_SkillManager。"
argument-hint: "战斗、Buff 或技能问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 战斗、Buff 与技能定位

> 最后核对：2026-07-31。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Combat/DamageReceiver.cs`：生命值、身体部位、受伤、死亡、掉落和网络表现边界。
2. `Assets/5_Scripts/5-3_GamePlay/Combat/IDamageSender.cs`：伤害发送接口。
3. `Assets/5_Scripts/5-3_GamePlay/Buff/BuffManager.cs`：Buff 生命周期、叠加、持久化与 Tick。
4. `Assets/5_Scripts/5-3_GamePlay/Skill/Mod_SkillManager.cs`：技能持有、施放、运行时技能列表。

## 战斗文件

- 武器：`Assets/5_Scripts/5-3_GamePlay/Combat/Mod_ColdWeapon.cs`。
- 伤害模块：`Mod_Damage.cs`、`Mod_Damage_AI.cs`。
- 防御模块：`Mod_Defense.cs`。
- 受伤/死亡动作：`Assets/5_Scripts/5-3_GamePlay/Combat/DamageReciverAction/`。
- 战利品：`Assets/5_Scripts/5-3_GamePlay/Combat/LootEntry.cs`。
- 战斗音频：`Assets/5_Scripts/5-3_GamePlay/Combat/CombatAudioRouter.cs`。
- 武器 Prefab：`Assets/2_Prefabs/Weapon/`。
- 矿物 Prefab：`Assets/2_Prefabs/Mine/`；地下矿洞生成与入口见 `flatworld-dimension`。

## Buff 文件与资源

- 定义：`Assets/5_Scripts/5-3_GamePlay/Buff/BuffData.cs`。
- 运行时：`Assets/5_Scripts/5-3_GamePlay/Buff/BuffRunTime.cs`。
- 动作：`Assets/5_Scripts/5-3_GamePlay/Buff/BuffAction*.cs`。
- 资源：`Assets/4_ScriptObjects/4-2_Buff/`。

## 技能文件与资源

- 定义基类：`Assets/5_Scripts/5-3_GamePlay/Skill/BaseSkill.cs`。
- 动作基类：`Assets/5_Scripts/5-3_GamePlay/Skill/BaseSkillAction.cs`。
- 运行时：`Assets/5_Scripts/5-3_GamePlay/Skill/RuntimeSkill.cs`。
- 具体技能：`Assets/5_Scripts/5-3_GamePlay/Skill/Skill_*.cs`。
- 资源：`Assets/4_ScriptObjects/4-3_Skill/`、`Assets/2_Prefabs/Skills/`。

## 边界约束

- `DamageReceiver` 是生命值权威模块；远程网络应用只刷新数值/表现，不在客户端重复结算伤害或死亡。
- 旧 `Mod_HealthPoints` 已删除；禁止重新建立第二套生命值模块，生命、死亡与通用战利品继续统一走 `DamageReceiver`。
- Buff ID 是持久化键；修改命名或叠加策略需考虑旧存档。
- 技能定义由 `GameRes.SkillDict` 注册，资源移动要检查 Addressables `Skill` 标签。
- 伤害死亡回调可能生成 Item、播放音效和更新 UI，修改时检查这些订阅者。
- `DamageReceiver.DeathStarted` 是带接收器参数的真实死亡信号；未被外部消费的死亡最终必须走 `ItemMgr.DespawnItem(item, saveData:false)`，同步清理运行时索引和 Chunk 存档，禁止直接 `Destroy` 遗留幽灵注册。
- 玩家死亡掉落规则由 `Assets/5_Scripts/5-3_GamePlay/Difficulty/GameDifficulty.cs` 的 `GameDifficultyService.Current.PlayerDeath` 统一提供；`Mod_PlayerDeathState` 不得直接判断预设枚举。官方预设与新世界自定义规则共享此入口。
- `DamageReceiver.Hurt()` 是直接伤害难度结算点：玩家攻击、生物攻击和非玩家实体等效生命倍率统一由 `GameDifficultyService.ResolveDirectDamageMultiplier()` 计算；倍率为 0 时不得触发最低 1 点伤害或装备耐久损耗。
- `DamageReceiver.ForceHurt()` 与 `Heal()` 分别承接玩家环境伤害倍率和治疗倍率；`DamageReceiver.DropLoot()`、`DamageReciver_Action_SpawnItem` 统一应用世界战利品数量倍率。
- `Mine_Coal/Copper/Tin/Iron` 的 `DamageReceiver.Data.LootTable` 只包含对应 `Ore_*` 与伴生 `Ore_Stone`，`Mine_Stone` 只掉落 `Ore_Stone`；禁止保留模板继承的 `Chicken` 掉落。
- 这些嵌套 Prefab 含项目自定义 `DamageType` 数据，编辑器工具不得用 `PrefabUtility.SaveAsPrefabAsset()` 全量重存受伤模块；优先使用 Inspector override 或精确属性修改，并检查 Console。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-07-31：为地下矿洞修正煤、铜、锡、铁、石矿的确定性矿石掉落，移除继承模板的非矿物掉落。
- 2026-07-30：删除无引用的旧 `Mod_HealthPoints`；清理 `DamageReceiver.DropLoot()` 过时 TODO，并在调用掉落行为前过滤实例化失败结果。
- 2026-07-29：统一内容校验器递归扫描 Prefab 与 ScriptableObject 中的 `LootEntry`/旧 `LootData`，报告战利品 ID、Prefab 对应关系、概率、数量范围和丢失引用。
- 2026-07-29：自定义难度接入玩家伤害、生物伤害、生物等效生命、环境伤害、治疗和战利品数量；统一从 `GameDifficultyService` 读取，禁止发送端重复乘算。
- 2026-07-29：难度目录增加新世界自定义类型；玩家死亡掉落继续通过 `GameDifficultyService` 统一读取，支持预设与自定义规则共用结算链。
- 2026-07-29：死亡销毁统一接入 `ItemMgr` 注销链，并增加生态补位使用的 `DeathStarted` 事件。
- 2026-07-27：战斗网络边界明确为本地权威结算、远端仅应用模块数据与表现。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Combat/CombatSmokeTests.cs`；当前基础覆盖伤害接收、Buff、技能管理和武器 Prefab 入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；战斗测试约定目录：`Assets/GameTest/Combat/`；场景目录：`Assets/GameTest/Scenes/Combat/`；冒烟分类：`Combat.Smoke`。
- 新增伤害、Buff、死亡、掉落、武器或技能行为时必须增加系统测试；修复 Bug 时先增加回归测试。攻击到受伤、死亡与掉落主流程变化时同步更新战斗冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；伤害随机项必须固定输入，死亡和掉落事件必须验证不会重复触发。
- 完成修改后检查 Unity 编译和 Console，再运行 `Combat.Smoke`；涉及 Item/Module、存档、AI、音频或联机时同步运行对应系统测试。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。
- 五种矿物掉落契约由 `Assets/GameTest/Dimension/DimensionSmokeTests.cs`（`Dimension.Smoke`）补充覆盖。

## 修改后维护本 Skill

新增伤害接口、身体部位版本、Buff 类型、技能资源、掉落动作、Prefab 路径或网络结算边界后，必须更新本 Skill；音效与 UI 路径变化也同步更新对应 Skill。
