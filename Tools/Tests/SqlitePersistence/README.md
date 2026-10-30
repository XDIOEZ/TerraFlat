# SQLite 存档隔离回归

在 Windows x64 上运行 `python Tools/Tests/SqlitePersistence/run.py`。脚本优先使用 Unity 已解析的 1.3.3 包，否则把固定版本下载到 `Library/FlatWorldGameplayTests/Dependencies/`，之后可离线重复运行。测试直接编译正式存储代码并使用该包的原生 SQLite，测试库仅存在 `Library/FlatWorldGameplayTests/SqlitePersistence/run-<随机 ID>/`，结束时删除。

覆盖坐标索引与 1,000 / 10,000 / 100,000 条区块查询耗时和托管分配、无变化时只更新时间不改记录、暂存隔离、冻结点后阶段、事务注入失败和取消回滚、过期保存拒绝、前代恢复、跨世界查询、跨连接会话冲突、在线备份与改名复制。世界退役覆盖正式和暂存记录，冻结点之后的暂存及其他世界继续保留，上代仍可恢复。Unity 的导入、Android 原生插件和真实游戏流程仍需单独验证。
