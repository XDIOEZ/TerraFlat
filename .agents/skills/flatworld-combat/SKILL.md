---
name: flatworld-combat
description: "Use when: 定位或修改 FlatWorld 的伤害、生命值、身体部位、死亡、战利品、武器、防御、技能定义与施放。关键词：DamageReceiver、IDamageSender、Mod_ColdWeapon、Mod_Defense、LootEntry、Mod_SkillManager。"
---

# FlatWorld 战斗与技能

## 入口

- 结算：`Assets/5_Scripts/5-3_GamePlay/Entities/Combat/{DamageReceiver,IDamageSender,Mod_Damage,Mod_Damage_AI,Mod_ColdWeapon,Mod_Defense}.cs`
- 技能：`Entities/Skill/{Mod_SkillManager,BaseSkill,BaseSkillAction,RuntimeSkill,Skill_*}.cs`
- 资源：`Assets/4_ScriptObjects/4-3_Skill/`、`Assets/2_Prefabs/Skills/`
- 受击表现：`Assets/5_Scripts/5-3_GamePlay/Presentation/ActorRenderColorEffect.cs`

## 不变量

- `DamageReceiver` 是生命、受伤、死亡与通用战利品的唯一权威；不要恢复第二套 Health 模块。
- 客户端远程副本只应用权威结果，不重复计算伤害、死亡或掉落。
- 管理员无敌通过监听伤害回满并在死亡回调兜底，不改写权威结算；关闭后必须解除监听。
- 技能由 `GameRes.SkillDict` 注册；移动资源同时检查 Addressables `Skill` 标签。
- Buff 生命周期属于 `flatworld-buff`；伤害 API 语义变化才联动 Buff/Environment，局部数值与表现无需扩散。
- 正式 AI 的生命、防御、攻击伤害、伤害碰撞窗静态值来自 Actor JSON modules；当前生命和攻击者等运行态仍由存档/模块维护。

## 验证

- 覆盖攻击→受伤→死亡→掉落，确认事件只触发一次、随机输入固定、池化特效每次重置。
- 默认不主动跑测试；需要时运行 `Combat.Smoke`，AI 攻击专项同时看 `AI.Smoke`。
- 测试入口：`Assets/GameTest/Combat/CombatSmokeTests.cs`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
