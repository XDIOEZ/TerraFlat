---
name: flatworld-world-model
description: "Use when: 定位或修改 FlatWorld 的纯 WorldModel、Chunk 运行时、异步生成调度、世界快照、Chunk 租约、WorldRuntimeHost 或 ChunkView 表现绑定。关键词：WorldRuntime、ChunkRuntime、ChunkTerrainData、ChunkGenerationScheduler、WorldRuntimeHost、ChunkView。"
---

# FlatWorld WorldModel

## 入口

- 纯模型：`Assets/5_Scripts/5-0_WorldModel/`
- Unity 宿主：`Assets/5_Scripts/5-3_GamePlay/World/WorldModel/WorldRuntimeHost.cs`
- 表现适配：同目录 `Presentation/{ChunkView,IChunkViewRenderer,Chunk*Renderer}.cs`
- 运行时桥接：`Assets/5_Scripts/5-3_GamePlay/World/Chunk/Management/ChunkMgr.{WorldRuntime,RuntimeWindow}.cs`
- 配置与资源：`Assets/Resources/Config/WorldModel/`、`Assets/2_Prefabs/World/WorldModel/`

## 主链

`观察者窗口 → ChunkMgr 请求 → 后台纯生成 → 主线程提交 → ChunkRuntime 租约 → ChunkView 分帧绑定 → 解绑并逐出`

## 边界

