---
name: flatworld-ai
description: "Use when: 定位或修改 FlatWorld 的动物、怪物、蜂群、AI 感知、决策、移动、战斗、生成、存档或批量表现。关键词：AIECS、AiecsSimulation、AiRuntimeBackendService、MonsterSpawnerManager。"
---

# FlatWorld AI

## 正式入口

- Actor 定义：`Assets/StreamingAssets/GameConfig/Actors/definitions/*.json`；默认使用 GameObject 外壳、`modules` 和 Animator。`ecs` 配置仅供可选 ECS 后端使用。
- GameObject 主控：`Entities/AI/AI_Base.cs`、`Mod_AI_Bird.cs`、`AI_Wolf.cs`、`Mod_BeeBehavior*.cs`；生命周期、分级 Tick 与对象池由 `ItemMgr` 驱动。
- 定义编译：`Entities/AIECS/Gameplay/AiecsDefinitionCompiler.cs`。解析当前合并的 Actor/MOD 目录，检查能力依赖并生成纯值模板；不要实例化旧 AI 来提取运行态。
- 批量模拟：`Entities/AIECS/Core/AiecsSimulation.cs`、`AiecsCapabilities.cs`、`AiecsCapabilitySystems.cs`。感知、决策、移动、战斗和可选能力按组件查询运行。
- 世界桥：`Entities/AIECS/Gameplay/AiecsEcologyRuntimeHost*.cs`、`AiecsGameplayBridge.cs`。负责正式世界生命周期、蜂巢、存档、旧 Unity 接口输入。
- 生成：`Entities/AI/Runtime/AiRuntimeBackendService.cs`、`Entities/AI/Spawning/MonsterSpawnerManager.cs`、`Resources/GameConfig/Spawners/Rules/*.json`。
- 表现：`Entities/AIECS/Presentation/AiecsWorldRenderer.cs`、`AiecsBatchRendererGroup.cs`、`Assets/9_Shaders/Shader/AiecsSpriteLit.shader`。动画目录在 `Assets/6_Art/Generated/Actors/AIECS/`。
- 设计文档：`开发文档文件夹/02_实体AI与ECS/AI_ECS逻辑与GameObject镜像代理架构待办.md`。

## 运行边界

- 原生 AIECS 的整体生命来自 Actor 生命配置，身体部位是独立耐久；禁止再按部位生命求和重写 `Vital.Hp/MaxHp`。一次攻击只扣一次整体血量，耗尽部位仍保留命中资格，避免缺肢后成为无敌实体。

- 当前所有物种（包括 Zombie）均由完整 GameObject AI 主控；`spawner-settings.json` 的 `useAiecsBackend=false`，全部正式生成条目为 `gameObject`。未经用户再次要求，不执行旧文档中的全量 AI ECS 迁移。
- 可选 ECS World 的唯一所有者是 `WorldEntityRuntime`，`AiecsSimulation` 只处理自身 AI 查询。停用 ECS AI 不能停用树木、作物等资源实体，也不能销毁共享 World；独立 World 仅用于诊断。
- 退出或脚本域重载时，Unity 可能先销毁 World；居民快照与外部查询必须检查 `AiecsSimulation.IsCreated`，不能只判 Bridge 非空。快照先完整采集再替换，失效时保留冷快照；宿主在 `OnDisable` 释放自有资源，重复释放必须安全，快照失败不能阻断 BRG、镜像和 Native 内存清理。

