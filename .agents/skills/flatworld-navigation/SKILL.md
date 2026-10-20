---
name: flatworld-navigation
description: "FlatWorld 稀疏导航网格、带权路径、16×16 分层流场、动态占地及连续群体移动。"
---

# 导航

## 入口

代码根目录 `Assets/5_Scripts/`：

| 目录/文件 | 职责 |
| --- | --- |
| `5-3_GamePlay/World/PathFinding/` | `WorldNavigationManager/Grid/Agent`；`Manager.PortalPath/SharedFlow` 适配长路与共享场 |
| `Shared/Navigation/` | 纯 `FlowNavigationCache`、Portal/Flow 图、`FlowPathBuildJob` |
| `5-3_GamePlay/Entities/AIECS/Navigation/AiecsFlowAgent.cs` | ECS 连续移动 |
| `5-3_GamePlay/World/Building/BuildingOccupancyRegistry.cs` | 离散建筑占地 |
| `5-3_GamePlay/Entities/Move/Mod_Mover_AI.cs` | GO AI 目标/导航代理 |
| `5-3_GamePlay/Entities/Item/Modules/Player/Mod_GameMCP_LLM.cs` | 路点经 GameController 租约和 Mod_Mover 执行 |

`Mod_ChunkLoader` 与 `NetworkChunkStreamingCoordinator` 建本地 owned 玩家窗口；`AiecsNavigationCrowd` 是借真实已加载网格的开发入口。

## 网格与路径契约

- 权威链：Tile 栈顶/有效支撑面的可走性与代价 + 离散动态占地 → 脏格/区 → `WorldNavigationGrid`。Collider/Physics2D 不是网格真源，建筑不修改 TileData。
- `worldGridOccupancy.cells` 是物品占格真源，纯 `WorldGridOccupancyData/GridCellOffset` 可供非 Unity 消费；生命周期桥只读格锚点并登记增删移动。实心资源的碰撞与占格分别配置，矿点继承一格占地，可拾取小石头不继承。
- 机器数据 Box 属于 `ChunkCollisionRenderer` 的物理表现，不改导航权威；初绑全量收集，普通 `MachineWorld.CellChanged` 按锚点增量更新、去除失效 Box，跨锚点占地回退全量扫描。校验 `navigation/machine_obstacle_dirty`。
- 来源 ID 为 long：Unity 有符号 InstanceID 保留低域，数据来源占高 32 位域；后端交接先撤旧再登新。失败/未表现完成的块不注册，View 入池前 Unbind。
- Ground 基础成本与 Liquid 成本独立；`WorldLiquidSystem.GetNavigationCost` 合成有限高成本，液体仍可走，抽干回到底材成本。平台 `TerrainSupportLayer.GetSurfaceCell` 遮断液体成本并使有效液深为 0。
- 导航块注册、地块局部变更及已加载格只读共用 `ResolveRuntimeLiquidNavigation`，每格只调用一次 `GetSurfaceLevel`，同一结果派生水深和实际路径代价；规则 `navigation/liquid_cell_snapshot` 核验平台遮水、移除平台和抽干水体。
- 区块注册、局部批量更新和卸载先冻结 `WorldTopologyDomain`，已规范化格使用 `GetEffectiveWalkableNormalized/SetCanonicalCell/RemoveCanonicalCell`，不逐格重新读取活动星球；外部 `SetCell/RemoveCell` 仍自行归一化。规则 `navigation/canonical_batch` 对照两套入口、失效版本、液深及环绕。
- `WorldNavigationGrid` 的 16×16 导航块在最后一格移除后复用完整数组（最多缓存64块），`Clear` 释放缓存；动态阻挡另有独立归属，不随格数据池化清除。`navigation/chunk_block_pool` 检查流式重载、来源阻挡及缓存上限。
- 16×16 块寻址用位掩码同时支持负数和整型边界，写入/移除时一次取块并带回旧格，避免重复查字典；`navigation/block_addressing` 对照边界、坐标回读、枚举、移除及读取基准。
- 直线快捷路径和路点跳过都检查中间格成本不高于端点；可走格成本变化也使旧路径失效。总成本读 `WorldNavigationPathResult.TotalCost`。
- `DestinationResult` 表达目的地结果，含 `RejectedByPathCost`；同目标四次无路径后保持 Failed，目标/网格修订/成功路径解除。新路径超限不覆盖已接受路径，旧路径走完后停续算，明显目标移动才再评估。
- 追击上限随 RequestPath 进入共享搜索，前沿到上限只结束该请求；成功/拒绝/取消/失败清等待链，缓存只用已收敛起点成本。
- 旧 GO 导航目标场的过期扫描按所有缓存目标（包括正在处理请求的目标）的最早有效截止时间跳过稳态帧；新增目标可提前截止，目标寿命配置变化立即重扫。搜索格预算只在目标场创建/移除或逆向搜索新存入格子时重新统计；规则 `navigation/goal_cache_maintenance` 对照原始逐帧全扫。Profiler `FlatWorld.Navigation.GoalCache.PruneScan/CountScan` 只覆盖实际扫描。
- `WorldNavigationManager.GoalField.TryBuildPath` 在同一目标场内复用原始格和路点平滑临时列表，成功路径始终另建独立 `Vector2[]` 返回，不能把可重用缓冲直接暴露给 AI；规则 `navigation/goal_path_buffers` 校验代价、终点、请求隔离和重复寻路 GC。
- Wrapped 网格/路点用规范格和最短位移，Job 用冻结 WorldTopologyDomain。当前通行配置只支持半径小于半格的圆形单位，能力/大体型差异尚未拆缓存，环绕跨度为 16 的正整数倍。
- Mod_Mover_AI 的 `ITemperatureSafetyMovement` 避险目标优先；普通目标/停止不抢占，死亡/卸载仍强停。体温模块拥有危险记忆与方向偏置，普通目标按整段缓存偏置，同一个避险目标重复提交不强制重寻路；鸟的地面到达/失败沿用 mover 结果。

