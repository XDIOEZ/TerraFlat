# 自然生态纯规则回归

- 在独立 4×4 `ChunkTerrainBuffer` 上调用 `ChunkEcologyGenerator.Generate`，检查稳定非零身份、位置唯一、气候边界、群系掩码、地块阻挡和零倍率。
- 规则夹具不物化 Item，不写生态差量。
- PlayMode 与地形共用 [Terrain/gameplay-suite.json](../Terrain/gameplay-suite.json)，不建立重复案例：在真实已加载空地上经 `NaturalEntityEcsService.TryRegister` 装配自有 `Tree_Pine` 资源实体，绑定正式区块表现，并采集真实身份/存档快照与导航占地。
- Lua 检查登记实体时该格从可走变阻挡、`TryTakeSnapshotAndRemove` 返回原身份且释放占地、同一快照重新登记后身份保持。地表覆盖栈与正式存档差量在同一条链检查，恢复实体的现场显式截图。
- 只生成自有树木，经模拟租约和 Lua 级收尾清理，失败/中断同样恢复原地格，不删自然生成的既有资源。
- 缺口：完整自然生成装配、伴生关系、随机数量/斑块分布、年度更新、自然删除标记、砍伐掉落、冷区块整场保存恢复。当前现场检查资源 ECS 的运行态与快照往返，未覆盖生成器自动分布和保存文件重进。
- 新增现场按用户要求本轮未编译、未运行，验收由用户进行。
