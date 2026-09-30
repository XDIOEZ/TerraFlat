---
name: flatworld-ai
description: "Use when: 定位或修改 FlatWorld 的动物、怪物、蜂群、AI 感知、决策、移动、战斗、生成、存档或批量表现。关键词：AIECS、AiecsSimulation、AiRuntimeBackendService、MonsterSpawnerManager。"
---

# FlatWorld AI

## 正式入口

- Actor 定义：`Assets/StreamingAssets/GameConfig/Actors/definitions/*.json`；默认使用 GameObject 外壳、`modules` 和 Animator。`ecs` 配置仅供可选 ECS 后端使用。
- GameObject 主控：`Entities/AI/AI_Base.cs`、`AI_Bird.cs`、`AI_Wolf.cs`、`Mod_BeeBehavior*.cs`；生命周期、分级 Tick 与对象池由 `ItemMgr` 驱动。
- 定义编译：`Entities/AIECS/Gameplay/AiecsDefinitionCompiler.cs`。解析当前合并的 Actor/MOD 目录，检查能力依赖并生成纯值模板；不要实例化旧 AI 来提取运行态。
- 批量模拟：`Entities/AIECS/Core/AiecsSimulation.cs`、`AiecsCapabilities.cs`、`AiecsCapabilitySystems.cs`。感知、决策、移动、战斗和可选能力按组件查询运行。
- 世界桥：`Entities/AIECS/Gameplay/AiecsEcologyRuntimeHost*.cs`、`AiecsGameplayBridge.cs`。负责正式世界生命周期、蜂巢、存档、旧 Unity 接口输入。
- 生成：`Entities/AI/Runtime/AiRuntimeBackendService.cs`、`Entities/AI/Spawning/MonsterSpawnerManager.cs`、`Resources/GameConfig/Spawners/Rules/*.json`。
- 表现：`Entities/AIECS/Presentation/AiecsWorldRenderer.cs`、`AiecsBatchRendererGroup.cs`、`Assets/9_Shaders/Shader/AiecsSpriteLit.shader`。动画目录在 `Assets/6_Art/Generated/Actors/AIECS/`。
- 设计文档：`开发文档文件夹/待实现功能文件夹/AI_ECS逻辑与GameObject镜像代理架构待办.md`。

## 运行边界

- 当前所有物种（包括 Zombie）均由完整 GameObject AI 主控；`spawner-settings.json` 的 `useAiecsBackend=false`，全部正式生成条目为 `gameObject`。未经用户再次要求，不执行旧文档中的全量 AI ECS 迁移。
- 可选 ECS World 的唯一所有者是 `WorldEntityRuntime`，`AiecsSimulation` 只处理自身 AI 查询。停用 ECS AI 不能停用树木、作物等资源实体，也不能销毁共享 World；独立 World 仅用于诊断。
- 退出或脚本域重载时，Unity 可能先销毁 World；居民快照与外部查询必须检查 `AiecsSimulation.IsCreated`，不能只判 Bridge 非空。快照先完整采集再替换，失效时保留冷快照；宿主在 `OnDisable` 释放自有资源，重复释放必须安全，快照失败不能阻断 BRG、镜像和 Native 内存清理。