## 共享 Flow 与并发

- GamePlay/AIECS 共同依赖纯 `FlatWorld.Navigation`，业务桥位于独立 AIECS.Gameplay。权威格按 16×16 连续数组存，归属只记 Chunk；TryGetCell 接收已规范格。
- 八邻成本 10/14，目标格成本、禁止切角。连续边缘缺口产生确定代表出口，一边可多出口，块内断区不连通；Portal 路保持可达/代价规则，但不保证逐格全局最短。
- 长路按目标格收请求，先更新整批目标再 Read 一次，批内目标不淘汰；Job 持有自有输出并 RegisterReader，批调度后 ScheduleBatchedJobs，完成才接收。取消核对对象/代际，成本修订重请求，阻挡变化只验实际路径。
- 快照借用 Job 登记 RegisterReader；写入/扩容/发布/销毁前完成依赖，NativeList 长度变化重取视图。目标句柄核对槽位代际与世界 Epoch，输出未消费期间不覆写。
- 表层液深/流向变化只更新块数据，成本/出口变化才重建图。内部变格重算本块，边缘变化还改邻块连接；未搜索的图仍可能随 Native 发布重排复制。
- ECS 少量共享目标，GO 按目标格复用句柄；同格移动仅改坐标，同块同连通分量跨格仅更新 256 格局部图，跨块/连通分量变化/出口变化重算宏观路线。
- `ConsumeChanges` 属旧管理器；共享场订独立 `CellChanged/Cleared`，逐格通知先于旧队列上限判断。世界切换先收读取 Job，再退订/释放。
- 只读观察经 `WorldNavigationFlowRegistry/IWorldNavigationFlowSource.TryReadPublished` 读取真实 Goal，`OwnsSharedNavigation` 检归属；显示不创建缓存/目标或触发搜索，跨帧保留自有采样。

## 连续群体移动

- `AiecsFlowAgent` 的 SharedGoal/Local/Hold 共用 Crowd Job；局部游荡/逃跑/接敌只给局部目标，战略中心选实际可走成员位置。
- 导航格不拥有单位和容量；单位连续 float2，可同格。每 Tick 冻结位置/速度，建立空间桶及 1×1 密度场，固定数量邻居提供有界 Steering/减速，密度不写回网格。
- 最终逐小步经 Navigation.CanStep；圆心线段到阻挡 AABB 的扫掠处理身体净空，软液体成本仅按中心判定。Steering 不将中心推入更贵地形，飞行态不受地面水流推动。
- 未知格阻挡，转弯净空不足先对齐格心；允许短暂视觉重叠，近战名额由战斗 Engagement Slot 控制。Native Unreachable 延迟重试与 GO 完整总代价拒绝是不同能力。
- 连续 Steering/密度默认开启，GM 关闭时仍保留 Flow/地形扫掠，不启用单格预约。
- GO 导航的局部避让每轮 Tick 冻结一次世界拓扑，九宫格桶、邻居最短距离和侧移探点复用该值；九宫格按 X/Y 拆分只取三次桶坐标，停止/无目标 AI 不再占用邻居桶，无实际侧移/碰撞则不额外探路。仍实时读取成员位置与地形；规则 `navigation/avoidance_topology` 覆盖负坐标、环绕接缝、九宫格及无效探路跳过。
- `Mod_Mover_AI.StopMovement` 是明确取消目标和路径；模拟范围休眠仍用独立 Pause 语义保留恢复条件。AI 真实推进 Lua 用自有同阵营生物隔离玩家避让，并检查到达后结束和取消后无目标/路径。
