-- 只读正式 ChunkMgr 的窗口和脚下区块，不刷新窗口或额外申请租约。
test.runtime("world_streaming", "live_window", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.runtimeActive and data.world ~= nil and data.epoch > 0, "正式纯世界运行时未建立")
    assert(data.windowReady and data.retainedChunks > 0, "可见窗口没有完成表现绑定")
    assert(data.footFound and data.footData == "Ready" and data.footPresentation == "Bound" and data.footView, "脚下权威数据或真实表现缺失")
    assert(data.footSimulationLeases > 0 and data.footPresentationLeases > 0, "当前窗口区块没有正式使用租约")
end, "真实流送窗口、脚下数据和表现租约")
