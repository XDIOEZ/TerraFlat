---
name: flatworld-combat
description: "FlatWorld 伤害与生命结算、死亡掉落、武器窗口、投射物、命中状态、技能及 GO/ECS 战斗边界。"
---

# 战斗与技能

## 入口

代码根目录 `Assets/5_Scripts/`：

| 目录/文件 | 职责 |
| --- | --- |
| `5-3_GamePlay/Entities/Combat/` | `DamageReceiver/IDamageSender/Mod_Damage/Mod_Damage_AI/Mod_Defense`，动画武器、弓与投射物 |
| `Shared/Combat/CombatContext.cs` | 纯伤害上下文与 `CombatRules` |
| `5-3_GamePlay/Entities/Skill/` | `Mod_SkillManager/BaseSkill/BaseSkillAction/RuntimeSkill/Skill_*` |
| `5-3_GamePlay/Presentation/ActorRenderColorEffect.cs` | 受击颜色表现 |

内容：`Assets/StreamingAssets/GameConfig/LootTables/loot-tables.json`；技能 SO/Prefab：`Assets/4_ScriptObjects/Gameplay/Skills/`、`Assets/2_Prefabs/Gameplay/Skills/`。技能由 GameRes.SkillDict 注册，Addressables 标签为 Skill。

## 结算与生命周期