- `5-0_WorldModel` 保持纯 C#，后台生成不得访问 Unity 对象。
- `ChunkRuntime + ChunkTerrainData` 是权威状态；Tilemap、Collider 和 Renderer 只是表现。
- 正式地块写入统一经 `ChunkTerrainData.WriteCell` 同步核心数据、固定视线遮挡位及版本；生成时建初始遮挡位，建筑和机械占地通过独立动态位叠加，读者只读合成结果。
- 墙体裂缝等耐久表现必须从 `ChunkTerrainData` 的 `flatworld.tileBuilding.damage` 权威层推导；`IChunkViewRenderer.Bind` 时重建、监听 `TerrainChangeKind.Environment/Cell/TileStack` 增量刷新、`Unbind` 时解除订阅，禁止在表现组件中保存第二份生命值。
- 墙脚、岸线等依赖邻接关系的表现除监听自身 `ChunkTerrainData.Changed` 外，还必须监听正交相邻区块的共享边界变化；`ChunkCommitted` 只表示邻区就绪，不能覆盖后续拆除或放置造成的运行时更新。
- ChunkView 的 Ground / Liquid / Back / Blocking、环境阈值、支撑面、积雪与雪冠统一由 `ChunkBatchRendererGroupService` 跨 Chunk 分层绘制；`ChunkTilemapRenderer` 保留历史类名，但材质直接在根组件配置。Prefab 仅保留 Blocking Tilemap 维护 `TilemapCollider2D` 所需碰撞格，不再挂 TilemapRenderer；扩展层使用同一 Owner 的独立 VisualLayer 槽并在 Owner 重建时重提。
- BRG 没有 `SpriteRenderer/TilemapRenderer` 的 Sorting Layer 字段，不能指望较低的 Render Queue 跨 Sorting Layer 压到 `Tilemap` 层下面；当前地形 BRG 使用 Default 排序域和 2987~2992 队列。草等需要盖在地形之上、普通世界 Sprite 之下的表现必须与 BRG 共用 Default 排序域，并使用高于 2992、低于 3000 的透明队列。玩家、生物、建筑和世界物品的 `WorldSorting` JSON 应统一使用比 Default 更靠前的 `Player` 排序层，再由同层 Y 轴决定实体间前后；只提高 Default 层内 Order 无法保证实体不被 BRG 地形盖住。
- 地形 Sprite 几何只经资源会话级 `SharedSpriteMeshCache` 构造，最终 Tile/MOD/Liquid 目录和 Palette 在 Ready 前预热，动态 Sprite 保留懒加载兜底。普通流送与 `ReleaseUnusedBackend` 不清 Mesh；`BatchMeshID` 仅存当前 Backend，退出世界销毁 BRG 后再次进入必须重新注册共享 Mesh。缓存清理先通知 BRG 解绑再销毁 Mesh，禁止反向依赖 Batch 内部实现。
- 世界内 F5 不销毁 WorldRuntime、Chunk、租约或 BRG；`ChunkTilemapRenderer` 随 Bind/Unbind 成对订阅 `GameRes.ResourcesReloaded`，发布后用原权威地形刷新碰撞映射和批量视觉。候选期间不预热或清除共享 Mesh；运行中液体身份集合及数字索引必须不变，因为原世界和后台生成器仍持有原编号表。
- Chunk BRG 自定义 Shader 的全部数值/向量/颜色材质属性必须统一声明在 `UnityPerMaterial` CBUFFER，且同一 Shader 的所有活跃 Pass 保持一致布局；不要 `UsePass` 借用另一个材质布局不同的 Shader Pass，否则 BatchRendererGroup 会因 SRP Batcher 不兼容而拒绝绘制。
- BRG 自定义 AoS 数据寻址必须在 `UNITY_SETUP_INSTANCE_ID` 后使用 `GetDOTSInstanceIndex()` 取得可见列表映射后的真实实例索引；`unity_InstanceID` 只是单次 draw 的局部序号，大批次被 Unity 拆分后会重复从零计数。误用会出现“数据、Owner 和实例数量均正常，但视野扩大后地面永久缺块”，不能靠增加加载距离或重建 Owner 修复。
- BRG 地形实例按脏格增量上传；岸线与墙脚方向放入实例数据，连续水深使用每水格四个格角深度在 Shader 内双线性插值。边界数据依赖八方向邻区，正交邻区变化刷新共享边、对角邻区变化刷新共享角，不得退回整 Chunk `SetTilesBlock` 或每 Chunk 水深纹理重建。
- 水面 BRG 的 `FlowX/FlowY` 对河流保存四格角的下游速度，对海洋重复保存当前格 `windX/windY` 派生的单位方向；海浪和岸边泡沫从该实例数据取方向，不能再用材质 `_FlowDirection` 独立决定海浪流向。
- `ChunkMgr` 随 `WorldManager` 常驻 DDOL；`ChunkView` 及其自然物表现必须挂到当前世界场景的独立根节，禁止以 `ChunkMgr.transform` 作为活动或池化 View 的父级。
- `ChunkView` Prefab 必须直接装配 `NaturalItems/ChunkNaturalItemRenderer` 与 `LightOccluders/ChunkLightOccluderRenderer`，并由根组件序列化引用；流送时不在 `Awake` 动态添加表现组件，缺失时明确报错。
- 相机驱动的本地区块窗口必须覆盖真实视口，并按相机半宽/半高分别计算 X/Y 距离；禁止为超宽屏取最大边后构造巨大正方形窗口。普通玩法可以受自动视距上限保护，但管理员无限视野不能继续被普通上限截断；管理员手动增加加载距离只作为最低加载圈数，不能关闭相机自动扩圈。
- 区块窗口变化时必须取消已经离开当前数据窗口、但仍处于 pending/后台队列中的旧生成请求；不能只逐出已完成 Chunk。否则 FIFO 生成队列会持续计算过期区块，导致新进入视野的区块长期饥饿并显示为黑块。
- 已经排队但仍属于当前窗口的生成请求也必须随玩家当前位置重新排序；只在首次入队时按距离排序会让后来进入镜头的新区块卡在历史队列尾部。
- 草与花使用 ChunkTilemapRenderer 的同一个 BRG Owner，以独立 VisualLayer 和单格槽提交；解绑只清自己的槽，Owner 全量修复后通过 BatchPresentationRebuilt 重提。花层首次绑定应沿 Ecology.Placements 一次扫描并直接提交同格首个未采集点，不能对每个放置点反复调用全列表 TryFindAt，避免密集区块出现 O(n²) 查询。草地图集 Sprite 必须按贴图配置跨 Chunk 共享身份，否则 Sprite 网格缓存和 BRG 批次会被每 Chunk 重复切开；批量绑定按 IIncrementalChunkViewRenderer 分步推进。BRG 后端不得在普通流送中因 Owner 短暂归零立即销毁；只在世界窗口彻底关闭后释放。窗口变化时可事件式校验当前绑定，并在登记丢失时从权威 Terrain 原地重建基础层，禁止使用每帧或定时轮询。
- 高视距会一次产生大量已 Ready 的 ChunkView 表现任务；调度必须跨 Chunk 优先完成基础地形 BRG，再补齐草地、碰撞、导航、自然物等后续表现。启动和后续表现每次取队都按玩家当前位置重选，跨区块时应在完整窗口节流前撤销旧视野任务；禁止让单个 Chunk 的全部表现器串行完成后才开始下一个 Chunk，否则会出现“数据已经生成但视野大片长期空白”的表现饥饿。
- 单个表现器会批量实例化实体时实现 `IIncrementalChunkViewRenderer`，让 `ChunkView` 按步骤推进；基础地形启动与后续表现分别受主线程时间预算约束，后续队列同一区块每帧最多执行一步。自然物必须先生成宿主、后生成伴生物，初始绑定完成前暂停季节补位与延迟伴生物检查；同步入口复用相同步骤。
- `WorldManager.prefab` 的序列化分帧预算会覆盖 `ChunkMgr` 字段默认值；排查黑块时先用 `gameplay_chunk_render_debug` 对照 `generationQueued/Active`、`pendingCommits`、`readyDataWithoutView`、`pendingBaseTerrain` 与实际 `presentationStartsPerFrame`，不要仅凭源码默认值判断表现吞吐。
- 流送性能由 `WorldRuntime.StreamingDiagnostics` 在阶段边界记账：生成排队/执行、提交排队/处理、差量恢复、各 `renderer.*` 同步步骤与表现等待分开；仅 Editor/Development 启用，有界缓存且不逐格刷日志。诊断必须同时检查 `WorldRuntimeHost` 现有 owner 和 Update/Advance 心跳，禁止在读取时自动重绑、提交或补生成。未测 GPU 不可凭 BRG 登记正常断言 GPU 没瓶颈。
- 慢区块日志由纯模型记录分段耗时、主线程限频输出；河网 `Lazy.Value` 可能由任意等待线程执行，`river.region_compute` 必须归实际执行线程，`river.region_get` 才代表包含共享等待的总耗时。只有 `region_get` 很慢而没有 `region_compute` 的区块是在等同一区域计算，不能误判为各区块重复计算。
- 高度河网区域未就绪时，生成调度按与水文缓存相同的区域键只运行一个同组任务，其余任务留在优先队列，空闲名额先计算其它区域；区域缓存就绪后同组区块恢复并行。稀疏源头和单主路径限制计算格数，选路只查相邻八格真实高度；严格下坡的最大堆出队顺序已保证无需额外 `processed` 集合。
- 共享河网区域的计算不能绑定到单个区块的取消：当前区块离开窗口时，只要队列仍有同区域有效请求就继续算完并复用缓存；该区域所有请求都取消或世界关闭时才停止。原区块结果仍须丢弃，不能把已取消区块提交回世界；判断剩余需求要沿用与河网缓存相同的区域键。
- `river.route_network` 若仍占主耗时，慢日志区分邻格选择与原始高度噪声，并列出出队格数与高度缓存未命中次数；这些阶段存在嵌套，子阶段毫秒数不能直接相加。采样计时只在诊断开启且缓存未命中时执行，避免逐格 Unity 日志。
- 耗时为单调墙钟而非 CPU 使用率；Editor 暂停跨越的请求单独标记并排除等待汇总，不能把暂停后的完成通知积压当作运行时算力证据。父子阶段有重叠，累计时长不能相加；采样差值只统计本段结束的阶段，不冒充仅落在时间窗口内的 CPU 时间。
- 以空间换显示延迟时，纯模型窗口需分别维护模拟圈、完整表现预加载圈和数据保留圈；本地 ChunkView 在预加载圈提前绑定并持有表现租约，进入活动圈不应重新绑定。世界进入就绪判定只检查活动圈，关闭窗口必须释放全部预加载表现需求。BRG 按 Owner 区块边界与相机裁剪面筛选实例，离屏 Owner 保留已提交数据，靠近时由 Culling 直接显示。
- BRG 剔除由流送设置的 `WorldStreamingPreferences.OwnerCullingEnabled` 控制，默认关闭以便对照高视距的全量绘制；开启时单次回调按 Owner 计算可见性，只在 Owner 可见集合或实例增删变化后重建每批可见索引。交换删除实例时须同步维护状态引用与索引；全部 Owner 可见时直接走全量可见路径。关闭剔除仍须正常生成 BRG 的可见实例和绘制命令，不能跳过回调。
- 卸载 BRG Owner 时先批量交换删除并修正被搬动实例的句柄，最后按 Batch 合并上传仍有效的脏区；单格增量继续即时上传。
- `Mod_ChunkLoader` 的窗口刷新与逐帧 `RetargetRuntimePresentationQueue` 必须使用相同的表现预加载距离，否则下一帧会撤销外圈 ChunkView；同一 Chunk 内的移动只更新任务优先级，跨 Chunk 或视距变化才重建窗口。
- 外圈数据预取只须等待可见窗口的数据与基础地形 BRG 完成；草地、导航、自然物等后续表现继续轮转时不应长期占住后台生成的空闲时机。存档地形差量必须先于基础地形绑定恢复，同一 ChunkRuntime 实例不能因窗口刷新重复恢复。
- BRG 的单格 `SetVisual` 不得在 Owner 丢失时隐式重新注册 Owner；否则脚本热重载或后端重建后的第一次脏格刷新只会恢复局部实例，却让后续校验误判整块已登记。增量刷新发现 Owner 丢失时必须先从权威 Terrain 全量重建，再恢复单格增量路径。
- `ChunkTilemapRenderer.Bind` 激活水层 GameObject 时会同步触发 `WaterVisualStyleBinding.OnEnable`；绑定期允许它先更新共享材质，但必须抑制 `NotifyWaterVisualStyleChanged` 的增量 BRG 修复，因为紧随其后的全量提交会直接读取最新材质。不要把这个正常激活时序误判成 Owner 丢失。
- 提交生成结果前校验世界纪元与请求版本；取消、失败和逐出路径必须释放结果及租约。
- 生成保持固定种子和稳定签名；修改地形内容规则时同时使用 `flatworld-map`。
- 地表出生搜索只判断可走地形与 Liquid 深度，应复用正式生成的 Profile、世界纪元、水文缓存和单格地形规则；不要为候选格生成完整 Chunk 或生态放置记录。当前结构阶段不改可走标记和液体，若以后改变这一约束，出生查询也必须纳入对应规则。
- 高度河网的径流单元把海洋格计入采样总数，但海洋格不贡献径流；计算顺序应先判海平面再采降水。陆地降水复用已采样高度，纯降水查询不需要温度通道。
- 新增 `SurfaceBiomeKind` 或群系条件时，必须覆盖 Profile 当前可选的全部分类算法；`LegacyLand` 只复用旧气候采样，不会自动继承其他分支的群系规则。
- 雪地必须同时使用海拔修正后的实际温度与地形修正后的最终降水；海拔只通过降温提高积雪概率，不得单独把高地覆盖为雪。
- 地块 JSON 的 `runtimeTileId` 与 `tileAsset` 提供运行时数字编号和外观映射；`ChunkTilePaletteSO.TryGetTile` 优先查询 JSON 运行时目录，再回退旧 Palette。Profile 中的 `tile.block.*` 必须与本体 JSON 编号一致；MOD 新地块可通过 JSON 接入，不需要改写本体 Palette 或冻结 Profile。
- 地块行为解析和旧地形身份遵守“冻结 Profile → 当前 Profile → JSON 整数目录”顺序；玩家新建筑可使用 JSON 当前目录，但仍须拒绝覆盖被冻结映射占用的编号。`RuntimeTileDefinition` 和 Behaviour 属于主线程资源层，不能放入纯世界模型或后台生成任务。
- 存档冻结的生成 Profile 只负责保证旧世界生成确定性；运行时玩家建筑使用当前版本的 `tile.block.*` 内容目录，读取旧地形时仍优先冻结映射、缺失才回退当前目录。新增可建造 Tile 必须使用从未被旧 Profile 占用的稳定数字 ID，禁止复用旧 ID。
- Unity 序列化的私有配置结构体字段不会被 C# 编译器识别为 Inspector 赋值；出现 CS0649 时只在对应字段范围使用局部禁用，不要为消警告改写运行时默认值。
- 可走性联动 `flatworld-navigation`，维度地址联动 `flatworld-dimension`，快照持久化联动 `flatworld-data-save`。

