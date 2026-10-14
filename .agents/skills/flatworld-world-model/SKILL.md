---
name: flatworld-world-model
description: "FlatWorld 纯世界模型、异步区块生成、租约、流送、差量恢复与 Unity 表现绑定。"
---

# WorldModel

## 入口与数据流

| 路径 | 职责 |
| --- | --- |
| `Assets/5_Scripts/5-0_WorldModel/` | 纯模型、生成、快照、租约与调度 |
| `Assets/5_Scripts/5-3_GamePlay/World/WorldModel/` | `WorldRuntimeHost`；`Presentation/` 下 `ChunkView/IChunkViewRenderer/Chunk*Renderer` |
| `Assets/5_Scripts/5-3_GamePlay/World/Chunk/Management/` | `ChunkMgr.WorldRuntime/RuntimeWindow/LiquidFlow` |
| `Assets/Resources/Config/WorldModel/`、`Assets/2_Prefabs/World/WorldModel/` | 配置与表现 Prefab |

观察者窗口 → 后台纯生成 → 主线程校验/提交 → 差量恢复 → ChunkRuntime 租约 → ChunkView 分步绑定 → 解绑/逐出。

## 权威、所有权与坐标

- `ChunkRuntime + ChunkTerrainData` 是权威；Tilemap/Collider/Renderer 是投影。后台与 Burst 只接收冻结值，不访问 Unity 对象；`RuntimeTileDefinition/Behaviour` 留在主线程资源层。
- 提交核对世界 Epoch 和请求版本；取消/失败/逐出释放生成结果及租约。`Seal` 将池化 Liquid 深度/类型和环境数组唯一所有权交给区块，Dispose 归还；`height` 随区块存活，只作原生成高度。生成结束一次汇总固定湿度/两层降水/风力，`ChunkTerrainData.WeatherProfile` 持有不可变天气概率/强度/持续时间，运行时不随环境层差量重算。
- Liquid 的会话索引来自冻结 `LiquidTypeCatalog`；哈希/持久化使用稳定 LiquidId。Ground 与 Liquid 分开，`SetLiquid` 不写 Ground。`LiquidCellValue.NormalizeDepth` 清除 epsilon 尾数并清类型，抽取量按实际写入前后差计算。
- 正式 Chunk/Map/冷地表与出生查询通过 `GameRes.RequireWorldLiquidTypes` 取得 Ready 后的完整表；ChunkMgr Awake 只绑定宿主，首次请求才建世界，换场景释放旧生成器，不能把启动空目录冻结成 BuiltIn 表。
- `WriteCell` 同步数据、固定 LOS 遮挡位与版本，建筑/机械叠独立动态位。`TerrainSupportLayer.GetSurfaceCell` 投影平台有效表面，不改底材/底液。
- `ChunkTerrainChanged.EnvironmentLayerId/AffectsEnvironmentLayer` 表达真实变化层；液体变化影响当前及八邻区岸线/四角液深/导航。`SetLiquidBatch` 索引唯一递增，跨块先全部写回再 Publish，仅发 `LiquidBatchChanged`；借用索引限同步回调内使用。
- `SetEnvironmentBatch` 按层接受唯一递增格索引，各层先写后 `PublishEnvironmentBatches`，每个真实变化层只发一次 `Changed`；消费者先筛层，再用 `GetCellIndices(width)` 兼容单格/批量。只读层和通知索引只借用于同步阶段；同层待发或回调中禁止写入，发布失败保留未发层供重试，土壤层不改变液体版本。
- `WorldTopologyDomain` 是 noEngine 的纯坐标真源，Jobs 使用主线程冻结副本；`WorldTopologyRuntime` 仍读取存档，仅主线程。`ChunkRuntime.Address/ChunkOrigin` 永远是规范坐标，View Transform 由 `WorldLocalPresentation` 选本地镜像；HUD 经 `ToLogical` 还原。连续曲线用 double 和显式半周期符号策略。
- `ChunkMgr` 常驻 DDOL；View/自然物挂当前世界 Scene 根，不挂管理器。Prefab 直接装配并引用 NaturalItems/LightOccluders 表现组件。
- 居民、ECS 掉落和 MachineWorld 独立于 View；解绑只撤显示。自然散落点成功生成 ECS 掉落后才移除基线 GUID，失败回滚；树/矿点/建筑不是散落点。机器连通组全量恢复后 Tick、全量快照后休眠。

## 流送与生成

