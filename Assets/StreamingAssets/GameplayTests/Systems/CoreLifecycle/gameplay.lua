-- 观察正式入世已完成的本地玩家、资源闸门和可见区块，不模拟 Scene 流程。
test.runtime("core_lifecycle", "live_entry", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.resourceState == "Ready" and data.startupReady, "正式资源没有完成加载")
    assert(data.inWorld and data.gameplayReady and not data.entryInProgress, "正式入世闸门尚未释放")
    assert(data.localProfile and data.playerInitialized and data.playerActive and data.profile ~= nil, "本地测试角色未完成生命周期")
    assert(data.footViewReady and data.windowReady, "输入已解锁但脚下或可见区块表现未就绪")
end, "正式入世、玩家生命周期和表现就绪")
test.screenshot("正式入世后地表与 HUD")