- GO 的 DamageReceiver、原生 ECS 的 AiecsDamageSettlementSystem 各为目标唯一生命权威，外部代理 HP 是快照；远程 Client 仅应用权威结果。
- ResolveDamage 先提交钳制 HP 和实际损失快照，再发回调；同步/死亡收尾/解锁保证执行。死亡在通知前锁存一次，重入/延迟结果不重复掉落；Unload 取消延迟销毁，显式复活/新 Load 重置死亡标记。
- GO 世界血条在同轮实体内首次创建、隐藏停用并复用；连续受伤仅续一个隐藏协程的截止时间。Unload、回池、销毁释放血条和监听，新 Load 不复用旧代际面板；隐藏血条不刷新文本或布局。
- 致命扣血在回调前把 `DeathCauseRecord` 锁存到生命 Data，随血量保存/同步，重生清除；`ForceHurt` 的来源方传稳定原因 ID，特殊发送端用 `IDeathCauseSource`，投射物用 `IDeathCauseSnapshotSource` 冻结射手和发射武器。Shared Context 只携固定容量定义 ID，不让叙事字段溢出阻断伤害。
- Heal 查询 IHealingCostPolicy 可恢复额度，按实际恢复量支付，无策略免费；普通回血不复活，启用部位生命时只分配给存活部位，重生/满血赋值才恢复耗尽部位。
- 掉落以稳定物品 ID 定义，实体顶层 lootTableId 展开到唯一接收器；LootPrefabName 实为 Item.itemData.IDName，非外壳名。引用在目录加载时校验。
- `ResourceHarvestHitBonus` 在玩家匹配工具且实际正伤害提交后独立抽取 5% 单件奖励；GO 读取 weakTool/资源模块，原生资源复用本次专精倍率，均由权威端调用。按目标掉落表的正概率加权选一项，零数量/空 ID 排除；致命命中仍可中奖，不扣最终掉落、不乘产量倍率。规则回归 `combat/harvest_hit_bonus` 位于 `external-rules.lua`。
- LootEntry.OnValidate 只在引用数据就绪时同步 ID；导入/构建中的 Prefab 未就绪时保留原值，不在此抛缺失 ID 异常。
- 掉落数量共用 `LootAmountRules`，具体范围和分布以掉落表配置为准；GO/自然 ECS/动物 ECS 都调用 `RollAmount`，再应用一次既有产出倍率。回归见 `combat/loot_amount` 与 `combat/tree_leaf_configuration`。
- 非均匀数量通过 `DiscreteTable/amountProbabilityTableId` 引用公共离散概率表；树叶引用 `loot.tree.leaf.amount`，GO 参数展开须保留表 ID，难度只在 `DiscreteProbabilityConfiguration.Modifying` 修正独立状态。`combat/discrete_probability` 覆盖原表/结构体复制隔离、最终百分比、抽样边界及冻结指纹。
- Actor JSON modules 是生命/防御/攻击/窗口静态真源，当前 HP/攻击者由运行模块保存；管理员无敌监听伤害回满及死亡兜底。
- 临时无敌由调用方持有 `AcquireTemporaryInvulnerability()` 的 `IDisposable` 租约，多来源独立释放；普通/上下文/环境/生存/结构损伤均拦截且不耗受击冷却，卸载、停用、死亡和回池作废旧租约，直接权威设置生命仍生效。
- Physical 是统一物理攻击/防御，ImpactKind 决定表现与刃伤资格；ResolvePhysical 合计攻击、只扣一次防御。工具弱点防御后乘 2，小刀对低矮作物为 1.5；未破防仍 0，Mod_ResourceHarvest 或生命 weakTool 单处声明。装备材质耐久由 `EquipmentDurabilityRules` 读取 `Resources/Config/Equipment/durability-materials.json` 在最终定义模板建立；未知材质保留定义值，黑曜石50仅适用武器。攻击装备在几何与逻辑命中确认后、伤害结算前每目标扣一点；逻辑未命中不扣，零伤或伤害拒绝仍扣。投射物冻结发射武器数据，在有效命中时磨损，再发布可回收回调。非攻击装备只定义上限，不新增磨损来源。
- 战斗只读 `GameRuleConfiguration.Current.CreatureCombat`，纯值后端通过 `CombatTuning` 冻结参数；攻击修正在防御前，`CreatureCombatRuleSet.ResolveReceivedDamageMultiplier` 的玩家受击修正在防御后（含 ECS 转交命中），环境伤害、玩家攻击和非玩家目标不应用该受击修正。
- 玩家死亡模块通过 `DropAllCarriedItems/SetDropAllCarriedItems/ClearDropAllCarriedItemsOverride` 接收来源无关的机制开关；角色覆盖随本模块保存，世界默认刷新不抹掉覆盖，卸载清运行缓存，新 Load 从自身载荷重建。回归 `combat/death_rule_override`。
- 中立环境物理伤害由 HurtEnvironmentalDamage 与 IGameplayEnvironmentalDamageBridge 接入正式结算，忽略阵营和普通攻击间隔，也不消费普通受击冷却，仍保留防御/部位/死亡掉落；雷击服务按世界、区块和序号去重，GO、原生动物/资源及建筑分别走自身生命权威。
- 返回 0 是有效但全防御命中，可显示数字/附加状态；负值 -1 是死亡/冷却等拒绝，不触发表现或状态。ResolvedDamageValues 已应用难度、防御/倍率，按 DamageValue 裁过量。
- 出血经 DamageReceivedStatusEffectRegistry 消费同一笔 DamageReceiverDamageInfo，要求 Blood 标签和实际切割/穿刺/劈砍损失；部位提示读同笔 BodyPartHits，不重新抽样。玩家受击表现由接收侧订阅保证。
- 濒死 HP 不再被被动恢复改成小正数；控制/UI 在本地 Event_PlayerEnterWorld（模块已加载）按权威血量恢复，不重发 OnDead。
- 太空死亡由 Mod_PlayerDeathState 读取实际 SpaceScene，回 earth 对应地表的固定主出生点；SpaceSession.Respawn 在完整维度事务期间解除驾驶/支撑/下降并锁住迁移，成功清乘员、失败恢复，不能只改玩家显示位置或使用最近地表 ActiveAddress 判断当前场景。

## 武器、命中与组合

