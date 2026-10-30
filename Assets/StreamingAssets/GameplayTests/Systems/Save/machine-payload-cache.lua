test.system("save", "machine_snapshot_cache", {}, function(data)
    assert(data.unchangedReused, "未变机械节点仍重新冻结字节")
    assert(data.progressRefrozen and data.contentsRefrozen and data.layoutRefrozen,
        "进度、嵌套库存或专用库存布局变化复用了旧载荷")
    assert(data.originalProgress == 0 and data.originalAmount == 3,
        "后续机械变化污染了已经冻结的历史快照")
    assert(math.abs(data.finalProgress - 0.4) < 0.00001 and data.finalAmount == 7,
        "最终机械快照没有包含真实进度和物品数量")
    assert(data.manualSeconds == 4 and data.electricalJoules == 42,
        "未发布通知的手摇时间或储能变化没有进入存档")
    assert(data.slotCapacity == 12 and data.slotTag == "probe:accepted" and
        data.inputAction == "probe:toggle" and data.panelPrefab == "probe:panel",
        "机器完整库存持久化字段没有随布局变化刷新")
    assert(data.scopeRefrozen and data.unknownRefrozen and data.publicPolicyIsolated,
        "跨作用域、未声明变化跟踪的逻辑或公开捕获串用了旧缓存")
    assert(data.batchReused and data.batchRevisionApplied and data.batchCaptures == 2 and data.batchEnded,
        "同轮船机采集没有复用，或本轮变化与下一轮采集没有正确失效")
    assert(data.archiveVersion == 2 and data.archiveWorlds == 2 and data.archiveAmount == 7,
        "独立机械字节档案丢失版本、世界或嵌套库存")
    assert(data.archiveRemoved and data.archiveEventId == 77 and data.archiveLiquid == 2 and data.oldVersionRejected,
        "机械冻结封装丢失生成删除标记、压力事件预算或接受了旧版本")
end, "机械载荷缓存、最终失效、同轮船体复用与独立档案往返")
