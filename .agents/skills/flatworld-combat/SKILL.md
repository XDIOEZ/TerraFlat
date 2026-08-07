---
name: flatworld-combat
description: "Use when: 定位或修改 FlatWorld 的伤害、生命值、身体部位、死亡、战利品、武器、防御、技能定义与施放。关键词：DamageReceiver、IDamageSender、Mod_ColdWeapon、Mod_Defense、LootEntry、Mod_SkillManager。"
---

# FlatWorld 战斗与技能定位

> 最后核对：2026-08-07。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Entities/Combat/DamageReceiver.cs`：生命值、身体部位、受伤、死亡、掉落和网络表现边界。
2. `Assets/5_Scripts/5-3_GamePlay/Entities/Combat/IDamageSender.cs`：伤害发送接口。
3. `Assets/5_Scripts/5-3_GamePlay/Entities/Skill/Mod_SkillManager.cs`：技能持有、施放、运行时技能列表。

Buff 定义、生命周期、效果、叠加与存档统一见 `flatworld-buff`，不要在本 Skill 重复维护。

## 战斗文件

- 武器：`Assets/5_Scripts/5-3_GamePlay/Entities/Combat/Mod_ColdWeapon.cs`。
- 伤害模块：`Mod_Damage.cs`、`Mod_Damage_AI.cs`。
- 防御模块：`Mod_Defense.cs`。
- 受伤/死亡动作：`Assets/5_Scripts/5-3_GamePlay/Entities/Combat/DamageReciverAction/`。
- 战利品：`Assets/5_Scripts/5-3_GamePlay/Entities/Combat/LootEntry.cs`。
- 战斗音频：`Assets/5_Scripts/5-3_GamePlay/Entities/Combat/CombatAudioRouter.cs`。
- 武器 Prefab：`Assets/2_Prefabs/Weapon/`。
- 通用武器 Animator：`Assets/8_Animations/Item/Weapon/Weapon_Uni.controller`；由武器与工具 Prefab 通过 GUID 共用，状态名固定为 `Idle_0`、`Attack_1`、`Attack_2`。
- 矿物 Prefab：`Assets/2_Prefabs/Mine/`；地下矿洞生成与入口见 `flatworld-dimension`。

## 技能文件与资源

- 定义基类：`Assets/5_Scripts/5-3_GamePlay/Entities/Skill/BaseSkill.cs`。
- 动作基类：`Assets/5_Scripts/5-3_GamePlay/Entities/Skill/BaseSkillAction.cs`。
- 运行时：`Assets/5_Scripts/5-3_GamePlay/Entities/Skill/RuntimeSkill.cs`。
- 具体技能：`Assets/5_Scripts/5-3_GamePlay/Entities/Skill/Skill_*.cs`。
- 资源：`Assets/4_ScriptObjects/4-3_Skill/`、`Assets/2_Prefabs/Skills/`。

## 边界约束

- `DamageReceiver` 是生命值权威模块；远程网络应用只刷新数值/表现，不在客户端重复结算伤害或死亡。
- 旧 `Mod_HealthPoints` 已删除；禁止重新建立第二套生命值模块，生命、死亡与通用战利品继续统一走 `DamageReceiver`。
- 技能定义由 `GameRes.SkillDict` 注册，资源移动要检查 Addressables `Skill` 标签。
- 伤害死亡回调可能生成 Item、播放音效和更新 UI，修改时检查这些订阅者。
- `DamageReceiver.DeathStarted` 是带接收器参数的真实死亡信号；未被外部消费的死亡最终必须走 `ItemMgr.DespawnItem(item, saveData:false)`，同步清理运行时索引和 Chunk 存档，禁止直接 `Destroy` 遗留幽灵注册。
- 玩家死亡掉落规则由 `Assets/5_Scripts/5-3_GamePlay/Core/Difficulty/GameDifficulty.cs` 的 `GameDifficultyService.Current.PlayerDeath` 统一提供；`Mod_PlayerDeathState` 不得直接判断预设枚举。官方预设与新世界自定义规则共享此入口。
- `DamageReceiver.Hurt()` 是直接伤害难度结算点：玩家攻击、生物攻击和非玩家实体等效生命倍率统一由 `GameDifficultyService.ResolveDirectDamageMultiplier()` 计算；倍率为 0 时不得触发最低 1 点伤害或装备耐久损耗。
- `DamageReceiver.Hurt()` 对实际受伤且存活、拥有 `Mover` 的实体施加受击减速；默认移动倍率 0.5、持续 0.35 秒，连续命中只刷新持续时间而不叠加倍率，环境伤害 `ForceHurt()` 不触发。
- `DamageReceiver.ForceHurt()` 与 `Heal()` 分别承接玩家环境伤害倍率和治疗倍率；`DamageReceiver.DropLoot()`、`DamageReciver_Action_SpawnItem` 统一应用世界战利品数量倍率。
- `Mine_Coal/Copper/Tin/Iron` 的 `DamageReceiver.Data.LootTable` 只包含对应 `Ore_*` 与伴生 `Ore_Stone`，`Mine_Stone` 只掉落 `Ore_Stone`；禁止保留模板继承的 `Chicken` 掉落。
- 这些嵌套 Prefab 含项目自定义 `DamageType` 数据，编辑器工具不得用 `PrefabUtility.SaveAsPrefabAsset()` 全量重存受伤模块；优先使用 Inspector override 或精确属性修改，并检查 Console。

## 高耦合联动

只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；武器数值、动画或特效等局部变化不要扩散到无关系统。

| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| `DamageReceiver.Hurt/ForceHurt/Heal`、最大生命或真实伤害语义 | `flatworld-buff`；涉及环境倍率时再加载 `flatworld-environment` | Buff 与环境效果只调用权威结算入口，不重复乘算或触发最低伤害 | `Buff.Smoke`；环境倍率变化时追加 `Environment.Smoke` |
| `IDamageSender`、`DamageType`、武器命中或 AI 伤害发送接口 | `flatworld-ai` | AI 攻击仍通过统一发送/接收边界，客户端不重复结算 | `AI.Smoke` |
| 死亡事件、Despawn、战利品生成或 Item 清理顺序 | `flatworld-item-module`、`flatworld-data-save` | 死亡只触发一次，ItemMgr 注销、Chunk 差量和掉落状态一致 | `ItemModule.Smoke`、`DataSave.Smoke` |
| 本地/远程伤害应用、死亡表现或权威状态同步 | `flatworld-networking` | 服务端/本地权威结算，远程副本只应用状态与表现 | `Networking.Smoke` |

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-07：将旧 `Assets/9_Anim` 的通用武器 Animator、待机与两段攻击动画归并到 `Assets/8_Animations/Item/Weapon/`，GUID 与武器动画状态契约保持不变。
- 2026-07-31：实体受到直接攻击并存活时临时降低移动速度，连续受击刷新减速时间，结束后恢复原有速度倍率。
- 2026-07-31：为地下矿洞修正煤、铜、锡、铁、石矿的确定性矿石掉落，移除继承模板的非矿物掉落。
- 2026-07-30：删除无引用的旧 `Mod_HealthPoints`；清理 `DamageReceiver.DropLoot()` 过时 TODO，并在调用掉落行为前过滤实例化失败结果。
- 2026-07-29：统一内容校验器递归扫描 Prefab 与 ScriptableObject 中的 `LootEntry`/旧 `LootData`，报告战利品 ID、Prefab 对应关系、概率、数量范围和丢失引用。
- 2026-07-29：自定义难度接入玩家伤害、生物伤害、生物等效生命、环境伤害、治疗和战利品数量；统一从 `GameDifficultyService` 读取，禁止发送端重复乘算。
- 2026-07-29：难度目录增加新世界自定义类型；玩家死亡掉落继续通过 `GameDifficultyService` 统一读取，支持预设与自定义规则共用结算链。
- 2026-07-29：死亡销毁统一接入 `ItemMgr` 注销链，并增加生态补位使用的 `DeathStarted` 事件。
- 2026-07-27：战斗网络边界明确为本地权威结算、远端仅应用模块数据与表现。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Combat/CombatSmokeTests.cs`；当前基础覆盖伤害接收、受击减速与恢复、技能管理和武器 Prefab 入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；战斗测试约定目录：`Assets/GameTest/Combat/`；场景目录：`Assets/GameTest/Scenes/Combat/`；冒烟分类：`Combat.Smoke`。
- 新增伤害、死亡、掉落、武器或技能行为时必须增加系统测试；修复 Bug 时先增加回归测试。攻击到受伤、死亡与掉落主流程变化时同步更新战斗冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；伤害随机项必须固定输入，死亡和掉落事件必须验证不会重复触发。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Combat.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类；只有攻击特效或界面观感变化才做定向截图。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。
- 战斗精简 Smoke 位于 `Assets/GameTest/Combat/CombatSmokeTests.cs`（`Combat.Smoke`），保留受击减速与恢复这一关键行为；矿物掉落细节不再属于 Smoke 集合。

## 修改后维护本 Skill

新增伤害接口、身体部位版本、技能资源、掉落动作、Prefab 路径或网络结算边界后，必须更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。
