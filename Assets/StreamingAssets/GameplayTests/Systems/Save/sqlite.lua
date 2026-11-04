test.system("save", "sqlite_records", {}, function(data)
    assert(data.nativeTemporaryDirectoryConfigured, "Android 原生 SQLite 临时目录没有指向应用缓存目录")
    assert(data.temporarySpillRoundtrip, "超过临时表缓存的存档载荷未能提交并完整回读")
    assert(data.formalIsolated and data.stageVisible, "未提交暂存泄漏到正式进度，或实时回读看不到暂存")
    assert(data.futureStageIsolated and data.futureStageRetained, "冻结点之后的暂存混入旧保存或被旧提交删除")
    assert(data.canceled and data.failurePreserved, "失败提交改写了已提交进度")
    assert(data.previousRestored, "数据库没有保留上一逻辑提交")
    assert(#data.frozenItemIds == 1 and data.frozenItemIds[1] == 71, "冻结后的源变化污染了旧提交")
    assert(data.futureSchemaRejected, "未知必要记录版本未被拒绝")
    assert(data.chunkFailurePreserved and data.weatherFailurePreserved, "损坏记录在读取失败前发布了部分地形、生态或天气")
    assert(data.modMetadataDeferral.readAccepted and data.modMetadataDeferral.metadataPending and
        data.modMetadataDeferral.deferredRejects and data.modMetadataDeferral.pendingRetained,
        "选档阶段应暂存 MOD 元数据，并在兼容检查失败后保留待恢复状态")
end, "SQLite暂存隔离、冻结修订、失败事务和上一提交")
