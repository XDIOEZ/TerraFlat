# FlatWorld 系统脚本结构图

> 第四分页的数据真源。网页只负责把本 Markdown 解析成可拖拽、可缩放的大地图；新增或调整节点时优先修改这里。

<!-- map-size: 5400x3100 -->

## 阅读规则

- 每个脚本节点最多保留一条最重要的主连接，避免完整依赖图变成蜘蛛网；没有合适主关系的脚本允许不连线。
- 连接线表示两个脚本之间最值得优先追踪的一条主要引用 / 被引用关系；线条不画箭头，不表达调用方向，完整调用链仍以代码为准。
- 点击脚本节点会在本机 Wiki 中通过 VS Code URL 打开对应源码；公开只读 Wiki 不暴露本机项目路径，因此不会启用跳转。
- 节点说明只写该脚本最核心的职责，控制在一到两句话内。

## 区域

| ID | 名称 | X | Y | 宽 | 高 | 说明 |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| core | 核心与数据 | 180 | 160 | 1540 | 1210 | 启动、资源、场景、存档、任务和全局事件的主干。 |
| world | 世界模型与区块 | 1770 | 160 | 1950 | 1210 | 纯世界模型、确定性生成、区块流送以及 Unity 表现绑定。 |
| environment | 环境与维度 | 3770 | 160 | 1450 | 1210 | 世界地址、维度切换、时间、天气与逐格环境温度。 |
| gameplay | Item 与玩法模块 | 180 | 1430 | 2190 | 1470 | Item/Module、背包、制作、战斗、Buff 与建筑玩法。 |
| ai | AI 与导航 | 2420 | 1430 | 1420 | 1470 | 感知、状态机、移动、导航代理以及共享流场。 |
| platform | 玩家、表现与扩展 | 3890 | 1430 | 1330 | 1470 | 玩家输入、UI、对话、新手引导、本地化、音频、MOD 与联机。 |

## 脚本节点

| ID | 系统 | 脚本 | 路径 | X | Y | 说明 | 主连接 |
| --- | --- | --- | --- | ---: | ---: | --- | --- |
| game-manager | 核心 | GameManager.cs | Assets/5_Scripts/5-3_GamePlay/Core/Lifecycle/GameManager.cs | 320 | 320 | 世界新建、继续、运行与退出的总生命周期入口，负责把资源、存档和世界进入流程串起来。 | game-res |
| game-res | 核心 | GameRes.cs | Assets/5_Scripts/5-3_GamePlay/Core/Lifecycle/GameRes.cs | 720 | 320 | 游戏资源目录门面；Ready 之前禁止进入世界，加载计划和句柄所有权由其分部脚本管理。 | item-catalog |
| scene-mgr | 核心 | SceneMgr.cs | Assets/5_Scripts/5-3_GamePlay/Core/Lifecycle/SceneMgr.cs | 1120 | 320 | 统一封装场景切换事件和加载入口，避免业务系统各自直接驱动 SceneManager。 | |
| save-data-mgr | 数据 | SaveDataMgr.cs | Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.cs | 320 | 660 | 当前存档会话与世界数据的权威管理入口，负责加载、快照与持久化协调。 | game-manager |
| auto-save | 数据 | AutoSaveController.cs | Assets/5_Scripts/5-3_GamePlay/Core/Save/AutoSaveController.cs | 720 | 660 | 按运行状态调度自动保存，并把捕获与异步写盘串成不会重入的保存流程。 | game-manager |
| quest-manager | 任务 | QuestManager.cs | Assets/5_Scripts/5-3_GamePlay/Core/Quests/QuestManager.cs | 1120 | 660 | 任务目录与玩家任务运行时的全局入口，负责定义发现、运行时创建和生命周期协调。 | game-manager |
| player-quest-runtime | 任务 | PlayerQuestRuntime.cs | Assets/5_Scripts/5-3_GamePlay/Core/Quests/PlayerQuestRuntime.cs | 320 | 1000 | 维护单个玩家的接取、阶段、目标进度和奖励状态，并消费统一玩法进度信号。 | quest-manager |
| game-event-manager | 事件 | GameEventManager.cs | Assets/5_Scripts/5-3_GamePlay/Core/GameEvents/GameEventManager.cs | 720 | 1000 | 全局游戏事件的运行时调度器，负责触发条件、行动、冲突与活动状态。 | save-data-mgr |
| mod-runtime | 扩展 | ModRuntimeManager.cs | Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/ModRuntimeManager.cs | 1120 | 1000 | 扫描并加载 MOD 清单、依赖与扩展内容，维护统一 API 版本和运行阶段。 | game-res |

