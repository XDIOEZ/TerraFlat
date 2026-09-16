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
- Ground / Water / Back / Blocking 的基础视觉统一由 `ChunkBatchRendererGroupService` 跨 Chunk 批量绘制；`ChunkTilemapRenderer` 保留历史类名用于 Prefab 兼容，但职责已变为 BRG 提交器。Blocking Tilemap 只维护 `TilemapCollider2D` 所需碰撞格，禁止重新开启其 TilemapRenderer 作为第二份视觉。
- Chunk BRG 自定义 Shader 的全部数值/向量/颜色材质属性必须统一声明在 `UnityPerMaterial` CBUFFER，且同一 Shader 的所有活跃 Pass 保持一致布局；不要 `UsePass` 借用另一个材质布局不同的 Shader Pass，否则 BatchRendererGroup 会因 SRP Batcher 不兼容而拒绝绘制。
- BRG 地形实例按脏格增量上传；岸线与墙脚方向放入实例数据，连续水深使用每水格四个格角深度在 Shader 内双线性插值。边界数据依赖八方向邻区，正交邻区变化刷新共享边、对角邻区变化刷新共享角，不得退回整 Chunk `SetTilesBlock` 或每 Chunk 水深纹理重建。
- `ChunkMgr` 随 `WorldManager` 常驻 DDOL；`ChunkView` 及其自然物表现必须挂到当前世界场景的独立根节，禁止以 `ChunkMgr.transform` 作为活动或池化 View 的父级。
- 提交生成结果前校验世界纪元与请求版本；取消、失败和逐出路径必须释放结果及租约。
- 生成保持固定种子和稳定签名；修改地形内容规则时同时使用 `flatworld-map`。
- 新增 `SurfaceBiomeKind` 或群系条件时，必须覆盖 Profile 当前可选的全部分类算法；`LegacyLand` 只复用旧气候采样，不会自动继承其他分支的群系规则。
- 雪地必须同时使用海拔修正后的实际温度与地形修正后的最终降水；海拔只通过降温提高积雪概率，不得单独把高地覆盖为雪。
- Profile 新增地形 TileId 时，同时注册 `ChunkTilePaletteSO` 表现映射；`tile.block.*` 只负责玩法 TileBlock 解析，不能替代 Tilemap 调色板。
- Unity 序列化的私有配置结构体字段不会被 C# 编译器识别为 Inspector 赋值；出现 CS0649 时只在对应字段范围使用局部禁用，不要为消警告改写运行时默认值。
- 可走性联动 `flatworld-navigation`，维度地址联动 `flatworld-dimension`，快照持久化联动 `flatworld-data-save`。

- 平台的 `TerrainSupportLayer` 属于权威环境扩展，`GetSurfaceCell` 只投影有效地表，不改原始水格；支撑面与积雪的专用 Renderer 解绑仅清理自身 Tilemap，不能撤销权威状态。

## 验证

- 循环坐标统一使用独立 `FlatWorld.WorldTopology` 程序集中的 `WorldTopologyDomain` 值副本；该程序集仅引用 Unity.Mathematics、noEngineReferences=true。`ChunkGenerationTopologySnapshot` 的整数归一化和连续洞穴坐标均委托 Domain，不再自行定义 Wrap；连续曲线保留 double 精度及显式的半周期符号策略，不影响普通坐标 API 默认半周期取负方向的契约。
- Jobs/Burst 只能接收主线程冻结的 Domain，不能调用仍读取 SaveDataMgr 的 `WorldTopologyRuntime`。创建、保存或运算 Domain 不得注册 GameObject 生命周期或创建物理镜像；ChunkView 的可选物理表现只经 Bind/Unbind 与 Terrain.Changed 通知协调。

- 默认检查静态诊断、Unity 编译和 Console。

## Skill 维护原则

- 只补充可复用的易错点、隐含约束和必要注意事项，不记录近期改动流水账。