- 平台的 `TerrainSupportLayer` 属于权威环境扩展，`GetSurfaceCell` 只投影有效地表，不改原始水格；支撑面与积雪的专用 Renderer 解绑仅清理自身 BRG 槽，不能撤销权威状态。

## 验证

- 大型内陆淡水湖由纯 `LargeFreshwaterLakeKernel` 在河流覆盖和地表分类之间生成；`lake.large.*` 参数属于冻结 Profile，候选区域必须按世界拓扑规范化并在相邻区块得到完全相同的湖岸/岛屿。不得把海洋格改为淡水，也不能只增加旧汇流小湖的搜索预算来替代大型盆地。
- 大湖与河流共用水深和 `GeneratedHydrologyKind.Lake` 权威层，湖心 Flow 为零；河口波纹传播仅写 BRG 的表现角速度，不能写回环境流向或推动湖面漂浮物。生成规则变化递增当前签名，已有缓存地块不会仅因参数修改自动重建。

- 单机自然生成中的可拾取散落点可直接生成 ECS 掉落；确认生成成功后才标记基线 GUID 已移除，失败须回滚新掉落，避免基线与独立快照重复恢复。树木、矿石节点、传送门及已安装建筑不能按散落点处理。
- ECS 掉落生命周期独立于 ChunkView；卸载显示批次只回收 Mesh/Renderer，不能销毁权威掉落实体或写自然物删除差量。