| world-runtime | WorldModel | WorldRuntime.cs | Assets/5_Scripts/5-0_WorldModel/WorldRuntime.cs | 1880 | 310 | 纯 C# 世界运行时的区块总管，保存 Chunk 权威状态、租约、提交结果和快照。 | runtime-chunk-mgr |
| runtime-chunk-mgr | WorldModel | ChunkMgr.cs | Assets/5_Scripts/5-0_WorldModel/ChunkMgr.cs | 2240 | 310 | 根据观察者窗口决定区块加载、保留和释放，并安排后台生成任务与主线程提交。 | chunk-scheduler |
| chunk-runtime | WorldModel | ChunkRuntime.cs | Assets/5_Scripts/5-0_WorldModel/ChunkRuntime.cs | 2600 | 310 | 单个区块的权威运行时容器，承载生成结果、运行状态、版本和租约相关数据。 | world-runtime |
| chunk-scheduler | 生成 | ChunkGenerationScheduler.cs | Assets/5_Scripts/5-0_WorldModel/Generation/ChunkGenerationScheduler.cs | 2960 | 310 | 控制纯区块生成任务的并发、取消与完成队列，让耗时生成远离 Unity 主线程。 | runtime-chunk-mgr |
| deterministic-generator | 生成 | DeterministicChunkGenerator.cs | Assets/5_Scripts/5-0_WorldModel/Generation/DeterministicChunkGenerator.cs | 3320 | 310 | 以世界种子和坐标确定性生成地形、气候与生态基础数据，可安全放到后台线程执行。 | chunk-runtime-bridge |
| gameplay-chunk-mgr | 区块表现 | ChunkMgr.cs | Assets/5_Scripts/5-3_GamePlay/World/Chunk/Management/ChunkMgr.cs | 1960 | 750 | Unity 场景侧的区块管理入口，维护激活窗口并把纯 WorldModel 与场景表现连接起来。 | world-runtime-host |
| world-runtime-host | 区块表现 | WorldRuntimeHost.cs | Assets/5_Scripts/5-3_GamePlay/World/WorldModel/WorldRuntimeHost.cs | 2360 | 750 | Unity 生命周期边界，只转发帧时间和主线程提交，玩法决策仍留在纯 WorldModel。 | gameplay-chunk-mgr |
| chunk-view | 区块表现 | ChunkView.cs | Assets/5_Scripts/5-3_GamePlay/World/WorldModel/Presentation/ChunkView.cs | 2760 | 750 | 单个 Chunk 的 Unity 表现绑定器，持有表现与导航租约并驱动各 Renderer。 | gameplay-chunk-mgr |
| chunk-runtime-bridge | 区块桥接 | ChunkMgr.WorldRuntime.cs | Assets/5_Scripts/5-3_GamePlay/World/Chunk/Management/ChunkMgr.WorldRuntime.cs | 3160 | 750 | 把后台生成、权威模拟和每帧提交接入场景 ChunkMgr，并限制主线程提交预算。 | runtime-chunk-mgr |

| world-address | 维度 | WorldAddress.cs | Assets/5_Scripts/5-3_GamePlay/World/Dimension/WorldAddress.cs | 3900 | 320 | 统一构造和解析地表、矿洞等世界地址，业务代码不直接拼接 WorldKey 字符串。 | dimension-manager |
| dimension-manager | 维度 | DimensionManager.cs | Assets/5_Scripts/5-3_GamePlay/World/Dimension/DimensionManager.cs | 4300 | 320 | 负责维度目录、切换请求、世界地址与环境覆盖，是地表/地下迁移的总入口。 | game-manager |
| dimension-portal | 维度 | DimensionPortal.cs | Assets/5_Scripts/5-3_GamePlay/World/Dimension/DimensionPortal.cs | 4700 | 320 | 可挂到自然入口或建筑上的维度交互模块，把玩家入口操作交给维度管理流程。 | dimension-manager |
| day-time | 环境 | DayTimeSystem.cs | Assets/5_Scripts/5-3_GamePlay/World/Time/DayTimeSystem.cs | 3900 | 740 | 跨场景维护世界时间、天数和季节，并为天气、事件和生存系统提供统一时间基准。 | save-data-mgr |
| weather | 环境 | WeatherMgr.cs | Assets/5_Scripts/5-3_GamePlay/World/Environment/WeatherMgr.cs | 4300 | 740 | 管理星球级天气阶段、风、降水与环境事件，并把权威状态写入当前 PlanetData。 | temperature |
| temperature | 环境 | TemperatureMgr.cs | Assets/5_Scripts/5-3_GamePlay/World/Environment/TemperatureMgr.cs | 4700 | 740 | 汇总地块气候、星球/季节/天气修正和局部冷热源，提供统一逐格环境温度查询。 | day-time |

