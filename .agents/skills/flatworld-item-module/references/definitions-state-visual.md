# 定义编译、状态与表现

## 定义和热刷新

- Manifest 唯一发现内容；RuntimeItemDefinition 冷编译 Prefab/组件槽/参数。shellPrefab 引通用目录，shellAddress 为独立壳，sourcePrefab 仅编辑器/冗余排除，删源同步删字段；生成不回退同名 Prefab。
- 未绑定组件仅在精确编译位置接受当前定义或冷编译来源身份，可将共享模板默认槽/能力转为具体定义；不覆盖无关 ID/槽/已绑定 Prefab。独立 Prefab 同时匹配根地址和组件位置；目录预检复用计划，纯数据能力不找 GO。
- ItemMgr 冷工厂取 GameRes.InstantiateItemShell 原壳，以最终 ItemData 仅装配一次；公共 InstantiatePrefab 仍已配置。Load 按 StableName 单次建索引，排子 Item、拒重复槽。
- 非 Actor 显式提供/继承正 weight/volume 和明确 stackable，冷载荷外壳同约束。ModuleJsonConfigurator 参数严格契约，向量写 x/y/z，不将 Unity Vector 计算属性给 JToken.FromObject。gameName 属定义，业务 id/翻译键/formerIds 分离。
- F5 RefreshLiveConfiguration 原位更新显式参数和仍受定义控制的视觉，不换 ItemData/模块或重 Load；结构与删参数后的默认值供新实例。ApplyResourceConfiguration/OnResourcesReloaded 保进度、刷新缓存/订阅；发布清闲池、禁活旧实例回池。
- GameRes.ResourceAssets 持 Sprite/材质/动画/壳句柄；Sprite Pivot 排序，子装饰用根 SortingGroup，池材质恢复定义或壳默认。占位图 `Assets/6_Art/Generated/Shared/ItemPlaceholder/素材占位符.png[素材占位符]`，ItemSprite 标签。

## 动画

- Mod_AnimatorController.visualMode 选 Animator/SingleSprite。运动优先：远端显式态→已加载启用 IActorAnimationMotionSource→Mover；源报主动运动/WorldOffset/RollDegrees，被动位移不迈步。PlayAttack(worldDirection)/PlayJump 仅表现，不管伤害/物理。
- 初始 Animator 状态由 Item 待播；停用冷壳不查 layerCount/HasState 或 Play/Update，激活初始化后才验播。单图正式禁 Animator、回池/重装配时清待播引用。
- singleSpriteStates 配具体壳状态名/Sprite，PlayAnimation/TryPlayAnimation 切图；TrySetSprite 支持跨图集且不启身体 Animator。空图/未知态拒，复位/停用/卸载/换模式还原原图。
- 单图动作仅独立父层，不改原视觉 localPose、Item根或刚体；附加视觉禁刚体/碰撞，手持武器仍正式命中链。切模式/卸载还父级/同级顺序并保最新翻面，停组件拒新动作、停GO先归零，正式卸载再拆层。OnDestroy 的 IsBeingDestroyed 只释引用不向销毁宿主挂节点；单移组件/正常回池仍还原层级。

## 物质、燃烧与产出

- ItemData.MatterState 存物态，定义 matter/reactions 管静态相变/反应；ItemMatterRuntime/MaterialThermalProcessor 按自身温度结算，热源仅供热。matter.transitions 的 inputAmount/remainderOutputItemId 支持整组/尾数多产物，扣料/全部产物同原子事务并传真温，不跨槽拼组。木 Wood 500℃起3→1炭、尾数每份1灰；炭1000℃起1→1灰。
- 高频转化扫描用索引遍历 IReadOnlyList<RuntimeItemMatterTransition>、首个匹配，避接口装箱。
- Fuel 数据/Combustion 状态/FuelInteraction 交互/ICombustionStateReceiver 表现组合；flameScale/flameHeightRatio/flameEmissionMultiplier 控视觉，attachFlameToAnchor 跟燃点，followSourceRenderer/sourceRendererAnchorOffset 跟贴图。火把底图熄灭，熄火清粒子；氧气模块只管储量/缺氧，供给方判断真实环境并扣量。
- 玩法分类 ItemData.Tags；lootTableId 引稳定物品 ID，IResourceYieldModifier 源头聚合、每产出链一次并统一随机取整。requiredTool/minimumTier 是专精加成参考，不是普通采矿资格。

回归：`item_runtime/{shell_final_configuration,initial_animator_activation,single_sprite_animation,matter_transition_scan}`。
