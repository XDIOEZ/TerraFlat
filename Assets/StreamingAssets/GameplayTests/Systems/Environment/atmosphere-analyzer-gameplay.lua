test.runtime("environment", "atmosphere_analyzer_lifecycle", {}, function(data)
    assert(data.canUseWhileHeld and data.nullActorRejected, "正式检测仪的手持使用资格错误")
    assert(data.openedByLocalUse and data.secondUseClosed, "正式使用没有打开仪器屏幕或再次使用没有收起")
    assert(data.sourceStatus == "Measured" and data.sourceWorldKey == data.actualWorldKey,
        "屏幕没有读取手持玩家所在星球的真实大气")
    assert(data.totalMoles > 0 and data.pressureKPa > 0 and data.componentCount > 0,
        "真实星球大气读数缺失")
    assert(data.screenUsesMeasuredSnapshot and data.visibleComponentCount == data.componentCount,
        "仪器显示屏没有绑定真实测量数据或遗漏气体成分")
    assert(data.unchangedInventoryReusesSnapshot, "大气未变仍在反复分配测量快照")
    assert(data.cancelEventClosedPanel and data.cancelEventReopenedSamePanel,
        "正式面板取消事件没有关闭检测仪，或取消后不能重新打开原窗口")
    assert(data.atmosphereExtracted and data.extractionRevisionAdvanced and data.extractionSnapshotReplaced
        and data.extractedTotalMoles < data.totalMoles and data.oldSnapshotUnchangedDuringExtraction,
        "正式抽气后测量快照未更新、总量未减少或旧快照被篡改")
    assert(data.panelSnapshotUpdatedAfterExtraction,
        "正式面板没有刷新到抽气后的最新快照")
    assert(data.atmosphereRestoredByEmission and data.restoredTotalMatchesBefore and data.restoredRevisionAdvanced
        and data.panelSnapshotUpdatedAfterRestore,
        "测试抽取的大气没有完整排放恢复，或恢复后的面板快照未刷新")
    assert(data.reopenedExistingPanel and data.returnedToHandReopens, "仪器重复打开不能复用窗口")
    assert(data.leavingHandClosesPanel and data.cannotReadAfterLeavingHand and data.cannotOpenAfterLeavingHand,
        "离开手持后仪器窗口或旧道具操作仍然有效")
    assert(data.moduleUnloadClosesPanel and data.moduleReloadReopens and data.finalScreenSnapshotBound,
        "测量模块卸载没有关闭屏幕或重新加载后引用失效")
end, "大气检测仪真实使用开关、世界读数、离手拒绝及重载；结束图保留正式仪器面板")