- `AiRuntimeBackendService.ConfigureRoutes` 按生成条目冻结物种归属，未列出的物种默认 GameObject；同一物种不能配置两个后端。显式 ECS 物种停用或失败时不静默回退。`ItemMgr.InstantiateItem` 只拒绝显式 ECS 物种。
- `MonsterSpawnerManager` 恢复 GameObject 生成、营养初始化、人数预算及远距休眠；GM、事件、技能和 MOD 的生物生成复用 `AiRuntimeBackendService.TrySpawnDirect`。GameObject 返回前必须完成 `Item.Load` 并核对 `IAIActor.ActorItem`。
- GameObject AI 使用真实模块、Mod_Mover、Animator 与表现组件，不是镜像空壳。只有显式 ECS 生物才用池化镜像与 BRG；两套后端不得同时接管同一生物。
- GameObject AI 依赖的移动、检测和朝向模块必须在合并后的 Actor JSON 中启用；`enabled=false` 会跳过 `OnLoad` 和 Tick，即使 AI 仍提交目标也不会初始化导航。检查父定义与子定义覆盖，Prefab 的模块开关使用 `ModuleData.Enabled`（不要混淆移动数据中表示奔跑的 `Data.isRunning`）；内容校验器以 `FWC-ACTORJSON-004` 提示关闭的 AI 基础模块，禁止在 `Awake/OnLoad` 中强制开启来绕过配置。
- 鸟类飞行能力 ID 固定为 `Mod_AI_Bird.ModuleId`（`AI_Bird`），JSON 的 `prefab=Mod_AI_Bird` 只用于定位实现。Bird、Seagull、Bee 的定义、外壳与资源构建器必须保持该 ID 一致；蜜蜂在依赖绑定阶段解析飞行、检测和生命模块，内容校验器以 `FWC-ACTORJSON-005` 拒绝错误的飞行能力 ID。
- 保留 `IAiEcologyBackend`、`AiecsGameplayBridge`、镜像及命中接口，GamePlay 不反向依赖 AIECS Gameplay 程序集。延迟命中仍须核对身份和绑定代际；保留接口不代表完整混合 AI 感知/战斗已经验收。
- ECS 居民生命周期独立于 ChunkView；正式世界 BRG 只提交当前已绑定 ChunkView 内的主体与阴影。区块卸载只撤销本地表现，不删除居民或依赖“相机最近镜像”继续绘制未加载区块。
- GM 的 GameObject 图标来自当前 Actor 定义与外壳；仅显式 ECS 物种读取 BRG 图集。事件与 MOD 的 GUID 命令通过统一服务分发到 `IAIAdvanceCommandReceiver` 或 ECS；取消是可选接口，不破坏旧 MOD 接收器。
- GameObject Actor 可以组合 Lua Item 模块，MOD 定义不强制要求 `ecs.capabilities`；ECS 的能力完整性由实际 ECS 编译入口检查。
- GameObject 居民沿用 ItemData 与休眠快照；蜂巢独占保存所属蜜蜂的 GUID 和 BeeState。ECS 停用时保留 `EntitiesResidents` 冷快照，不清空、不自动转换，不把留存快照当作已恢复的 GameObject。
- 资源 ECS 与 AI 后端独立；蜜蜂采蜜通过 `NaturalEntityEcsService` 查询 ECS 作物，鸟类栖息地快照同时读取原生树木与 ECS 树木，禁止为资源重新创建完整 Item。
- F5 更换玩家时释放旧外部玩家代理，暂停无玩家期间的模拟，并在新玩家出现后重绑；不销毁本世界已存在的 ECS 居民。开发 Playground 与正式生态不可同时推进两个 World。

## 新增物种

1. 默认新增 Actor JSON，复用具备 `IAIActor` 的合法外壳并配置模块；需要动画时声明 Animator Controller，无动画 Actor 可直接保留外壳上的静态 Sprite。生成规则选择 `gameObject`，`config.id` 作为存档身份不能随意改名。
2. 只有用户明确选择的物种才配置 `entities`，再补齐 ECS 能力与图集并打开总开关；不能靠物种名硬编码后端，也不能直接全量启用。
3. 修改代码与配置后说明未完成项；Unity 编译、运行、画面与性能验收交给用户，不主动操作当前游戏现场。

## 易错边界

- 蜜蜂普通追击与护巢追击均受 `AngryLockRadius` 和 LOS 约束；丢失目标后按 `LostTargetAngerSeconds`（默认十秒）保留愤怒并搜索最后可见位置，到点等待，重获目标后重置计时，超时才清零。夜间归巢不能提前中断追击或搜索，蜂巢销毁后的独立蜜蜂不得调用领地巡逻。

