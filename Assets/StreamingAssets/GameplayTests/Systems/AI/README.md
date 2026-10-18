# AI 生物测试

2026-10-18 统一出生入口补充，**仅编写，未编译、未运行**：自然/夜怪、事件、GM/技能/MOD、蜂巢、生物战利品与鸡蛋孵化都进入 `MonsterSpawnerManager.TrySpawnCreature`；来源保留各自时间、落点与预算规则。`spawn-countdown.lua` 的 `ai/creature_request` 采正式请求守卫，覆盖非权威、未就绪、空物种和非有限坐标拒绝。`creature-creation.lua` 使用正式隔离世界采真实创建、重复 GUID 拒绝、加载前后回调顺序、绑定返回失败回收、回调销毁后不误删后继新实例（另采 `replacementReusedShell` 标明是否恰好复用同一外壳，不为测试清空世界池）、原 GUID 恢复、休眠身份冲突与唤醒不算新出生，并清理自有居民。未直接覆盖每个来源的触发条件、蜂巢完整昼夜/护巢、孵化多只的实际失败回滚、ECS 实体现场或自然全局预算扣减；现有来源通过静态调用链确认接入，最终玩法效果由用户验收。

2026-10-18 自然动物倒计时补充，**仅编写，未编译、未运行**：`spawn-countdown.lua` 调用 `ai/spawn_countdown`，读取正式七份自然动物规则与八个物种，通过正式 `AdvanceCountdownSchedule/SelectPendingCountdownEntry/CompleteCountdownSpawnAttempt` 检查初始完整间隔、到期前和恰好到期、一轮受阻不丢失也不累积、整轮完成后重新计时、概率落空消费机会及零/必定概率边界、混合规则中鸡猪各自计时且保持原权重对应的相对频率、一个物种受阻仍轮到其它物种、二进制存档保留剩余时长与待出生队列、恢复观察基准不补离线、长时钟精度和无效间隔拒绝。三类生命周期工作优先序直接采正式轮转入口。夹具不绑定空间，连续推进的是同一份正式持久状态；跨区块不重置由其不依赖区块/玩家坐标的所有权结构保证，没有真实跑图、选点、满员或预算耗尽现场，也没有验证最后的出生和帧耗时，仍由用户验收。

本轮小鱼生成与抗水流规则补充：`ai/ecology_config` 读取正式目录核对小鱼仅海洋+河流且 `waterOnly`；新增 `ai/fish_current` 使用独立未加载鱼模块读取 `IWaterCurrentExposure`，并用正式 `water-current` 目录在 0/1/70/10000kg 检查鱼推动速度为零、零速公共采样返回零及其他物种仍保留推动。夹具未加载真实鱼或世界，不代表河流海洋实际移动验收；本轮未编译、未运行。

2026-10-18 全物种单图动画补充，**仅编写，未编译、未运行**：`ai/single_sprite_catalog` 复用正式 Actor Manifest/继承加载器，通过 `ResourceReloadContext` 候选上下文隔离目录缓存，并将最终参数交给正式 `ModuleJsonConfigurator.Apply`，检查全部正式物种的动画模块启用且为 SingleSprite；Ghost/SmallFish 原外壳保持一个 SpriteRenderer，新纯动画控制器没有 Renderer、碰撞体或刚体。通用主动信号组合规则在 [ItemRuntime](../ItemRuntime/README.md)，真实物种行为、风场、飞行/水下/搁浅观感与联机仍由用户验收。

补强动画身份注册：此前原型类型与 `visualMode` 检查没有注册 `ItemMods`，会漏掉 JSON 把 `Module_Animator_AI/AI动画模块` 当能力 ID 的错误。现在对全部正式物种先把最终显式 `animator` 槽位身份交给生产 `BindRuntimeIdentity`，再调用 `ModuleInit→ItemMods.AddMod→RequireSingleModById`，检查 `StableName/ModuleId/PrefabId`、启用态、唯一能力组和数据绑定；每种还按同一链注册显式错误 ID，只在精确查询处捕获拒绝，保证错误不会被 Canonical 默认值掩盖。临时 Item 使用 `test:...` 身份，避免读取当前正式 Actor 的旧配置。基础身份采样只检查动画模块注册与精确查询；Bird/Bee 的真实飞行依赖绑定见下一段。本轮未编译、未运行。

本轮新增 Bird/Bee 真实依赖绑定采样：从正式 Manifest 的最终继承配置中选取飞行模块及其六个必需模块，克隆停用的 Bird/Bee Prefab，由正式 `FindModulePrototype` 按能力或具体 Prefab 身份定位外壳内的真实模块，按生产顺序先注册全部显式身份、再 `ModuleInit`，最后实际调用 `Mod_AI_Bird.BindModuleDependencies`。Lua 检查七个模块的独立数据绑定、启用态、唯一动画能力和单图模式；再把同一动画实例移除后注册为错误显式 ID，确认飞行模块在精确动画依赖处拒绝。夹具只覆盖模块注册与依赖，不启动 GameRes，不执行完整定义装配、`Item.Load/Module.LoadRuntime`、外壳激活或世界居民生成；当前只编写、未编译、未运行，完整生成、蜂巢居民与表现仍由用户验收。

