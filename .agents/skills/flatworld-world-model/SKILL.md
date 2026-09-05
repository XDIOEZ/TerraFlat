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
- 水深颜色使用带一格邻区边框的 Chunk 深度纹理，并在水格中心之间双线性采样；纹理边角会依赖对角区块，因此 `ChunkTilemapRenderer` 必须订阅八方向邻区，且只在对角邻区的共享角发生变化时刷新。
- Chunk 水深纹理的 MPB 坐标映射输入是世界位置，偏移为 `(1 - Water Tilemap 世界原点) / 纹理尺寸`；不能只加边框偏移，否则 Tilemap 合批后会采到纹理边缘，形成整块色差。
- `ChunkMgr` 随 `WorldManager` 常驻 DDOL；`ChunkView` 及其自然物表现必须挂到当前世界场景的独立根节，禁止以 `ChunkMgr.transform` 作为活动或池化 View 的父级。
- 提交生成结果前校验世界纪元与请求版本；取消、失败和逐出路径必须释放结果及租约。
- 生成保持固定种子和稳定签名；修改地形内容规则时同时使用 `flatworld-map`。
- 新增 `SurfaceBiomeKind` 或群系条件时，必须覆盖 Profile 当前可选的全部分类算法；`LegacyLand` 只复用旧气候采样，不会自动继承其他分支的群系规则。
- 雪地必须同时使用海拔修正后的实际温度与地形修正后的最终降水；海拔只通过降温提高积雪概率，不得单独把高地覆盖为雪。
- Profile 新增地形 TileId 时，同时注册 `ChunkTilePaletteSO` 表现映射；`tile.block.*` 只负责玩法 TileBlock 解析，不能替代 Tilemap 调色板。
- Unity 序列化的私有配置结构体字段不会被 C# 编译器识别为 Inspector 赋值；出现 CS0649 时只在对应字段范围使用局部禁用，不要为消警告改写运行时默认值。
- 可走性联动 `flatworld-navigation`，维度地址联动 `flatworld-dimension`，快照持久化联动 `flatworld-data-save`。

## 验证

- 默认检查静态诊断、Unity 编译和 Console。
- 仅用户明确要求时运行 `WorldModel.Smoke` 或对应生成/持久化分类；测试位于 `Assets/GameTest/WorldModel/`。

## Skill 维护原则

- 只补充可复用的易错点、隐含约束和必要注意事项，不记录近期改动流水账。
