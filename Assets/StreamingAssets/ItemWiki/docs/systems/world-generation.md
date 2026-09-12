# 地图生成与 WorldModel

## 系统定位

负责世界区块运行时、确定性生成、后台生成调度、Chunk 权威状态、租约、运行时窗口和 Unity 表现绑定。

## 当前架构

WorldModel 已经是当前正式地图运行链：

`观察者窗口 → ChunkMgr 请求 → 后台纯生成 → 主线程提交 → ChunkRuntime 租约 → ChunkView 分帧绑定 → 解绑/逐出`

## 权威状态

- `ChunkRuntime + ChunkTerrainData` 是区块权威。
- Tilemap、Collider、Renderer 是表现，不允许维护第二份独立玩法状态。
- 后台生成代码位于 `5-0_WorldModel`，必须保持纯 C#，禁止访问 UnityEngine Object。

## 当前生成器

核心实现位于：

- `Generation/DeterministicChunkGenerator.cs`
- `ChunkGenerationScheduler.cs`
- `ChunkGenerationSettingsSnapshot.cs`
- `EcologyGeneration.cs`
- `CaveLayoutKernel.cs`
- `CaveGenerationFeatureGenerator.cs`

正式 Profile：

- 地表：`Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset`
- 洞穴：`Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Cave.asset`
- Tile 表现映射：`ChunkTilePalette_Default.asset`

## 生成原则

- 固定世界种子和生成签名必须得到稳定结果。
- 提交后台生成结果前校验世界纪元与请求版本，过期结果必须丢弃并释放。
- 修改生成规则时必须考虑生成签名和现有世界快照，不允许新旧规则无标识混用。
- 新增地形 TileId 时必须同时注册 Palette 表现映射。
- 群系最终判断同时考虑生成算法定义的气候和地形修正；表现层不能反向决定群系。

## ChunkView

- ChunkView 挂在当前世界场景专用根节点，不挂到常驻 `ChunkMgr` 下。
- View 绑定时从权威数据重建 Tilemap、自然物、农业、积雪/平台等表现。
- View 解绑只清表现，不清权威世界状态。

## 与地图内容的区别

- “这个群系生成什么、概率是多少”属于 [map.md](map.md)。
- “什么时候生成、在哪个线程、如何提交与显示”属于本系统。

## 对应 Skill

`.agents/skills/flatworld-world-model/SKILL.md`
