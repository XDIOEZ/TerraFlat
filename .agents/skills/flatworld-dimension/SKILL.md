---
name: flatworld-dimension
description: "Use when: 定位或修改 FlatWorld 的维度、星球表面/地下矿洞切换、世界地址、动态世界 Scene、维度独立地图与种子、维度入口、维度环境覆盖或未来星球旅行。关键词：DimensionManager、WorldAddress、DimensionPortal、DimensionCatalogSO、CaveLayoutKernel。"
---

# FlatWorld 维度

## 入口

- 地址/管理：`Assets/5_Scripts/5-3_GamePlay/World/Dimension/{WorldAddress,DimensionManager}.cs`
- 定义/入口/进度：同目录 `{DimensionCatalogSO,DimensionPortal,DimensionTravelProgressStore}.cs`
- 正式洞穴生成：定位 `DeterministicChunkGenerator`、`CaveLayoutKernel`、`CaveGenerationFeatureGenerator`

## 不变量

- 地表 `WorldKey=PlanetId`；非地表为 `PlanetId__dimension__DimensionId`。只用 `WorldAddress` API，业务代码不拼字符串。
- 每个 WorldKey 独立 `PlanetData/MapData/Chunk` 差量；新维度从地表继承 TopologyMode、Radius、ChunkSize，清空维度运行态。
- 切换链必须保存当前状态、释放玩家/世界、创建动态 Scene、运行目标世界、定位锚点、等待完整活动窗口和世界收尾，再解锁输入；失败路径恢复状态。玩家 `Mover` 的序列化奔跑开关也必须在输入解锁后恢复。
- 维度切换的 `Event_PlayerEnterWorld` 只表示玩家实例已创建，不能作为加载页完成信号；专属呈现必须等 `AreRuntimeWindowPresentationsReady`、出口/地块效果及物理同步后由 `DimensionManager` 显式完成。诊断阈值只能告警，不能放行未完成窗口。
- 玩家位置与入口锚点保存在 `ItemSpecialData` 的 `flatworld.dimensions`，不改 MemoryPack 布局。
- 环绕世界由额外相机绘制平移副本，但 `Light2D` 等局部空间组件不会随画面自动平移；这类表现必须为当前可见世界镜像同步轻量代理，并将纯表现代理排除出光照、AI、存档等玩法查询。
- 洞穴只走 WorldModel 的 Profile、`DeterministicChunkGenerator`、`CaveLayoutKernel` 与 `CaveGenerationFeatureGenerator`；旧 Map 不再维护独立洞穴生成器或采样器。
- 洞穴规则需要参考地表时，只能使用 `CavePortalPairingSnapshot` 冻结的地表 Profile、派生种子与拓扑做纯采样；禁止查询已生成或已加载的地表 Chunk、Tilemap，配对指纹必须参与洞穴生成指纹。地下湖概率使用同一快照复算的最终降水，不能读取地表环境层。
- 洞穴布局若要跟随地表尺度，必须从配对地表复制 `world.coordinateScale` 并统一换算距离参数；群系交界默认只复算基础气候群系，不引入依赖区域构建结果的水文覆盖。
- 地表入口必须是已安装 MineEntrance；CaveExit 在基线完成后创建以进入差量。
- 矿洞默认抑制天气/怪物生成，`FixedLighting` 是上限。
- 当前只支持离线切换；服务器权威迁移协议完成前不得开放联机。

## 验证

- 覆盖地表键兼容、往返、位置/锚点恢复、入口唯一性、Chunk 差量、环境覆盖与失败清理。
- 地址/生命周期联动 Core+Data；生成联动 Map；环境联动 Environment；联机限制联动 Networking。
- 默认不主动跑测试；需要时运行 `Dimension.Smoke`，地块效果追加 `Dimension.TileEffects`。测试目录：`Assets/GameTest/Dimension/`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