- 动物食性与捕食关系使用 ItemData 的稳定数据 Tag：`Carnivore`、`Herbivore`、`Omnivore`。猎物感知捕食者统一查询 `Carnivore`，玩家作为独立威胁来源处理；不要按 `Wolf`/`Predator` 等物种名硬编码，更不能改回 Unity Tag。
- 感知使用 ECS 身份、几何、稀疏空间桶和整数格 LOS；建筑占地与地形阻挡要独立于导航可走性。移动前快照用于感知，移动后快照用于命中；Native 借用必须进入 Job 依赖链。
- 外部武器命中 ECS Actor 时，空间桶、阵营和 LOS 先筛候选，再临时投影受击 Collider2D 让 Physics2D 判接触；镜像代理是否已绑定不能影响命中资格，生命仍只由 ECS 结算。
- 正式模拟使用 60Hz 基础时钟与每实体距离脉冲。休眠/恢复不能追补全部离线 Tick；玩法系统查活跃实体，表现和统计可以查全部实体。
- 决策只选择意图，行为准备目标，攻击在 Active 阶段重新确认目标、朝向和 LOS。前摇、Active、后摇的时钟不能混用。
- 水深和水流从共享导航快照进入 `AiecsFlowAgent`；飞行态不接受地面水流推动。环境、觅食、产蛋、蜂群等托管桥只提交少量结算或外部数据，不允许逐实体恢复旧 AI 状态机。
- GameObject 鸟的 `BirdFlightNavigationProfile` 只检查飞行线路经过的地形是否已加载；它的区块寻址必须和 `ChunkMgr.ResolveWorldAddress` 使用同一份当前 `ActiveGenerationProfile` 区块尺寸，不能退回默认生成尺寸，否则自定义区块大小会让空中移动被误判为未加载。
- `AI_Base`、`Mod_AI_Bird`、`Module_AI_BehaviorGraph` 等原生脚本和 Prefab 是当前正式运行时，不能按“仅作者数据”删除或禁用。
- `AI_Base` 的头顶参数由 `AI_DebugOverlayRenderer` 集中绘制，关闭时停用唯一绘制器；模块加载注册，卸载/销毁注销，禁止给每个动物重新增加空转 `OnGUI`。状态采样读取运行时 `_currentState`，`AI_Chicken.Data.State` 仅在加载/保存时同步，不能当实时状态。
- 小鸡只按所属 `Scene.handle` 缓存场景名称，回池清空、重新加载或迁移后重建；产蛋、昼夜与禁睡仍实时读取本世界时钟，不缓存时间或用活动场景替代所属世界。
- 小鸡受伤后的禁睡期使用 `AI_ChickenSaveData` 保存的绝对游戏日截止值，直到下一次夜晚开始才解除；逃跑结束、低血量、跨午夜和远距重载都不能提前恢复睡眠。逃跑威胁记忆与睡眠警戒是两个独立状态。
- 鸟类产蛋由 `Mod_AI_Bird.EggLaying` 驱动，`AnimalEggLayingSchedule` 随个体保存绝对游戏日；成功落下一颗后重新计算至少两天的冷却，不能追补休眠或跳时积压。`Bird` 必须移除继承自鸡的通用生产模块，`Bee` 显式关闭产蛋；产物和随机延迟由 Actor JSON 配置，联机仅权威端生成。
- GameObject 鸟类捕鱼由 `Mod_AI_Bird.Predation` 驱动：饥饿时只锁定水中的 `Fish`，俯冲伤害必须走 `DamageReceiver`，低于抓取血线后通过 `IAquaticPredatorCarryTarget` 临时携带到可站立陆地，再继续攻击并消费死亡掉落的 `Meat`；携带状态不持久化，受惊、温度避险、进入疲劳降落或卸载时必须释放猎物。蜜蜂等注册独立 `IBirdFlightPilot` 的常驻飞行物种不进入该链路。

