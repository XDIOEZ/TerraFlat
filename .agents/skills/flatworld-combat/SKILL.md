---
name: flatworld-combat
description: "Use when: 定位或修改 FlatWorld 的伤害、生命值、身体部位、死亡、战利品、武器、防御、技能定义与施放。关键词：DamageReceiver、IDamageSender、Mod_ColdWeapon、Mod_Defense、LootEntry、Mod_SkillManager。"
---

# FlatWorld 战斗与技能

## 入口

- 结算：`Assets/5_Scripts/5-3_GamePlay/Entities/Combat/{DamageReceiver,IDamageSender,Mod_Damage,Mod_Damage_AI,Mod_ColdWeapon,Mod_Defense}.cs`
- 技能：`Entities/Skill/{Mod_SkillManager,BaseSkill,BaseSkillAction,RuntimeSkill,Skill_*}.cs`
- 资源：`Assets/4_ScriptObjects/Gameplay/Skills/`、`Assets/2_Prefabs/Gameplay/Skills/`
- 受击表现：`Assets/5_Scripts/5-3_GamePlay/Presentation/ActorRenderColorEffect.cs`

## 不变量

- `DamageReceiver` 是生命、受伤、死亡与通用战利品的唯一权威；不要恢复第二套 Health 模块。
- 普通攻击、环境伤害和部位伤害共用 `ResolveDamage`：先提交限定在 `0..MaxHp` 的血量与实际损失快照，再发布回调；部位数值计算不能夹带事件。权威同步、死亡收尾和结算锁释放属于必经生命周期，不能排在可能抛异常的表现/掉落之后而失去执行保证。
- 死亡入口必须在通知观察者前登记一次性状态；重入、延迟销毁和权威结果重复到达不得重放死亡/掉落。普通回血不复活，显式复活或新实例加载才重置死亡标记，Unload 必须取消旧实例的延迟销毁任务。
- `LootEntry.LootPrefabName` 实际保存稳定物品 ID；编辑器选择 Prefab 时取 `Item.itemData.IDName`，禁止取 `GameObject.name`，例如骨头 `Bone` 的通用外壳名是 `Prop`。Item/Actor 目录加载阶段必须校验表引用和模块内嵌掉落，不能等到死亡时才验证生成物身份。
- JSON Item/Actor 的死亡掉落统一在 `GameConfig/LootTables/loot-tables.json` 以稳定 ID 定义，并由实体顶层 `lootTableId` 引用；禁止同时保留内联 `Data.LootTable`，运行时会把表展开到唯一 `DamageReceiver`。
- 客户端远程副本只应用权威结果，不重复计算伤害、死亡或掉落。
- 管理员无敌通过监听伤害回满并在死亡回调兜底，不改写权威结算；关闭后必须解除监听。
- 技能由 `GameRes.SkillDict` 注册；移动资源同时检查 Addressables `Skill` 标签。
- Buff 生命周期属于 `flatworld-buff`；伤害 API 语义变化才联动 Buff/Environment，局部数值与表现无需扩散。
- 正式 AI 的生命、防御、攻击伤害、伤害碰撞窗静态值来自 Actor JSON modules；当前生命和攻击者等运行态仍由存档/模块维护。
- 历史武器/Actor 大量通过 Prefab 或 JSON 继承覆盖旧单值 `Damage`；迁移到四类伤害时只能在最终运行实例 `Load` 后读取合并结果，禁止在 `OnValidate` 提前固化父模板数值。
- 树木、矿物等世界资源的 `DamageReceiver.Data` 会进入世界存档；调整 Prefab 防御时若旧存档也必须生效，要同步提升数据版本并在 `Load` 按稳定物品 ID 迁移，不能只改 Prefab。
- `DamageReceiver` 与实际受击 `Collider2D` 不保证位于同一节点；Collider 还可能位于同一 Item 的兄弟模块。组件解析在当前节点/父级/子级都失败时必须回到最近的 Item 根搜索完整子树；命中特效应优先使用碰撞回调传入的 Collider 定位，并在缺失时回退子级、父级或接收器中心，禁止直接假定 `receiver.GetComponent<Collider2D>()` 非空。
- ItemDefinition 的模块 JSON 不应写入 `AttackEffects: []` 等 Unity 资源引用集合；运行时 `PopulateObject` 会用空数组覆盖 Prefab 引用，导致命中特效被清空。迁移器应跳过 `UnityEngine.Object` 集合。
- 类型命中特效由 `Mod_Damage.impactEffectSet` 显式引用 `CombatImpactEffectSet`，`AttackEffects` 只放数字等每次都播放的通用反馈；不能用通用列表是否为空阻断命中形状。动画与数字统一读取攻击数值 `CombatDamage.DominantKind`，不要按武器名称分类或分别实现占比比较；映射资源留在 GamePlay 程序集，避免 Effect 反向引用战斗程序集。
- 玩家自身的受击伤害数字与部位提示应在接收侧订阅 `DamageReceiver.OnDamageReceived` 统一保证，不能依赖攻击者 `Mod_Damage.AttackEffects`（AI 攻击资源可以为空）；部位提示必须读取同一笔 `DamageReceiverDamageInfo.BodyPartHits`，禁止再次随机部位。
- 命中特效必须区分 `0` 与 `-1`：`0` 表示有效命中但被护甲完全抵消，应播放数字 0；`-1` 表示死亡、受伤冷却等无效结算，不应播放命中特效；可破坏 Tile 也应把零伤害命中返回给 `Mod_Damage`。
- 概率命中状态不要硬编码进 `Mod_Damage`；伤害模块只发布实体命中目标与结算结果，`DamageOnHitBuffApplier` 等独立组件再通过目标 `BuffManager` 添加状态。`0` 仍属于有效实体命中并可触发状态，负数无效结算不触发；Tile 伤害不发布实体命中事件。
- 玩家进入 `Mod_PlayerDeathState` 濒死状态后，`Mod_Food` 等被动生命模块不得继续改写 `DamageReceiver.Hp`，否则会把死亡状态抬成极低正数。
- 濒死控制与界面属于血量的派生运行态，须在本地玩家 `Event_PlayerEnterWorld`（全部模块加载后）按权威血量恢复；不能依赖模块 Load 顺序，也不能重放 `OnDead`，否则读档会重复死亡掉落。
- 启用身体部位生命时，普通总量回血只能分配给仍存活的部位，不能复活已耗尽的手脚；直接重生或满血赋值才允许恢复全部部位。
- 武器的 `Mod_Damage` 必须是武器实例内的直接子物体，禁止再嵌套 `Mod_Damage.prefab` 实例；Prefab 组合可显式序列化跨模块引用，JSON 组合则必须在所有模块注册后通过 `IItemModuleDependencyBinder` 按唯一稳定 ID 绑定，禁止层级搜索或静默补建。攻击动画曲线只负责开关已存在的碰撞体。
- 动画武器的伤害盒必须跟随 `Render` 下实际武器 `SpriteRenderer` 的局部位置、旋转、缩放与 Sprite 边界；同时处理 `flipX/flipY` 对 Pivot 偏移的反转。禁止把 `Mod_Damage` 固定在 `Render` 原点并沿用模板的默认 1×1 BoxCollider，否则武器旋转后会出现大面积错位。
- `Mod_Damage` 开启伤害窗口时必须主动扫描当前重叠目标，不能只依赖 `OnTriggerEnter2D`；玩家、AI 与技能统一走公共伤害窗口，避免碰撞体后开时漏掉已经重叠的接收器。
- 标准物品武器的 `Mod_Damage.MaxAttackTargets` 默认统一为 `3`；特殊单体攻击可显式调低。Prefab 与 Item JSON 都可能覆盖 C# 默认值，调整默认目标数时必须同步检查这两类序列化配置。
- `DamageSender` 与 `DamageReciver` 是战斗专用 Trigger 对，Physics2D 矩阵中两层都只能与彼此接触；交互、拾取、玩家身体和普通阻挡不得与任一伤害层建立接触对。`DamageReceiver` 必须自带同节点专用 Trigger Collider，禁止借用 Item 根的普通阻挡 Collider；冲撞技能等物理伤害发送器也必须归入 `DamageSender`。Tile/建筑伤害继续使用不依赖接触矩阵的显式空间查询。
- 带 `Owner` 的武器、投射物仍保持伤害物品自身作为 `IDamageSender.attacker`，兼容资源节点、难度与既有结算语义；防自伤只在 Trigger、主动重叠扫描和最终结算入口额外排除 `item.Owner`，禁止为了防自伤全局改写攻击者身份。
- 受击后附加状态统一消费 `DamageReceiverDamageInfo`，具体规则通过 `DamageReceivedStatusEffectRegistry` 注册，禁止把出血/中毒等业务硬编码进 `Mod_Damage`。需要按真实伤害类型判定时读取 `ResolvedDamageValues`（已应用难度、防御和受击倍率），并按 `DamageValue` 裁掉过量伤害；玩家/动物出血只认切割、穿刺、劈砍分量，纯钝击和被防御完全抵消的分量不能触发。
- 玩家弓类远程武器使用 `Mod_Bow` 监听 `GameController.AttackStarted/AttackEnded` 完成按住蓄力与松开发射；弹药只通过通用 `Arrow` 标签和同库存事务选择，不按木/石/铜/铁写特殊分支。箭矢自身组合 `Mod_Projectile + Mod_Damage`：前者只负责飞行、蓄力倍率与落地回收，后者继续作为唯一伤害发送器；两者用 `IItemModuleDependencyBinder` 显式绑定，使新增 MOD 箭种只需遵守相同模块契约即可接入。
- 出血资格使用稳定 `Blood` 标签表达“该实体有血”，不要用 `Player`/`Animal` 类型或物种标签代替。玩家和有血动物可以同时保留自己的分类标签；幽灵、机械体等无血实体只要不声明 `Blood` 就不会触发刃伤出血规则，MOD 生物也通过同一标签接入。

## 验证

- 覆盖攻击→受伤→死亡→掉落，确认事件只触发一次、随机输入固定、池化特效每次重置。
- 默认不主动跑测试；需要时运行 `Combat.Smoke`，AI 攻击专项同时看 `AI.Smoke`。
- 测试入口：`Assets/GameTest/Combat/CombatSmokeTests.cs`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
