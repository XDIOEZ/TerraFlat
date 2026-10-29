---
name: flatworld-world-model
description: "FlatWorld 纯世界模型、异步区块生成、租约、流送、差量恢复与 Unity 表现绑定。"
---

单机地表↔太空的表现池见 `ChunkMgr.PresentationCache.cs`：最多9个完整绑定过且已 Unbind 的壳移入持久根，回程重新绑定当前 WorldRuntime；不缓存旧 ChunkRuntime/导航/租约。维度退出通过注册表先解绑并保留本机管理器，资源代际不符、正式退出与销毁释放，相关现场 Lua 为 `presentation_cache`。

# WorldModel

## 入口与数据流

| 路径 | 职责 |
| --- | --- |
| `Assets/5_Scripts/5-0_WorldModel/` | 纯模型、生成、快照、租约与调度 |
| `Assets/5_Scripts/5-3_GamePlay/World/WorldModel/` | `WorldRuntimeHost`；`Presentation/` 下 `ChunkView/IChunkViewRenderer/Chunk*Renderer` |
| `Assets/5_Scripts/5-3_GamePlay/World/Chunk/Management/` | `ChunkMgr.WorldRuntime/RuntimeWindow/LiquidFlow` |
| `Assets/Resources/Config/WorldModel/`、`Assets/2_Prefabs/World/WorldModel/` | 配置与表现 Prefab |

观察者窗口 → 后台纯生成 → 主线程校验/提交 → 差量恢复 → ChunkRuntime 租约 → ChunkView 分步绑定 → 解绑/逐出。

纯模型按 `Core/Chunks/Terrain/Liquids/Weather/Occupancy/Generation/Messaging` 归类；同一类型的 partial 文件放在一起，目录职责见源码根的 `README.md`。

## 权威、所有权与坐标

- `WorldSceneBindingIndex` 记录世界与 Scene 的绑定；同场景存在多个世界时必须提供明确上下文。`GameplayWorldRegistry` 按 WorldKey 持有纯 `WorldRuntime`，一个区块管理器借用调度；飞船 `ship:<id>` 经 `CreateLocalWorld` 拥有独立 Scene/PhysicsScene2D、16×16 空区块和普通 ChunkView。`UsesLocalCoordinates` 禁止星球环绕，`SimulateWithoutObservers` 保持无人船运行；切地表/太空只撤投影，正式结束才清退世界。
- `ChunkRuntime + ChunkTerrainData` 是权威；Tilemap/Collider/Renderer 是投影。后台与 Burst 只接收冻结值，不访问 Unity 对象；`RuntimeTileDefinition/Behaviour` 留在主线程资源层。
- 本机可见性与投影源恢复使用 `LocalPresentationWorldKey`，不能用角色所属船内世界替换实际显示场景。
- 提交核对世界 Epoch 和请求版本；取消/失败/逐出释放生成结果及租约。`Seal` 将池化 Liquid 深度/类型和环境数组唯一所有权交给区块，Dispose 归还；`height` 随区块存活，只作原生成高度。生成结束一次汇总固定湿度/两层降水/风力，`ChunkTerrainData.WeatherProfile` 持有不可变天气概率/强度/持续时间，运行时不随环境层差量重算。
- Liquid 的会话索引来自冻结 `LiquidTypeCatalog`；哈希/持久化使用稳定 LiquidId。Ground 与 Liquid 分开，`SetLiquid` 不写 Ground。Liquid 权威数组、批次、快照与存档均使用 `int LiquidLevel` 0～10，一份一级；`LiquidCellValue.NormalizeLevel` 限级并在零级清类型，`GetLiquidDepth` 只派生 0～1 比例。整数流动同量扣源和增目标，不能分别取整写回造水。
- 正式 Chunk/Map/冷地表与出生查询通过 `GameRes.RequireWorldLiquidTypes` 取得 Ready 后的完整表；ChunkMgr Awake 只绑定宿主，首次请求才建世界，换场景释放旧生成器，不能把启动空目录冻结成 BuiltIn 表。
- `WriteCell` 同步数据、固定 LOS 遮挡位与版本，建筑/机械叠独立动态位。`TerrainSupportLayer.GetSurfaceCell` 投影平台有效表面，不改底材/底液。
- `ChunkTerrainChanged.EnvironmentLayerId/AffectsEnvironmentLayer` 表达真实变化层；液体变化影响当前及八邻区岸线/四角液深/导航。`SetLiquidBatch` 索引唯一递增，跨块先全部写回再 Publish，仅发 `LiquidBatchChanged`；借用索引限同步回调内使用。
- `SetEnvironmentBatch` 按层接受唯一递增格索引，各层先写后 `PublishEnvironmentBatches`，每个真实变化层只发一次 `Changed`；消费者先筛层，再用 `GetCellIndices(width)` 兼容单格/批量。只读层和通知索引只借用于同步阶段；同层待发或回调中禁止写入，发布失败保留未发层供重试，土壤层不改变液体版本。
- `WorldTopologyDomain` 是 noEngine 的纯坐标真源，Jobs 使用主线程冻结副本；`WorldTopologyRuntime` 仍读取存档，仅主线程。`ChunkRuntime.Address/ChunkOrigin` 永远是规范坐标，View Transform 由 `WorldLocalPresentation` 选本地镜像；HUD 经 `ToLogical` 还原。连续曲线用 double 和显式半周期符号策略。
- 活动世界 `WorldTopologyRuntime.TryGetActiveBounds` 复用星球半径/区块尺寸/拓扑模式计算结果，每次仍读当前存档与场景；换星球、同实例修改几何、无效配置和运行重置立即失效。规则 `navigation/topology_bounds_cache`。
- `ChunkMgr` 常驻 DDOL；View/自然物挂当前世界 Scene 的 `Runtime Objects/Chunks` 分类下，不挂管理器。Prefab 直接装配并引用 NaturalItems/LightOccluders 表现组件。
- 居民、ECS 掉落和 MachineWorld 独立于 View；解绑只撤显示。自然散落点成功生成 ECS 掉落后才移除基线 GUID，失败回滚；树/矿点/建筑不是散落点。机器连通组全量恢复后 Tick、全量快照后休眠。

