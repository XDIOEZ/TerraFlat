---
name: flatworld-ai
description: "FlatWorld 动物、怪物与蜂群的生成、GameObject AI、可选 AIECS、行为能力和居民生命周期。"
---

# AI

## 入口

代码根目录 `Assets/5_Scripts/5-3_GamePlay/Entities/`：

| 目录/文件 | 职责 |
| --- | --- |
| `AI/AI_Base.cs`、`Mod_AI_Bird*.cs`、`AI_Wolf.cs`、`Mod_BeeBehavior*.cs` | 正式 GameObject 主控 |
| `AI/Runtime/AiRuntimeBackendService.cs`、`AI/Spawning/MonsterSpawnerManager.cs` | 后端路由、统一生物出生事务、人数预算/远距休眠 |
| `AIECS/Gameplay/AiecsDefinitionCompiler.cs` | 合并 Actor/MOD 定义冷编译为纯值模板 |
| `AIECS/Core/AiecsSimulation.cs`、`AiecsCapabilities/AiecsCapabilitySystems.cs` | 感知、决策、移动、战斗能力查询 |
| `AIECS/Gameplay/AiecsEcologyRuntimeHost*.cs`、`AiecsGameplayBridge.cs` | 世界、蜂巢、快照、Unity 接口桥 |
| `AIECS/Presentation/AiecsWorldRenderer.cs`、`AiecsBatchRendererGroup.cs` | 可选 ECS 表现 |

Actor：`Assets/StreamingAssets/GameConfig/Actors/definitions/`；生成规则：`Assets/Resources/GameConfig/Spawners/Rules/`；ECS 图集：`Assets/6_Art/Generated/Actors/AIECS/`，Shader：`Assets/9_Shaders/Shader/AiecsSpriteLit.shader`。

## 后端与生命周期

- F4 → 生成与 AI → 世界生物信息读取 `MonsterSpawnerManager.Monitoring.cs` 快照；按本地玩家世界查询现有管理器/后端，不补建调度状态。倒计时/上限为真实游戏秒，调度/重试/最近尝试年龄为现实秒；生物合计含当前世界存活 GO、ECS 和已索引休眠居民，排除玩家/尸体/创建候选。仅可见时采样，真实生成分支只记录最近结果，不重新选点或抽概率；只读计时回归 `ai/spawn_monitor`。

- `MonsterSpawnerManager.Update` 已初始化且未到生态 Tick、也无分帧出生任务时，在进入世界上下文前返回；初始化和出生任务仍及时推进。`FlatWorld.MonsterSpawner.Advance` 包含世界作用域进出，Tick/Create 继续细分实际工作；回归 `ai/spawner_update_work`。

- **追近不等于动作**：共用 `FlatWorld.Navigation.AIApproachRange`，停止内圈取配置停止距离与动作范围一半中的较小值。地面动作目标必须用 `Mod_Mover_AI.SetInteractionDestination`（可带路径代价上限），不覆写普通 `stopDistance`；巡逻、温度避险保持各自配置。鱼 `forageStopDistance=0.3`、`eatRange=0.9`，追近过程中仍按实际距离累计摄食，越界或换目标才清零。鸟啄食、行为图、鸡/野猪、狼/雪豹、蜜蜂使用同一内外圈原则；只有真正攻击前摇/伤害窗口/后摇或技能锁定才禁止移动，冷却不强制停车。狼编队、野猪左右站位及可选 ECS 站位必须把目标偏移和停止余量一同算入动作范围。禁止通过放大进食/伤害范围掩盖提前停车。离线真实鱼方法回归：`python Tools/Tests/AiApproachRanges/verify.py`；Lua 规则：`ai/approach_ranges`。

