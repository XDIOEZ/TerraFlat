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
| `AI/Runtime/AiRuntimeBackendService.cs`、`AI/Spawning/MonsterSpawnerManager.cs` | 后端路由、生成、人数预算/远距休眠 |
| `AIECS/Gameplay/AiecsDefinitionCompiler.cs` | 合并 Actor/MOD 定义冷编译为纯值模板 |
| `AIECS/Core/AiecsSimulation.cs`、`AiecsCapabilities/AiecsCapabilitySystems.cs` | 感知、决策、移动、战斗能力查询 |
| `AIECS/Gameplay/AiecsEcologyRuntimeHost*.cs`、`AiecsGameplayBridge.cs` | 世界、蜂巢、快照、Unity 接口桥 |
| `AIECS/Presentation/AiecsWorldRenderer.cs`、`AiecsBatchRendererGroup.cs` | 可选 ECS 表现 |

Actor：`Assets/StreamingAssets/GameConfig/Actors/definitions/`；生成规则：`Assets/Resources/GameConfig/Spawners/Rules/`；ECS 图集：`Assets/6_Art/Generated/Actors/AIECS/`，Shader：`Assets/9_Shaders/Shader/AiecsSpriteLit.shader`。

## 后端与生命周期

- 当前全部正式物种（含 Zombie）走 GameObject；`useAiecsBackend=false`、条目为 `gameObject`。正式 Actor 的最终继承配置统一启用 SingleSprite；GO 使用真实模块与主动移动信号，由 ItemMgr 分级 Tick 和池化，`ecs` 是可选后端配置。
- 生成条目 `probability` 是物种相对权重，`spawnChance` 是抽中物种且地点有效后的独立概率（默认 1）；独立判定落空消耗一次排队机会，不重抽其它物种、不扣生态预算或累计出生数。小鸡单独配置 0.5，野猪保留原权重和默认概率。
- `ConfigureRoutes` 冻结物种归属，未列物种默认 GO；显式 ECS 失败不自动回退。同物种单一后端，`TrySpawnDirect` 统一 GM/事件/技能/MOD 生成；GO 返回前完成 Item.Load 并核对 IAIActor。
- Actor 合并定义的移动/检测/朝向模块须启用；`ModuleData.Enabled` 决定 Load/Tick，`Data.isRunning` 是奔跑。飞行能力 ID 为 `AI_Bird`，`prefab=Mod_AI_Bird` 仅指实现；内容错误码 `FWC-ACTORJSON-004/005` 对应基础模块开关/飞行 ID。
- 生物 `animator` 稳定槽位的能力 ID 固定为 `动画接收模块`（`ModText.AnimatorReceiver`），并须启用；`Module_Animator_AI` 等只作具体 Prefab 身份。`FWC-ACTORJSON-006` 检查最终继承配置；规则要先 `BindRuntimeIdentity` 保留显式 ID，再走 `ModuleInit→ItemMods.AddMod→RequireSingleModById`，不能仅凭原型类型或模式判装配成功。
- `WorldEntityRuntime` 独占共享 ECS World；AI 停用不影响树/作物。居民独立于 ChunkView，BRG 只显示已绑定 View 内居民，解绑不删除居民。
- GO 保存 ItemData/休眠快照；蜂巢独占所属蜜蜂 GUID/BeeState；停用 ECS 保留 EntitiesResidents 冷快照，不转换为 GO。`IsCreated` 判断 World 有效性，快照先采全再替换，失效保留冷快照，清理可重复。
- F5 换玩家撤旧代理，无玩家暂停模拟，新玩家重绑而保留居民；开发 Playground 与正式生态只推进一个 World。GamePlay 与 AIECS 通过独立桥/接口协作，延迟命中核对身份和绑定代际。
- 资源 ECS 独立于 AI 后端，蜜蜂采蜜查 `NaturalEntityEcsService`，栖息地合并 GO/ECS 树，不恢复完整资源 Item。Lua Actor 不要求 ECS 能力声明。
- 树木栖息地 ECS 记录在注册、移除与定义刷新时增量维护，两秒快照只遍历存活树；Profiler `NaturalEntityRuntime.TreeHabitat` / `FlatWorld.MonsterSpawner.TreeCandidates` 观察峰值。
- ECS 用 60Hz 基础时钟与距离脉冲，恢复不追补全部离线 Tick；移动前快照用于感知、移动后用于命中。决策选意图，Active 再确认目标/朝向/LOS，前摇/Active/后摇时钟独立。
- 风推动由 `WorldMotionSystem` 共用完整物品目录的质量规则；ECS 模板/居民保存当前定义的 `CurrentWeight`，资源发布后同步。世界桥采当地风速，Burst 只消费被动 `WindVelocity` 并扫掠通行；不写入主动速度/动画，无风时不扫描居民，风停当次清残留。
- GO 鸟/蜜蜂空中通过 `IWindMotionOwner` 独占风位移，地面交给 Mover；悬停也受风且风不改变主动飞行朝向。幽灵直追与无 Mover 导航分别叠风；鱼仅离水受风，正在蹦跳时同步平移起点，入水或被携带/钓住后停止。
- 感知用稳定身份、空间桶和整数格 LOS；地形/建筑遮挡独立于导航。外部武器经候选筛选、临时 Collider 投影和 Physics2D 命中，镜像绑定不影响受击资格；整体 HP 来自 Actor 配置，部位耐久独立，耗尽部位仍可命中。
- 同 detector 的同步候选批次用 `GridLineOfSightQuery` 冻结当前观察者与世界上下文，复用已加载区块寻址；每格仍读当前固定/动态遮挡位，结束即清引用，不跨帧缓存可见性。规则 `ai/line_of_sight_batch`；Profiler `FlatWorld.Perception.GridLineOfSight/ResolveSightChunk`。

