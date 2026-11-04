---
name: flatworld-core
description: "FlatWorld 启动、世界进入退出、资源加载/原位更新、场景与玩家生命周期。"
---

# 核心生命周期

## 代码入口

根目录：`Assets/5_Scripts/5-3_GamePlay/Core/`。

| 目录 | 入口与职责 |
| --- | --- |
| `Lifecycle/` | GameManager、GameManager.UI、SceneMgr：世界与场景 |
| 同上 | GameRes、GameRes.Loading/LoadPlan/HotReload：资源会话 |
| 同上 | ResourceLoadPipeline、ResourceAssetScope、ResourceCatalogValidation、ResourceReloadContext：阶段/所有权/发布 |
| `Save/` | SaveDataMgr、AutoSaveController：持久化 |
| `Rules/` | GameRuleConfiguration：独立基础配置、任意来源修正与不可变业务快照；GameRuleMath：通用数量取整 |

`启动场景 → 主菜单必需资源 → 新建/继续/加入 → 请求世界资源 → 完整内容 Ready → 世界存档 → WorldEnter → 玩家创建/Load → PlayerEnter → 可见 ChunkView 绑定/物理收尾 → 解锁`

## 世界生命周期

- `Persistent Runtime` 与 `Runtime Objects` 分类根保持默认 HideFlags，不能用 `DontSave`；常驻只靠 `DontDestroyOnLoad`，退出运行或卸载所属场景后必须正常销毁整棵对象树。

- 全游戏逻辑由 `GameplayFixedStepSimulationDriver.Update` 驱动独立 `GameplaySimulationFrameClock`，不依赖 Unity `FixedUpdate`；本机 30/60/120 TPS（默认30，联机锁30），时间累积来自 `Time.unscaledDeltaTime * Time.timeScale`，每画面帧最多追赶4个 Tick，超额丢弃。Unity 2D 物理在 Driver 存活期间设为 `SimulationMode2D.Script`，由世界物理阶段按当次游戏 Tick delta 显式推进已登记的每个独立 `PhysicsScene2D` 一次；退出恢复先前模式。游戏时钟 `CurrentTime/PresentationTime/TickDeltaTime` 是逻辑、物理和视觉补间的时间源，`Time.fixedDeltaTime` 仅供仍独立运行的 Unity 固定帧，TPS设置不修改它。系统按 `GameplayFixedStepSimulationPhase` 登记并按世界时间→世界模拟→导航→玩法→物理准备→太空→物理→物理结果顺序运行；真实迁移及复活等待 `WaitForNextTick`。规则/现场回归 `core_lifecycle/fixed_step_scheduler、fixed_step_clock`。
- `Register` 可选 `GameplayFixedStepTickRate`（60/30/20/10/5 Hz），`Hz60` 代表每个基础固定步；低频以独立相位余量维持跨 30/60/120 TPS 的目标平均频率，一次只消费累计游戏秒、不循环补算；世界时间与物理阶段始终每个固定步执行。近身玩法跟基础固定步，世界实体 30、机器和世界后台 20、地火/实验液流 10 Hz。规则 `core_lifecycle/fixed_step_scheduler` 验证分级、可调基频、倍速、暂停及低帧分组。

- 常驻对象统一走共享 `PersistentRuntimeObjectHierarchy.MoveToPersistentCategory`，在 `DontDestroyOnLoad/Persistent Runtime` 下按职责收纳；自动创建单例可用 `PersistentRuntimeObjectCategoryAttribute` 声明类别。保留显式业务父子关系、激活状态和世界姿态，不能把场景对象或物理代理为整理而提升为常驻。需要原业务根时用 `GetBusinessOwnerRoot`，不要再把 `transform.root` 当业务所有者。第三方已常驻裸根仅在场景加载、资源就绪及入世完成时归入 `Infrastructure`，无逐帧扫描；退出后不补建根。回归 `core_lifecycle/persistent_object_hierarchy`（规则与现场）。

- `RuntimeSceneObjectHierarchy` 为每个 Scene 按需建立 `Runtime Objects` 分类根；只在创建或迁移时挂接，保留显式宿主和原场景生命周期。世界自有物理代理与回收池统一挂 `Physics` 分类，固定代理仍挂共享区块刚体。分类节点保持单位 Transform，不能用作世界身份、持久池或业务所有权。

