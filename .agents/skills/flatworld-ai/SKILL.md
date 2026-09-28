---
name: flatworld-ai
description: "Use when: 定位或修改 FlatWorld 的动物、怪物、蜂群、AI 感知、决策、移动、战斗、生成、存档或批量表现。关键词：AIECS、AiecsSimulation、AiRuntimeBackendService、MonsterSpawnerManager。"
---

# FlatWorld AI

## 正式入口

- Actor 定义：`Assets/StreamingAssets/GameConfig/Actors/definitions/*.json`；使用 `ecs.capabilities` 组合能力，在对应 `ecs` 字段填写参数。
- 定义编译：`Entities/AIECS/Gameplay/AiecsDefinitionCompiler.cs`。解析当前合并的 Actor/MOD 目录，检查能力依赖并生成纯值模板；不要实例化旧 AI 来提取运行态。
- 批量模拟：`Entities/AIECS/Core/AiecsSimulation.cs`、`AiecsCapabilities.cs`、`AiecsCapabilitySystems.cs`。感知、决策、移动、战斗和可选能力按组件查询运行。
- 世界桥：`Entities/AIECS/Gameplay/AiecsEcologyRuntimeHost*.cs`、`AiecsGameplayBridge.cs`。负责正式世界生命周期、蜂巢、存档、旧 Unity 接口输入。
- 生成：`Entities/AI/Runtime/AiRuntimeBackendService.cs`、`Entities/AI/Spawning/MonsterSpawnerManager.cs`、`Resources/GameConfig/Spawners/Rules/*.json`。
- 表现：`Entities/AIECS/Presentation/AiecsWorldRenderer.cs`、`AiecsBatchRendererGroup.cs`、`Assets/9_Shaders/Shader/AiecsSpriteLit.shader`。动画目录在 `Assets/6_Art/Generated/Actors/AIECS/`。
- 设计文档：`开发文档文件夹/待实现功能文件夹/AI_ECS逻辑与GameObject镜像代理架构待办.md`。

## 运行边界

- 当前 Actor 物种由 `AiRuntimeBackendService` 统一路由到 Entities。普通刷怪、事件、生物战利品、蜂巢、GM 召唤、技能和 MOD 的生物出生都调用同一 ECS 后端；失败时明确拒绝，不回退旧 GameObject AI。`ItemMgr.InstantiateItem` 不再创建 Actor。
- `MonsterSpawnerManager` 只决定出生时间、群系、地形、位置与预算；`AiecsEcologyRuntimeHost` 持有 Entity、GUID、生命、行为和居民数量。GamePlay 只依赖 `IAiEcologyBackend`，不能反向依赖 AIECS Gameplay 程序集。
- 玩家保持 GameObject。镜像代理是可池化的 Unity 接口空壳；普通 AI 和绘制状态都以 ECS 为准。延迟命中须核对 GUID、Entity 与绑定代际，避免代理回池后误伤新生物。
- 生物身体由 BRG 图集绘制，阴影走共享批量表现。只在外部接口需要时启用代理碰撞体；不要在代理上挂旧 AI、逐实体 Animator 或逐实体绘制组件。
- GM 生物目录从 BRG 动画图集取图标；召唤在玩家附近寻找合法导航落点后调用正式 `TrySpawnDirect`，镜像代理由生态宿主绑定。
- 游戏事件按 ECS Actor GUID 下发推进命令；同一物品目标共享 Flow 目标，命令随居民快照保存，完成、死亡、取消及世界退出时释放句柄。MOD 用 `ModApi` 的 GUID 接口控制生物；Actor 的旧 Lua Item 模块不运行。
- 新存档使用 `MonsterSpawnerSaveData.EntitiesResidents` 保存普通 ECS 居民；蜂巢独占记录所属蜜蜂，避免同 GUID 重复恢复。旧 GameObject 生物快照无需迁移。
- F5 更换玩家时释放旧外部玩家代理，暂停无玩家期间的模拟，并在新玩家出现后重绑；不销毁本世界已存在的 ECS 居民。开发 Playground 与正式生态不可同时推进两个 World。

## 新增物种

1. 新增 Actor JSON，配置 `ecs.capabilities` 和 `ecs.health`。每种生物必须有 `movement` 和 `perception`；`feeding` 依赖 `nutrition`，`eggLaying` 依赖 `reproduction`。纯 ECS 新物种不需要旧 `IAIActor` 或 `DamageReceiver`。
2. 复用已有共享能力及参数。新行为无法表达时，新增组件和批处理系统，并在定义编译器中装配；不要增加一个物种专用 MonoBehaviour Tick。
3. 准备动画帧并重导 AIECS 图集，配置生成规则。新增规则使用独立 JSON；`config.id` 是存档身份，不能随意改名。
4. 静态检查定义和图集；玩法、画面、性能需要分别在真实世界验收，脚本编译不能代替这些结论。

## 易错边界

- 感知使用 ECS 身份、几何、稀疏空间桶和整数格 LOS；建筑占地与地形阻挡要独立于导航可走性。移动前快照用于感知，移动后快照用于命中；Native 借用必须进入 Job 依赖链。
- 正式模拟使用 60Hz 基础时钟与每实体距离脉冲。休眠/恢复不能追补全部离线 Tick；玩法系统查活跃实体，表现和统计可以查全部实体。
- 决策只选择意图，行为准备目标，攻击在 Active 阶段重新确认目标、朝向和 LOS。前摇、Active、后摇的时钟不能混用。
- 水深和水流从共享导航快照进入 `AiecsFlowAgent`；飞行态不接受地面水流推动。环境、觅食、产蛋、蜂群等托管桥只提交少量结算或外部数据，不允许逐实体恢复旧 AI 状态机。
- 旧 `AI_Base`、`AI_Bird`、`Module_AI_BehaviorGraph` 等脚本与 Prefab 可作为内容编译时的作者数据来源，不代表当前 Actor 运行时仍创建这些 GameObject。

## 相关 Skill

- 改导航与 LOS：`flatworld-navigation`。
- 改伤害与身体部位：`flatworld-combat`。
- 改 Actor 定义、物品注册或存档：`flatworld-item-module`、`flatworld-data-save`。
- 改架构待办：`flatworld-dev-doc`。
