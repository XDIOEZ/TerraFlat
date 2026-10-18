# 物品模块运行时

2026-10-15：本系统登记的快速规则和 PlayMode 入口均已执行通过，见 [缓存与全系统 DEBUG](../../缓存与38系统DEBUG循环_2026-10-15.md)。以下功能缺口继续有效，玩法验收由用户进行。

快速入口：`rules.lua`；操作：`item_runtime/module_structure、clone_runtime、single_sprite_animation、single_sprite_shell_assembly、module_assembly_identity`。

`module_assembly_identity` 新增真实 `AI_Chicken`、`Mod_TurnBack` 的两种调用顺序：先 Awake 后绑定必须保留编译来源身份并成功装配，先绑定后 Awake 必须保留定义身份。2026-10-18 仅补充，Unity 在 PlayMode，本轮未编译或执行；实际自然生成由用户验收。

单图状态换图回归：`single_sprite_animation` 检查 `Flying/Landing/Ground` 状态映射、直接 `TrySetSprite`、同状态重入、未知状态/空图拒绝，以及停用、卸载、切模式与复位后的原图恢复；换图后身体 Animator 仍停用。`single_sprite_shell_assembly` 读取真实 Bird/Seagull 外壳的五种首帧配置，并采样正式飞行阶段选择函数。2026-10-18 本次仅补充断言，Unity 正在 PlayMode，未编译、未执行；不代表真实鸟类飞行、对象池重取或画面观感已验收。

已编写的规则覆盖：ModuleDataCollection 的结构版本、启停与移除退订；ItemInstanceDataFactory 的库存、模块和标签副本隔离。

2026-10-18 新增单图生物动画规则，使用独立临时 Item、停用物理的刚体和真实 `Mod_AnimatorController`，只加载动画模块并调用生产 `AdvancePresentation/PlayAttack/PlayJump/PlayHitReaction`：检查本地/远端主动迈步的左右摇摆与起伏、被动速度不误触发走路、四向与镜像父级攻击前探/归位、跳跃起落、动作中断切模式、重复加载卸载及模块启停。夹具完成绑定后激活根，移动组件保持停用，全步骤同步完成；关闭待机呼吸以核对精确归位。每步核对 Item 根/刚体不动、原 Sprite 局部姿态不被动画覆盖，切回 Animator 与卸载核对原父级、同级顺序和外部最新 Y 轴翻面；组件停用期间动作入口不能重建视觉层或残留动作，并核对身体 Animator 恢复与重新启用后可正常攻击。**本轮仅编写，未编译、未运行**；不代表玩家观感、真实调度、战斗命中、飞行/阴影叠加、对象池复用或网络端到端验收。

全 AI 切单图后的新增规则仍归 `single_sprite_animation`：最小临时 Module 只通过正式 `IActorAnimationMotionSource` 返回 Lua 指定的主动状态与外观高度/倾角，实际共用模块负责组合；检查来源优先于 Mover、通用旧状态入口不重启身体 Animator、主动飞行/游动可摆动、被动刚体速度不迈步、来源失效或停用后清除外观偏移，以及远端显式状态优先。各动作还调用正式 `TryGetBaseVisualMatrix`，逐元素核对提供给物理的矩阵保留原 Sprite 局部姿态与外部父层平移/镜像，同时剥离程序摇摆、前探和高度倾角。此组仅编写，未编译、未运行；没有真实生成鸟、蜜蜂、鱼或幽灵。

尚待补充：强制同一池对象复用、换代后的旧引用拒绝、模块 Tick 频率、世界完整物品与轻量掉落物转换；正式加载、卸载和再次加载见下方共享现场。

狼外壳装配新增回归：正式 Actor 加载器读取最终继承定义，真实 Wolf Prefab 与 `ItemModuleAssemblyCompiler` 编译单个 `animator` 槽位，再在停用副本中实际定位、应用配置、注册并要求同一内嵌动画模块；核对控制器仍为 Wolf、Animator 与 SpriteRenderer 各一份，去掉稳定身份后必须拒绝模糊定位。此规则不提供资源能力别名，不创建完整 GameRes 或启动世界；**仅编写，未编译、未运行**，不代表真实出生、初帧采样或对象池复用已验收。

首次身份绑定补充：真实狼外壳中的动画模块直接调用生产 `TryRestoreRuntimeIdentity`，检查编译来源的空能力 ID 可恢复完整身份，并检查已绑定身份可继续匹配；既不匹配来源模板也不匹配当前定义的能力 ID、稳定槽位和具体 Prefab 必须拒绝。**仅编写，未编译、未运行**，完整工厂生成及实际回池重取仍由现场验收覆盖。

通用装配身份规则 `module_assembly_identity`：正式编译器读取默认槽位/能力与定义不同的模板，检查来源原值及 Awake 补全为当前能力 ID 都能绑定；中途篡改槽位/能力必须拒绝。嵌套 Item 的同槽位模块不参与父物品编译、不能归父物品恢复且数据保持原样；包含 Item 的独立模块 Prefab 被拒绝。独立模块必须命中具体 Prefab 根和精确组件位置，仅同名祖先不能蒙混通过。预检解析到编译选定的原型，停用宿主同能力模块的隐式稳定名仍唯一。**仅编写，未编译、未运行**，没有覆盖完整资源目录预检、真实池复用或域重载现场。

后续修改触及以上关键流程时，需增强本组 Lua 和真实隔离场景夹具，再执行相关系统；不能把快速规则通过当成全路径覆盖。

## PlayMode 共享物品链路

入口：[物品、MOD与网络副本测试集](../ItemRuntime/gameplay-suite.json)，Lua业务断言在 [共享脚本](../ItemRuntime/gameplay.lua)。三个系统按同一实际实例链路组合，目录表映射同一案例，不重复执行。

已编写：正式 GameRes→ItemMgr 创建由玩家持有的自有燧石小刀→Item.Load→跨帧存活→ModuleSave→卸载登记事件→再次加载新代际；检查稳定模块身份、索引清理、数据独立。

中心查询回归：用正式创建、移动通知、卸载的小刀查真实 ItemMgr 空间桶，检查零半径、圆周边界、范围外、排除对象、图层、停用、非法半径及循环接缝去重；同轮采样 256 次正式大气只读查询的状态身份与分配预算。此新增回归尚未执行，不代表已通过。

覆盖边界：没有强制保证拿回同一个池对象，检查的是卸载与再次加载契约；调度频率、轻量掉落转换、运行时换定义仍未覆盖。

2026-10-18 单图层销毁边界补充：`single_sprite_animation` 通过正式 `Item.OnDestroy`、单独删除动画组件和 `PrepareForDespawn` 三种收尾检查身体与附加视觉。宿主销毁时视觉与临时表现层一同销毁且不产生 Unity 错误；普通回收或删除组件时恢复各自真实原父级、同级顺序、原局部位置/缩放和外部最新翻面，夹具同时覆盖嵌套视觉父级。**本轮仅静态修改，未编译、未运行规则或 PlayMode**；未覆盖存活 Item 内部的视觉父容器被单独销毁，也不代表真实对象池重取或玩法观感已验收。