- 业务只读 `GameRuleConfiguration.Current` 或订阅 `Changed`，不查询难度；外部通过 `SetModifier(source, callback, priority)` / `RemoveModifier` 修改候选。每次从独立基础值重建，来源按优先级和首次注册顺序执行；失败不替换来源或快照，通知期不允许重入写入。难度为优先级 -1000 的来源，机制可由后续外部来源或业务自身公开开关覆盖。难度定义复用分域只读 RuleSet 复制倍率，草稿、规则候选和最终发布各自独立。`SaveDataMgr.SaveData` 换根时由 `GameWorldRuleSession` 一次装配，退出设空档撤销会话来源；新建难度写入完成后再发布。

- GameManager 是新建、继续、运行、退出的权威。动态维度以 WorldKey 创建 Scene 并复用 RunWorld，不入 Build Settings；离线太空恢复使用静态 SpaceScene 引导，再按 SpaceSector__X__Y 创建星区场景。
- `SaveAndRestart → BackToHelloScene_Coroutine(requireSuccessfulSave:true)` 在撤除世界/网络前同步写盘，失败保留会话；成功清理后由等待旧 PID 退出的助手重开 Windows/Linux 运行包。MOD 菜单的“重启”及关闭确认统一复用 `TryRestartApplication`，所有 MOD 类型均退出旧进程并由新进程完整加载资源，不再调用菜单原位重载，也不等待后台预热完成；Editor/其它平台明确不支持自动重启，不能把角色死亡当重启。
- 迁移开始、等待加载页之前冻结源显示 Scene，再传给 `LoadPresentationSceneAndInvokeWhenReady`；乘员先退出太空时，星区租约会在等待加载页期间回收。复用或 Additive 加载目标后只卸载该源 Scene，失败恢复也只清理本次明确指定的目标 Scene。不能误用回退激活的船内 Scene；船内世界及物理岛保持原实例，不能用 Single 连带卸载或因显示切换重建。
- 联机本机 realm 切换走 GameManager.Space 的观察者接口，只改变本机显示与动态场景归属；不全局迁移、释放地表或重锁输入。换星区保留浮动原点，退出世界清本机显示缓存；地表恢复必须对应已保留的真实 ChunkMgr 运行时，缺目标时拒绝并提示。太空玩家关闭默认 Rigidbody2D 模拟，运动和跨界接触用纯数据，回地表恢复原值。
- 地表/太空往返保留会话纯数据 `WorldRuntime`，回程重新建立场景侧区块调度器并绑定视图；表现缓存开关只决定显示壳和星区实例复用，不能据此销毁会话模型。正式退出才释放会话模型。
- IsStartupReady 开放主菜单；菜单必要资源结束后由 GameRes 在菜单空闲时自动 RequestWorldResources 后台预加载完整世界目录，入世复用在途/完成的会话，完整 Ready 才创建世界。返回菜单只清理世界实体/存档并取消该世界的原位更新候选；保留完整目录、world Scope、派生资源缓存与 Ready，在途首次加载继续，切档复用。加载页 Completed 等待脚下区块及完整可见窗口绑定/物理完成，生成队列清空不等于完成。
- 新角色由 ItemMgr 在 Player.Load 前注入 GameConfig/Players 模板，已有存档/跨维度不重复注入。创建、网络提升与远程副本显式设置 ProfileContext，玩家事件一次发布。
- GameManager 从种子锚点沿固定右上斜线逐格找新角色出生点，首个无水可走格即停，不扩圈/铺二维网格；只受采样预算限制，环绕回起点结束。后台与同步回退共用正式单格规则，预搜索使用冻结纯数据并共享生成器/水文缓存，退出/失败取消。
- 跨场景单例保留有效实例，重复 GameRes 不开启资源协程。停止/退出通过已绑定引用清理，不重新查单例创建对象；终止流送直接销毁 View、取消后台任务，正常流送才回池。
- 世界退出事件由注册表按归属保存并解绑区块；退出写盘和后续收尾不重新查找或重复解绑管理器。
- 销毁回调借 `GameplayWorldRegistry.TryUseExistingWorldForOwnerRelease` 只用现有且 owner 引用相符的记录，允许卸载中释放，不调用 GetOrCreate；正常查询仍要求场景加载。回归 `GameplayWorldOwnerReleaseEditModeTests`。

## 资源会话

