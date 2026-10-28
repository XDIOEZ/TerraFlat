test.system("world_streaming", "world_scene_binding", {}, function(data)
    assert(not data.unknownResolved and data.firstAdded and data.singleWorld == "probe:world-a",
        "独立世界没有按绑定读取 WorldKey，或未绑定场景返回了世界")
    assert(not data.duplicateChanged, "重复注册同一拥有者改变了绑定")
    assert(data.secondAdded and not data.ambiguousResolved and data.explicitA == "probe:world-a"
        and data.explicitB == "probe:world-b" and not data.wrongExplicitResolved,
        "同场景多世界没有拒绝歧义，或显式世界被回退成其他归属")
    assert(data.replacementChanged and not data.staleOwnerRemoved and not data.staleContextResolved
        and data.replacementContext == "probe:world-a" and data.replacementOwns,
        "旧世界拥有者解除绑定时破坏了新拥有者")
    assert(data.replacementRemoved and data.remainingWorld == "probe:world-b"
        and data.lastRemoved and not data.emptyHasBindings,
        "解绑没有保留其他世界，或最后一个世界释放后仍有绑定")
    assert(data.sameWorldSecondScene and not data.clearSceneA and not data.clearSceneB,
        "跨场景绑定或会话清理未保留正确的绑定关系")
    assert(data.firstIdentity > 0 and data.firstIdentity == data.sameIdentity
        and data.secondIdentity ~= data.firstIdentity and not data.clearedIdentityStillFound
        and data.identityAfterClear > data.secondIdentity,
        "运行时世界身份不稳定、发生冲突，或清理后复用了旧任务的身份")
end, "逻辑世界与场景绑定、歧义拒绝和会话身份")

-- 独立 WorldRuntime 不生成地形，只测运行、表现和寻路租约的所有权。
test.system("world_streaming", "collision_dirty_queue", {}, function(data)
    assert(data.emptyInitially and data.bothPending and data.deduplicated and data.isolated,
        "碰撞空世界仍有工作，或同一块重复入队/首个世界消费了另一世界的脏批次")
    assert(data.requeued and data.nextBatch and data.rebound,
        "碰撞回调中新通知丢失，或解绑重绑把新世界的障碍交给旧世界处理")
    assert(data.destroyedReleased and data.cleared,
        "已销毁碰撞对象或世界重置后仍残留脏队列")
    assert(data.stable and data.iterations == 1024 and data.allocatedBytes == 0,
        "预热后的空队列查询/脏批次循环仍产生GC或漏掉实际待处理项")
end, "碰撞脏队列空转零分配、多世界隔离与回调重入")

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

test.system("world_streaming", "observer_windows", {}, function(data)
    assert(data.plan[1].x == 0 and data.plan[1].y == 0 and data.plan[2].x == 8 and data.plan[2].y == 0,
        "联机窗口计划没有按实际矩形区块尺寸定位每个观察者")
    assert(data.plan[1].activeX == 2 and data.plan[1].activeY == 1
        and data.plan[1].presentationX == 3 and data.plan[1].presentationY == 2,
        "联机窗口计划混淆了横纵距离或活动圈与预加载表现圈")
    assert(data.merged.chunks == 45 and data.merged.active == 5 and data.merged.simulationLeases == 5
        and data.merged.presentation == 21 and not data.merged.pending and data.sharedLeaseCount == 1,
        "重叠或重复观察者没有合并活动/表现/保留窗口，或重叠区块被重复领取模拟租约")
    assert(data.repeatDemandChanges == 0 and data.repeated.active == 5
        and data.repeated.simulationLeases == 5 and data.repeated.presentation == 21,
        "重复提交同一联机窗口造成租约累加或反复发布表现需求")
    assert(data.removed.chunks == 36 and data.removed.active == 3 and data.removed.simulationLeases == 3
        and data.removed.presentation == 15 and data.sharedAfterRemoval == 1,
        "移除一个观察者时误删剩余观察者窗口，或旧模拟租约没有归还")
    assert(data.retainedDormant == "Dormant" and data.preloadSimulationCount == 0 and data.navigationProtected,
        "保留圈/表现预加载圈意外继续模拟，或外部导航租约持有的区块被逐出")
    assert(data.empty.chunks == 1 and data.empty.active == 0 and data.empty.presentation == 0
        and data.released.chunks == 0 and data.unexpectedGenerationCount == 0,
        "所有观察者退出后仍有窗口租约/表现需求，或独立规则启动了不必要的地形生成")
    assert(data.negative[1].x == -4 and data.negative[1].y == -2
        and data.negative[2].x == 4 and data.negative[2].y == 2,
        "负坐标或非正方形区块原点没有按地板取整")
    assert(data.invalidFiltered == 1, "非法网络坐标没有被过滤，或误删了合法观察者")
    local wrapped = data.wrapped
    assert(wrapped.plan[1].x == 0 and wrapped.plan[1].y == 0
        and wrapped.merged.chunks == 12 and wrapped.merged.active == 3
        and wrapped.merged.simulationLeases == 3 and wrapped.merged.presentation == 12
        and wrapped.seamLeaseCount == 1,
        "跨世界接缝的观察者窗口没有归一化合并，或多周别名重复模拟同一区块")
    assert(wrapped.single.chunks == 12 and wrapped.single.active == 3
        and wrapped.single.presentation == 12 and wrapped.unexpectedGenerationCount == 0,
        "移除环绕同址观察者破坏剩余窗口")
end, "正式联机窗口计划、多观察者并集、离开释放与环绕去重")

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

test.system("world_streaming", "map_generation_plan", {}, function(data)
    assert(data.ordered and data.originalUnchanged and data.separatePlans and data.successReasonEmpty,
        "地图生成阶段排序、同阶段声明顺序或独立计划被改变")
    assert(data.rejectEmpty and data.rejectNullStage and data.rejectMissingTerrain,
        "空管线、空生成器或缺失地形阶段没有被正式计划拒绝")
    assert(data.rejectDuplicateTerrain and data.rejectUndefinedStage,
        "重复地形或非法阶段被接受，导致生成生命周期含糊")
end, "地图生成计划保持稳定阶段顺序并明确拒绝非法声明")
