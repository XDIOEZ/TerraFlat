-- 独立 WorldRuntime 不生成地形，只测运行、表现和寻路租约的所有权。
for _, row in ipairs({ { x = -64, y = 64 }, { x = 0, y = 0 } }) do
    test.system("world_streaming", "chunk_leases", row, function(data)
        assert(data.simulationCount == 2 and data.active == "Active", "模拟租约没有激活区块")
        assert(data.bound == "Bound" and data.navigationCount == 1, "表现或寻路租约状态错误")
        assert(data.dataStatus == "Absent", "领取租约意外生成了地形")
        assert(not data.evictWhileLeased, "仍在使用的区块被逐出")
        assert(data.afterFirst == 1 and data.stillActive == "Active", "重复归还租约或部分归还破坏计数")
        assert(data.dormant == "Dormant" and data.unbound == "Unbound", "最后一张租约归还后仍模拟或显示")
        assert(not data.evictWhileNavigation, "寻路持有的区块被逐出")
        assert(data.evictReleased and data.remainingChunks == 0, "完全释放后没有逐出区块")
    end, "区块独立租约与逐出：" .. row.x .. "," .. row.y)
end

test.system("world_streaming", "generation_lifecycle", {}, function(data)
    assert(data.newVersionAdvanced, "重新请求没有增加生成任务版本")
    assert(data.afterOldFailure == "Generating" and data.oldFailureReason == nil, "旧请求失败覆盖正在运行的新请求")
    assert(data.afterCurrentFailure == "Failed" and data.currentFailureReason == "当前请求", "当前请求失败未保存原因")
    assert(data.retryReasonCleared, "重试保留了上次失败原因")
    assert(data.afterCancelledFailure == "Absent" and data.cancelledReason == nil, "取消后的旧失败仍改变区块")
    assert(data.afterOldEpochFailure == "Generating" and data.oldEpochReason == nil, "旧世界失败污染新世界")
end, "生成请求版本、取消与世界 Epoch 失效")

test.system("world_streaming", "commit_snapshot", { tile = 23, level = 3, changedLevel = 8 }, function(data)
    assert(not data.staleAccepted and data.staleDisposed, "旧生成结果被发布或未释放缓冲")
    assert(data.committed and data.reason == nil and data.consumed and data.resultDisposed, "当前生成结果没有移交唯一所有权")
    assert(not data.repeatedAccepted, "同一生成结果被重复提交")
    assert(data.evicted and data.terrainDisposed, "逐出没有释放正式地形")
    assert(data.snapshotChunks == 1 and data.snapshotGround == 23, "正式快照受后续地形修改或逐出影响")
    assert(data.changedLevel == 8, "快照后的正式整数液体层没有真正发生修改")
    assert(data.snapshotLiquid == "probe:water" and data.snapshotLevel == 3, "快照没有保存独立的稳定液体身份与整数等级")
    assert(not data.oldEpochAccepted and data.epochDisposed, "旧世界结果被发布或丢弃后未释放")
end, "生成结果提交、快照冻结和唯一所有权释放")

test.system("world_streaming", "base_presentation_steps", {}, function(data)
    assert(data.firstYield and data.secondYield and data.initRegistered, "基础地形没有按初始化和扫描步骤推进")
    assert(not data.initReady and not data.partialReady and data.earlyEvents == 0,
        "基础地形尚未扫描/上传完成却提前发布就绪或重建事件")
    assert(data.firstSliceWrites == 64 and data.maxStepWrites <= 64 and data.fullWrites >= 320,
        "基础地形首步预算、每步上限或失效后的真实重扫错误")
    assert(data.uploadInvalidated and not data.uploadEarlyReady,
        "分层上传期间的视觉失效、Owner丢失或改格没有重新进入有界重扫")
    assert(data.neighbourEvicted and data.invalidated and not data.repairWhileBinding,
        "绑定期间邻区/资源失效被同步恢复绕过预算")
    assert(data.steps > 3 and data.completed and data.completedEvents == 1 and not data.completedInvalidation,
        "完整提交没有只发布一次就绪，或初绑失效请求污染已完成状态")
    assert(data.unbound and data.cancelledReleased and data.cancelledEvents == 1,
        "解绑或取消没有释放Owner，或取消仍然发布了完成事件")
    assert(data.replacementSurvived and data.replacementCompleted and data.finalEvents == 2,
        "旧枚举器清理破坏了新绑定，或重绑未正常完成")
    assert(data.liquidOnlyRefresh and data.queuedLiquidCells > 0 and data.terrainRefreshPreserved,
        "液体批量更新重新提交了地面/背景/阻挡层，或普通地形变化被错误跳过")
    assert(data.waterSkippedLavaScan and data.lavaRemovalPreserved,
        "无岩浆的水流错误触发全区块发光扫描，或新增/排空岩浆没有请求更新")
end, "基础地形分步预算、脏格重扫和取消所有权")