- `AiRuntimeBackendService` 的 GO Actor 身份、前进与取消命令查询使用 `ListPool<MonoBehaviour>` 和 `GetComponentsInChildren(true, list)`，避免每次查询创建子组件数组；指令先按原顺序检查绑定到本 Item 的真实 Actor，再用同一份即时组件列表寻找首个接收器。列表调用结束即回池，不缓存模块结构/代际，不改变 ECS 后端回退和死亡拒绝规则。
- ECS 宿主/后端路由按 WorldKey 独立，观察与战斗代理包含该世界所有真实玩家；距离档取最近玩家，不能只跟主机视角。联机客户端不 Tick 权威生态，`AiEcologyNetworkBridge` 将正式表现快照按连接 WorldKey/RealmRevision 分片同步，完整包才替换 BRG 列表，移动按世界最短位移插值；离开世界/断线释放副本缓存，死亡/掉落仍由服务器提交。

- GO 自然出生通过 `SpawnCreatureInSteps` 单任务分帧：池未命中异步复制外壳，逐模块配置/依赖/Load，每帧目标 1.5ms、最多 8 步；单个模块不可抢占。候选停用且未登记，完成后复核落点/人数/预算再交付，成功才记账；退出、失效或取消释放候选。显式即时出生与居民恢复仍保留同步入口。
- 当前全部正式物种（含 Zombie）走 GameObject；`useAiecsBackend=false`、条目为 `gameObject`。正式 Actor 的最终继承配置统一启用 SingleSprite；GO 使用真实模块与主动移动信号，由 ItemMgr 分级 Tick 和池化，`ecs` 是可选后端配置。
- 单图首帧必须配置 `visual.spriteAddress`，使用已注册 Addressables 的 `Assets/...png[切片名]`；摇摆初始化直接读取定义 Sprite，不启动旧 Animator、不依赖 Idle/Stand 状态。共享狼外壳的 Zombie 必须覆盖首帧。内容校验 `FWC-ACTORJSON-007/008` 检查模式与首帧；静态入口 `python Tools/Tests/ActorSway/verify.py`，正式规则 `ai/single_sprite_catalog`。
- 生成条目 `probability` 是物种相对权重，`spawnChance` 是抽中物种且地点有效后的独立概率（默认 1）；独立判定落空消耗一次排队机会，不重抽其它物种、不扣生态预算或累计出生数。小鸡单独配置 0.5，野猪保留原权重和默认概率。
- 所有自然生物（含夜怪）共用世界存档 `MonsterSpawnerSaveData.NaturalCountdown`；`spawner-settings.json/naturalSpawnIntervalGameSeconds` 是唯一基础刷新间隔，频率倍率统一缩放这一时钟，不再派生物种时钟；固定数量批次保留原有倍率规则，显式数量权重不缩量。禁止恢复逐物种/逐规则时钟、固定日窗口、逐日补生或死亡即时补员。到期后 `NaturalSelection` 只采样当前世界玩家附近已就绪区块，沿正式水陆/树木/群系/地块/光照/可见性/预算/人数规则筛选，再把跨配置合格物种按 `probability` 放进同一奖池；权重不换算间隔。没有合格落点时保持到期、尚未指定物种，不加载新区域。
- 一轮只绑定一个规则/物种及其批次数量。规则概率落空消费整轮，物种概率落空消费一次出生；不重抽直到成功、不扣失败预算。完成整批才重启共享时钟；有限选点/创建重试耗尽则放弃未出生部分并重计时，防止失效栖息地永久锁住全世界刷新。跨区块不重置，读档/无玩家/频率为零不补时间；所选物种、剩余批次与锚点随世界一起保存。事件/GM/孵化/蜂巢和居民恢复不消费自然时钟。监控只显示一个世界倒计时，未抽中物种没有待出生队列。离线生产调度回归 `python Tools/Tests/NaturalSpawnCountdown/verify.py`；Unity 规则 `ai/spawn_countdown`、`ai/spawn_monitor`、`ai/spawn_batches`。出生、显隐、居民装卸三路仍轮转。
- `spawnCountWeights` 可声明每批数量权重（显式数量不随频率缩量）；狼 1/2 只各 5%、3/4 只各 45%，`batchSpawnRadius=3`。首只前预检整批剩余人数/预算，成功后逐帧一只落在同批锚点附近且不重复占格；锚点随进度保存，离开玩家范围/换场景/锚区块卸载才重选，下一批重新选锚。规则 `ai/spawn_batches`。
- `MonsterSpawnerManager.TrySpawnCreature` 是自然/夜怪、事件、GM/技能/MOD、生物战利品、生产孵化与蜂巢的新出生入口；`AiRuntimeBackendService.TrySpawnDirect` 仅转发给已存在管理器。来源保留时间/落点/预算规则，通用事务检查权威、世界就绪、GUID（含休眠/创建中身份），统一装配、Load、回调后身份/代际复核与失败回收。自然调度关闭不禁用显式出生；ItemMgr 仍是物品/网络副本的底层工厂，不反向依赖生物管理器。
- `ConfigureRoutes` 冻结物种归属，未列物种默认 GO；显式 ECS 失败不自动回退。后端只通过 `IAiEcologyBackend.TryCreateActor` 接受管理器批准的实际创建，GO 专用初始化回调不能用于 ECS。快照与蜂巢老成员分别走 `TryRestoreCreature/TryRestoreResident`，恢复不消费出生计划/预算、不增加新生统计；同物种保持单一后端。
- Actor 合并定义的移动/检测/朝向模块须启用；`ModuleData.Enabled` 决定 Load/Tick，`Data.isRunning` 是奔跑。飞行能力 ID 为 `AI_Bird`，`prefab=Mod_AI_Bird` 仅指实现；内容错误码 `FWC-ACTORJSON-004/005` 对应基础模块开关/飞行 ID。
- 生物 `animator` 稳定槽位的能力 ID 固定为 `动画接收模块`（`ModText.AnimatorReceiver`），并须启用；`Module_Animator_AI` 等只作具体 Prefab 身份。`FWC-ACTORJSON-006` 检查最终继承配置；规则要先 `BindRuntimeIdentity` 保留显式 ID，再走 `ModuleInit→ItemMods.AddMod→RequireSingleModById`，不能仅凭原型类型或模式判装配成功。
- `WorldEntityRuntime` 独占共享 ECS World；AI 停用不影响树/作物。居民独立于 ChunkView，BRG 只显示已绑定 View 内居民，解绑不删除居民。
- GO 保存 ItemData/休眠快照；蜂巢独占所属蜜蜂 GUID/BeeState；停用 ECS 保留 EntitiesResidents 冷快照，不转换为 GO。`IsCreated` 判断 World 有效性，快照先采全再替换，失效保留冷快照，清理可重复。
- F5 换玩家撤旧代理，无玩家暂停模拟，新玩家重绑而保留居民；开发 Playground 固定出生 WorldKey，换层只撤玩家代理并隐藏原 BRG/血条，不清居民或自动重生。同一世界的开发与正式生态互斥，暂停、掉落与清理均借所属世界，不影响其他世界。GamePlay 与 AIECS 通过独立桥/接口协作，延迟命中核对身份和绑定代际。
- 资源 ECS 独立于 AI 后端，蜜蜂采蜜查 `NaturalEntityEcsService`，栖息地合并 GO/ECS 树，不恢复完整资源 Item。Lua Actor 不要求 ECS 能力声明。
- 树木栖息地两秒候选快照复用一次 `WorldTopologyDomain` 与 `ChunkMgr.RuntimeTerrainPresenceQuery`，只读真实 BiomeId，避免逐树重新解析世界/完整 Tile；最终随机落点仍实时检查原规则。回归为 `ai/tree_habitat_batch`。
- 树木栖息地 ECS 记录在注册、移除与定义刷新时增量维护，两秒快照只遍历存活树；Profiler `NaturalEntityRuntime.TreeHabitat` / `FlatWorld.MonsterSpawner.TreeCandidates` 观察峰值。
- ECS 用 60Hz 基础时钟与距离脉冲；脉冲直接按当前位置查询本世界 Native 导航 Chunk 索引，未知块休眠，跨块不粘出生块，View 解绑不删居民。整世界无人也显式冻结脉冲，恢复平移攻击/Buff/能力与尸体期限，繁殖按游戏日平移，不补算离线 Tick。规则 `ai/ecs_resident_ownership` 与 `dimension/cave_roundtrip` 覆盖归块及真实换层身份。移动前快照用于感知、移动后用于命中，前摇/Active/后摇时钟独立。
- 风推动由 `WorldMotionSystem` 共用完整物品目录的质量规则；ECS 模板/居民保存当前定义的 `CurrentWeight`，资源发布后同步。世界桥采当地风速，Burst 只消费被动 `WindVelocity` 并扫掠通行；不写入主动速度/动画，无风时不扫描居民，风停当次清残留。
- GO 鸟/蜜蜂空中通过 `IWindMotionOwner` 独占风位移，地面交给 Mover；悬停也受风且风不改变主动飞行朝向。幽灵直追与无 Mover 导航分别叠风；鱼仅离水受风，正在蹦跳时同步平移起点，入水或被携带/钓住后停止。
- 感知用稳定身份、空间桶和整数格 LOS；地形/建筑遮挡独立于导航。外部武器经候选筛选、临时 Collider 投影和 Physics2D 命中，镜像绑定不影响受击资格；整体 HP 来自 Actor 配置，部位耐久独立，耗尽部位仍可命中。
- GO 感知最终半径受 `Item.MaximumPerceptionRadius` 通用外部上限约束，批查询在目标倍率后钳制，旧候选/锁定目标也实时受限；雾的施加与撤销归 `WeatherMgr.Fog`，AI 不查询天气。幽灵直查玩家同样钳制；回归 `environment/fog_perception`。
- Actor 感知冻结时同轮仿射变换一次性写入 Burst 形状与粗筛包围盒，保留复杂形状、负缩放和独立查询的相同包围语义；规则 `ai/perception_shape_batch`。
- 狼的目标筛选与通用 Detector 最近物查询在单次遍历内最多获取一次 `WorldTopologyDomain`，无匹配对象时不解析世界边界；逐候选仍实时读位置和阵营，通用入口供鸡、野猪使用，规则 `ai/closest_target_batch` 覆盖接缝、同距、玩家和移动目标。
- `Mod_ItemDetector.ApplyDetectorResults` 仅在 `DebugMode` 开启时构造旧候选差集并比较进入/离开，普通批次保留去重、顺序和原标签 List 引用；关闭 Debug 时清理旧集合，回归 `ai/detector_apply_batch`。
- `ItemMgr.QueueDetectorQuery` 用本批 `Dictionary<Mod_ItemDetector,int>` 定位已有请求槽位，重复申请只更新最新版本且保持首次顺序；批次消费后清空索引，不跨批缓存检测器。
- `ItemMgr.Perception.Apply` 对请求版本已过期的检测器跳过候选精筛与 LOS，但仍逐项消费其 NativeStream 索引并结束当前流；有效请求和缺失检测器按原规则处理，回归 `ai/stale_perception_batch`。
- 同 detector 的同步候选批次用 `GridLineOfSightQuery` 冻结观察者与世界上下文；每条射线首次进入区块时验证加载态，射线内复用地形引用、逐格实时读取遮挡，下一条射线重新验证，防止沿用卸载/重建区块。规则 `ai/line_of_sight_batch`；Profiler `FlatWorld.Perception.GridLineOfSight/ResolveSightChunk`。
- 视线遇阻即返回不可见，仅在整条射线无遮挡时检查终点是否加载；起点、拐角侧格和中途区块照常验证。
- 水平和垂直的整数格 LOS 用直线逐格扫描代替通用 Bresenham 分支；中途仍实时检查遮挡，起终点豁免和环绕/区块加载语义保持，`ai/line_of_sight_batch` 覆盖两轴正负方向与接缝。