- **构建期优先**：能提前计算且不会变化的内容，尽量在 Unity 编辑器/构建阶段完成；运行时只处理真正动态的内容。内置配置、模块/行为树/配方及 Sprite 图集与索引优先预处理，Player 只负责按需加载、绑定和校验。MOD、热更新等动态内容仍走运行时流程；图集打包≠全部纹理预加载。
- **按需图集**：位置清单仅含地址，首次 Sprite 请求再接入用途目录；按源身份绑定，不因同名拆页。菜单依赖优先留在 Menu 桶，世界专用 Shared 分开；Baker 真打包核对身份、数量、尺寸并清理旧产物，已有单源纹理不再套空洞小图集。
- **内置烘焙**：`[BuiltInContentBake]` 静态无参入口由 Baker 在 Addressables/Player 构建前收集，生成 `Resources/CompiledContent/BuiltInConfigurations.bytes`（不入 Git）；`BuiltInContentBinaryCatalog` 仅管理键、版本和纯载荷。模块基于真实 Prefab/原型（含生命与战利品），行为树展开后运行时重绑工厂，配方在启动时复查物品引用。正式包缺失/契约不匹配直接失败，不静默回退 JSON；Editor、F5 与菜单重载通过覆盖整个异步事务的源配置租约读取原始内容。规则 `core_lifecycle/baked_configuration_catalog`；Player 对照参数 `-flatworld-source-configurations`。
- **Android 启动**：正式 APK 在 `BeforeSceneLoad` 跳过 PuerTS Unity MCP 调试宿主，避免它在主菜单前阻塞或载入尚未适配 16 KB 页的原生库；Development Build 保留手机 MCP。16 KB 设备仍须更新打包内不兼容的 `.so` 才能使用相关插件。
- **Android 图形后端**：正式 Android Player 使用 OpenGLES3 而非自动优先 Vulkan；Redmi K30 Pro（Android 12）真机在 Render Streaming/WebRTC `Hook_vkCreateInstance` → Adreno Vulkan 驱动发生启动前 SIGSEGV，禁用该调用路径以兼容既有手机。以后升级 Vulkan/WebRTC 时须先在真实设备验证，再考虑启用 Vulkan。
- **阶段与预算**：`GameRes.LoadPlan` 显式声明依赖；`ResourceLoadPipeline` 统一驱动嵌套 `IEnumerator`、失败/超时/取消与释放。菜单重载和原位更新顺序执行；首次未开放菜单32ms、菜单后台预热1ms、入世首次32ms/再次4ms、原位1ms、Lua缓存8ms，不改变固定模拟频率。菜单必要资源先完成，随后自动请求完整计划；普通退出保留完整资源缓存；仅显式重载或 GameRes 销毁时释放。`LastStartupLoadMilliseconds` 记录菜单 Ready，`LastResourceLoadMilliseconds` 记录世界资源预热到完整 Ready。
- **并发与预取**：`ResourceLoadPipeline.Concurrent` 按依赖推进，共享帧预算并允许独立请求提前发出；Item/Actor/Recipes/Buff 和小配置预取只产出私有输入，正式依赖阶段才绑定 Unity 对象、校验并发布。菜单自动预热保持单会话，Alt+R 可取消菜单后台世界计划再重载。失败不发布半成品，取消倒序释放。`BeginPureDataAsync` 立即启动私有任务、主线程推进回调；本地读取/分包/继承/缓存尽量合并一次后台事务，Android/WebGL 保留包内读取路径，后台禁止使用 Unity 对象。回归 `startup_cache_across_world_exit、startup_menu_preload、startup_loading_policy、startup_concurrent_stages、startup_production_dependencies、startup_eager_data_request、startup_configuration_publication`。
- **资产与预热**：`audio-request` 与 UI/目录并行，最终 `audio` 阶段收齐；常用资产、物品 Sprite、液体外观共用四槽队列并按类型/地址去重。材质/控制器就绪后分帧编译 Sprite 定义，全部成功才注册；完整会话 Ready 前预热 BRG Mesh、战斗/机器池，原位候选跳过共享预热。Editor Fast Mode 只针对默认 AssetDatabase Provider 优化，不能接管自定义 Provider/Bundle；具名子图必须严格校验。
- **首次生物与玩家代码**：`actor-code-prewarm` 在完整 Ready 前按共享预算预备 Actor 行为树、玩家 Prefab 实际脚本、Item 注册与 Tick 调度，以及新世界创建与存档管理代码；`IStartupRuntimeContentPrewarm` 允许 Prefab 在此阶段分帧预取纯内容。复用 `ResourceRuntimeMethodPrewarmer` 的 Mono 真实入口预编译，不执行玩法或静态构造；按方法去重，原位更新及 IL2CPP 跳过。规则 `startup_actor_code_prewarm` 与 `startup_production_dependencies`；冷启动峰值仍需实测。
- **配置缓存与编译**：`Shared/ContentCache` 使用 Manifest、MemoryPack 和源 SHA-256 校验复用 Item/Actor/Tile/Recipes/Buff；较大的强类型 JSON（≥16K字符）可自动缓存，小配置直接解析为独立 DTO。菜单完成后完整世界目录复用已验证的玩家/时间/温室/水流配置快照，原位更新仍重新读取来源。缓存坏/写盘失败回退原解析；DTO 不包含 Unity 引用或运行态，保留字典比较器。模块/端口复用严格编译结果，但每轮仍核对当前引用、配置及原型，MOD 合并后再 `validate-final`。冷复制用 `ItemData.CloneDefinitionDefaultsWithoutModules`/`ItemModulePrototypeCache`，不复制随后会丢弃的模块图，每个定义的可变状态独立。
- **所有权与回收**：菜单 prefab 与必要配置由 startup Root Scope/快照持有，完整世界使用独立 `ResourceAssetScope`；Addressables 初始化句柄归 `GameRes` 跨会话持有。加载失败/显式重载取消释放相应句柄，普通退出不清目录、MOD 或 world Scope；原位更新的旧代资源仍在世界退出后回收；原位候选只在完整验证后发布，失败不得破坏正式会话。
- **测试与计时**：`GameplayTestResourceCache` 每轮新建 Scope/句柄/模板，仅 Ready 后写缓存；完整启动计时包含首帧与异步等待，并区分执行、帧间等待和最长调度间隔，不把并发阶段相加或误作纯 CPU 时间。重点回归 `startup_local_item_catalog、item_sprite_compilation_pipeline、startup_module_parameter_templates、startup_definition_default_copies、startup_execution_timing、core_lifecycle/binary_content_cache、core_lifecycle/editor_asset_loading`。
- **Editor 手动验收**：菜单 `FlatWorld/测试/普通 Editor 启动十秒验收` 使用 GameStartScene，菜单通知立即排队完整资源，报告两阶段合计与目录数量，Lua 缓存须 disabled；输出 `Library/FlatWorldEditorStartupAcceptance/`。验收不构建 Player，不把菜单提前开放当作完整加载小于10秒。

