---
name: flatworld-dimension
description: "FlatWorld 世界地址、地表/洞穴切换、入口配对、维度生成与环境覆盖。"
---

# 维度

## 入口

- `Assets/5_Scripts/5-3_GamePlay/World/Dimension/`：`WorldAddress`、`DimensionManager`、`DimensionCatalogSO`、`Mod_DimensionPortal`、`DimensionTravelProgressStore`。
- `Assets/5_Scripts/5-0_WorldModel/Generation/`：`DeterministicChunkGenerator`、`CaveLayoutKernel`、`CaveGenerationFeatureGenerator`。
- `Assets/StreamingAssets/GameConfig/Items/shells/dimension_portals.json`：运行时入口定义。

## 地址与切换

- `WorldAddress` 生成键：地表为 `PlanetId`，其他为 `PlanetId__dimension__DimensionId`；各键独享 Planet/Map/Chunk 差量。新维度继承地表拓扑、半径、ChunkSize，运行态独立。
- 切换顺序：保存 → 退出 Ground/Liquid 接触 → 释放玩家/世界 → 动态 Scene → 目标世界/锚点 → 完整活动窗口、出口/地块效果、物理同步 → 刷新接触 → 解锁输入/恢复奔跑。失败恢复原状态。
- `Event_PlayerEnterWorld` 只表示玩家已创建；完成信号由 `DimensionManager` 在 `AreRuntimeWindowPresentationsReady` 及收尾后发布。位置/锚点存入 `ItemSpecialData.flatworld.dimensions`。
- 当前切换仅支持离线。矿洞默认抑制天气/怪物，`FixedLighting` 是光照上限。环绕补绘相机的局部 Light2D 使用轻量镜像代理，代理不进入玩法查询。

## 洞穴与入口

- 洞穴走 WorldModel Profile；`CavePortalPairingSnapshot` 冻结地表 Profile、派生种子、拓扑和空间倍率，纯采样决定配对，指纹进入洞穴签名，不读取已加载地表。
- 地下湖由同一快照复算地表高度带与最低降水门槛；群系交界用基础气候，洞室/隧道/河流/安全区跟随 `world.spatialDistanceScale`。
- 地表入口是已安装 `MineEntrance`；`CaveExit` 在基线完成后创建并进入差量，复用 `BuildingBodyShell` 和入口模块。独立 CaveExit Prefab 仅为编辑器迁移源；导航阻挡由 `WorldNavigationObstacle` 参数显式登记。
- 入口安全区分别改 Ground/Blocking，并经 `WorldLiquidSystem.TrySet(...,0)` 清 Liquid；准备期间不推进旧地块效果。
