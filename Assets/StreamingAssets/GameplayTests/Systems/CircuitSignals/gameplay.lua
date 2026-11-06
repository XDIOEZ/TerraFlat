test.runtime("circuit_signals", "wireless_automation", {}, function(data)
    for _, name in ipairs({"initial", "restoredPower", "snapshotRebuilt"}) do
        local state = data[name]
        assert(state.validSource and state.receiver and state.gate and state.relay,
            name .. "无线信号、逻辑门或继电器没有真实接通")
        assert(math.abs(state.watts - 60) < 0.01, name .. "远端电灯实际供电错误")
        assert(state.channel == 1 and not state.transmitMode, "恢复后频道或模式丢失")
    end
    assert(not data.off.relay and data.off.watts == 0 and not data.off.validSource,
        "发射侧失电后，远端触点、电灯或来源有效性未撤销")
    assert(data.receiverInitiallyZero, "恢复时重用了旧接收高电平")
end, "真实放置无线转换、OR门、继电器、电灯与快照重建")
