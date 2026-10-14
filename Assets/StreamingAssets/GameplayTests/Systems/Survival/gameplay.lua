-- 真实加载的自有生物通过正式体温模块结算，检查真实 Buff 和快照重载。
test.check("world_ready", "角色生存使用真实隔离世界")
test.runtime("survival", "temperature_conditions", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.actorInitialized and
        data.temperatureLoaded and data.buffsLoaded, "没有运行已加载的真实生物模块")
    assert(type(data.actualAmbient) == "number", "没有读取真实所在地环境温度")
    assert(math.abs(data.beforeExhaustion - .25) < .0001 and not data.coldBeforeExhaustion,
        "危险温度缓冲没有逐步消耗或疾病提前出现")
    assert(data.afterExhaustion == 0 and data.coldAfterExhaustion and not data.hotWhileCold,
        "低温缓冲耗尽后没有得到真实冻伤 Buff")
    assert(data.hotAfterSwitch and not data.coldAfterSwitch and data.bufferAfterSwitch == 0,
        "冷热切换重置共用缓冲或保留相反疾病")
    assert(not data.coldAfterSafe and not data.hotAfterSafe and
        math.abs(data.bufferAfterSafe - .5) < .0001, "回到安全体温后疾病未移除或缓冲未逐步恢复")
    assert(data.maximumBuffer > data.bufferAfterSafe and data.restoredInitialized and
        data.restoredGuid == data.expectedGuid and math.abs(data.restoredBuffer - data.bufferAfterSafe) < .0001,
        "生物快照重新加载把剩余缓冲补满或改变身份")
    assert(data.restoredMaximumBuffer == data.maximumBuffer, "正式加载改变体温配置容量")
    assert(data.actorRemoved, "体温测试自有生物没有在结束时清理")
end, "真实体温缓冲、冷热疾病、安全恢复和模块存档重载")
