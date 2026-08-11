---
name: flatworld-combat
description: "Use when: 定位或修改 FlatWorld 的伤害、生命值、身体部位、死亡、战利品、武器、防御、技能定义与施放。关键词：DamageReceiver、IDamageSender、Mod_ColdWeapon、Mod_Defense、LootEntry、Mod_SkillManager。"
---

# FlatWorld 战斗与技能定位

> 最后核对：2026-08-10。

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

## 高耦合联动
只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；武器数值、动画或特效等局部变化不要扩散到无关系统。
| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| `DamageReceiver.Hurt/ForceHurt/Heal`、最大生命或真实伤害语义 | `flatworld-buff`；涉及环境倍率时再加载 `flatworld-environment` | Buff 与环境效果只调用权威结算入口，不重复乘算或触发最低伤害 | `Buff.Smoke`；环境倍率变化时追加 `Environment.Smoke` |

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-12：修复武器连续命中后切割特效消失：`SlicingEffect` 从对象池取出时恢复 Prefab 初始旋转并重置 Animator，`Mod_Damage.AttackEffects` 引用与伤害结算不变。
- 2026-08-12：Chicken、Wolf、WildBoar 的 `DamageReceiver.Data.LootTable` 新增 100% 掉落的 `Bone`，数量分别为固定 `1`、随机 `1～3`、随机 `1～5`；保留原肉类掉落，并新增 `Combat.Smoke` Prefab 回归断言。
- 2026-08-10：修复矿洞死亡仍在地下同坐标复活：`Mod_PlayerDeathState` 现在解析主世界地表地址，并通过维度事务重建地表玩家；生命与状态通过保存前回调先恢复，避免 0 血状态被首次 `SavePlayer()` 写回。
- 2026-08-10：幽灵接触伤害移除专用 `GhostContactDamage`，直接复用斧头使用的通用 `Mod_Damage`；根节点 `BoxCollider2D` 作为伤害碰撞体，伤害 `20`、间隔 `1s`，统一进入 `DamageReceiver.Hurt()` 结算，并新增 `AI.Smoke` Prefab 回归断言。
- 2026-08-09：修复幽灵受伤不闪红：幽灵 Prefab 增加 `ActorRenderEffectController` 与 `ActorRenderColorEffect`，`DamageReceiver` 现在能找到统一 MPB 受击模块；伤害结算和死亡边界不变。

## 修改后自动测试
- 基础测试脚本：`Assets/GameTest/Combat/CombatSmokeTests.cs`；当前基础覆盖伤害接收、零权重身体部位的残血回退、受击减速与恢复、技能管理和武器 Prefab 入口。
- AI 攻击窗口首击重叠扫描回归位于 `Assets/GameTest/AI/AISmokeTests.cs`（`AI.Smoke`），仅断言 `Mod_Damage_AI` 进入补查路径，普通武器模块不进入。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；战斗测试约定目录：`Assets/GameTest/Combat/`；场景目录：`Assets/GameTest/Scenes/Combat/`；冒烟分类：`Combat.Smoke`。
- 新增伤害、死亡、掉落、武器或技能行为时必须增加系统测试；修复 Bug 时先增加回归测试。攻击到受伤、死亡与掉落主流程变化时同步更新战斗冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；伤害随机项必须固定输入，死亡和掉落事件必须验证不会重复触发。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Combat.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类；只有攻击特效或界面观感变化才做定向截图。

## 修改后维护本 Skill
新增伤害接口、身体部位版本、技能资源、掉落动作、Prefab 路径或网络结算边界后，必须更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。