- GO 生物几何接触后的逻辑命中由 `CreatureHitChanceRules` 计算：攻击者生命模块 `AccuracyLevel` (0–10) ×10%，目标 `SizeLevel` (0–10) ×10%，再乘武器与体力/血量修正；`DodgeChance` 和每个已加载 `ICreatureDodgeChanceSource` 按 `(1-闪避率)` 逐项乘算（单项最多95%），绝不加算或保底覆盖0级。默认玩家命中7/体型5，蜜蜂体型2（无额外技能闪避时7级命中14%）；不改变飞行贴图受击盒或资源攻击。Lua `combat/logical_hit_levels` 在 `external-rules.lua`。
- `Mod_BiteAttack` 是可组合的 `IAnimalCombatSkill`，通过 `IAnimalAttackRangeSource` 读取默认范围，不识别具体物种。仅比较 `Stack.Weight` 身体质量，目标至少为发动者一半时挂咬，否则拖行；3/6/9秒结算物理伤害，冷却180秒随模块快照保存。`ActorActionConstraints` 来源租约管理普攻禁止、乘法减速叠加与唯一跟随，双方死亡/卸载/代际变化/运动重置会释放；`core:bite_*` 为非持久来源标识。`IPeriodicDamageSource` 独立结算周期命中，不占普通受击冷却，防御、阵营和死亡仍走正式入口。
- 联机玩家强制跟随及减速由 `NetworkWorldPlayer.ActionConstraints` 同步，服务器用同一跟随位置覆盖客户端移动提交，B目标不能翻滚挣脱。`combat/bite_attack` 覆盖质量边界、配置、减速/攻击租约和冷却快照；真实9秒时序、碰撞、多人叠加与断线/切世界仍需现场回归。

- 技能模块通过所属 `Item.OnAct` 接收使用并在 Unload 解绑，不直接订阅玩家右键；快捷栏独占手持物使用入口。
- Mod_Damage 管统一发送/窗口，工具由 Mod_ResourceTool/Mod_Shovel 等通过 ICombatDamageContextModifier 附上下文；动作表现经 IWeaponActionAnimation，概率状态由独立 DamageOnHitBuffApplier 组合。
- 需等动画结束结算的工具用 IWeaponActionCompletionAnimation；回调 true 才提交，换手、停用、卸载或动作取消回调 false。完成按 Animator 实际进度判断，不用固定延时替代。
- 动画武器不读取全局鼠标或旧 Input；攻击/长按由持有者 Mod_GameController 的中央语义驱动，工具/AI 复用公开动作接口。落地武器 Load 后也不获得输入权，联机假人拾取专项持续加载真实燧石刀覆盖该路径。
- 动画近战物品的 JSON 必须显式组合 `damage` 与 `animation`；只声明伤害会使外壳内的动作模块在装配时被清退，已有 Animator 不能代替动作模块。
- JSON 模块装配后 IItemModuleDependencyBinder 按稳定 ID 绑定；武器 Mod_Damage 的节点层级须匹配 Animator 伤害窗口曲线，不能随意挪动。修饰器从已注册 ItemMods 缓存并校验 FixedList 容量，多伤害盒合法，聚合器排除所有其它 Mod_Damage，避免互调。
- 武器自身 Source 与 Owner Credit 分开，防自伤在候选/扫描/结算排 Owner，不改 IDamageSender.attacker；上下文携 generation/world/dimension 和 double 模拟 Tick/Time。
- 太空联机经 `SpaceSession.CombatNetworking` 校验真实连接玩家并驱动原武器输入、体力及弹药事务；`ShipCombatBridge` 在攻击者规范坐标系查询船体和乘员，远端船不依赖 Host 视野。太空 `Mod_Projectile` 不参与地表 Physics2D，固定规范坐标扫掠命中，回收交 `SpaceLooseItemState`；客户端投射物仅复制表现。
- 窗口打开主动扫已重叠目标；动画武器在 Animator 后 LateUpdate 逐帧采样，窗口预约集跨帧保留、关窗清空，DamageInterval 不限制姿态采样；持续伤害用 Pulse。默认 MaxAttackTargets=3，可被 Prefab/JSON 覆盖。
- AttackShape2D 只描述范围；空间桶/身份/阵营/LOS 筛候选，临时受击 Collider 经 Physics2D Overlap/BoxCast 判几何。两个后端共用配额/预约，Sequence/Window/Pulse 在生产处唯一。
- 机械武器 Pulse 的候选/去重/命中列表通过 Unity CollectionPool 租约在调用结束归还，支持重入；命中先按扫掠时间再按到接触点的距离排序，预先计算距离并复用静态比较器，不在排序时捕获当前攻击中心。分段 Marker 为 `FlatWorld.Combat.MachineCandidates/MachinePhysicsQuery/MachineApplyHits`；规则 `mechanical/machine_pulse_order`。
- DamageSender/DamageReciver 是专用 Trigger 层对，只互相接触；DamageReceiver 自带同节点专用 Trigger，身体/拾取阻挡分开。Tile/建筑走显式空间查询。一般组件解析可回最近 Item 根查子树，命中特效优先实际回调 Collider。
- 动画武器伤害盒跟随实际 Sprite 局部姿态/flip，用物理轮廓或紧致网格，缺失才 bounds；TightSprite 默认，右上工具可配 UpperRightQuadrant。
- Mod_Weapon_AnimationAction 每段 StartAttack 调持有者 Stamina.TryConsumeStamina，不足不开始；配置 staminaCostPerAttack 受统一角色体力参数修正。Mod_ColdWeapon 仍按 ColdWeaponStaminaObserver.StaminaConsumeSpeed 每秒扣，基础与 StaminaConsumeSpeedRate 是不同参数。
- CombatImpactEffectSet 是类型特效，AttackEffects 是每次反馈；DominantKind 统一选类型，空反馈不阻断命中。JSON 不覆盖 Prefab Unity 资源引用集合。
- 动画武器通过正伤害回执局部卡肉，`hitStopDuration` 默认0.12秒、每段挥击最多一次；独立动画时钟扣除停顿，不改 Time.timeScale，停用/卸载恢复当前攻速。`HitShakeDuration/HitShakeDistance` 默认0.16秒/0.035米，GO 经 `ActorRenderHitShakeEffect` 合成 Shader 位移并外扩裁剪边界，原生资源仅在提交显示矩阵时抖动，均不改碰撞/占格/掉落位置。规则入口 `combat/hit_feedback_clock`、`combat/hit_shake_render`；可选 AIECS 动物和联机远端表现未覆盖。
- On-hit 的 applicationStacks/CombatOnHitBuff.Stacks 一次传完整层数并原子施加，GO 回调与纯数据各应用一次，Tile 不发实体命中事件。

