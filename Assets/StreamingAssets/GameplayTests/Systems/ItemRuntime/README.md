# 物品模块运行时

2026-10-25 性能回归补充：`ai_entity_live_query` 调真实 AI 资格查询，覆盖普通物品、动态新增/移除、停用子能力及正负查询零分配；`world_tick_move_query` 调真实世界调度入口，覆盖首次移动登记、同场景反复移动零分配、注销和重绑。当前只静态编写，未编译、未运行；跨场景迁移与完整模块 Tick 仍需现场验收。

2026-10-24 新增 `item_runtime/compiled_module_parameters`：对正式 PreparedParameters 检查首次严格编译拒绝未知/保留字段、错误类型和未知特殊块，复查保留模块自有校验及当前碰撞体要求，反复应用后实例列表互不共享、特殊参数仍正确写入。目录复查复用当前装配计划，不重复解析普通字段。Unity 正在 PlayMode，按用户要求未编译、未运行；实际启动耗时与 MOD 资源重载由用户验收。

2026-10-15：本系统登记的快速规则和 PlayMode 入口均已执行通过，见 [缓存与全系统 DEBUG](../../缓存与38系统DEBUG循环_2026-10-15.md)。以下功能缺口继续有效，玩法验收由用户进行。

快速入口：`rules.lua`；操作：`item_runtime/module_structure、clone_runtime、single_sprite_animation、single_sprite_shell_assembly、module_assembly_identity`。

矿物参考入口：`mineral-properties.lua`，操作 `item_runtime/mineral_properties`。读取正式 `GameConfig/Items/mineral-properties.json`，检查12类矿物的硬度/韧性初值、矿脉与掉落物共享、1～10边界、未知ID与非法配置拒绝；只覆盖独立参考目录，尚未接入采矿、伤害、耐久、存档或UI。2026-10-24 专项快速规则通过，报告 `Library/FlatWorldGameplayTests/Runs/20261023T194429966Z_f77d3850/report.json`；未进入 PlayMode，未验证手机端读取。

2026-10-24 巨型矿山补充同一 Lua 的 `item_runtime/giant_mines`：采样正式继承目录与掉落选择接口，检查现有11类小矿山各有巨型版、20万生命、底边固定的三分之一碰撞、26格偏移占地、防种植不额外占根格、完整受击范围、原矿物参考和开采/防御/产物继承；石矿保留原巨型最终产量，其他巨矿复用小矿完整掉落表。用户正在 PlayMode 且自行验收，本轮未触发 Unity 重新编译或执行新增断言；未验证真实命中、掉落与寻路。

2026-10-24 火种寿命：新增 `tinder_material/timed_despawn`，读取正式棉花/火种配置，检查打火石材料资格、100℃受热转火种且保留纺织、1440 游戏秒边界、暂停、同一时间重复调度、跳时、跨世界时钟、模块快照续计和库存无副产物清空。独立编译使用实际 Unity 项目程序集与 MemoryPack，生产时钟和真实序列化往返的 124 项检查已通过（`Library/FireSeedLifetimeTask`）；Unity 正处于 PlayMode，未打断，新增 Lua 规则、Unity 重新编译、真实掉落/库存与远程客户端联机尚未执行。计时状态属于整堆批次，合并沿用库存既有的目标堆叠状态；没有逐份追踪不同批次的寿命。

`initial_animator_activation` 使用真实 SmallFish/Bird 控制器经过正式 `ApplyAnimator`：停用冷外壳只登记状态名，激活并初始化后再检查状态层和短名/完整路径；非法状态仍拒绝，单图接管、取消和回池清除待播。另使用真实控制器运行停用外壳的 `Mod_AnimatorController.Load`，捕捉原生未播放控制器警告，并检查冷态 Move 参数在激活后的恢复。未运行的真实自然出生和画面仍需单独验证。

`cleanup_lookup` 检查批量回收时的单例只读路径稳定且零分配；不模拟完整场景卸载与 Unity GC 回收。

2026-10-18 平均帧优化补充 `world_matter_structure/world_matter_contact`：正式层级资格缓存检查模块增删、载荷替换、加载换代、自定义 AI 排除与稳态零分配；正式物质入口检查复用液体定义后的岩浆加热、遇水接触、零时长拒绝与数量不变。掉落物同步驱动复用 `ChunkMgr.RuntimeTerrainPresenceQuery`，其地形实时变化、环绕、卸载和世界代际边界复用 `terrain/contact_query_context` 规则。Unity 正在 PlayMode，本轮只静态修改，未编译或运行；真实平均帧和玩法由用户验收。

2026-10-24：`world_matter_structure` 增加掉落模块缓存实时动态字段验证（轨迹中、漂浮、下沉、完结、移除）。世界物质条目的火焰表现改为只处理活动火焰集合，避免每帧遍历全部散落物；单次刷新使用租借列表隔离注册/销毁回调，`ItemMgr.LateUpdate.WorldMatterFlames` Marker 便于复测。本规则不替代游戏中的真实火焰生命周期、联机、性能对比验收。

