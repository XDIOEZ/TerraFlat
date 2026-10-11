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

- 当前全部正式物种（含 Zombie）走 GameObject；`useAiecsBackend=false`、条目为 `gameObject`。GO 使用真实模块/Mod_Mover/Animator，由 ItemMgr 分级 Tick 和池化；`ecs` 是可选后端配置。
- `ConfigureRoutes` 冻结物种归属，未列物种默认 GO；显式 ECS 失败不自动回退。同物种单一后端，`TrySpawnDirect` 统一 GM/事件/技能/MOD 生成；GO 返回前完成 Item.Load 并核对 IAIActor。
- Actor 合并定义的移动/检测/朝向模块须启用；`ModuleData.Enabled` 决定 Load/Tick，`Data.isRunning` 是奔跑。飞行能力 ID 为 `AI_Bird`，`prefab=Mod_AI_Bird` 仅指实现；内容错误码 `FWC-ACTORJSON-004/005` 对应基础模块开关/飞行 ID。
- `WorldEntityRuntime` 独占共享 ECS World；AI 停用不影响树/作物。居民独立于 ChunkView，BRG 只显示已绑定 View 内居民，解绑不删除居民。
- GO 保存 ItemData/休眠快照；蜂巢独占所属蜜蜂 GUID/BeeState；停用 ECS 保留 EntitiesResidents 冷快照，不转换为 GO。`IsCreated` 判断 World 有效性，快照先采全再替换，失效保留冷快照，清理可重复。
- F5 换玩家撤旧代理，无玩家暂停模拟，新玩家重绑而保留居民；开发 Playground 与正式生态只推进一个 World。GamePlay 与 AIECS 通过独立桥/接口协作，延迟命中核对身份和绑定代际。
- 资源 ECS 独立于 AI 后端，蜜蜂采蜜查 `NaturalEntityEcsService`，栖息地合并 GO/ECS 树，不恢复完整资源 Item。Lua Actor 不要求 ECS 能力声明。
- ECS 用 60Hz 基础时钟与距离脉冲，恢复不追补全部离线 Tick；移动前快照用于感知、移动后用于命中。决策选意图，Active 再确认目标/朝向/LOS，前摇/Active/后摇时钟独立。
- 感知用稳定身份、空间桶和整数格 LOS；地形/建筑遮挡独立于导航。外部武器经候选筛选、临时 Collider 投影和 Physics2D 命中，镜像绑定不影响受击资格；整体 HP 来自 Actor 配置，部位耐久独立，耗尽部位仍可命中。

## 行为能力与表现

- 食性使用稳定数据 Tag `Carnivore/Herbivore/Omnivore`；玩家独立为威胁。当前动物继承 `Module_Temperature`，安全范围在 Actor 配置；`ITemperatureSafetyMovement` 优先于追击/觅食/游荡，陆地由 Mod_Mover_AI、鸟/鱼由自定义移动实现。
- `AI_DebugOverlayRenderer` 集中绘头顶信息；实时读 `_currentState`，`AI_Chicken.Data.State` 只在加载/保存同步。小鸡只缓存所属 Scene.handle 名称，时间实时读取；受伤禁睡保存绝对游戏日截止，逃跑记忆与睡眠警戒独立。
- 蜜蜂追击/护巢受 AngryLockRadius/LOS 约束，丢目标按 LostTargetAngerSeconds 保持搜索最后可见位置，到点等待，重见重计时；夜间归巢不抢占追击，蜂巢销毁后无领地巡逻。
- 鸟飞行导航只查线路是否加载，区块尺寸来自当前 ActiveGenerationProfile。`IBirdFlightPilot` 可组合独立 Pilot；巡航/避让用限角速度圆弧，弧线与位移共用地形查询。
- 鸟离地撤实体阻挡，仅留感知/受击 Trigger；`BirdLift` 下独立运动学刚体承接受击盒，按当前 Sprite/镜像/动画姿态同步，卸载恢复配置。空中死亡先收视觉高度，再统一生命/掉落流程。
- `EggLaying/AnimalEggLayingSchedule` 保存绝对日，成功产蛋后冷却至少两天，不追补；Bird 无鸡生产模块、Bee 关产蛋。产物/延迟来自 Actor，只有权威端生成。
- `ContainerFeeding` 经允许 Animal 的种子端口交易一件真实物品，完整种子快照随鸟保存，死亡掉落成功后才清空。`Predation` 锁水中 Fish，走 DamageReceiver 后由 `IAquaticPredatorCarryTarget` 携到陆地，再攻击/吃 Meat；携带不保存，受惊/避险/疲劳/卸载释放。
- 水生 AI 的 `AquaticHabitat` 读真实 Liquid 和整段线路，不读平台投影液深；未知格不是干地。鱼共享 Buff 潮湿计时，缺水阈值 `minimumWetStacks=3`；逃离 Carnivore、玩家和 `IAquaticPredatorThreat`，线路须全程水域可达。
- 鱼饱食低于 90% 吃近物、低于 70% 主动觅食，先筛水域后选最近；`eatRange` 共用于扫描/计时/消费。Stranded 间歇蹦跳回水，起跳期间推进地面位移，逐段检查阻挡/未知格；离水回水优先温度游动，钓线/携带接管清蹦跳。
- 鱼用 `underwater-creature` 整身染色，无实体阻挡/半身裁切，保留受击 Trigger；水平侧视只镜像视觉节点，不旋根/碰撞体，视觉摆动由 AquaticActorPresentation 处理。无阴影物种在 `visual.shadows.enabled` 配置。