- 水生 GameObject AI 使用 `AquaticHabitat` 检查真实液体层与整段游动线路，不读取会被平台抹零的表面有效水深，不借用陆地可走性决定出生。未加载区块不是干地；个体饥饿、Buff 与受伤仍走原模块，离水伤害只在权威端结算。当前小鱼以潮湿层数作为缺水阈值，`minimumWetStacks=3`；水中潮湿沿用 Buff 通用的每秒 1 层节奏，不另外复制一套鱼专用计时器。鱼主动逃离 `Carnivore`、玩家及实现 `IAquaticPredatorThreat` 的水生捕食能力，逃生点必须保持整段水域可达。
- 当前生物 Actor 通过定义继承 `Module_Temperature`：普通生物默认安全体温 5~50℃，雪兔/雪豹等特殊物种直接在 Actor JSON 扩大范围，幽灵使用近似免疫的超宽范围。装备或其它系统只能通过体温模块的按来源范围修正扩展/收窄范围，不在各 AI 内复制阈值。
- 温度避险统一走 `ITemperatureSafetyMovement` 高优先级能力；普通陆地 AI 由 `Mod_Mover_AI` 实现，鸟和鱼因存在自定义飞行/游动直接实现同一能力。温度避险期间普通觅食、追击、游荡目标不得覆盖安全目标。
- 水下整身显示使用 `underwater-creature` 排序类别与地面、水面之间的材质队列，并给整身保留水下染色；保留受击 Trigger，但禁止实体碰撞和半身水线裁切。小鱼饱食度低于 90% 才吃嘴边现成食物，低于 70% 才在觅食半径内主动寻找；水中觅食须先过滤生境再选最近对象，不能让岸上最近食物挡住水中的食物。
- 鱼类主体保持水平侧视，不按二维游动向量整体旋转；左右移动按水平位移镜像，水下移动时允许对视觉子节点做轻微 Z 轴摆动。不要旋转根节点或碰撞体；不需要世界接触阴影和太阳长投影时，在 Actor 的 `visual.shadows.enabled` 显式设为 `false`。
- 鱼的近处食物扫描、进食计时和实际消费掉落物共用 `eatRange`；调范围时同步脚本默认值、Actor JSON、`Mod_AI_Fish` 模块 Prefab 与 `SmallFish` 外壳 Prefab，避免配置覆盖后仍使用旧距离。
- 鱼离水后由 `Mod_AI_Fish` 的 Stranded 状态间歇蹦跳回水，地面位移只在起跳期间推进，高度和甩身交给 `AquaticActorPresentation` 的视觉节点；回水搜索读取真实液体深度，陆地蹦跳逐段检查导航阻挡和未知区块。离水回水优先于温度游动；钓线与捕食者携带接管时清空蹦跳，卸载/对象池复用不得残留目标或视觉偏移。
- 鸟类离地后不保留实体阻挡碰撞，只保留 Trigger 感知/受击几何；否则位置驱动飞行会绕过质量结算，让蜜蜂等极轻生物推动玩家。近身攻击距离必须在实体碰撞前成立，不能要求攻击者先把目标顶开才能进入攻击范围。
- 鸟类巡航方向属于个体运行态，换目标、逃跑与边界避让都通过限角速度的圆弧转向；巡航半径由 Actor 参数配置，低速接近落点可以收小半径，蜜蜂等独立 Pilot 应显式配置自己的机动参数。弧线与实际位移共用一次地形查询上下文，未加载区块仍阻挡。
- 鸟类受击盒由 `Mod_AI_Bird` 按当前 Sprite 轮廓、镜像与变换对齐，保留在 `BirdLift` 下并使用独立运动学刚体；根刚体停用不能连带取消可见鸟的受击查询。升降和 Animator 更新后都要同步受击姿态，卸载时停用独立刚体并恢复盒子配置；空中死亡在 `DeathStarted` 收回视觉高度后再由生命模块统一掉落，禁止另扣血或另发死亡事件。

## 相关 Skill

- 改导航与 LOS：`flatworld-navigation`。
- 改伤害与身体部位：`flatworld-combat`。
- 改 Actor 定义、物品注册或存档：`flatworld-item-module`、`flatworld-data-save`。
- 改架构待办：`flatworld-dev-doc`。