`module_assembly_identity` 新增真实 `AI_Chicken`、`Mod_TurnBack` 的两种调用顺序：先 Awake 后绑定必须保留编译来源身份并成功装配，先绑定后 Awake 必须保留定义身份。2026-10-18 仅补充，Unity 在 PlayMode，本轮未编译或执行；实际自然生成由用户验收。

单图状态换图回归：`single_sprite_animation` 检查 `Flying/Landing/Ground` 状态映射、直接 `TrySetSprite`、同状态重入、未知状态/空图拒绝，以及停用、卸载、切模式与复位后的原图恢复；换图后身体 Animator 仍停用。`single_sprite_shell_assembly` 读取真实 Bird/Seagull 外壳的五种首帧配置，并采样正式飞行阶段选择函数。2026-10-18 本次仅补充断言，Unity 正在 PlayMode，未编译、未执行；不代表真实鸟类飞行、对象池重取或画面观感已验收。

单图死亡回归补充：`single_sprite_animation` 使用独立生命模块的正式 `ResolveNetworkAuthoritativeDeath→DeathStarted` 和动画 `AdvancePresentation`，采固定图片、上抛、旋转、底边不穿地、约 180° 落地保持、重复 Death/Dead 不重播，以及攻击/跳跃/受击/移动/状态换图不能抢占。覆盖偏心枢轴、父级镜像与非等比缩放、Sprite 双翻面、已有飞行高度归地面、零血量事件缺失兜底、显式复活、卸载重载、手动演示复位，并通过正式 `PrepareForDespawn/NotifyReturnedToPool/PrepareForPoolReuse` 在同一临时外壳重新绑定载荷检查死亡残留清理。生命模块不启动 UI、完整伤害或战利品，取池夹具也没有经过真实 ItemMgr 池查询；当前 Unity 正在 PlayMode，**仅静态编写，未编译、未运行**。真实鸟/鱼/蜂死亡、完整对象池工厂、尸体停移动与关闭伤害、联机血量同步到延迟回收的时序和最终观感由用户验收。

死亡伤害窗口补充同一入口：克隆正式 `Mod_Damage.prefab` 保留序列化碰撞体引用，绑定临时 Item 并正式加载；Lua 采真实 `DamageCollider.enabled`，检查致死事件在动画推进前立即关窗、死后 `StartAttack` 和重载不能开窗，以及常开接触窗在新生命重载/同外壳复用后恢复、初始关闭的攻击窗不会被误开。只检查实际窗口与生命周期，没有受击目标或完整 AI 战斗现场；本次同样未编译、未运行。

已编写的规则覆盖：ModuleDataCollection 的结构版本、启停与移除退订；ItemInstanceDataFactory 的库存、模块和标签副本隔离。

2026-10-18 新增单图生物动画规则，使用独立临时 Item、停用物理的刚体和真实 `Mod_AnimatorController`，只加载动画模块并调用生产 `AdvancePresentation/PlayAttack/PlayJump/PlayHitReaction`：检查本地/远端主动迈步的左右摇摆与起伏、被动速度不误触发走路、四向与镜像父级攻击前探/归位、跳跃起落、动作中断切模式、重复加载卸载及模块启停。夹具完成绑定后激活根，移动组件保持停用，全步骤同步完成；关闭待机呼吸以核对精确归位。每步核对 Item 根/刚体不动、原 Sprite 局部姿态不被动画覆盖，切回 Animator 与卸载核对原父级、同级顺序和外部最新 Y 轴翻面；组件停用期间动作入口不能重建视觉层或残留动作，并核对身体 Animator 恢复与重新启用后可正常攻击。**本轮仅编写，未编译、未运行**；不代表玩家观感、真实调度、战斗命中、飞行/阴影叠加、对象池复用或网络端到端验收。

全 AI 切单图后的新增规则仍归 `single_sprite_animation`：最小临时 Module 只通过正式 `IActorAnimationMotionSource` 返回 Lua 指定的主动状态与外观高度/倾角，实际共用模块负责组合；检查来源优先于 Mover、通用旧状态入口不重启身体 Animator、主动飞行/游动可摆动、被动刚体速度不迈步、来源失效或停用后清除外观偏移，以及远端显式状态优先。各动作还调用正式 `TryGetBaseVisualMatrix`，逐元素核对提供给物理的矩阵保留原 Sprite 局部姿态与外部父层平移/镜像，同时剥离程序摇摆、前探和高度倾角。此组仅编写，未编译、未运行；没有真实生成鸟、蜜蜂、鱼或幽灵。

尚待补充：已加载世界实例强制同池复用、换代后的旧引用拒绝、模块 Tick 频率、世界完整物品与轻量掉落物转换；正式加载、卸载和再次加载见下方共享现场。

