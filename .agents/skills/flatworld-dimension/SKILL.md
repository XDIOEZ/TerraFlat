---
name: flatworld-dimension
description: "FlatWorld 世界地址、地表/洞穴切换、入口配对、维度生成与环境覆盖。"
---

# 维度

## 入口

- `Assets/5_Scripts/5-3_GamePlay/World/Dimension/`：`WorldAddress`、`DimensionManager`、`DimensionCatalogSO`、`Mod_DimensionPortal`、`DimensionTravelProgressStore`。
- `Assets/5_Scripts/5-0_WorldModel/Generation/`：`DeterministicChunkGenerator`、`CaveLayoutKernel`、`CaveGenerationFeatureGenerator`。
- `Assets/StreamingAssets/GameConfig/Items/shells/dimension_portals.json`：运行时入口定义。
- 加载页纹理、透明度和平铺宽度由 `DimensionLoadingTheme` 配置，`GameManager.UI` 统一应用；默认地表复用 `Grass_Ground`，以 128 参考像素平铺并完整显示纹理。

## 地址与切换

- `WorldAddress` 生成键：根世界为 `RootWorldKey`，其他为 `RootWorldKey__dimension__DimensionId`；根键不要求星球或 Scene，星球归属沿用数据的 `BodyId`。`GameSaveData.GetOrCreateWorldInstanceData` 只继承生成几何与环境配置，Map/生态/积雪/天气进度独立；重复进入保留原实例和状态。
- 世界实例用 `PlanetData.GenerationPresetId` 选择共享预设，`GenerationNumericOverrides/GenerationTextOverrides` 在冻结恢复后覆盖；`ChunkMgr.CreateFrozenWorldGenerationProfile` 统一冷采样、出生、流送与入口复核。创建方从目录明确传入默认预设，通用数据入口不按维度名称猜矿洞。`surface.default/cave.default` 直接复用 SO，`core:surface.*` 复用既有共享地表参数，地形只由最终 `terrain.mode` 决定。选择地球地形不改 `BodyId`、WorldKey、种子或存档身份。
- 地理温度来自生成预设，运行时额外温差来自实例 `GlobalTemperature`；大气取独立 `AtmosphereProfileId/Atmosphere`，昼夜取所属星体与根世界时间，光照/天气策略取维度定义。`PortalItemId/PortalTargetDimensionId/PairPortalsWithTargetWorld` 独立管理入口，地球式地下仍生成配对出口；`FadeLightingFromPortals/SuppressTerrainWallShadows` 不再读生成模式。地表与矿洞共用原世界/区块流程，离线太空落地只接精确坐标。
- 单人和联机复用 `DimensionTransitionService` 的权威解析/准备/提交；网络桥只负责远端请求、身份与回执。玩家可独立进出维度，`GameplayWorldRegistry` 按 WorldKey 保留 Scene、ChunkMgr、独立物理和运行态，同步 `UseWorld` 租约不能跨 yield。
- 个人最后位置保存在 `ItemSpecialData.flatworld.dimensions`；手工锚点、入口状态和天然出口索引属于 `GameSaveData.DimensionStateJson`，多人共享，不扫描其他玩家存档建立配对。权威解析只读预览，准备成功才提交锚点。
- `ApplyNetworkPresentation` 保留同一个 Player/NetworkIdentity，只锁本机输入；目标完整窗口、出口/地块效果、物理同步就绪后发送 presentation-ready，服务器再放行新 realm 的移动/库存。不能全局 WorldExit、ReleasePlayer、暂停全服或创建空 Scene 冒充就绪；失败不发送 ready，允许后续快照重试呈现。
- 同世界重生和传送的 RealmRevision 变化也走该事务，不允许 Scene 相同或旧窗口 Ready 就快速确认；落点用 `PlayerTeleportService.ApplySurfacePose`，等待用 `WaitForSurfaceArrival`。空窗口、旧中心或准备阶段的小窗口均不能放行，权威重生在提交新世界坐标前保存模块，避免旧 Transform 回写落点。
- 入口请求发出前由 `DimensionManager.LocalPresentation` 取得同一份加载页/owner 输入锁/运动冻结租约；目标呈现复用原始物理与奔跑状态，加载幕完全退场才发 ready 并释放。未提交的拒绝立即收尾，在线已提交而呈现失败保留冻结供重试；断线、退出、管理器停用及 F5 换主角清租约。往返 Lua 同时采样请求当帧和迁移全程门禁。
- `TryBeginPlanetLanding` 在线仅允许权威本机的内部精确恢复，走 `ExecuteExact` 的世界准备、源 realm 复核和正式归属提交；客户端不能指定任意恢复目标，不能回落旧全局卸载事务。离线太空迁移包装仍走原入口。
- 离线切换的 WorldExit 会释放世界注册；目标进入和失败回程在重建主角前调用 `GameplayWorldRegistry.SelectLocalWorld` 绑定已加载地表，避免降落准备因目标未注册而停住。回归 `ship_landing_gameplay`。
- 离线地表间精确迁移在重建角色后复用 `CompleteNetworkWorldTransfer` 同步乘员权威世界；提交后失败时回程同步原世界，避免后续入口仍按旧世界校验。
- 单机地表↔太空迁移先在原世界上下文清理并解绑区块，保留本机 ChunkMgr 和可复用空壳；其他世界、普通退出及联机仍完整释放。回归 `presentation_cache`。
- 离线迁移的世界退出已由注册表保存并解绑，写盘后不再重复解绑；目标进入通知开始后若失败，回程先清理部分进入的世界。
- 矿洞默认抑制天气/怪物，`FixedLighting` 是光照上限；开放洞口的自然采光按距离线性衰减，20米归零，强度与局部灯光独立。环绕补绘相机的局部 Light2D 使用轻量镜像代理，代理不进入玩法查询。
- 洞穴关闭静态岩壁的 URP 墙影遮罩，避免玩家被阴影盖黑；地表墙影、洞穴实际光照与阻挡碰撞照常保留。

## 洞穴与入口

- 洞穴走 WorldModel Profile；`CavePortalPairingSnapshot` 冻结地表 Profile、派生种子、拓扑和空间倍率，纯采样决定配对，指纹进入洞穴签名，不读取已加载地表。
- 地下湖由同一快照复算地表高度带与最低降水门槛；群系交界用基础气候，洞室/隧道/河流/安全区跟随 `world.spatialDistanceScale`。
- 地表入口是已安装 `MineEntrance`；`CaveExit` 在基线完成后创建并进入差量，复用 `BuildingBodyShell` 和入口模块，不保留独立旧 Prefab；导航阻挡由 `WorldNavigationObstacle` 参数显式登记。
- 入口安全区分别改 Ground/Blocking，并经 `WorldLiquidSystem.TrySet(...,0)` 清 Liquid；准备期间不推进旧地块效果。
