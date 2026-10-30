-- 正式时钟服务登记自有测试钟，跨日通知不推进活动世界的天气与生态。
test.check("world_ready", "真实隔离世界就绪")
test.check("player_ready", "真实主角就绪")
test.runtime("time", "live_clock", {}, function(data)
    assert(data.scope == "live-isolated-world", "没有在真实隔离世界运行")
    assert(data.referenceResolved, "登记的引用钟没有指向同一个正式时钟对象")
    assert(math.abs(data.afterAdvance - data.beforeAdvance - 3) < 0.01, "正式推进没有结算完整三秒")
    assert(data.afterDay == data.beforeDay + 1 and math.abs(data.sourceTime - 1) < 0.01, "跨午夜没有正确归一化日内时刻")
    assert(data.timeNotifications == 1 and data.dayNotifications == 1, "一次跨日推进没有产生一组真实通知")
    assert(math.abs(data.notifiedBefore - data.beforeAdvance) < 0.01 and math.abs(data.notifiedAfter - data.afterAdvance) < 0.01, "通知携带的绝对时间与正式钟不一致")
    assert(data.referenceTime == data.sourceTime and data.activeSceneTime == data.sourceTime, "活动场景与引用场景没有消费同一个时间源")
    assert(math.abs(data.savedSeconds - data.afterAdvance) < 0.01 and data.savedDay == data.afterDay, "正式时间存档遗漏跨日状态")
    assert(data.lighting >= 0 and data.lighting <= 1 and data.sunLighting >= 0 and data.sunLighting <= 1, "实际场景光照读数无效")
    assert(data.sourceRemoved and data.referenceRemoved and data.activeReferenceRestored, "测试钟或场景引用没有恢复")
    assert(data.restoredSeconds == data.originalSeconds, "临时钟推进改动了活动世界主时钟")
end, "实际时钟服务跨日与场景引用")

test.runtime("time", "fixed_clock_steps", {}, function(data)
    assert(data.steps >= 4 and data.notifications == data.steps,
        "正式昼夜钟没有在每个固定步恰好推进一次")
    assert(data.allInFixedStep and data.consecutiveTicks,
        "昼夜通知发生在画面帧更新，或固定步重复、遗漏")
    assert(math.abs(data.elapsed - data.expectedElapsed) < 0.0001,
        "昼夜钟推进量与固定步及世界时间倍率不一致")
end, "真实昼夜钟随固定步推进一次")