## 行为能力与表现

- 单图共用 `Mod_AnimatorController`；鸟、蜂、鱼、幽灵通过 `IActorAnimationMotionSource` 报告主动移动与已有外观高度/倾角，风、水流等被动位移不计迈步。单图模式下旧身体 Animator 与鱼独立游动摇摆让出姿态，Ghost/Fish 的纯控制器不附带 Renderer 或物理组件；目录与窄信号回归见 AI/ItemRuntime 测试说明。
- 动物环境短音由可组合 `Mod_AnimalAmbientAudio` 承担；AI 仅通过 `IAnimalAmbientAudioActivity.AllowsAmbientAudio` 暴露平静待机、游荡或觅食资格，睡眠、战斗、逃离及鱼被携带/钓住时抑制。`AI_Base.IsAmbientAudioState` 与移动动画分类分开，行为图按 `wander/forage/idle` 节点语义判定；物种 Cue 与间隔写 Actor JSON，Zombie 显式移除从 Wolf 继承的 `animalAudio`。
- 食性使用稳定数据 Tag `Carnivore/Herbivore/Omnivore`；玩家独立为威胁。当前动物继承 `Module_Temperature`，安全范围在 Actor 配置；`ITemperatureSafetyMovement` 优先于追击/觅食/游荡，陆地由 Mod_Mover_AI、鸟/鱼由自定义移动实现。
- 温度避险不记录/回退安全点；记最多 8 个危险锚点 30 秒，以危险点指向角色的向量引导分段随机离开。陆地普通目标保留距离衰减偏置，线路接近危险记录则保持强偏置并走局部随机段；同一段目标缓存。安全环境持续 2 秒且距危险记录至少 3 格后才解除强制避险，体温未恢复则等待。
- 雪地专属白兔/雪豹在 `snow-actors.json` 配置初始体表 -10℃，安全范围分别 -30～20℃/-30～25℃。新增物种按栖息地与玩法定位配安全范围与初始体温，不在通用温度模块堆物种特例。
- 小鱼在 `fish-actors.json` 单独配置安全体表 -26.5～63.5℃，以初始 36.5℃ 为基准将冷热容差都翻倍；不改变其他动物的通用温度规则。
- `AI_DebugOverlayRenderer` 集中绘头顶信息；实时读 `_currentState`，`AI_Chicken.Data.State` 只在加载/保存同步。小鸡只缓存所属 Scene.handle 名称，时间实时读取；受伤禁睡保存绝对游戏日截止，逃跑记忆与睡眠警戒独立。
- GM 动物、蜜蜂和蜂巢头顶诊断共用 `AI_DebugOverlay.CanDrawWorldLabels`，正式背包/制作/设置等交互面板或手机抽屉打开时暂时让位，关闭后仍保留 GM 开关状态。
- GO AI 状态评估回调在实例内复用，不在每次判定时分配委托；调试头顶标签仅在 IMGUI Repaint 绘制并复用 GUIContent。
- 蜂巢首次先登记完整成员意图及稳定 GUID，普通首次/恢复/日出/繁殖共用每巢每帧一只的实例化配额；未完成意图和睡眠状态随存档保留，成功创建后才提交，死亡不重补初始成员。受击护巢绕过配额立即出巢，卸载不继续推进待生成工作。
- 蜜蜂追击/护巢受 AngryLockRadius/LOS 约束，丢目标按 LostTargetAngerSeconds 保持搜索最后可见位置，到点等待，重见重计时；夜间归巢不抢占追击，蜂巢销毁后无领地巡逻。
- 鸟飞行导航只查线路是否加载，区块尺寸来自当前 ActiveGenerationProfile。`IBirdFlightPilot` 可组合独立 Pilot；巡航/避让用限角速度圆弧，弧线与位移共用地形查询。
- 鸟离地撤实体阻挡，仅留感知/受击 Trigger；`BirdLift` 下独立运动学刚体承接受击盒，按当前 Sprite/镜像/动画姿态同步，卸载恢复配置。空中死亡先收视觉高度，再统一生命/掉落流程。
- `EggLaying/AnimalEggLayingSchedule` 保存绝对日，成功产蛋后冷却至少两天，不追补；Bird 无鸡生产模块、Bee 关产蛋。产物/延迟来自 Actor，只有权威端生成。
- `ContainerFeeding` 经允许 Animal 的种子端口交易一件真实物品，完整种子快照随鸟保存，死亡掉落成功后才清空。`Predation` 锁水中 Fish，走 DamageReceiver 后由 `IAquaticPredatorCarryTarget` 携到陆地，再攻击/吃 Meat；携带不保存，受惊/避险/疲劳/卸载释放。
- 水生 AI 的 `AquaticHabitat` 读真实 Liquid 和整段线路，不读平台投影液深；未知格不是干地。鱼共享 Buff 潮湿计时，缺水阈值 `minimumWetStacks=3`；逃离 Carnivore、玩家和 `IAquaticPredatorThreat`，线路须全程水域可达。
- 水生线路保留每 0.2 米采样，只合并同次调用中连续相同规范格的查询；每次重新读取水态，不跨帧缓存。鱼觅食筛选委托和捕食能力列表复用，模块结构变化仍自动失效。
- 鱼饱食低于 90% 吃近物、低于 70% 主动觅食，先筛水域后选最近；`eatRange` 共用于扫描/计时/消费。Stranded 间歇蹦跳回水，起跳期间推进地面位移，逐段检查阻挡/未知格；离水回水优先温度游动，钓线/携带接管清蹦跳。
- 鱼用 `underwater-creature` 整身染色，无实体阻挡/半身裁切，保留受击 Trigger；`AquaticActorPresentation` 保留水平翻面、水下排序与染色，单图摇摆及搁浅/携带的高度倾角交共用表现层，不旋根/碰撞体。无阴影物种在 `visual.shadows.enabled` 配置。
