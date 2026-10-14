-- 生物实际出生、推进命令、异步寻路、沿路移动与收尾属于同一条现场链。
test.check("world_ready", "AI 与导航共用真实隔离世界")
test.check("terrain_ready", "自有生物所在区块已加载")
test.runtime("ai", "advance_navigation", {}, function(data)
    assert(data.scope == "live-isolated-world", "没有采样真实隔离世界")
    assert(data.actorGuid ~= 0 and data.actorInitialized and data.aiLoaded and data.moverLoaded,
        "正式生成没有完成生物、AI 或移动模块加载")
    assert(data.limitCallbackReceived and not data.limitAccepted and data.limitRejectedByCost,
        "实际导航请求没有按路径代价拒绝")
    assert(data.playerThreatExcluded, "自有测试生物仍会优先避让近处玩家，无法独立检查推进")
    assert(data.commandAccepted and data.commandStored, "正式 AI 推进命令未写入真实生物")
    assert(data.pathAccepted and data.gridRevision >= 0, "真实导航代理没有获得可执行路径")
    assert(data.maximumDisplacement > 1 and data.maximumSpeed > .01,
        "生物仅接受命令却没有经过正式 Tick 实际移动")
    assert(data.stayedWalkable, "移动中的生物进入真实不可走格")
    assert(data.reached and data.distanceToGoal <= .65 and data.commandClearedAtArrival,
        "真实生物没有到达目标或到达后仍保留推进命令")
    assert(data.cancelAccepted and not data.hasDestinationAfterCancel and
        not data.hasPathAfterCancel and not data.pendingAfterCancel, "取消命令后旧路径仍然运行")
    assert(data.cleanupRegistered, "自有生物没有登记失败时的收尾")
end, "正式生物推进、导航代价拒绝、移动到达和取消")
test.screenshot("自有生物实际沿导航路径到达后的现场")
test.runtime("ai", "cleanup", {}, function(data)
    assert(data.hadFixture and data.released and data.actorRemoved, "自有测试生物未清理")
end, "删除自有生物并撤销正式导航代理")