| item-catalog | Item | ItemDefinitionCatalogLoader.cs | Assets/5_Scripts/5-3_GamePlay/Entities/Item/Definitions/ItemDefinitionCatalogLoader.cs | 300 | 1580 | 从 JSON 清单解析 ItemDefinition、继承与模块配置，并构建运行时可实例化定义。 | game-res |
| item-maker | Item | ItemMaker.cs | Assets/5_Scripts/5-3_GamePlay/Entities/Item/Core/ItemMaker.cs | 700 | 1580 | 按稳定定义创建或恢复 Item 实例，是数据定义进入实体生命周期的主要构造入口。 | item |
| item | Item | Item.cs | Assets/5_Scripts/5-3_GamePlay/Entities/Item/Core/Item.cs | 1100 | 1580 | 所有 GameObject Item 的抽象基类，维护 ItemData、模块集合以及统一生命周期边界。 | item-mods |
| item-mods | Item | ItemMods.cs | Assets/5_Scripts/5-3_GamePlay/Entities/Item/Core/ItemMods.cs | 1500 | 1580 | 保存 Item 的模块注册表并提供稳定模块查找，使玩法能力通过组合而不是专用物品脚本扩展。 | item |
| item-mgr | Item | ItemMgr.cs | Assets/5_Scripts/5-3_GamePlay/Entities/Item/Management/ItemMgr.cs | 1900 | 1580 | 管理运行时实体、生成、玩家与空间感知等 Item 级全局服务，并协调对象池与 Tick。 | item-maker |
| inventory | 背包 | Inventory.cs | Assets/5_Scripts/5-3_GamePlay/Items/Inventory/Inventory.cs | 300 | 2000 | 背包事务与槽位数据的核心容器，负责增减、移动、容量和变化事件，不允许业务直接改 Stack。 | item |
| mod-inventory | 背包 | Mod_Inventory.cs | Assets/5_Scripts/5-3_GamePlay/Items/Inventory/Mod_Inventory.cs | 700 | 2000 | 把 Inventory 作为 Item 模块接入交互、实例 UI、固定频率 Tick 和模块存档。 | inventory |
| crafting-service | 制作 | CraftingService.cs | Assets/5_Scripts/5-3_GamePlay/Items/Crafting/CraftingService.cs | 1100 | 2000 | 所有制作入口共用的原子预检与提交服务，统一处理扣料、产出和能力约束。 | inventory |
| damage-receiver | 战斗 | DamageReceiver.cs | Assets/5_Scripts/5-3_GamePlay/Entities/Combat/DamageReceiver.cs | 1500 | 2000 | 实体生命和受伤处理的权威入口，统一身体部位、防御、死亡与伤害结果。 | item |
| buff-manager | Buff | BuffManager.cs | Assets/5_Scripts/5-3_GamePlay/Entities/Buff/BuffManager.cs | 1900 | 2000 | 管理单个实体的 Buff 实例、叠加、持续时间、周期效果与存档恢复。 | item |
| buff-dispatcher | Buff | BuffEffectDispatcher.cs | Assets/5_Scripts/5-3_GamePlay/Entities/Buff/BuffEffectDispatcher.cs | 300 | 2420 | 把 JSON 的效果 typeId 映射为缓存 C# 委托，让 Tick 热路径不重复做字符串分派。 | damage-receiver |
| mod-building | 建筑 | Mod_Building.cs | Assets/5_Scripts/5-3_GamePlay/World/Building/Mod_Building.cs | 700 | 2420 | 统一建筑召唤器与已放置建筑的快照生命周期，负责安装、拆除和建筑数据版本。 | item |
| building-occupancy | 建筑 | BuildingOccupancyRegistry.cs | Assets/5_Scripts/5-3_GamePlay/World/Building/BuildingOccupancyRegistry.cs | 1100 | 2420 | 以离散世界格记录动态建筑占地，同时服务放置冲突和导航脏区，不依赖 Physics2D。 | mod-building |

