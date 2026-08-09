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
- 受击表现：`DamageReceiver.cs` 触发 `Assets/5_Scripts/5-3_GamePlay/Presentation/ActorRenderColorEffect.cs`，由角色渲染控制器统一提交 MPB。
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
- 管理员无敌不改写 `DamageReceiver` 的权威结算：`PlayerAdminController` 仅在管理员无敌开启时监听 `OnDamageReceived` 回满生命，`Mod_PlayerDeathState` 作为致死回调兜底拦截濒死；关闭开关后不得残留伤害监听效果。
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

- 2026-08-09：野猪独立 `AttackTrigger_AI` 覆盖改为 `2.2×0.9`、圆角 `0.45` 的横向胶囊伤害触发盒；AI 进入攻击同步按横向 `1.6`、竖向 `0.45` 椭圆判断，避免上下方向空挥。
- 2026-08-09：`AI_AttackController` 在伤害窗口启用时才同步攻击事件状态；野猪起手首帧继续保持 `IsAttacking=false`，避免首击发生额外攻击开始/停止脉冲，伤害窗口参数不变。
- 2026-08-09：通用武器动画模块在载入与回到待机时强制关闭命中碰撞体；`Axe.prefab` 修正伤害模块错误的 `m_Enabled=1` 覆盖，斧头及同类手持物只会在攻击动画命中帧内结算伤害。
- 2026-08-09：管理员无敌默认改为关闭，运行时重置同样保持关闭；默认名为“管理员”的 Player Prefab 现在可正常受伤和死亡，仍可由管理员开关主动开启无敌。
- 2026-08-09：野猪 `Attack.anim` 禁用循环并延长至一次伤害起手到下一次起手的完整 `2.18s` 周期；`0.06s` 开始、`0.12s` 持续的伤害窗口和前冲峰值保持不变，避免冷却中重复播放攻击表演。
- 2026-08-09：修复身体部位生命的零权重残血软锁：随机命中无候选时回退结算仍存活部位，确保生命可降至 0 并进入既有死亡流程；`Combat.Smoke` 增加回归覆盖。
- 2026-08-09：管理员无敌改为 `PlayerAdminController` 可切换运行时状态；开启时即时恢复受击生命并拦截濒死，关闭后完整恢复普通伤害、环境伤害与死亡流程。
- 2026-08-08：`DamageReceiver` 的受击闪白改由角色渲染 MPB 模块驱动，连续命中震动可重触发且不再创建材质实例。
- 2026-08-08：野猪 `Attack.anim` 增加 `0→0.22→0` 的局部前冲位置曲线，峰值覆盖 `0.2s` 伤害窗口，与 `AttackTrigger_AI` 的前方判定盒保持一致。
- 2026-08-08：野猪 `AttackTrigger_AI` 的伤害触发盒调整为 `1.6×1.6`，并与 AI 攻击距离 `1.4` 对齐，减少攻击动画已播放但玩家未进入伤害碰撞体的情况。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Combat/CombatSmokeTests.cs`；当前基础覆盖伤害接收、零权重身体部位的残血回退、受击减速与恢复、技能管理和武器 Prefab 入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；战斗测试约定目录：`Assets/GameTest/Combat/`；场景目录：`Assets/GameTest/Scenes/Combat/`；冒烟分类：`Combat.Smoke`。
- 新增伤害、死亡、掉落、武器或技能行为时必须增加系统测试；修复 Bug 时先增加回归测试。攻击到受伤、死亡与掉落主流程变化时同步更新战斗冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；伤害随机项必须固定输入，死亡和掉落事件必须验证不会重复触发。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Combat.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类；只有攻击特效或界面观感变化才做定向截图。
- 管理员无敌的真实伤害/死亡回归由 `FlatWorldGoldenPathScenarios.PlayerMovement.cs` 在 `OnWorldReady` 通过 `DamageReceiver.ForceHurt()` 覆盖；Cleanup 必须恢复生命、玩家名与无敌开关。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。
- 战斗精简 Smoke 位于 `Assets/GameTest/Combat/CombatSmokeTests.cs`（`Combat.Smoke`），保留受击减速与恢复这一关键行为；矿物掉落细节不再属于 Smoke 集合。

## 修改后维护本 Skill

新增伤害接口、身体部位版本、技能资源、掉落动作、Prefab 路径或网络结算边界后，必须更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。
