---
name: flatworld-navigation
description: "Use when: 定位或修改 FlatWorld 的稀疏网格寻路、16×16 Chunk 分层流场、动态导航脏区、TileData 权重、建筑占地、AI 移动或联机本地导航窗口。关键词：WorldNavigationManager、WorldNavigationGrid、FlowNavigationCache、WorldNavigationAgent、BuildingOccupancyRegistry、Mover_AI。"
---

# FlatWorld 导航

## 入口

- 网格/请求：`Assets/5_Scripts/5-3_GamePlay/World/PathFinding/WorldNavigationManager.cs`
- ECS 网格适配：同目录 `WorldNavigationManager.SharedFlow.cs`；纯数据共享缓存、导向图与 Job 在 `Assets/5_Scripts/Shared/Navigation/`。
- ECS 查表/移动：`Entities/AIECS/Navigation/AiecsFlowAgent.cs`；真实游戏网格的显式开发入口在 `Entities/AIECS/Gameplay/AiecsNavigationCrowd.cs`。
- 动态占地：`World/Building/BuildingOccupancyRegistry.cs`
- Tile 桥：`World/Map/Base/Map.cs`
- AI 移动：`Entities/Move/Mover_AI.cs`
- 调用方：`World/Chunk/Mod_ChunkLoader.cs`、`Networking/Gameplay/NetworkChunkStreamingCoordinator.cs`

## 不变量

- 权威链：Tile 栈顶可走性/权重 + 动态建筑占地 → 脏格/脏区 → 稀疏 `WorldNavigationGrid`。
- 新运行时世界注册导航时读取 `ChunkRuntime.Terrain` 的 `TerrainCell`，不读取旧 `TileData` SO；海洋、河流与地下水都必须同时带 `Water | Walkable`，统一使用有限的高 `NavigationCost`，由带权寻路决定绕行而不是把水注册成障碍。
- 短距离直视线快捷路径只能在中间格代价不高于起终点代价时使用；包含河流等高代价格时必须进入带权寻路，不能只检查可走性。
- `WorldNavigationAgent` 接收路径后的路点跳过也必须沿用同一代价限制；只用几何 LOS 会把已经绕开的高代价地形重新拉直穿过。
- `WorldNavigationGrid.SetCell` 的可走格代价发生变化时必须使旧路径失效，否则运行中的 AI 会继续执行按旧权重生成的路线。
- 限制移动总代价时读取 `WorldNavigationPathResult.TotalCost`；异步新路径超限不能覆盖当前已接受路径，导航代理应让旧路径走完并停止自动续算，只有目标再次明显移动才重新评估。
- `WorldNavigationAgent.DestinationResult` 是当前目的地请求的权威结果；上层必须消费 `RejectedByPathCost`，不能通过速度为零或是否持有路径反推拒绝原因。
- 追击总代价上限必须随 `RequestPath` 传入共享搜索；Dijkstra 前沿代价达到某个请求上限时，只结束该请求并返回明确的代价拒绝，不能等完整搜索结束才判断，也不能取消同目标其它成员的请求。拒绝、成功、取消和失败都须清理起点等待链与上限索引；缓存路径只能使用已结算起点的代价，不能把尚未收敛的暂定代价当成超限依据。
- 运行时只用项目内置导航，不恢复 Aron Granberg A*，也不把 Physics2D 扫描当权威。
- 动态可交互建筑的导航占地与放置占用共用 `BuildingOccupancyRegistry` 的离散世界格记录；实体 Collider 的尺寸/接触状态不能改变导航占格，避免相邻建筑因物理接触污染逻辑层。
- 移除覆盖层后恢复基础层权重；建筑不改 TileData。
- 失败/未表现完成的 Chunk 不注册导航；View 入池或销毁前先 Unbind。
- 本地导航窗口只跟随 owned 玩家；远程副本不移动它。
- Wrapped 世界的网格邻接使用规范化格与最短位移，旧 Agent 路点移动也使用 `ShortestDelta`；不能恢复“不跨接缝”的过期规则。Job 只用冻结的 `WorldTopologyDomain`，不能访问依赖存档的 `WorldTopologyRuntime`。

- 水上平台的可走性和代价来自 `TerrainSupportLayer.GetSurfaceCell`；构建导航窗口和增量更新都读取有效支撑面，原始 `TerrainCell` 保留水格身份。平台变化须发布同一格的导航脏区。

## ECS 分层流场的边界