狼外壳装配新增回归：正式 Actor 加载器读取最终继承定义，真实 Wolf Prefab 与 `ItemModuleAssemblyCompiler` 编译单个 `animator` 槽位，再在停用副本中实际定位、应用配置、注册并要求同一内嵌动画模块；核对控制器仍为 Wolf、Animator 与 SpriteRenderer 各一份，去掉稳定身份后必须拒绝模糊定位。此规则不提供资源能力别名，不创建完整 GameRes 或启动世界；**仅编写，未编译、未运行**，不代表真实出生、初帧采样或对象池复用已验收。

首次身份绑定补充：真实狼外壳中的动画模块直接调用生产 `TryRestoreRuntimeIdentity`，检查编译来源的空能力 ID 可恢复完整身份，并检查已绑定身份可继续匹配；既不匹配来源模板也不匹配当前定义的能力 ID、稳定槽位和具体 Prefab 必须拒绝。**仅编写，未编译、未运行**，完整工厂生成及实际回池重取仍由现场验收覆盖。

通用装配身份规则 `module_assembly_identity`：正式编译器读取默认槽位/能力与定义不同的模板，检查来源原值及 Awake 补全为当前能力 ID 都能绑定；中途篡改槽位/能力必须拒绝。嵌套 Item 的同槽位模块不参与父物品编译、不能归父物品恢复且数据保持原样；包含 Item 的独立模块 Prefab 被拒绝。独立模块必须命中具体 Prefab 根和精确组件位置，仅同名祖先不能蒙混通过。预检解析到编译选定的原型，停用宿主同能力模块的隐式稳定名仍唯一。另检查带 Item 外壳的缓存命中、子物品隔离，以及独立模块拒绝前后缓存互不污染。2026-10-25 单项 Lua 执行通过，共 12 项断言；没有覆盖完整资源目录预检、真实池复用或域重载现场。

后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。

## PlayMode 共享物品链路

入口：[物品、MOD与网络副本测试集](../ItemRuntime/gameplay-suite.json)，Lua业务断言在 [共享脚本](../ItemRuntime/gameplay.lua)。三个系统按同一实际实例链路组合，目录表映射同一案例，不重复执行。

已编写：正式 GameRes→ItemMgr 创建由玩家持有的自有燧石小刀→Item.Load→跨帧存活→ModuleSave→卸载登记事件→再次加载新代际；检查稳定模块身份、索引清理、数据独立。

`shell_final_configuration` 额外用自有停用小刀外壳调用正式冷工厂、配置、对象池与准备布局：检查原始外壳没有提前装配、最终数据直接绑定、同一池对象和模块组件复用后切到最新数据、子 Item 同名模块排除、宿主重复 StableName 拒绝，以及公共 Prefab 工厂仍返回已配置实例。2026-10-18 新增，本轮 Unity 正在 PlayMode，未执行；不代表实际自然生成峰值已降低。

中心查询回归：用正式创建、移动通知、卸载的小刀查真实 ItemMgr 空间桶，检查零半径、圆周边界、范围外、排除对象、图层、停用、非法半径及循环接缝去重；同轮采样 256 次正式大气只读查询的状态身份与分配预算。此新增回归尚未执行，不代表已通过。

覆盖边界：没有强制保证拿回同一个池对象，检查的是卸载与再次加载契约；调度频率、轻量掉落转换、运行时换定义完整事务仍未覆盖。

`natural_replacement_ownership` 快速规则调用正式生态登记和交接入口，检查同实例重复登记、重复 GUID 拒绝、错误身份/未加载候选拒绝后保留源归属、成功交接后销毁旧壳不删新体，以及新体销毁正常清除登记。此规则只覆盖生态归属，不覆盖真实受热、多产物回滚、区块重载和联网完整替换；2026-10-25 仅编写，未编译、未运行，现场验收由用户执行。

2026-10-23 手持跟随：`single_sprite_animation` 补充身体参考点与正式 Hotbar 跟随的对照，覆盖走跑、攻击、跳跃、镜像缩放、远端身体状态及复位/卸载/切模式/停用；光标用正式 FocusPoint 核对更新位置后的方向。当前 PlayMode 未打断，本轮未编译、未运行；真实鼠标/手机输入、刺击位置接管和联机完整流程仍由用户验收。

2026-10-18 单图层销毁边界补充：`single_sprite_animation` 通过正式 `Item.OnDestroy`、单独删除动画组件和 `PrepareForDespawn` 三种收尾检查身体与附加视觉。宿主销毁时视觉与临时表现层一同销毁且不产生 Unity 错误；普通回收或删除组件时恢复各自真实原父级、同级顺序、原局部位置/缩放和外部最新翻面，夹具同时覆盖嵌套视觉父级。**本轮仅静态修改，未编译、未运行规则或 PlayMode**；未覆盖存活 Item 内部的视觉父容器被单独销毁，也不代表真实对象池重取或玩法观感已验收。
