# 格子建筑联机回归

执行 `python Tools/Tests/TerrainNetworking/verify.py`，外部编译当前真实源码（含Unity项目文件尚未列出的新脚本），输出只进入 `Temp/TerrainNetworkingValidation`。不启动、暂停或停止Unity。已单独构建生产程序集时，可用 `--skip-build` 只运行35项领域断言；`--assembly-dir` 可指定已构建程序集目录，用于稳定基线的隔离验证。

测试与Unity Lua共用 `GameplayTestNetworkTerrainRules.Capture`，实际调用 `TerrainBuildingCellState` 和 `NetworkTerrainChunkCursor`，不是另外实现复制算法。覆盖支撑与底层液体分离、墙/损伤、重复和过期消息、完整快照与增量乱序、坏批次拒绝、缺片/去重、重新补拉和重新加载区块。

边界：独立CLR没有真实Mirror连接或Unity场景，不能验证Weaver、可视效果、库存请求收发或双端重连。Unity正式入口为Networking/rules-suite.json及Building/gameplay-suite.json；后者覆盖木/石地板、木/石平台和格子木墙的真实后端、发布目标、扣料及失败回滚。
