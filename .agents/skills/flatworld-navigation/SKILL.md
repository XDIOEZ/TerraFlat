---
name: flatworld-navigation
description: "Use when: 定位或修改 FlatWorld 的稀疏网格寻路、动态导航脏区、TileData 权重、建筑占地、AI 移动、跨 Chunk 导航或联机本地导航窗口。关键词：WorldNavigationManager、WorldNavigationGrid、WorldNavigationAgent、BuildingOccupancyRegistry、Mover_AI。"
---

# FlatWorld 导航

## 入口

- 网格/请求：`Assets/5_Scripts/5-3_GamePlay/World/PathFinding/WorldNavigationManager.cs`
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
- 运行时只用项目内置导航，不恢复 Aron Granberg A*，也不把 Physics2D 扫描当权威。
- 移除覆盖层后恢复基础层权重；建筑不改 TileData。
- 失败/未表现完成的 Chunk 不注册导航；View 入池或销毁前先 Unbind。
- 本地导航窗口只跟随 owned 玩家；远程副本不移动它。
- Wrapped 世界只规范化窗口；一期不在两侧建立图邻接边，AI 不跨缝寻路。

## 验证

- 使用确定地图和起终点，覆盖可达、不可达、动态阻挡、跨 Chunk 边缘与请求取消。
- 坐标/窗口变化联动 `flatworld-map`，owned 玩家联动 Networking，占地联动 Building，AI 决策联动 AI Skill。
- 默认不主动跑测试；需要时运行 `Navigation.Smoke`。测试入口：`Assets/GameTest/Navigation/NavigationSmokeTests.cs`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