## 流送与生成

- `DeterministicChunkGenerator` 主文件只组织流水线；`SurfaceTerrain/TerrainStages` 管地表、洞穴与结构，`HydrologySampling` 管纯采样，`HydrologyRegions` 独占区域缓存/纪元取消/预热与定位借用。热缓存命中不创建 Lazy；结构先于地形扩展，扩展后再编译生态，规则 `terrain/generation_stage_ownership`。

- 多格静态资源通过 `EcologySpawnRuleSnapshot.SharedRadius/FootprintWidth/FootprintHeight` 声明：`EcologyGeneration.Shared` 用稀疏周期网格和负 GUID 独立复算所有覆盖区块，`DeterministicChunkGenerator.SharedResources` 验证完整占地及入口安全区并过滤内部普通生态。`SharedAnchor` 始终是规范坐标，代理 `LocalX/Y` 可以在当前区块之外，禁止直接拿它索引地形。
- `ChunkMgr.SharedNaturalEntities` 的会话注册表只创建一个 Resource Entity；各 `ChunkNaturalItemRenderer.Shared` 持有引用，唯一表现宿主可交接，最后引用释放才捕获/移除。状态和删除标记只写锚点所属区块，死亡不再捕获，退出世界清理注册表。区块碰撞继续复用 `ChunkCollisionRenderer` 按范围裁剪同一 runtimeId，寻路使用完整 `worldGridOccupancy`，不得为每块重建独立生命。
- 大矿山示例为 `Mine_Stone_Large`，覆盖半径包含贴图和受击范围而非仅阻挡框；资源目录校验器验证三者一致。纯规则 `ecology/shared_resources`；编辑器外回归 `python Tools/Tests/SharedNaturalResources/verify.py`，只在 Temp 中移除原生 Profiler 初始化，不能替代真实 Unity 碰撞/保存回归。

- 单人和联机多观察者共用 `ChunkMgr.RefreshRuntimeWindows`，观察者请求经纯模型 `RefreshWindows` 合并模拟/表现/保留需求；本机蒙版、就绪等待与导航仍只跟随本机角色。