- `AiRuntimeBackendService.ConfigureRoutes` 按生成条目冻结物种归属，未列出的物种默认 GameObject；同一物种不能配置两个后端。显式 ECS 物种停用或失败时不静默回退。`ItemMgr.InstantiateItem` 只拒绝显式 ECS 物种。
- `MonsterSpawnerManager` 恢复 GameObject 生成、营养初始化、人数预算及远距休眠；GM、事件、技能和 MOD 的生物生成复用 `AiRuntimeBackendService.TrySpawnDirect`。GameObject 返回前必须完成 `Item.Load` 并核对 `IAIActor.ActorItem`。
- GameObject AI 使用真实模块、Mover、Animator 与表现组件，不是镜像空壳。只有显式 ECS 生物才用池化镜像与 BRG；两套后端不得同时接管同一生物。
- 保留 `IAiEcologyBackend`、`AiecsGameplayBridge`、镜像及命中接口，GamePlay 不反向依赖 AIECS Gameplay 程序集。延迟命中仍须核对身份和绑定代际；保留接口不代表完整混合 AI 感知/战斗已经验收。
- ECS 居民生命周期独立于 ChunkView；正式世界 BRG 只提交当前已绑定 ChunkView 内的主体与阴影。区块卸载只撤销本地表现，不删除居民或依赖“相机最近镜像”继续绘制未加载区块。
- GM 的 GameObject 图标来自当前 Actor 定义与外壳；仅显式 ECS 物种读取 BRG 图集。事件与 MOD 的 GUID 命令通过统一服务分发到 `IAIAdvanceCommandReceiver` 或 ECS；取消是可选接口，不破坏旧 MOD 接收器。
- GameObject Actor 可以组合 Lua Item 模块，MOD 定义不强制要求 `ecs.capabilities`；ECS 的能力完整性由实际 ECS 编译入口检查。
- GameObject 居民沿用 ItemData 与休眠快照；蜂巢独占保存所属蜜蜂的 GUID 和 BeeState。ECS 停用时保留 `EntitiesResidents` 冷快照，不清空、不自动转换，不把留存快照当作已恢复的 GameObject。
- 资源 ECS 与 AI 后端独立；蜜蜂采蜜通过 `NaturalEntityEcsService` 查询 ECS 作物，鸟类栖息地快照同时读取原生树木与 ECS 树木，禁止为资源重新创建完整 Item。
- F5 更换玩家时释放旧外部玩家代理，暂停无玩家期间的模拟，并在新玩家出现后重绑；不销毁本世界已存在的 ECS 居民。开发 Playground 与正式生态不可同时推进两个 World。

## 新增物种

1. 默认新增 Actor JSON，复用具备 `IAIActor` 的合法外壳，配置模块与 Animator；生成规则选择 `gameObject`，`config.id` 作为存档身份不能随意改名。
2. 只有用户明确选择的物种才配置 `entities`，再补齐 ECS 能力与图集并打开总开关；不能靠物种名硬编码后端，也不能直接全量启用。
3. 修改代码与配置后说明未完成项；Unity 编译、运行、画面与性能验收交给用户，不主动操作当前游戏现场。

## 易错边界

- 动物食性与捕食关系使用 ItemData 的稳定数据 Tag：`Carnivore`、`Herbivore`、`Omnivore`。猎物感知捕食者统一查询 `Carnivore`，玩家作为独立威胁来源处理；不要按 `Wolf`/`Predator` 等物种名硬编码，更不能改回 Unity Tag。
- 感知使用 ECS 身份、几何、稀疏空间桶和整数格 LOS；建筑占地与地形阻挡要独立于导航可走性。移动前快照用于感知，移动后快照用于命中；Native 借用必须进入 Job 依赖链。
- 外部武器命中 ECS Actor 时，空间桶、阵营和 LOS 先筛候选，再临时投影受击 Collider2D 让 Physics2D 判接触；镜像代理是否已绑定不能影响命中资格，生命仍只由 ECS 结算。
- 正式模拟使用 60Hz 基础时钟与每实体距离脉冲。休眠/恢复不能追补全部离线 Tick；玩法系统查活跃实体，表现和统计可以查全部实体。
- 决策只选择意图，行为准备目标，攻击在 Active 阶段重新确认目标、朝向和 LOS。前摇、Active、后摇的时钟不能混用。
- 水深和水流从共享导航快照进入 `AiecsFlowAgent`；飞行态不接受地面水流推动。环境、觅食、产蛋、蜂群等托管桥只提交少量结算或外部数据，不允许逐实体恢复旧 AI 状态机。
- GameObject 鸟的 `BirdFlightNavigationProfile` 只检查飞行线路经过的地形是否已加载；它的区块寻址必须和 `ChunkMgr.ResolveWorldAddress` 使用同一份当前 `ActiveGenerationProfile` 区块尺寸，不能退回默认生成尺寸，否则自定义区块大小会让空中移动被误判为未加载。
- `AI_Base`、`AI_Bird`、`Module_AI_BehaviorGraph` 等原生脚本和 Prefab 是当前正式运行时，不能按“仅作者数据”删除或禁用。

## 相关 Skill

- 改导航与 LOS：`flatworld-navigation`。
- 改伤害与身体部位：`flatworld-combat`。
- 改 Actor 定义、物品注册或存档：`flatworld-item-module`、`flatworld-data-save`。
- 改架构待办：`flatworld-dev-doc`。