- 循环坐标统一使用独立 `FlatWorld.WorldTopology` 程序集中的 `WorldTopologyDomain` 值副本；该程序集仅引用 Unity.Mathematics、noEngineReferences=true。`ChunkGenerationTopologySnapshot` 的整数归一化和连续洞穴坐标均委托 Domain，不再自行定义 Wrap；连续曲线保留 double 精度及显式的半周期符号策略，不影响普通坐标 API 默认半周期取负方向的契约。
- Jobs/Burst 只能接收主线程冻结的 Domain，不能调用仍读取 SaveDataMgr 的 `WorldTopologyRuntime`。创建、保存或运算 Domain 不得注册 GameObject 生命周期或创建物理镜像；ChunkView 的可选物理表现只经 Bind/Unbind 与 Terrain.Changed 通知协调。

- 验收统一进入真实 Play Mode，实际移动跨区块、触发生成/流送/逐出并观察权威状态与表现；编译与 Console 只作为运行门禁和故障定位。

- Liquid 独立持有池化的 `LiquidDepth[]/LiquidTypeIndex[]`，Seal 移交唯一所有权、取消或逐出时归还；编号来自资源会话冻结的 `LiquidTypeCatalog`，稳定哈希与持久化使用 LiquidId，不能使用会话编号。`height` 在 Seal 时随环境数组移交给正式区块，供 Surface Ground 高度分层读取，直到区块 Dispose 才归还；它不参与玩法内容指纹，不增加存档字段，也不能用于反算液深。
- 高度分层只读取原生成高度：Ground 的 `Transform0.w` 保存当前高度（负值禁用），`Data1` 按左、右、下、上保存邻高，BRG 步长保持 112 字节。缺失邻区、非法高度、无 Ground 或有 Liquid 的邻格回退为本格同高；本格非 Surface 或有 Liquid 时禁用。复用 Environment/Liquid 脏区及邻区 Changed，并同时响应 ChunkCommitted / ChunkEvicted，不能靠轮询或重新采样 Noise 补边界。
- `TerrainChangeKind.Liquid` 必须驱动当前格、八方向邻区的岸线/四角液深和导航刷新。TerrainCell 不保存液体标记，SetLiquid 不能修改任何 Ground 字段；生成筛选读取 LiquidDepth，有效表面接触额外考虑 TerrainSupportLayer，纯液体变化不得产生 Ground 差量。