`ai/single_sprite_aquatic` 在独立停用 Item 上，以正式小鱼材质调用真实 `AquaticActorPresentation.Tick/SetStrandedHop/SetCarried`，检查游动信号、单图模式下原节点不再叠加旧摇摆/高度、搁浅和携带的高度倾角输出，以及切回 Animator 后旧游动路径仍可用。没有加载鱼 AI、液体或世界，也没有推进共用动画模块；两者组合由 ItemRuntime 的真实接口与共用层规则覆盖，实际鱼完整行为未运行。

`ai/line_of_sight_batch` 在独立 `WorldRuntime` 中调用正式整数格视线查询，检查同轮跨射线共用区块寻址且预热后不分配托管内存；建筑和地形阻挡仍逐格读取当前权威值。覆盖同格/端点规则、任一对角侧格遮挡、未知端点/途中区块、区块卸载重绑/地形替换、观察者位置变化、维度/世界代际、环绕最短路/等价坐标、自定义矩形区块与负坐标，以及查询/世界释放。本轮已通过外部 C# 编译，Lua 未运行，实际玩法未验收；不代表真实生物感知、候选代际全链路或 60 帧已验收。

2026-10-17 蜂巢出巢性能补充但未执行：`ai/hive_resident_schedule` 直接采正式初始成员意图、同帧创建配额及成功提交规则，检查初始三名唯一身份、重复维护不重抽蜂蜜、普通同帧最多一次/次帧继续、护巢绕过限额、未完成意图的 JSON 保存恢复、成功清理待生成与睡眠状态，以及死亡后不免费补足。夹具不加载真实蜜蜂，因此不代替完整 `Item.Load` 失败清理、真实卸载、日出画面和帧耗时验收。

2026-10-17 水生线路性能补充但未执行：`ai/aquatic_traversal` 调用正式 `AquaticHabitat.CanTraverse`，只由夹具提供逐格可游泳结果，检查同格查询去重、跨格保持、岸/未知格拒绝、负坐标取格、零距离起点、环绕接缝和调用间水域变化不复用旧缓存。没有生成鱼或改动当前世界，不覆盖实际液体定义、觅食消费、逃离行为和最终帧耗时；用户自行验收，本轮未编译、未运行。

2026-10-17：小鱼配置采样新增安全体表 -26.5～63.5℃与仅水域生成断言；直接读取独立本体 JSON，不刷新正在运行的 Actor 目录缓存。本轮未运行，真实天气/水体温度、游动和自然死亡仍由用户验收。

2026-10-15：本系统登记的快速规则和 PlayMode 入口均已执行通过，见 [缓存与全系统 DEBUG](../../缓存与38系统DEBUG循环_2026-10-15.md)。以下功能缺口继续有效，玩法验收由用户进行。

- `rules.lua`：直接调用 `AnimalEggLayingSchedule`。覆盖首次无资格、两天最小冷却、随机延迟上下界、配置提高冷却、成功后从实际日期重计时、非有限参数拒绝。
- 自然刷新规则通过 `SpawnerConfigCatalogLoader.LoadBuiltIn` 读取正式目录，检查小鸡独立机会为 0.5、野猪默认机会为 1 且原权重均保留；直接调用独立概率的 0/0.5/1 边界与 `CompleteSpawnAttempt`，检查普通/补员队列在落空和出生时的扣减、出生累计与预算不误扣。本轮只补静态回归，未运行；不覆盖真实选点、全局预算扣除或随机长期分布。
- 同脚本直接调用 GO 主路径的 `AIStateMachine/AI_StateMachineRunner`、`AIAdvanceStateNode`、`AI_AttackController`、`AIBehaviorGraphBlackboard`、`Mod_ItemDetector.CalculateEffectiveDetectionRadius`。覆盖节点退出/切换/进入顺序、切换拦截、重复/缺失身份保护、Reset复用、失效目标停车、到达去重与重入、前摇/伤害窗口/后摇/冷却、受击事实和来源记忆、世界计时独立及体型感知范围。
- 夹具：独立日历、状态机、攻击控制器、行为图黑板和局部回调，不生成动物、不改当前世界居民。攻击探针检查正式窗口时钟，没有伤害目标或动画器。
- `gameplay-suite.json` / `gameplay.lua`：与导航共用一套现场链。正式隔离世界中的 `Zombie` 经 `AiRuntimeBackendService.TrySpawnDirect` 完成真实 AI/移动模块加载；正式导航管理器异步请求先检查严格代价上限拒绝，再经 `TrySetAdvanceCommand` 驱动生物实际 Tick、获得路径、沿已加载可走格移动三格、到达清命令和取消旧路径。
- Lua 断言读取真实身份、模块加载、导航回调、路径结果、实际最大位移/速度、沿途可走性、目标距离和到达/取消后的状态，不能仅凭生成或命令受理算通过。宿主定时截图，随后检查自有生物已删除。
- 夹具只创建一只自有生物，使用附近实际已加载的空地，不修改玩家速度或全局时钟。跨帧等待有真实时间上限；路径请求异常时取消，生物登记每个 Lua 的逆序收尾，断言失败和中断也清理。
- [导航目录](../Navigation/README.md) 共用这份清单，不再复制案例或重复计数。
- 缺口：水生/飞行导航、地形障碍绕行、门户长路、觅食消费、完整物种行为选择/目标 LOS、攻击实际伤害、蜂巢居民、远距休眠及 GO/ECS 交接。
- 当前状态：新增现场案例只完成静态编写；按用户要求本轮不编译、不运行、不作验收，也不停止正在运行的 PlayMode。