## 弓、投射物与飞行

- Mod_Bow 从 AttackStarted/Ended 驱动 `ProjectileChargeCycle`；默认松开发射并持续耗体力，`ReleaseMode=LoadThenReleaseToFire` 满蓄力后停耗、首次松手上膛、再次按下松开发射，未满松手取消，取消/卸载清时序。来源 `flatworld.combat.bow_charge`；木弓/反曲弓基础每秒2.25，弩 `Crossbow_Wood` 继承反曲弓数值与普通箭。弹药经同库存事务，AmmoItemId 与 AmmoTag 任一匹配；箭组合 Mod_Projectile+Mod_Damage。规则回归 `combat/archery_cycle`。
- 弓伤害倍率经 Launch 传投射物；IProjectileChargeModifier 提供额外速度/伤害，蓄力结束收状态。麦克风修饰只由本地玩家蓄力期间拥有，不存档。
- 投石索复用 Mod_Bow，以 AmmoItemId 或 AmmoTag 任一匹配选弹；石头与硬质矿石通过 SlingAmmo_Base 组合 Mod_Projectile+Mod_Damage，石头保留独立丢弃飞行伤害。不要给石头子定义统一增加 SlingAmmo 标签，以免黏土/硝石继承弹药资格；`combat/archery_cycle` 覆盖最终模块与配置边界。
- 默认箭矢/投掷物从手持武器根位置沿瞄准方向前移生成；配置局部管口的武器使用真实管口，发射与轨迹预览共用位置。`TrajectorySettings` 从实际弹药定义/Prefab 缓存，资源变更失效；预览与 UseVisibleArc 使用同速度/时长/虚拟重力，匀速地面位移，落点是无碰撞预计终点。
- Dynamic Rigidbody2D 驱动地面，虚拟高度只偏移视觉/攻击盒。高速飞行用实际伤害盒作前后帧 NonAlloc 扫掠，再交 Mod_Damage 结算；实体接触先判伤，正伤正常命中/回收/附着，只有有效 0 伤才反弹。
- AirborneOnly 停用地面实体碰撞，落地物理步前补最近接触判伤；逻辑闪避不重抽，本次飞行排其实体碰撞，回收/卸载/重发撤临时排除。反弹旋转跟刚体速度衰减至停止。
- 自然树 HitBounds 覆盖 BodyBounds；ECS 障碍 Collider 经 GameplayCombatBridge 绑定原生 Record，投射物传实际 Collider/接触点，近战仍用独立投影，两路共 ApplyWeaponHit/预约。回池解绑，镜像继承目标，身份变化递增投影版本；物理已反弹的零伤回执只补表现。
- 可回收箭保持独立 Runtime Item，以目标 Item 根局部姿态附着并 NotifyRuntimeItemMoved，目标失效保留最后世界位。损坏材料从普通配方 ExactItem 按用量加权取一份，无法确定实际 Tag 材料则无回收。
- 命中硬目标变成别物品由具体 Mod_DiscardFlightDamage 参数启用，不由通用投射物按材质 Tag 推断。
- Mod_DiscardFlightDamage 在真实轨迹结束后发布 FlightCompleted，HitTarget 与 DealtDamage 分开：有效零伤命中算前者，闪避/负数拒绝不算。雪球组合该模块与1点钝击，50%碎裂由 Mod_Snowball 决策；只有未命中碎裂才给落点加一层雪，不把雪球规则写进飞行伤害模块。回归 `combat/snowball_throw`。