- 模拟圈、完整表现预加载圈、数据保留圈分别维护；就绪只检查活动圈，关闭释放全部表现需求。Mod_ChunkLoader 按玩家持久化加载距离提交窗口，相机缩放不改距离；距离 d 包含中心块，窗口边长是 (2d-1) 个区块。
- ChunkMgr.TryGetRuntimeWindowBounds 默认读取已提交窗口，usePresentedBounds 选择 ChunkWindowPresentationTransition 的平滑表现窗；正式目标不延迟，云雾和旧 View 回收共用同一状态。边界按实际 Profile 宽高，队列 retarget 不提前移动目标；Mod_ChunkLoader 对外整体投影本地镜像，表现消费者不得反向改流送。
- 离开窗口的 pending/后台请求取消，已有地面的旧 View/表现租约保留到平滑雾窗外才解绑；重入目标复用，换维度/关闭窗口清理。折返、连续跨块不重置到旧边界，规则见 Rendering/zoom_cloud_transition。保留请求按玩家现位置重排，同区块移动只改优先级；Mod_ChunkLoader 与 RetargetRuntimePresentationQueue 共用预加载距离。
- 基础地形跨 Chunk 优先，草/碰撞/导航/自然物后续轮转；续步骤先取基础未完成块，再按当前距离选择，单块每帧最多一步。`ChunkTilemapRenderer` 初绑每步最多64格，网格批写跨步、每层上传分别让帧，全部完成才发布基础就绪/重建事件；绑定间的地形/邻区脏格合并重扫，资源变化重启扫描，取消按绑定版本释放。`IIncrementalChunkViewRenderer` 扫描跳过项也计预算，自然物先宿主后伴生；宿主扫描时同时缓存伴生候选，第二阶段只扫候选，保持相对顺序和原每步预算并在解绑/取消时清理。规则 `ecology/companion_bind_candidates`。完成初绑后再季节补位。单项装配仍可能超时。
- `ChunkMgr.ProcessRuntimePresentationFrame` 每帧借用管理器实际所属世界，返回后才让帧；取消、解绑与天气收尾同样按归属执行。已注销或被替代的管理器不推进、不补建世界；已解绑池壳可直接销毁。
- `ChunkMgr.OnDestroy` 先终止再借现有 owner 释放上下文；记录缺失/已换 owner 时只清自身 View、协程、池及调度器，不按世界地址清新世界，也不销毁会话世界模型。意外卸载已销毁且未快照的活体无法补回；无场景时当前居民休眠，不能视为持续模拟。
- 正常注销和 OnDestroy 共用已核对的释放上下文；建筑收尾的坐标换算只读尚未释放的 Profile，不调用 EnsureWorldRuntime。释放结束后仍拒绝重新访问已终止管理器；回归真实起降后再离开地表。
- 差量在基础绑定前恢复，同一 Runtime 不因窗口刷新重复恢复。外圈预取只等待可见数据/基础地形，不等全部后续表现。
- `WorldManager.prefab` 序列化提交/基础/后续预算覆盖源码默认值。GM 可调数量与毫秒上限；`StreamingDiagnostics` 分开记录排队、生成、提交、恢复、renderer 步骤与等待，读取诊断不推进世界。
- 水文缓存键包含 Epoch、维度、种子、配置和稳定 Region。同组只算一次；单 Chunk 取消不终止仍有需求的共享河网，全部需求消失/世界结束才取消。Region/Halo 只扩大采样，结果只提交核心区域；世界退出清水文/海洋距离降水/海洋水源区块/火山缓存。
- 出生搜索、正式流送和联机生态冻结共用 `ChunkMgr.PrepareActiveGenerationSnapshot`，统一尺寸、空间倍率、运行时覆盖和保存配置；出生只检查可走地形/液深，不生成完整生态。`WorldAirHumidityField` 只读差量恢复后的 Ready 区块，依 Terrain/LiquidRevision/Epoch 失效，缺影响邻区返回 false，不触发生成。
- 最终生成配置由 `WorldGenerationProfileCache` 复用，世界/种子/资源代际、实例覆盖与冻结规则变化失效；有 Profile Hook 时绕过。洞穴配对与冷地表共用失效条件，性能分看 `FreezeProfile/RefreshWindows`；离线回归 `Tools/Tests/WorldGenerationProfileCaching/`。
- `WorldGenerationRuntimeHooks` 的 Profile Hook 在主线程冻结参数；`WorldModelTerrainDecorator` 在生成器创建时捕获委托，正式流送与出生查询共用。地形回调只能读请求中的冻结值并修改纯地形缓冲，不访问 Unity 对象或当前单例；测试平地等临时规则由订阅者退出时撤销。
- Tile 外观查询优先 JSON 的 `runtimeTileId/tileAsset`，再回退 Palette；地形身份按冻结 Profile → 当前 Profile → JSON 目录解析，玩家新建筑用当前目录，稳定数字 ID 与冻结映射不冲突。
- 流送计时是单调墙钟，父子阶段重叠不能相加；暂停跨越请求单独记录。宏观 Lazy 执行时间归实际执行者，get 包含等待，local_refine 只计本块。
- 生态邻区宿主标签在 `DeterministicChunkGenerator.EcologyCache` 按纪元/地址/种子/Profile/拓扑/标签有界缓存；正式地形预填，冷查询先筛纯坐标候选，取消不写结果，换世界清空。`ecology.neighbor_terrain` 累计本请求冷邻区重建时间。

