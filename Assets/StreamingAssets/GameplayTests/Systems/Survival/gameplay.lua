-- 真实加载的自有生物通过正式体温模块结算，检查真实 Buff 和快照重载。
test.check("world_ready", "角色生存使用真实隔离世界")
test.runtime("survival", "temperature_conditions", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.actorInitialized and
        data.temperatureLoaded and data.buffsLoaded, "没有运行已加载的真实生物模块")
    assert(type(data.actualAmbient) == "number", "没有读取真实所在地环境温度")
    assert(math.abs(data.beforeExhaustion - .25) < .0001 and not data.coldBeforeExhaustion,
        "危险温度缓冲没有逐步消耗或疾病提前出现")
    assert(data.coldTemperature < data.safeMinimum,
        "耗尽冷缓冲时角色体温没有处于正式危险范围")
    assert(data.afterExhaustion == 0 and data.coldAfterExhaustion and not data.hotWhileCold,
        "低温缓冲耗尽后没有得到真实冻伤 Buff")
    assert(data.coldConditionSourceKey == "temperature:hypothermia" and
        data.coldConditionDefinitionId == "低温冻伤", "冻伤 Buff 的正式来源或定义不匹配")
    assert(data.hotTemperature > data.safeMaximum,
        "切换高温后角色体温没有处于正式危险范围")
    assert(data.hotAfterSwitch and not data.coldAfterSwitch and data.bufferAfterSwitch == 0,
        "冷热切换重置共用缓冲或保留相反疾病")
    assert(data.hotConditionSourceKey == "temperature:heatstroke" and
        data.hotConditionDefinitionId == "热射病", "热射病 Buff 的正式来源或定义不匹配")
    assert(not data.coldAfterSafe and not data.hotAfterSafe and
        math.abs(data.bufferAfterSafe - .5) < .0001, "回到安全体温后疾病未移除或缓冲未逐步恢复")
    assert(data.safeTemperature >= data.safeMinimum and data.safeTemperature <= data.safeMaximum,
        "回温后角色体温仍在危险范围")
    assert(data.maximumBuffer > data.bufferAfterSafe and data.restoredInitialized and
        data.restoredGuid == data.expectedGuid and math.abs(data.restoredBuffer - data.bufferAfterSafe) < .0001,
        "生物快照重新加载把剩余缓冲补满或改变身份")
    assert(data.restoredMaximumBuffer == data.maximumBuffer, "正式加载改变体温配置容量")
    assert(data.actorRemoved, "体温测试自有生物没有在结束时清理")
end, "真实体温缓冲、冷热疾病、安全恢复和模块存档重载")

test.runtime("survival", "temperature_water_callback_order", {}, function(data)
    assert(data.actorRemoved, "体温回调测试自有生物没有清理")
    assert(#data.events == 3, "水冷、回调内改温与环境传热的通知数量错误")
    assert(math.abs(data.events[1] - (data.initial - .5)) < .0001, "第一条通知不是入水降温")
    assert(math.abs(data.events[2] - data.injected) < .0001, "回调内的基础体温修改未立即通知")
    assert(math.abs(data.events[3] - data.expectedAfterCallback) < .0001 and
        math.abs(data.afterCallback - data.expectedAfterCallback) < .0001,
        "环境传热没有读取水冷回调后的最新体温")
    assert(math.abs(data.afterTargetCleared - data.expectedAfterTargetCleared) < .0001,
        "水冷回调令体温低于目标后，旧水冷目标仍继续生效")
end, "真实生物入水通知、回调改温、环境传热与水冷目标清理顺序")