## Skill 维护原则

- 液体流动是默认关闭的实验模块：纯计算在 `LiquidFlowSolver`，生命周期在 `WorldLiquidFlowExperiment` / `ChunkMgr.LiquidFlow`。使用 `TryGetSurfaceElevation` 缓存冻结生成高度；通用批量基础与实验核心分开提交，关闭实验不等于撤销已保存的液深变化。
- `SetLiquidBatch` 要求索引唯一递增，跨 Chunk 先全部写回再 `PublishLiquidBatch`；批次仅发 `LiquidBatchChanged`，不会逐格发 `Changed`。借用的变化索引只能在同步回调内读取；新消费方必须显式订阅批事件。BRG 在帧末合并邻块脏格，Navigation 与植被也消费批事件。
- 实验流动从外部修改或显式唤醒开始；模拟内部只能在已开放的有限区域传播，不能通过再次扩大区域绕过海洋保护。等待邻区、停用、异常和换世界都必须结束批次并归还租约；存档差量与建筑恢复完成前不能计算流量。四邻格共享松弛预算，并同时限制整格总流入/总流出，避免透支、溢出与棋盘振荡。

- `World/Mechanical/MechanicalWorld` 的模拟权威独立于 `ChunkView`：真实端口连通网络整组唤醒、先全量恢复再 Tick、先全量快照再休眠；不可因可见 Chunk 卸载而删除节点或冻结一半扭矩源。
- 机械图在世界作用域建立时冻结 `WorldTopologyDomain` 值副本，构图、查格和交互候选窗口必须使用同一坐标域；换世界重建图，节点增删、移位或拓扑变化置脏并由 `Rebuild` 清空候选窗口。窗口只缓存节点集合，最终交互距离仍按玩家和光标的实时位置判断；旧 MOD 的归一化委托构造入口保留。
- 机械 Sprite 按区块 BRG Owner 分层提交；节点格或转速变化只更新对应实例槽，Owner 全量重建后从机械数据网重提。连续转动和轴纹滚动用实例速度/相位在 GPU 计算，轴纹 `frac` 必须在片元阶段执行并限制在 Sprite UV 区间，避免顶点跨接缝插值撕裂；循环世界显示坐标取当前区块格，节点规范化坐标仅作数据身份。不能恢复逐节点 `Update`、SpriteRenderer 或重建地形批次。
- 机械距离只查询缓存的 Chunk `BoundsInt`，含滞回和冷却；拓扑变化时重算单区块属性和循环世界最短包围跨度。表现层只查询已存在的 ChunkView，不为传动网络申请整条地图加载。

- 只补充可复用的易错点、隐含约束和必要注意事项，不记录近期改动流水账。