## 行为能力与表现

- ECS 生物由 `AiecsSwayAnimation` 在共享首图上合成摇摆、呼吸、定向攻击和死亡翻转，BRG 主体与太阳阴影共用结果；动作按完整身份保存，联机副本用 GUID 建立本地身份。目录每状态只留首图与定义基础姿态，参数沿用正式动画模块；鸟类保留外壳状态贴图与真实高度。导出器不再读取 Animator 时间轴；规则 `ai/ecs_sway_animation`，PlayMode 中不重导或刷新。

- 狼普通攻击距离2.1米、前摇0.25秒，真实命中盒按原尺寸放大50%；`biteAttackSkill` 组合通用撕咬。活动技能保留Attack状态并禁止普通攻击起手；继承狼外壳的Zombie/SnowLeopard显式移除该模块，由自身定义决定是否复用。

- 狼 `ai` 槽位的能力 ID 必须为 `AI`，`Module_AI_Wolf` 仅为 Prefab 身份；组群、呼叫和编队只接纳已就绪、启用、存活且友好的狼，按持久 Guid 选同一头狼。两只起协同追击，独狼避让、双狼低血撤退规则保留；回归 `ai/wolf_pack` 使用正式定义注册身份后调用真实决策。
- 狼的一轮状态评估先筛选有效同伴，再复用同一列表计算追击编队；新一轮和卸载时清空，支援呼叫仍实时复核资格。
- 狼追击丢失视野进入 `Search`：只去 `AI_Wolf.TargetMemory` 保存的最后可见/同伴报告坐标，默认赶路上限15秒、到点搜索3秒，重见恢复追击；旧感知候选须重新核验LOS，不更新隐藏目标坐标或起手攻击。集火与有效支援呼叫立即允许保留追踪记忆，不能等待首次状态切换后才生效。未见目标的同伴不传播情报；目标身份/代际、卸载和指令取消清记忆。规则 `ai/wolf_target_memory`；离线入口/记忆回归 `python Tools/Tests/AiTargetMemory/verify.py`。
- 狼群追击编队单次刷新冻结 `WorldTopologyDomain`，左右翼分配、同伴分离和站位归一化共用这份拓扑；同伴位置和路径可走性仍实时读取，回归 `ai/wolf_formation_topology`。
- 狼在攻击距离内即可起手，编队点不作为攻击门槛；站位默认进入 0.3 米内停车、偏离 0.6 米才恢复，目标离开攻击范围立即追击。保持状态跨编队刷新和追击/攻击切换，脱战或换目标清除；只有中断未完成攻击才重计冷却。规则 `ai/wolf_combat_position` 通过正式节点检查出手、漂移、追击和冷却，真实惯性与伤害仍需现场验收。
- 单图共用 `Mod_AnimatorController`；鸟、蜂、鱼、幽灵通过 `IActorAnimationMotionSource` 报告主动移动与已有外观高度/倾角，风、水流等被动位移不计迈步。单图模式下旧身体 Animator 与鱼独立游动摇摆让出姿态，Ghost/Fish 的纯控制器不附带 Renderer 或物理组件；目录与窄信号回归见 AI/ItemRuntime 测试说明。
- 单图死亡由动画模块订阅生命 `DeathStarted`：冻结致死瞬间 Sprite，上抛并翻转 180° 后保持；`deathDuration/deathJumpHeight` 由 Actor 根定义并继承，正式 AI 统一 0.76 秒/0.35 米。生命 `Data.DestroyDelay=1.1` 留出落地停尸时间；死亡/掉落/回收仍归生命模块，动画不自行删物。
- 尸体保留期间 AI 基类停止行为并取消动物技能，行为图退出当前节点，幽灵停止主动直追；`Mod_Mover_AI` 清主动速度并拒绝普通/温度避险目标，继续原有被动风/水流。新生命经正式 `Load` 恢复 AI；`Mod_Damage` 只依本物品的生命立即关窗，不按 Owner 关闭已发射投射物。
- 鸟/海鸥的单图状态经 `Mod_AI_Bird.ResolveAnimationStateName` 选择 Ground/Walk/TakingOff/Flying/Landing，具体 Sprite 配在外壳动画模块的 `singleSpriteStates`，使用各状态 `_0` 首帧并继续程序摇摆；`BirdActorAssetBuilder` 重建时保留同一映射。飞行 AI 只发状态，不在通用动画中识别物种。
- 动物环境短音由可组合 `Mod_AnimalAmbientAudio` 承担；AI 仅通过 `IAnimalAmbientAudioActivity.AllowsAmbientAudio` 暴露平静待机、游荡或觅食资格，睡眠、战斗、逃离及鱼被携带/钓住时抑制。`AI_Base.IsAmbientAudioState` 与移动动画分类分开，行为图按 `wander/forage/idle` 节点语义判定；物种 Cue 与间隔写 Actor JSON，Zombie 显式移除从 Wolf 继承的 `animalAudio`。
- 食性使用稳定数据 Tag `Carnivore/Herbivore/Omnivore`；玩家独立为威胁。当前动物继承 `Module_Temperature`，安全范围在 Actor 配置；`ITemperatureSafetyMovement` 优先于追击/觅食/游荡，陆地由 Mod_Mover_AI、鸟/鱼由自定义移动实现。
- 温度避险不记录/回退安全点；记最多 8 个危险锚点 30 秒，以危险点指向角色的向量引导分段随机离开。陆地普通目标保留距离衰减偏置，线路接近危险记录则保持强偏置并走局部随机段；同一段目标缓存。安全环境持续 2 秒且距危险记录至少 3 格后才解除强制避险，体温未恢复则等待。
- 雪地专属白兔/雪豹在 `snow-actors.json` 配置初始体表 -10℃，安全范围分别 -30～20℃/-30～25℃。新增物种按栖息地与玩法定位配安全范围与初始体温，不在通用温度模块堆物种特例。
- 小鱼在 `fish-actors.json` 单独配置安全体表 -26.5～63.5℃，以初始 36.5℃ 为基准将冷热容差都翻倍；不改变其他动物的通用温度规则。
- `AI_DebugOverlayRenderer` 集中绘头顶信息；实时读 `_currentState`，`AI_Chicken.Data.State` 只在加载/保存同步。小鸡只缓存所属 Scene.handle 名称，时间实时读取；受伤禁睡保存绝对游戏日截止，逃跑记忆与睡眠警戒独立。
- GM 动物、蜜蜂和蜂巢头顶诊断共用 `AI_DebugOverlay.CanDrawWorldLabels`，正式背包/制作/设置等交互面板或手机抽屉打开时暂时让位，关闭后仍保留 GM 开关状态。
- GO AI 状态评估回调在实例内复用；普通动物、蜜蜂、蜂巢共用 AI_DebugOverlayRenderer，仅在 IMGUI Repaint 绘制，蜂巢保留独立开关。普通动物文字按真实状态、语言和 Item 代际复用，Buff 每帧核对名称/ID、永久态与显示秒数，显示值未变时不重建列表或格式化；尺寸随文字/样式/字体变化重算，屏外不提交 GUI，回归 `ai/debug_overlay_cache`。
- 蜂巢首次先登记完整成员意图及稳定 GUID，普通首次/恢复/日出/繁殖共用每巢每帧一只的实例化配额；未完成意图和睡眠状态随存档保留，成功创建后才提交，死亡不重补初始成员。受击护巢绕过配额立即出巢，卸载不继续推进待生成工作。
- 蜜蜂追击/护巢受 AngryLockRadius/LOS 约束，丢目标按 LostTargetAngerSeconds 保持警戒搜索：先到最后可见位置，再在附近持续巡飞，不读取隐藏目标新位置；重见恢复追击，夜间归巢不抢占搜索，蜂巢销毁后同样可搜索。规则 `ai/bee_target_search` 采正式搜索计时、目的地与清理，不覆盖真实飞行和感知现场。
- 鸟飞行导航只查线路是否加载，区块尺寸来自当前 ActiveGenerationProfile。`IBirdFlightPilot` 可组合独立 Pilot；巡航/避让用限角速度圆弧，弧线与位移共用地形查询。
- 鸟前视弧线跨连续小段复用规范格游标，只合并纯地形存在查询；实际位移、下一条弧线及地面复合约束重新复核。落点环扫共用本轮拓扑和地形寻址，逐候选实时 `CanLand`；规则 `ai/flight_traversal_batch`。
- 鸟离地撤实体阻挡，仅留感知/受击 Trigger；`BirdLift` 下独立运动学刚体承接受击盒，按当前 Sprite/镜像/动画姿态同步，卸载恢复配置。空中死亡先收视觉高度，再统一生命/掉落流程。
- 鸟的 `LiftRoot` 高度未变化时不重复写 Transform；飞行受击盒按 Sprite/翻面/相对矩阵与现有几何尺寸检测变化，未变时跳过 4 角投影，运动学刚体姿态仍每次同步。规则 `ai/flight_hitbox_geometry_cache`。
- `EggLaying/AnimalEggLayingSchedule` 保存绝对日，成功产蛋后冷却至少两天，不追补；Bird 无鸡生产模块、Bee 关产蛋。产物/延迟来自 Actor，只有权威端生成。
- `ContainerFeeding` 经允许 Animal 的种子端口交易一件真实物品，完整种子快照随鸟保存，死亡掉落成功后才清空。`Predation` 锁水中 Fish，走 DamageReceiver 后由 `IAquaticPredatorCarryTarget` 携到陆地，再攻击/吃 Meat；携带不保存，受惊/避险/疲劳/卸载释放。
- 水生 AI 的 `AquaticHabitat` 读真实 Liquid 和整段线路，不读平台投影液深；未知格不是干地。鱼共享 Buff 潮湿计时，缺水阈值 `minimumWetStacks=3`；逃离 Carnivore、玩家和 `IAquaticPredatorThreat`，线路须全程水域可达。
- 小鱼自然刷新同时允许 `海洋/河流`，仍受 `waterOnly` 的真实水域限制；`Mod_AI_Fish` 通过 `IWaterCurrentExposure=false` 声明水流免疫，`water-current.json` 的 `SmallFish.pushSpeed=0` 供统一 GO/ECS 推动目录复用，不在水流核心识别鱼物种。
- 水生线路保留每 0.2 米采样，只合并同次调用中连续相同规范格的查询；每次重新读取水态，不跨帧缓存。鱼觅食筛选委托和捕食能力列表复用，模块结构变化仍自动失效。
- 鱼饱食低于 90% 吃近物、低于 70% 主动觅食，先筛水域后选最近；`eatRange` 共用于扫描/计时/消费。Stranded 间歇蹦跳回水，起跳期间推进地面位移，逐段检查阻挡/未知格；离水回水优先温度游动，钓线/携带接管清蹦跳。
- 鱼用 `underwater-creature` 整身染色，无实体阻挡/半身裁切，保留受击 Trigger；`AquaticActorPresentation` 保留水平翻面、水下排序与染色，单图摇摆及搁浅/携带的高度倾角交共用表现层，不旋根/碰撞体。无阴影物种在 `visual.shadows.enabled` 配置。