- 模拟圈、完整表现预加载圈、数据保留圈分别维护；就绪只检查活动圈，关闭释放全部表现需求。窗口按相机真实 X/Y 半幅计算，管理员视野与普通上限分开。
- 离开窗口的 pending/后台请求取消，保留请求按玩家现位置重排；同区块移动只更新优先级。`Mod_ChunkLoader` 窗口与 `RetargetRuntimePresentationQueue` 共用预加载距离。
- 基础地形跨 Chunk 优先，草/碰撞/导航/自然物后续轮转；取队按当前距离，单块后续每帧最多一步。`IIncrementalChunkViewRenderer` 扫描跳过项也计预算，自然物先宿主后伴生，完成初绑后再季节补位。单项装配仍可能超时。
- 差量在基础绑定前恢复，同一 Runtime 不因窗口刷新重复恢复。外圈预取只等待可见数据/基础地形，不等全部后续表现。
- `WorldManager.prefab` 序列化提交/基础/后续预算覆盖源码默认值。GM 可调数量与毫秒上限；`StreamingDiagnostics` 分开记录排队、生成、提交、恢复、renderer 步骤与等待，读取诊断不推进世界。
- 水文缓存键包含 Epoch、维度、种子、配置和稳定 Region。同组只算一次；单 Chunk 取消不终止仍有需求的共享河网，全部需求消失/世界结束才取消。Region/Halo 只扩大采样，结果只提交核心区域；世界退出清水文/海洋距离降水/海洋水源区块/火山缓存。
- 地表出生查询复用正式 Profile、单格规则与水文缓存，只检查可走地形/液深，不生成完整生态。`WorldAirHumidityField` 只读差量恢复后的 Ready 区块，依 Terrain/LiquidRevision/Epoch 失效，缺影响邻区返回 false，不触发生成。
- `WorldGenerationRuntimeHooks` 的 Profile Hook 在主线程冻结参数；`WorldModelTerrainDecorator` 在生成器创建时捕获委托，正式流送与出生查询共用。地形回调只能读请求中的冻结值并修改纯地形缓冲，不访问 Unity 对象或当前单例；测试平地等临时规则由订阅者退出时撤销。
- Tile 外观查询优先 JSON 的 `runtimeTileId/tileAsset`，再回退 Palette；地形身份按冻结 Profile → 当前 Profile → JSON 目录解析，玩家新建筑用当前目录，稳定数字 ID 与冻结映射不冲突。
- 流送计时是单调墙钟，父子阶段重叠不能相加；暂停跨越请求单独记录。宏观 Lazy 执行时间归实际执行者，get 包含等待，local_refine 只计本块。

## 表现与资源会话

- Ground/Water/Back/Blocking 由 `ChunkGroundMeshRenderer` 批绘，Blocking Tilemap 仅留碰撞；安全单格 Sprite 用 Quad/GPU Cell，外扩 Sprite 保留顶点。树/资源/建筑/机械主体经 `ChunkDepthMeshRenderer` 按本地 Y 整行排序。
- 玩家/GO AI 用 SpriteRenderer 与行网格同 Player Layer/Order；行中心排序有半格近似，真实脚点仍用于几何/遮挡。草/花/阴影 BRG 只能在自身排序域排序。
- 草/花共用 ChunkTilemapRenderer Owner，以 VisualLayer/单格槽隔离；提交确认后登记缓存，解绑清自身整个图层并合并删除。`IRecoverableChunkViewRenderer` 在窗口刷新核对权威数据和真实实例，Owner 重建从权威数据恢复，不用旧可见缓存。`sorting.batchProfiles` 是排序配置真源，草图集 Sprite 跨 Chunk 共享。花初绑一次扫 Placements，避免逐点全表查询。
- 世界 Fire 是独立玩法层，表现走 Y 行网格/GPU 图集翻帧；实体燃烧不写火格。地面积雪走地形光照 Mesh，墙冠用独立 Y 行网格，二者低于 Player。
- `ChunkNaturalItemRenderer` 从所属 View 找 Owner，GUID/部件号标主体，太阳阴影用独立负槽；共用 `WorldEntityRuntime`。解绑先保存并撤表现/导航，最后释放世界。
- 墙裂读权威 `flatworld.tileBuilding.damage`；自身与邻块共享边/角变化增量刷新，Bind 重建、Unbind 退订。水面顶点含高度、四角水深与 Flow；河流为下游速度、海洋为当地风向，湖面 Flow 为零。
- 地形高度分层只读原 height；非 Surface/有液体时禁用，未知/无地面/有液体邻格回退本高，Commit/Evict/邻区变化补边。
- 地形独占 Static Rigidbody2D + Manual Composite；树/机器 `ChunkObstacleColliderSet` 复用 BoxCollider，非 Composite。正式 Item Collider 出现撤数据代理，卸载后恢复；中央脏队列在 ChunkMgr.FixedUpdate 消费，地形与障碍通知分开。
- `SharedSpriteMeshCache` 属于资源会话；Ready 前预热最终目录，普通流送不清 Mesh，Backend 重建重新注册 BatchMeshID。清理先 BRG 解绑再毁 Mesh，会话结束先禁止 Owner 重登记，逐项释放异常不阻断其余资源。
- BRG Shader 全 Pass 使用一致 `UnityPerMaterial` 布局；AoS 寻址在 `UNITY_SETUP_INSTANCE_ID` 后用 `GetDOTSInstanceIndex()`。Owner 丢失先全量重建，单格 SetVisual 不隐式登记；卸载交换删除后修正句柄并合并上传。
- `OwnerCullingEnabled` 默认关；开时按 Owner 可见集合重建索引，关时仍发布可见实例/绘制命令。后端在普通流送 Owner 暂空时保留，仅完整窗口关闭后释放。
- F5 保留 Runtime/Chunk/租约/BRG，用权威 Tile/Liquid 刷新视觉，运行中的液体身份集合/索引不变。PlayMode 的 `AutomaticAssetRefresher` 锁程序集重载，退出编辑后应用 C# 改动。
- 默认关闭的实验流动分为纯 `LiquidFlowSolver` 与 `WorldLiquidFlowExperiment` 生命周期；恢复差量/建筑后才算，在显式开放区域内传播，四邻共用松弛预算并限总流入/出。结束/异常/换世界归还租约，关闭不撤已存液深。
