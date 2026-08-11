---
name: flatworld-navigation
description: "Use when: 定位或修改 FlatWorld 的稀疏网格寻路、动态导航脏区、TileData 权重、建筑占地、AI 移动、跨 Chunk 导航或联机本地导航窗口。关键词：WorldNavigationManager、WorldNavigationGrid、WorldNavigationAgent、BuildingOccupancyRegistry、Mover_AI。"
argument-hint: "寻路、可走性或动态障碍问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 导航系统定位

> 最后核对：2026-08-11。运行时导航由项目内置实现负责，不依赖 Aron Granberg A* Pathfinding Project，也不使用 Physics2D 碰撞扫描作为权威数据。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/World/PathFinding/WorldNavigationManager.cs`：稀疏世界网格、Map 注册、批处理修订和路径请求。
2. `Assets/5_Scripts/5-3_GamePlay/World/Building/BuildingOccupancyRegistry.cs`：动态建筑占地覆盖层。
3. `Assets/5_Scripts/5-3_GamePlay/World/Map/Base/Map.cs`：TileData 变化到导航脏区的桥接。
4. `Assets/5_Scripts/5-3_GamePlay/Entities/Move/Mover_AI.cs`：AI 运行时移动。

## 权威数据与链路

```text
Data_TileMap.GetTopTile（地形顶层可走性/权重）
+ BuildingOccupancyRegistry（动态建筑阻挡）
→ Map.MarkPenaltyDirty
→ WorldNavigationManager.QueueNavigationCell(s) / RegisterMap
→ WorldNavigationGrid 批量发布修订
```

## 关键调用方

- Chunk 跟随：`Assets/5_Scripts/5-3_GamePlay/World/Chunk/Mod_ChunkLoader.cs`。
- 联机本地导航窗口：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkChunkStreamingCoordinator.cs`。
- AI 目标与移动：`Assets/5_Scripts/5-3_GamePlay/Entities/AI/`、`Assets/5_Scripts/5-3_GamePlay/Entities/Move/Mover_AI.cs`。
- 动态建筑：`Assets/5_Scripts/5-3_GamePlay/World/Building/Mod_Building.cs`。
- Ghost 导航编辑器检查：`Assets/5_Scripts/5-2_Editor/GhostNavigationTest/`。

## 当前约束

- `WorldNavigationManager` 维护绝对世界坐标的稀疏 `WorldNavigationGrid`，每个已就绪 Map 一次批量注册所有非空格。
- 导航始终读取地形栈顶层；移除覆盖层后必须自动恢复基础层的 `Penalty/IsWalkable`。
- 建筑不改写地形栈权威数据，只通过 `BuildingOccupancyRegistry.GetEffectiveWalkable()` 叠加动态不可走性。
- 失败 Chunk 不得注册导航格或留在加载等待中；`ChunkMgr` 必须以失败回调结束等待者并安全回收。
- `WorldNavigationAgent` / `Mover_AI` 必须通过 `WorldNavigationManager` 提交路径请求，不得回退到物理全场扫描或直接穿越不可走格。
- 联机时导航图跟随本地 owned 玩家，Chunk 流送仍按所有观察者并集。
- WorldModel 数据预取圈只生成 `ChunkRuntime`，不得注册导航；区块进入可见圈后由 `ChunkMgr.RuntimeWindow` 表现协程完成 `ChunkView.Bind()` 才领取 Navigation 租约并注册。排队期间离开窗口或世界重置必须取消表现项，只有 `ChunkView.IsBound` 的区块可以持有导航租约。View 进入对象池或被延迟销毁前必须先完成 `ChunkView.Unbind()`，池内禁用对象不得残留 Navigation/Presentation 租约。
- 地下矿洞开放格顶层为 `TileBase_Stone`（`IsWalkable=true`、`Penalty=1000`），岩壁顶层为 `TileBase_StoneWall`（`IsWalkable=false`、`Penalty=0`）；独立“建筑阻挡层”的 TilemapCollider 只负责实体物理碰撞，`WorldNavigationGrid` 仍以顶层 TileData 为权威。

## 高耦合联动

只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；调试显示或编辑器可视化变化不要扩散到玩法系统。

| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| `WorldNavigationGrid` 坐标规范化、跨 Chunk 边缘或本地导航窗口 | `flatworld-map`；涉及 owned 玩家窗口时再加载 `flatworld-networking` | 导航窗口与 Chunk 窗口一致，远程玩家不移动本地导航窗口 | `Map.Smoke`；联机时追加 `Networking.Smoke` |
| TileData Penalty、脏格/脏区 API、连接重算或占地覆盖 | `flatworld-map`、`flatworld-building` | 地图提交静态数据，建筑只叠加动态占地 | `Map.Smoke`、`Building.Smoke` |
| 目的地校验、路径不可达语义、`WorldNavigationAgent` 或 AI 移动入口 | `flatworld-ai` | AI 不绕过可走性检查或退回直接 `MoveTowards` | `AI.Smoke` |

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-11：移除已无运行时、程序集、场景与 Prefab 引用的 Aron Granberg A* Pathfinding Project embedded package 及 `AstarGizmos` 配置；当前导航完全由 `WorldNavigationManager`、`WorldNavigationGrid` 与 `WorldNavigationAgent` 实现。
- 2026-08-10：`ChunkView` 动态延迟池明确要求入池与销毁前先 `Unbind()`，即使分帧绑定被世界退出中止，也必须同步释放唯一 Navigation/Presentation 租约后再禁用或销毁。
- 2026-08-09：幽灵追击状态改为直接沿世界最短方向位移，明确跳过地形可走性和导航路径校验；非追击状态仍保留原导航代理用于避光/闲逛移动。
- 2026-08-09：新版矿洞在 `DeterministicChunkGenerator` 已将房间/隧道写为可走地面、岩壁写为顶层 Blocking Cell；`ChunkView` 绑定时继续一次性把同一 `ChunkTerrainData` 交给碰撞与导航，矿脉和传送门 Item 不反向修改导航权威。
- 2026-08-09：Ghost 继续以 `WorldNavigationAgent` 和新版权威地形校验目的地；移动后只通知 `ItemMgr` 刷新 `WorldAddress` 实体索引，不再检查旧活动 Chunk 或通过父级切换归属。
- 2026-08-08：WorldModel 外圈改为空闲单任务预取且不再做方向预测；预取区块不注册导航。可见区块的 `ChunkView` 依次跨帧绑定地面、环境、碰撞、草地和 Navigation，完成前离开窗口会用绑定版本安全终止，只有最终绑定的 View 持有唯一 Navigation 租约。
- 2026-08-05：导航地形读取迁移到 `TileStackCell` 顶层 API；覆盖层移除后恢复基础层状态，并继续与 `BuildingOccupancyRegistry` 动态占地叠加。失败 Chunk 不进入导航 Ready/注册链。

- 2026-07-31：静态阻挡 Tile 独立渲染到“建筑阻挡层”，但导航仍读取原数据格顶层；禁止改为扫描该 TilemapCollider 决定节点权重。
- 2026-07-31：矿洞生成新增不可走岩壁 TileData；房间和隧道在地图初次完成后统一进入现有全 Chunk 导航更新，不逐格触发烘焙。
- 2026-07-27：导航权威数据切换为 `TileData + BuildingOccupancyRegistry`，移除物理碰撞全场扫描依赖。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Navigation/NavigationSmokeTests.cs`；当前基础覆盖 `WorldNavigationManager`、动态占地和 AI 移动入口。
- 真实单机异步寻路由 Golden Path 操作 `navigation.loaded-grid` 覆盖：从玩家周围已加载稀疏网格选择确定可走目标，等待 `RequestPath` 回调并断言到达与 Waypoint；Cleanup 取消未完成请求。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；导航测试约定目录：`Assets/GameTest/Navigation/`；场景目录：`Assets/GameTest/Scenes/Navigation/`；冒烟分类：`Navigation.Smoke`。
- 新增寻路、动态脏区、TileData 权重、建筑占地或 AI 移动行为时必须增加系统测试；修复 Bug 时先增加回归测试。可达路径与动态障碍更新主流程变化时同步更新导航冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；路径测试必须使用确定地图与起终点，并验证不可达路径不会产生伪结果。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Navigation.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类；路径可视化仅在最终调试显示变化时截图。
- 导航精简 Smoke 位于 `Assets/GameTest/Navigation/NavigationSmokeTests.cs`，保留地形与建筑占地共同控制可走性的关键行为。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

改变图尺寸、节点权重规则、脏区 API、建筑占地、AI 移动组件、联机跟随策略或导航测试路径后，必须更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。

## 有限世界一期导航边界（2026-08-06）

- 玩家环绕后只刷新对侧的规范 Chunk 导航窗口和权重。
- 一期明确不在地图两侧建立导航邻接边，AI 不跨缝寻路；不要把环面坐标最短位移误解为导航图连边。
- 世界环绕寻路不再属于精简 Smoke 集合；修改有限世界拓扑时应按需运行或补充专项测试。