- `GamePlay` 与 `FlatWorld.AIECS` 共同引用无业务依赖的 `FlatWorld.Navigation`；跨两者的桥接放在独立 `FlatWorld.AIECS.Gameplay`，不能让核心导航引用 AI、Item 或 GamePlay，也不能让 AIECS 与 GamePlay 循环引用。
- 共享缓存只读取 `WorldNavigationGrid` 最终有效值，沿用 10/14 八邻接、目标格地形代价和禁止对角切角；不能另建一套地形/建筑/Physics2D 权威。旧 `RequestPath`、总代价拒绝和取消路径仍由旧后端负责，尚未迁移为 ECS 追击规则。
- `ConsumeChanges` 只供旧管理器消费；共享缓存订阅独立的 `CellChanged/Cleared`。逐格通知必须在旧队列的数量上限判断之前发出，否则大量变更会漏掉 ECS 脏块；世界切换先等待快照读取 Job，再取消订阅和释放缓存。
- 导航 Chunk 固定 16×16，出口由双方都可走的连续边缘缺口生成，一侧可有多个出口；块内断开的区域不能因为“属于同一 Chunk”就连通。每个缺口使用确定的代表格，缓存到代表格的带权局部图，因此保留可达性与代价规则，但不保证等于完整逐格搜索的全局最短路线。
- 目标按玩家/编队创建少量共享句柄，禁止逐 AI 注册目标或创建 `WorldNavigationAgent`。目标在同一格内移动只更新坐标；在同 Chunk 的同一连通分量跨格只更新该目标的 256 格导向图。跨 Chunk、传送到不同连通分量或出口图变化才重算区块级路线；不能省掉连通分量变化的失效判断。
- `AiecsFlowAgent` 的 SharedGoal/Local/Hold 共用同一移动和软避让 Job，默认枚举值保持旧群体兼容。游荡、逃跑及短距离接敌只提供局部目标；`CanSteer` 检查扫掠、切角和地形代价，不能用直线近路绕过昂贵地形，也不能为局部目标新建完整场。战略中心选实际可走的群体成员位置，不能直接把可能落在墙里的平均坐标用作 Goal。
- 原生 Brain 会按共享采样的 Unreachable 状态暂时释放目标并延迟重试；这与旧 `chasePathCostLimit` 的完整总代价拒绝不同，后者尚未迁移。软分离允许短暂重叠，尚不等于严格接敌名额、窄路让行或大型单位通行。
- 内部格变化只重算本块局部图；边缘变化还刷新相邻块的连接，未变的邻块出口锚点复用原图。Native 发布表会在块变化时重新排列并复制原图，不能把“没有重新搜索”当作“完全没有复制成本”。首次加载、流送和大批脏块的时间预算仍需实际测量。
- 快照借用者必须登记 `RegisterReader`；扩容、发布或销毁前完成这些依赖。目标身份同时检查槽位代际和世界 Epoch，旧 World 的句柄不能命中新世界同槽位。
- 导航观察通过 `WorldNavigationFlowRegistry/IWorldNavigationFlowSource` 取得后端真实玩家 Goal，后端销毁前注销；GM 等只读观察者调用 `TryReadPublished`，不得调用 `Read/CreateGoal/UpdateGoal` 为显示触发搜索或生成替代目标。缓存归属检查使用 `OwnsSharedNavigation`，不能借 `GetSharedNavigation` 隐式创建新缓存；未就绪、脏数据、死亡或身份失效时不显示旧场。异步观察同样登记 `RegisterReader`，跨帧只保留自有采样输出。
- 当前共用一个既有网格通行配置，圆形移动只支持半径小于半格；大体型、飞行/游泳能力差异须先按通行配置拆缓存，不得静默共用。循环世界跨度须是 16 的正整数倍，不能通过修改存档尺寸掩盖不支持的域。
- 地形移动用圆心线段与阻挡格 AABB 的距离做扫掠，再保留格级禁止切角；转弯净空不足时先向当前格心对齐。ECS 空间桶只做有界软分离，不提供严格生物碰撞或完整窄路让行策略。
- ECS 群体跨格使用独立的 Tick 占格/预约表，不把生物位置写回 `WorldNavigationGrid/FlowNavigationCache`。每格容量可配置且最小为 1；前进格容量不足时依次尝试仍朝目标推进、地形代价不更高的前侧/侧格，全部满员才等待。预约冲突按 Tick 轮转起点串行裁决，移动 Job 仍并行执行；本 Tick 只能留在原格或进入自己获准的一个相邻格。正式 Bridge 批量出生同样按该容量拒绝同格超额生成。
- 开发入口借用当前真实已加载窗口，不取得战斗区域租约；未知格视为阻挡，卸载块释放缓存。有限 Gizmos、输入上限 20000 和少量共享搜索次数都不是完整生物同屏、真实战斗或性能验收证据。

## 验证

- 使用确定地图和起终点，覆盖可达、不可达、动态阻挡、跨 Chunk 边缘与请求取消。
- 坐标/窗口变化联动 `flatworld-map`，owned 玩家联动 Networking，占地联动 Building，AI 决策联动 AI Skill。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
