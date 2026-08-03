---
name: flatworld-navigation
description: "Use when: 定位或修改 FlatWorld 的 A* 寻路、GridGraph、动态导航脏区、TileData 权重、建筑占地、AI 移动、跨 Chunk 导航或联机本地导航窗口。关键词：AstarGameManager、BuildingOccupancyRegistry、Mover_AI、AILerp。"
argument-hint: "寻路、可走性或动态障碍问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 导航系统定位

> 最后核对：2026-08-03。导航权威数据不是 Physics2D 碰撞扫描。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/PathFinding/AstarGameManager.cs`：GridGraph 初始化、移动、脏区、节点写入。
2. `Assets/5_Scripts/5-3_GamePlay/Building/BuildingOccupancyRegistry.cs`：动态建筑占地覆盖层。
3. `Assets/5_Scripts/5-3_GamePlay/Map/Base/Map.cs`：TileData 变化到导航脏区的桥接。
4. `Assets/5_Scripts/5-3_GamePlay/Move/Mover_AI.cs`：AI 运行时移动。

## 权威数据与链路

```text
TileData（地形可走性/权重）
+ BuildingOccupancyRegistry（动态建筑阻挡）
→ Map.MarkPenaltyDirty
→ AstarGameManager.QueueNavigationCell / QueueNavigationRegion
→ AstarWorkItem 写节点并重算连接
```

## 关键调用方

- Chunk 跟随：`Assets/5_Scripts/5-3_GamePlay/Chunk/Mod_ChunkLoader.cs`。
- 联机本地导航窗口：`Assets/5_Scripts/5-4_Networking/Gameplay/NetworkChunkStreamingCoordinator.cs`。
- AI 目标与移动：`Assets/5_Scripts/5-3_GamePlay/AI/`、`Assets/5_Scripts/5-3_GamePlay/Move/Mover_AI.cs`。
- 动态建筑：`Assets/5_Scripts/5-3_GamePlay/Building/Mod_Building.cs`。
- Ghost 导航编辑器检查：`Assets/5_Scripts/5-2_Editor/GhostNavigationTest/`。

## 当前约束

- `AstarGameManager` 维护每轴最大 512 的局部 GridGraph。
- 首次 Scan 后应用 TileData；跨 Chunk 使用 `GridGraph.TranslateInDirection` 更新边缘。
- `Mod_ChunkLoader` 完成网格移动后不得再调用全窗口 `RefreshChunkPenalty`。
- Ghost 等障碍敏感 AI 不得直接 `MoveTowards`；使用 `Seeker + AILerp`。
- 提交目的地或生成点前，使用 `TryGetNodePenalty_GridGraphFast` 验证节点可走。
- 联机时导航图跟随本地 owned 玩家，Chunk 流送仍按所有观察者并集。
- 地下矿洞开放格顶层为 `TileBase_Stone`（`IsWalkable=true`、`Penalty=1000`），岩壁顶层为 `TileBase_StoneWall`（`IsWalkable=false`、`Penalty=0`）；独立“建筑阻挡层”的 TilemapCollider 只负责实体物理碰撞，A* 仍以顶层 TileData 为权威。

## 高耦合联动

只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；调试显示或编辑器可视化变化不要扩散到玩法系统。

| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| GridGraph 尺寸、平移、跨 Chunk 边缘或本地导航窗口 | `flatworld-map`；涉及 owned 玩家窗口时再加载 `flatworld-networking` | 图窗口与 Chunk 窗口一致，远程玩家不移动本地图 | `Map.Smoke`；联机时追加 `Networking.Smoke` |
| TileData Penalty、脏格/脏区 API、连接重算或占地覆盖 | `flatworld-map`、`flatworld-building` | 地图提交静态数据，建筑只叠加动态占地 | `Map.Smoke`、`Building.Smoke` |
| 目的地校验、路径不可达语义、`Seeker`/`AILerp` 或 AI 移动入口 | `flatworld-ai` | AI 不绕过可走性检查或退回直接 `MoveTowards` | `AI.Smoke` |

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-07-31：静态阻挡 Tile 独立渲染到“建筑阻挡层”，但导航仍读取原数据格顶层；禁止改为扫描该 TilemapCollider 决定节点权重。
- 2026-07-31：矿洞生成新增不可走岩壁 TileData；房间和隧道在地图初次完成后统一进入现有全 Chunk 导航更新，不逐格触发烘焙。
- 2026-07-27：导航权威数据切换为 `TileData + BuildingOccupancyRegistry`，移除物理碰撞全场扫描依赖。
- 2026-07-27：运行时地块/建筑变化统一进入脏格/脏区批处理，在 A* WorkItem 中更新节点与连接。
- 2026-07-27：Ghost 导航改为 `Seeker + AILerp`，并在目的地提交前验证节点权重。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Navigation/NavigationSmokeTests.cs`；当前基础覆盖AstarGameManager、动态占地和 AI 移动入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；导航测试约定目录：`Assets/GameTest/Navigation/`；场景目录：`Assets/GameTest/Scenes/Navigation/`；冒烟分类：`Navigation.Smoke`。
- 新增 A*、动态脏区、TileData 权重、建筑占地或 AI 移动行为时必须增加系统测试；修复 Bug 时先增加回归测试。可达路径与动态障碍更新主流程变化时同步更新导航冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；路径测试必须使用确定地图与起终点，并验证不可达路径不会产生伪结果。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Navigation.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类；路径可视化仅在最终调试显示变化时截图。
- 矿洞墙地可走性契约由 `Assets/GameTest/Dimension/DimensionSmokeTests.cs`（`Dimension.Smoke`）补充覆盖。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

改变图尺寸、节点权重规则、脏区 API、建筑占地、AI 移动组件、联机跟随策略或导航测试路径后，必须更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。
