# 战斗系统

## 系统定位

负责生命值、受击、四类伤害、防御、死亡、战利品、近战/远程伤害发送器和技能伤害结算。

## 当前机制

- `DamageReceiver` 是生命、受伤、死亡和通用战利品的唯一权威。
- 普通攻击、环境伤害和身体部位伤害最终都进入统一伤害结算。
- 当前伤害分为：`Cutting`、`Piercing`、`Chopping`、`Blunt`。
- 有效结算中的 `0` 表示被完全抵消，仍可播放命中反馈；负值表示无效结算，不触发普通命中特效。
- 死亡必须只结算一次，重复回调、延迟销毁和权威结果重放不能重复掉落。
- 死亡 Loot 由顶层 `lootTableId` 引用 `GameConfig/LootTables/loot-tables.json`。
- 武器实际伤害窗口由 `Mod_Damage` 负责；攻击动画只负责开关已经存在的伤害盒。

## 武器与体力

- 动画近战/工具每一段 `StartAttack` 都通过玩家 `Mod_Stamina.TryConsumeStamina` 扣体力。
- 每件武器基础体力成本来自 Item JSON 的动画模块参数，继续受难度倍率影响。
- 弓使用 `Mod_Bow` 蓄力，拉弓期间按秒消耗体力。
- 箭矢由 `Mod_Projectile + Mod_Damage` 组合；飞行与回收属于 Projectile，伤害仍由 Damage 模块统一处理。

## 关键入口

- `Assets/5_Scripts/5-3_GamePlay/Entities/Combat/`
- `DamageReceiver.cs`
- `Mod_Damage.cs`
- `Mod_Defense.cs`
- `Entities/Skill/`
- Loot：`Assets/StreamingAssets/GameConfig/LootTables/loot-tables.json`

## 关键边界

- 不恢复第二套 Health 模块。
- Buff 附加效果通过独立状态处理器消费伤害结果，不硬编码到 `Mod_Damage`。
- DamageSender / DamageReceiver 使用专用 Trigger 层，不与普通阻挡/拾取碰撞层混用。
- 远程副本只应用权威伤害结果，不再次计算伤害或掉落。

## 修改时联动

- Buff：命中附加状态、出血、中毒。
- 装备：防御效果。
- 生存：体力、濒死、环境伤害。
- Item：武器数值与 Loot ID。

## 对应 Skill

`.agents/skills/flatworld-combat/SKILL.md`
