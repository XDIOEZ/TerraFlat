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
- 墙体裂缝等耐久表现必须从 `ChunkTerrainData` 的 `flatworld.tileBuilding.damage` 权威层推导；`IChunkViewRenderer.Bind` 时重建、监听 `TerrainChangeKind.Environment/Cell/TileStack` 增量刷新、`Unbind` 时解除订阅，禁止在表现组件中保存第二份生命值。
- 墙脚、岸线等依赖邻接关系的表现除监听自身 `ChunkTerrainData.Changed` 外，还必须监听正交相邻区块的共享边界变化；`ChunkCommitted` 只表示邻区就绪，不能覆盖后续拆除或放置造成的运行时更新。
- Ground / Liquid / Back / Blocking 的基础视觉统一由 `ChunkBatchRendererGroupService` 跨 Chunk 批量绘制；`ChunkTilemapRenderer` 保留历史类名用于 Prefab 兼容，但职责已变为 BRG 提交器。Blocking Tilemap 只维护 `TilemapCollider2D` 所需碰撞格，禁止重新开启其 TilemapRenderer 作为第二份视觉。
- BRG 没有 `SpriteRenderer/TilemapRenderer` 的 Sorting Layer 字段，不能指望较低的 Render Queue 跨 Sorting Layer 压到 `Tilemap` 层下面；当前地形 BRG 使用 Default 排序域和 2987~2992 队列。草等需要盖在地形之上、普通世界 Sprite 之下的表现必须与 BRG 共用 Default 排序域，并使用高于 2992、低于 3000 的透明队列。
- 地形 Sprite 几何只经资源会话级 `SharedSpriteMeshCache` 构造，最终 Tile/MOD/Liquid 目录和 Palette 在 Ready 前预热，动态 Sprite 保留懒加载兜底。普通流送与 `ReleaseUnusedBackend` 不清 Mesh；`BatchMeshID` 仅存当前 Backend，退出世界销毁 BRG 后再次进入必须重新注册共享 Mesh。缓存清理先通知 BRG 解绑再销毁 Mesh，禁止反向依赖 Batch 内部实现。
- 世界内 F5 不销毁 WorldRuntime、Chunk、租约或 BRG；`ChunkTilemapRenderer` 随 Bind/Unbind 成对订阅 `GameRes.ResourcesReloaded`，发布后用原权威地形刷新碰撞映射和批量视觉。候选期间不预热或清除共享 Mesh；运行中液体身份集合及数字索引必须不变，因为原世界和后台生成器仍持有原编号表。
- Chunk BRG 自定义 Shader 的全部数值/向量/颜色材质属性必须统一声明在 `UnityPerMaterial` CBUFFER，且同一 Shader 的所有活跃 Pass 保持一致布局；不要 `UsePass` 借用另一个材质布局不同的 Shader Pass，否则 BatchRendererGroup 会因 SRP Batcher 不兼容而拒绝绘制。
- BRG 自定义 AoS 数据寻址必须在 `UNITY_SETUP_INSTANCE_ID` 后使用 `GetDOTSInstanceIndex()` 取得可见列表映射后的真实实例索引；`unity_InstanceID` 只是单次 draw 的局部序号，大批次被 Unity 拆分后会重复从零计数。误用会出现“数据、Owner 和实例数量均正常，但视野扩大后地面永久缺块”，不能靠增加加载距离或重建 Owner 修复。
- BRG 地形实例按脏格增量上传；岸线与墙脚方向放入实例数据，连续水深使用每水格四个格角深度在 Shader 内双线性插值。边界数据依赖八方向邻区，正交邻区变化刷新共享边、对角邻区变化刷新共享角，不得退回整 Chunk `SetTilesBlock` 或每 Chunk 水深纹理重建。
- `ChunkMgr` 随 `WorldManager` 常驻 DDOL；`ChunkView` 及其自然物表现必须挂到当前世界场景的独立根节，禁止以 `ChunkMgr.transform` 作为活动或池化 View 的父级。
- 相机驱动的本地区块窗口必须覆盖真实视口，并按相机半宽/半高分别计算 X/Y 距离；禁止为超宽屏取最大边后构造巨大正方形窗口。普通玩法可以受自动视距上限保护，但管理员无限视野不能继续被普通上限截断；管理员手动增加加载距离只作为最低加载圈数，不能关闭相机自动扩圈。
- 区块窗口变化时必须取消已经离开当前数据窗口、但仍处于 pending/后台队列中的旧生成请求；不能只逐出已完成 Chunk。否则 FIFO 生成队列会持续计算过期区块，导致新进入视野的区块长期饥饿并显示为黑块。
- 已经排队但仍属于当前窗口的生成请求也必须随玩家当前位置重新排序；只在首次入队时按距离排序会让后来进入镜头的新区块卡在历史队列尾部。
- ChunkView 的草地 Tilemap 与基础地形 BRG 生命周期不同；若出现“草仍可见但整块基础地面变黑”，应检查 BRG owner 登记而不是继续增加生成并发。BRG 后端不得在普通流送中因 Owner 短暂归零立即销毁；只在世界窗口彻底关闭后释放。窗口变化时可事件式校验当前绑定，并在登记丢失时从权威 Terrain 原地重建基础层，禁止使用每帧或定时轮询。
- 高视距会一次产生大量已 Ready 的 ChunkView 表现任务；调度必须跨 Chunk 优先完成基础地形 BRG，再轮转补齐草地、碰撞、导航、自然物等后续表现。禁止让单个 Chunk 的全部表现器串行完成后才开始下一个 Chunk，否则会出现“数据已经生成但视野大片长期空白”的表现饥饿。
- BRG 的单格 `SetVisual` 不得在 Owner 丢失时隐式重新注册 Owner；否则脚本热重载或后端重建后的第一次脏格刷新只会恢复局部实例，却让后续校验误判整块已登记。增量刷新发现 Owner 丢失时必须先从权威 Terrain 全量重建，再恢复单格增量路径。
- `ChunkTilemapRenderer.Bind` 激活水层 GameObject 时会同步触发 `WaterVisualStyleBinding.OnEnable`；绑定期允许它先更新共享材质，但必须抑制 `NotifyWaterVisualStyleChanged` 的增量 BRG 修复，因为紧随其后的全量提交会直接读取最新材质。不要把这个正常激活时序误判成 Owner 丢失。
- 提交生成结果前校验世界纪元与请求版本；取消、失败和逐出路径必须释放结果及租约。
- 生成保持固定种子和稳定签名；修改地形内容规则时同时使用 `flatworld-map`。
- 新增 `SurfaceBiomeKind` 或群系条件时，必须覆盖 Profile 当前可选的全部分类算法；`LegacyLand` 只复用旧气候采样，不会自动继承其他分支的群系规则。
- 雪地必须同时使用海拔修正后的实际温度与地形修正后的最终降水；海拔只通过降温提高积雪概率，不得单独把高地覆盖为雪。
- 地块 JSON 的 `runtimeTileId` 与 `tileAsset` 提供运行时数字编号和外观映射；`ChunkTilePaletteSO.TryGetTile` 优先查询 JSON 运行时目录，再回退旧 Palette。Profile 中的 `tile.block.*` 必须与本体 JSON 编号一致；MOD 新地块可通过 JSON 接入，不需要改写本体 Palette 或冻结 Profile。
- 地块行为解析和旧地形身份遵守“冻结 Profile → 当前 Profile → JSON 整数目录”顺序；玩家新建筑可使用 JSON 当前目录，但仍须拒绝覆盖被冻结映射占用的编号。`RuntimeTileDefinition` 和 Behaviour 属于主线程资源层，不能放入纯世界模型或后台生成任务。
- 存档冻结的生成 Profile 只负责保证旧世界生成确定性；运行时玩家建筑使用当前版本的 `tile.block.*` 内容目录，读取旧地形时仍优先冻结映射、缺失才回退当前目录。新增可建造 Tile 必须使用从未被旧 Profile 占用的稳定数字 ID，禁止复用旧 ID。
- Unity 序列化的私有配置结构体字段不会被 C# 编译器识别为 Inspector 赋值；出现 CS0649 时只在对应字段范围使用局部禁用，不要为消警告改写运行时默认值。
- 可走性联动 `flatworld-navigation`，维度地址联动 `flatworld-dimension`，快照持久化联动 `flatworld-data-save`。

- 平台的 `TerrainSupportLayer` 属于权威环境扩展，`GetSurfaceCell` 只投影有效地表，不改原始水格；支撑面与积雪的专用 Renderer 解绑仅清理自身 Tilemap，不能撤销权威状态。

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

- `World/Mechanical/MechanicalWorld` 的模拟权威独立于 `ChunkView`：真实端口连通网络整组唤醒、先全量恢复再 Tick、先全量快照再休眠；不可因可见 Chunk 卸载而删除节点或冻结一半动力源。
- 机械距离只查询缓存的 Chunk `BoundsInt`，含滞回和冷却；拓扑变化时重算单区块属性和循环世界最短包围跨度。表现层只查询已存在的 ChunkView，不为传动网络申请整条地图加载。

- 只补充可复用的易错点、隐含约束和必要注意事项，不记录近期改动流水账。