## 表现与资源会话

- Ground/Water/Back/Blocking 由 `ChunkGroundMeshRenderer` 批绘，Blocking Tilemap 仅留碰撞；安全单格 Sprite 用 Quad/GPU Cell，外扩 Sprite 保留顶点。树/资源/建筑/机械主体经 `ChunkDepthMeshRenderer` 按本地 Y 整行排序。
- `ChunkGroundMeshRenderer` 的 Vertex/GPU Cell 托管数组按纹理批次从 ArrayPool 借用并在 Dispose 归还；每次租借清空本次有效范围。**索引格式、GPU 上传和包围盒扫描必须使用真实 `vertexCount/cellCount`，不得使用池数组的 `Length`**。构造或字典登记失败要释放已创建 Mesh/Buffer/材质和数组，单组释放异常不得阻断其它组回收；复测对比 `FlatWorld.ChunkStreaming.Bind.MeshGroup.Create.{CellArray,VertexArray}` 的耗时与 GC。
- 玩家/GO AI 用 SpriteRenderer 与行网格同 Player Layer/Order；行中心排序有半格近似，真实脚点仍用于几何/遮挡。草/花/阴影 BRG 只能在自身排序域排序。
- 草/花共用 ChunkTilemapRenderer Owner，以 VisualLayer/单格槽隔离；提交确认后登记缓存，解绑清自身整个图层并合并删除。`IRecoverableChunkViewRenderer` 在窗口刷新核对权威数据和真实实例，Owner 重建从权威数据恢复，不用旧可见缓存。`sorting.batchProfiles` 是排序配置真源，草图集 Sprite 跨 Chunk 共享。花初绑一次扫 Placements，避免逐点全表查询。
- 世界 Fire 是独立玩法层，表现走 Y 行网格/GPU 图集翻帧；实体燃烧不写火格。地面积雪走地形光照 Mesh，墙冠用独立 Y 行网格，二者低于 Player。
- `ChunkNaturalItemRenderer` 从所属 View 找 Owner，GUID/部件号标主体，太阳阴影用独立负槽；共用 `WorldEntityRuntime`。解绑先保存并撤表现/导航，最后释放世界。
- 墙裂读权威 `flatworld.tileBuilding.damage`；自身与邻块共享边/角变化增量刷新，Bind 重建、Unbind 退订。水面顶点含高度、四角水深与 Flow；河流为下游速度、海洋为当地风向，湖面 Flow 为零。
- `TerrainSurfaceHeightLayer` 从原 `height` 档位、`terrain.surface.excavatedLevels/coverLevels` 推导表面高低；非 Surface 忽略生成海拔，支撑面回到基准面，液面低一档，Blocking 顶部高一档。`TerrainHeightShadowManager` 统一查询/提交低侧阴影，只借用已有维度服务，不为只读查询创建管理器；四向直边与斜邻圆角共用 BRG，未知邻格不制造边或角。订阅八邻区 Commit/Evict，变化按3×3邻域裁剪合并脏格，随 Owner 镜像、重建和解绑维护表现。
- 高低阴影在单次刷新内复用一格 halo 表面采样，下一轮重新读取地形/液体/配置；脏格与邻区集合随壳复用。规则 `rendering/terrain_shadow_surface_cache`，离线 `Tools/Tests/TerrainHeightShadows/`。
- `GameplayWorldRuntime.Physics` 持有 `WorldPhysicsService2D`；原生空间由 `WorldPhysicsScene2D` 创建/借用，世界身份不再直接充当查询场景。物理代理公共模式为 `WorldFixed`（相对所属世界固定）和 `IndependentDynamic`（在所属世界内独立运动）；Unity 刚体类型、唯一原生模拟入口和生命周期由世界服务管理，区块合批是固定模式的内部实现。不同世界使用独立原生场景，物品只在登记、结构变化、迁移、卸载时更新物理归属，不逐帧扫描。规则 `world_streaming/world_physics_ownership`、`world_physics_definition` 覆盖原生接触隔离、精确区域、临时查询池和回收。
- `TerrainSupportLayer` 分开标记结构所有权与外部显示：`ExternalOwnershipLayer` 防止结构地板随普通土层事务再移交，`ExternalVisualLayer` 只防重画；普通 Tile 地板仍由 ChunkView 显示。支撑上的玩家填土通过 `GetExposedTileId/GetSurfaceCell` 进入普通农业与采挖，迁移作物使用 `IPlantableCrop` 的活体/冷状态坐标接口，不按具体作物 ID 分支。
- 地形独占 Static Rigidbody2D + Manual Composite；树/机器 `ChunkObstacleColliderSet` 从世界服务借 BoxCollider，非 Composite。正式 Item Collider 出现撤数据代理，卸载后恢复；中央脏队列按 Bind 时的真实世界分桶，由世界物理服务在 Simulate 前提交；解绑/重绑撤旧桶，回调重新入队保留到下一 Tick。地形与障碍通知分开，`FlatWorld.ChunkPhysics.{FlushObstacles,RebuildObstacles,PublishObstacles}` 区分重建和镜像通知；规则 `world_streaming/collision_dirty_queue`。
- 自有代理与障碍盒沿用 `usedByComposite=false` 默认值，停用重绑不重复写此原生开关；池化实体先激活节点再提交初始位姿和速度，`world_physics_proxy_gameplay` 检查恢复数据与刚体一致。`world_physics_ownership` 覆盖回池、销毁旧宿主和 Composite 下复用。
- `CombineAdjacent` 且有储存能力的机器共用 `ChunkMergedObstacleRectangles`：先逐源沿区块裁剪，再精确拼接同模板实体矩形，保留空洞/间距；只在源几何变化重建，镜像复用合并盒。库存、交互、战斗和导航仍逐格；回归 `industrial_fluids/combined_tank_collision_*`，离线 `Tools/Tests/CombinedTankColliders/verify.py`。
- `SharedSpriteMeshCache` 属于资源会话；Ready 前预热最终目录，普通流送不清 Mesh，Backend 重建重新注册 BatchMeshID。清理先 BRG 解绑再毁 Mesh，会话结束先禁止 Owner 重登记，逐项释放异常不阻断其余资源。
- BRG Shader 全 Pass 使用一致 `UnityPerMaterial` 布局；AoS 寻址在 `UNITY_SETUP_INSTANCE_ID` 后用 `GetDOTSInstanceIndex()`。Owner 丢失先全量重建，单格 SetVisual 不隐式登记；卸载交换删除后修正句柄并合并上传。
- `OwnerCullingEnabled` 默认关；开时按 Owner 可见集合重建索引，关时仍发布可见实例/绘制命令。后端在普通流送 Owner 暂空时保留，仅完整窗口关闭后释放。
- F5 保留 Runtime/Chunk/租约/BRG，用权威 Tile/Liquid 刷新视觉，运行中的液体身份集合/索引不变。PlayMode 的 `AutomaticAssetRefresher` 锁程序集重载，退出编辑后应用 C# 改动。
- 默认关闭的实验流动分为纯 `LiquidFlowSolver` 与 `WorldLiquidFlowExperiment` 生命周期；恢复差量/建筑后才算，在显式开放区域内传播，四邻共用松弛预算并限总流入/出。结束/异常/换世界归还租约，关闭不撤已存液深。
- 正式 `WorldLiquidBodySystem` 保留变化唤醒的邻格守恒扩散；`PumpSearch` 为当前流送世界按真实湿格/同种液体/阻挡划分连通摘要，水域共享候选、边界进度与数据请求。泵优先直接扣大于一级的真实供水格并保留一级；仅搜索完成且无候选才抽入口末级，未知边界、预算满或读取失败均不能判为耗尽。数量变化增量维护候选，干湿/类型/阻挡变化使相关组失效；轻量摘要可跨数据卸载保留，不持有完整地形。`DiffusionStreaming` 在未知边界暂封流量，按需单请求准备邻区，仅恢复地形/液体/地形阻挡；`DataRetention` 租约不激活模拟或表现。最多24个额外数据块、64个排队请求，满额保留边界等待，停稳后释放并安全回收窗口外无租约块；退出/切换实验器取消请求。仍使用0～10整数水量，一级差停稳，未实现细分尾水或真实水面高度。离线 `Tools/Tests/ConnectedLiquidPumping/`；Lua `surface_fluids/diffusion_data_retention_lease` 与 `industrial_fluids/connected_surface_pump`、`shared_surface_pump_search`。