## 原位更新

- RequestResourceReload 在主菜单完整替换会话，单机世界加载隔离候选、校验后同帧发布 ResourcesReloaded；不保存退出或重建活跃 Item/Chunk。联机不做单边更新；TryReloadResources 仅无世界/活跃 Item 时可用。
- 候选期间正式 Ready 保留，IsResourceReloadInProgress 防重入；ResourceReloadContext 只交换目录/会话标量，每次向 Unity 让出前还原引用。持续玩家、库存、区块运行态不属于候选。
- 结构错误丢弃候选；单个 Actor 错误隔离并保留已发布版本。物品/机器身份删除时暂留旧定义供现存实例与冷区块解析，退出保存/清实例后再回收旧代 Addressables、模板、Lua。
- 在用地块/MOD 身份、Tile 数字编号、液体身份布局及 Bundle 二进制变更不在世界内发布。候选跳过共享预热，发布后按需 GetOrCreate；失败候选不清正式 BRG/Mesh。

## 通用 Lua 测试场景

- `Assets/3_Scenes/Development/GameplayTestScene.unity` 复用正式 WorldManager/建世界/默认玩家模板；测试系统入口、Lua 编写与阶段截图统一见 [flatworld-lua-testing](../flatworld-lua-testing/SKILL.md)。
- 可选陈列通过世界生成 Hook 配置平地空生态，按正式资源/机器/掉落/Item 后端遍历非 Actor 定义；保存隔离租约独立于场景组件，退出写盘并返回菜单后才恢复正式目录。报告在 `Library/FlatWorldGameplayTests/Runs/`，隔离存档在 `Saves/`。