## 原生 ECS 与接触伤害

- Hurt(IDamageSender) 适配同一提交核心；Hurt(CombatDamageContext) 使用明确 Source/Credit，旧 Attacker 为空不等于无来源。专属 incoming rule 未提供纯实现时显式拒绝。
- Native 阵营先校验字符/UTF-8 容量再解码，坏值返回 -1、不耗冷却并限频诊断，不截断或中断整轮。
- `FactionRelationService` 以不区分顺序和大小写的值类型阵营对查询外部关系，避免高频查询拼接字符串；空阵营、同阵营、Neutral 默认与 MOD 覆盖优先级不变，回归 `combat/faction_relation_lookup`。
- Mod_ContactDamage/ContactDamageRuntime 共用真实身体查询与完整身份限频；资源定义由 NaturalEntityEcsProfileCompiler 编译。空中目标/Client 不结算，移除/死亡/卸载/停用撤来源，冷却不保存。
- Engagement Slot 只限制目标级同时接敌，起手取资格，已进入动作沿原时序；最终距离/方向/LOS/阵营仍独立判定。
- 同目标命中按时间/攻击键串行，不同目标并行；死亡先锁存、尸体时间队列批回收、掉落按预算消费。原生后端尚未迁移完整投射物扫掠/附着、技能和生态掉落修饰。

## 木盾与副手

- `Mod_Shield` 提供盾牌能力与伤害窗，`Mod_ShieldCombat` 组合角色输入、装备、防御和速度；`ShieldActionClock` 负责单击/长按/冷却互斥。副手槽稳定 ID `OffHand`，标签 `EquipmentSlot.OffHand`。
- 左键短按盾击（动作中 +5 防御），长按举盾（+10、移速 ×0.5）；只有真实副手盾牌允许 `Parry` 输入（默认 Q，短窗 +10，不是无敌）。配置在 `shells/shields.json`，不能加被动护甲代替动作防御。
- 临时防御用 `AcquireTemporaryDefense`，不改存档基础防御；普通/部位结算均消费有效防御。客户端 `OffhandParry` 走现有战斗权威交接，`NetworkWorldPlayer.Shield` 只复制表现。协议版本 25。
- 独立验证：`python Tools/Tests/ShieldActions/verify.py --build`；正式 Lua：`Systems/Combat/shield-rules.lua`。Unity 播放中不刷新或运行场景测试。