| ai-base | AI | AI_Base.cs | Assets/5_Scripts/5-3_GamePlay/Entities/AI/AI_Base.cs | 2550 | 1580 | 传统 GameObject AI 的共同基类，聚合感知、移动、状态评估与战斗行为入口。 | item |
| ai-state-runner | AI | AI_StateMachineRunner.cs | Assets/5_Scripts/5-3_GamePlay/Entities/AI/AI_StateMachineRunner.cs | 2980 | 1580 | AI 状态机统一运行入口，负责评估下一状态、执行切换并 Tick 当前节点。 | ai-base |
| item-detector | AI | Mod_ItemDetector.cs | Assets/5_Scripts/5-3_GamePlay/Entities/AI/Mod_ItemDetector.cs | 3410 | 1580 | 提供范围感知与视线阻挡判断，把候选目标查询从具体 AI 行为中拆开。 | ai-base |
| mover-ai | 移动 | Mover_AI.cs | Assets/5_Scripts/5-3_GamePlay/Entities/Move/Mover_AI.cs | 2550 | 2020 | AI 公共移动模块；对上层保留目标/停止/到达接口，内部转接无限地图导航。 | world-nav-agent |
| world-nav-agent | 导航 | WorldNavigationAgent.cs | Assets/5_Scripts/5-3_GamePlay/World/PathFinding/WorldNavigationAgent.cs | 2980 | 2020 | 面向单个移动者的导航代理，持有目标并消费共享导航结果生成局部移动方向。 | world-nav-manager |
| world-nav-manager | 导航 | WorldNavigationManager.cs | Assets/5_Scripts/5-3_GamePlay/World/PathFinding/WorldNavigationManager.cs | 3410 | 2020 | 场景级导航入口，维护动态脏区、地块权重和共享寻路数据并对接 Chunk 生命周期。 | gameplay-chunk-mgr |
| flow-nav-cache | 导航 | FlowNavigationCache.cs | Assets/5_Scripts/Shared/Navigation/FlowNavigationCache.cs | 2980 | 2460 | 按世界缓存 16×16 分层流场与跨区块出口数据，让大量代理共享目标路径结果。 | world-nav-manager |

| game-controller | 玩家 | GameController.cs | Assets/5_Scripts/5-3_GamePlay/Player/Controller/GameController.cs | 4010 | 1580 | 本地玩家的输入与玩法控制入口，协调移动、交互、瞄准以及外部控制租约。 | game-manager |
| input-binding | 输入 | InputBindingService.cs | Assets/5_Scripts/5-3_GamePlay/Player/Controller/InputBindingService.cs | 4430 | 1580 | 管理 PlayerInputActions 的运行时按键覆盖、重绑和持久化，UI 只消费公开 API。 | game-controller |
| ui-manager | UI | UIManager.cs | Assets/5_Scripts/5-5_UI/Core/UIManager.cs | 4850 | 1580 | 正式 Prefab 面板的创建、缓存与生命周期管理入口，统一维护 PanelRoot 和打开关闭流程。 | game-manager |
| base-panel | UI | BasePanel.cs | Assets/5_Scripts/5-5_UI/Core/BasePanel.cs | 4010 | 1990 | 所有正式面板的基础行为契约，负责显示隐藏、输入锁、焦点和取消快捷键等公共规则。 | ui-manager |
| soliloquy | 对话 | CharacterSoliloquyController.cs | Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/CharacterSoliloquyController.cs | 4430 | 1990 | 角色自言自语的唯一调度入口，只组合上下文、内容 Provider 与屏幕气泡显示器。 | game-controller |
| new-player-guide | 引导 | NewPlayerGuideController.cs | Assets/5_Scripts/5-3_GamePlay/Presentation/Guide/NewPlayerGuideController.cs | 4850 | 1990 | 新角色生存教程控制器，只贡献教程 Facts 并消费玩法成功事件，文字由对话系统展示。 | soliloquy |
| localization | 本地化 | FlatWorldLocalizationService.cs | Assets/5_Scripts/5-7_Localization/FlatWorldLocalizationService.cs | 4010 | 2400 | 对 Unity Localization 做薄封装，统一语言切换、文本查询、语言持久化和稳定键命名。 | ui-manager |
| audio-service | 音频 | AudioService.cs | Assets/5_Scripts/5-6_Audio/Runtime/AudioService.cs | 4430 | 2400 | 全局音频播放与总线入口，负责 AudioCue 解析、声源池、音量和淡入淡出。 | game-res |
| network-bootstrap | 联机 | NetworkGameBootstrap.cs | Assets/5_Scripts/5-4_Networking/Gameplay/NetworkGameBootstrap.cs | 4850 | 2400 | 在主菜单场景自动装配正式联机入口，不要求手工修改既有场景。 | network-manager |
| network-manager | 联机 | FlatWorldGameNetworkManager.cs | Assets/5_Scripts/5-4_Networking/Gameplay/FlatWorldGameNetworkManager.cs | 4430 | 2720 | 正式联机管理器；先同步主机世界快照，客户端完成世界加载后再生成玩家并接入状态协调器。 | game-manager |
