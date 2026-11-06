test.runtime("circuit_signals", "oscillator_automation", {}, function(data)
    assert(data.initiallyLow and data.onSignal and math.abs(data.onWatts - 60) < 0.01,
        "振荡器没有从低电平起振并通过继电器点亮电灯")
    assert(not data.offSignal and data.offWatts == 0, "低半周期没有断开实际电力")
    assert(data.configured and data.stopped and data.stayedLow, "改频或停机后仍产生脉冲")
    assert(math.abs(data.restoredFrequency - 2) < 0.0001 and not data.restoredEnabled and data.restoredLow,
        "快照重建没有保留配置或错误保留了高电平")
    assert(data.restarted and math.abs(data.restoredWatts - 60) < 0.01, "恢复后不能重新驱动真实电网")
end, "振荡器真实驱动继电器、电灯、启停和快照恢复")
