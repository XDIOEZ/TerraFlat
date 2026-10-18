# 自然生态纯规则回归

2026-10-18 新增但未执行：`body_read_batch` 在自有 Entities World 检查发布值批读、停用实体、批内生命/姿态写入、显式失效、新增/移除及跨批清理；`spatial_geometry` 检查碰撞冷编译的偏移/缩放、树干覆盖与重新编译；`soil_snapshot_reuse` 检查复用容器逐次读取权威水肥与耗水尾数。尚未覆盖真实九宫格泵水回调、完整发布通知顺序及改后平均帧耗时。

2026-10-15：本系统登记的快速规则和 PlayMode 入口均已执行通过，见 [缓存与全系统 DEBUG](../../缓存与38系统DEBUG循环_2026-10-15.md)。以下功能缺口继续有效，玩法验收由用户进行。

- 在独立 4×4 `ChunkTerrainBuffer` 上调用 `ChunkEcologyGenerator.Generate`，检查稳定非零身份、位置唯一、气候边界、群系掩码、地块阻挡和零倍率。
- 规则夹具不物化 Item，不写生态差量。
- 邻区标签缓存规则经正式 `DeterministicChunkGenerator.HasHostTagInChunk` 和地形扩展计数，检查正负结果复用、标签大小写/任务版本复用，以及世界种子、纪元、区块地址、Profile 和环绕拓扑隔离。生成中取消必须不写缓存，重试后可复用；世界退出后旧纪元拒绝查询，新纪元重新生成。
- 独立 `WorldRuntime` 正式执行 `Generate→TryCommit`，检查现成地形能预填蜂巢所需花朵标签，冷暖生成的完整生态身份、位置、偏移、宿主和规则不变。2026-10-17 新增规则的相关 C# 已通过外部编译，按用户要求未执行测试。
- PlayMode 与地形共用 [Terrain/gameplay-suite.json](../Terrain/gameplay-suite.json)，不建立重复案例：在真实已加载空地上经 `NaturalEntityEcsService.TryRegister` 装配自有 `Tree_Pine` 资源实体，绑定正式区块表现，并采集真实身份/存档快照与导航占地。
- Lua 检查登记实体时该格从可走变阻挡、`TryTakeSnapshotAndRemove` 返回原身份且释放占地、同一快照重新登记后身份保持。地表覆盖栈与正式存档差量在同一条链检查，宿主自动定时截图。
- 只生成自有树木，经模拟租约和 Lua 级收尾清理，失败/中断同样恢复原地格，不删自然生成的既有资源。
- 缺口：完整自然生成装配、伴生关系、随机数量/斑块分布、年度更新、自然删除标记、砍伐掉落、冷区块整场保存恢复。当前现场检查资源 ECS 的运行态与快照往返，未覆盖生成器自动分布和保存文件重进。
- 新增现场已在本轮执行，验收由用户进行。
2026-10-18 新增但未执行：`ecology/capture_batch` 直接调用正式 `EcologyWorldSaveData.CaptureChangedItems`，Lua 检查初始顺序、已有重复 GUID 只覆盖首条、同批重复 GUID 最后输入生效、空/零 GUID 拒绝、删除标记逐次清除、其他区块不变及后续批次重建索引。区块解绑仍逐个执行原本 Item 保存/深拷贝或 ECS 快照移交，仅最后写差量时一次查区块和建立临时索引；不代表自然物模块捕获或卸载总体耗时已经下降到目标。
