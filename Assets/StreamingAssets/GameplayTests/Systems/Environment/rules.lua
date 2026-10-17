local function near(a, b, message, tolerance)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= (tolerance or 0.0001), message)
end

test.system("environment", "light_registry", {}, function(data)
    assert(data.registryAvailable, "URP 活跃光源登记表适配失败")
    assert(data.createdRegistered and data.disabledRemoved and data.reenabledRegistered,
        "光源创建、组件启停没有立即反映到正式登记表")
    assert(data.inactiveRemoved and data.reactivatedRegistered and data.destroyedRemoved,
        "对象启停或销毁遗留了光照成员")
    assert(data.recreatedRegistered and data.proxyExcluded and data.proxyRemovedRestored,
        "光源重建或环绕代理分类缓存未正确失效")
end, "活跃光源登记表、创建启停销毁重建、环绕代理分类失效")

test.system("environment", "light_sampling", {}, function(data)
    near(data.dark, 0, "没有光源时不再完全黑暗")
    near(data.timeOnlyOnce, 0.4, "太阳快照重复叠加或漏掉额外全局光")
    near(data.center, 0.8, "点光中心亮度改变")
    near(data.innerBoundary, 0.8, "点光内半径边界改变")
    near(data.falloff, 0.4, "点光线性衰减改变")
    near(data.outerBoundary, 0, "点光外半径边界漏光")
    near(data.changedColor, 0.2, "同帧颜色、透明度或强度变化没有立即采到")
    near(data.zeroIntensity, 0, "同帧关灯后仍有逻辑光照")
    near(data.disabled, 0, "禁用点光后仍有逻辑光照")
    near(data.reenabled, 0.8, "同帧重新开灯没有恢复逻辑光照")
    near(data.movedOutside, 0, "同帧移动光源后仍读取旧位置")
    near(data.changedRadius, 0.8 / 6, "同帧半径变化未参与采样")
    near(data.wrappedSeam, 0.8, "环绕世界缝两侧没有采用最短距离")
    near(data.wrappedEquivalent, data.wrappedSeam, "环绕等价坐标光照不同")
    near(data.infiniteFar, 0, "非环绕世界错误连接远端光源")
    near(data.proxyIgnored, 0.4, "纯渲染环绕代理被重复计入玩法光照")
    near(data.destroyed, 0, "销毁后缓存引用仍贡献光照")
    near(data.unsupportedType, 0, "原本不参与玩法采样的灯类型被额外计入")
    near(data.saturated, 1, "多光叠加没有钳制到满亮")
    assert(data.sampleAllocatedBytes == 0, "预热后的重复光照采样仍分配托管内存")
end, "实时光照数值、太阳去重、动态参数、代理过滤、环绕距离和热路径分配")

for _, sample in ipairs({
    { temperature = 0, humidity = 0, factor = 0 },
    { temperature = 10, humidity = 0.25, factor = 0.25 },
    { temperature = 20, humidity = 0.5, factor = 0.5 },
    { temperature = 30, humidity = 0.75, factor = 0.75 },
    { temperature = 40, humidity = 1, factor = 1 },
    { temperature = 40, humidity = 0, factor = 0.5 },
    { temperature = 0, humidity = 1, factor = 0.5 },
    { temperature = -100, humidity = 0, factor = 0 },
    { temperature = 100, humidity = 1, factor = 1 },
    { temperature = 20, humidity = 0.5, factor = 0.5, wind = 1, precipitation = 1 }
}) do
    local input = sample
    test.system("environment", "natural_fog_profile", input, function(data)
        assert(data.found, "正式天气配置缺少自然雾要素")
        near(data.probability, 0.3 * input.factor, "自然雾触发概率没有随温湿条件提高")
        near(data.activationProbability, 0.09 * input.factor, "自然雾没有沿用统一自然天气概率倍率")
        near(data.intensityLimit, 0.1 + 0.9 * input.factor, "自然雾强度上限没有随温湿条件提高")
        near(data.durationLimit, 60 + 180 * input.factor, "自然雾持续时间上限没有随温湿条件增长")
        near(data.defaultProbability, data.probability, "代码默认值和正式资源配置不一致")
    end, "自然雾温湿度、起雾机会和强度时长上限：" .. input.temperature .. "/" .. input.humidity)
end

test.system("environment", "natural_fog_distribution", {}, function(data)
    local expected = {832, 1984, 2368, 1984, 832}
    assert(#data.bins == 5 and data.sampleCount == 8000, "自然雾抛物线分布采样不完整")
    for index, count in ipairs(expected) do
        assert(data.bins[index] == count, "自然雾强度不是中间高两端低的抛物线分布：" .. index)
    end
    near(data.minimumIntensity, 0.1, "自然雾抽样越过最小强度")
    near(data.maximumIntensity, 1, "自然雾抽样越过最大强度")
    near(data.minimumDuration, 60, "自然雾抽样越过最小时长")
    near(data.maximumDuration, 240, "自然雾抽样越过最大时长")
    near(data.middleIntensity, 0.55, "最常见的中间雾强度错误")
    near(data.middleDuration, 150, "中间雾没有使用相应持续时间")
    near(data.coolIntensity, 0.2125, "较低温湿条件没有降低同一次抽样的强度")
    near(data.coolDuration, 82.5, "较低温湿条件没有缩短同一次抽样的时长")
end, "自然雾小雾和大雾少、中雾多的抛物线分布")

test.system("environment", "natural_fog_lifecycle", {}, function(data)
    near(data.beforeCheck, 0, "自然雾在首次天气检查前提前出现")
    near(data.startedFog, 0.8, "自然抽签没有把雾要素投影到天气快照")
    near(data.startedRemaining, 12, "自然雾没有使用正式要素持续时间")
    near(data.neighborFog, data.startedFog, "同一天气区的地块没有共享自然雾")
    near(data.rainWithFog, 0.4, "自然雾阻止了独立降水")
    near(data.fogWithRain, 0.8, "降水覆盖了自然雾")
    near(data.restoredFog, 0.8, "自然雾状态经正式存档记录转换后丢失")
    near(data.replicaFog, 0.8, "客户端天气快照丢失自然雾")
    near(data.overrideFog, 0.2, "手动雾覆盖没有优先于自然雾")
    near(data.resumedFog, 0.8, "手动雾到期后没有恢复仍在持续的自然雾")
    near(data.zeroOverrideFog, 0, "手动零雾没有临时屏蔽自然雾")
    near(data.afterZeroOverrideFog, 0.8, "手动零雾到期后没有恢复自然雾")
    near(data.expiredFog, 0, "自然雾持续时间结束后没有消散")
    near(data.rainAfterFog, 0.4, "自然雾到期错误终止降水")
    near(data.clearedFog, 0, "晴天入口没有清除自然雾")
end, "自然雾正式调度、区域共享、到期、覆盖和保存副本链路")

test.system("environment", "natural_fog_validation", {}, function(data)
    assert(data.nonFiniteSample == "ArgumentOutOfRangeException" and data.outOfRangeSample == "ArgumentOutOfRangeException",
        "天气强度抽样接受了非有限或越界的随机数")
    assert(data.emptyTemperatureRange == "ArgumentOutOfRangeException" and data.negativeTemperatureWeight == "ArgumentOutOfRangeException",
        "天气规则接受了无效温度区间或负权重")
end, "自然雾气候权重、温度区间和随机样本校验")

for _, id in ipairs({"rain", "wind", "thunder", "fog"}) do
    local element_id = id
    test.system("environment", "natural_recovery_probability", {id = element_id}, function(data)
        near(data.floor, 0.2, "自然天气刚结束的概率倍率改变")
        near(data.seconds, 300, "自然天气恢复时长不是300游戏秒")
        near(data.defaultFloor, data.floor, "代码默认恢复倍率和正式配置不同")
        near(data.defaultSeconds, data.seconds, "代码默认恢复时长和正式配置不同")
        near(data.checkSeconds, 30, "自我调节改变了原30秒检查间隔")
        near(data.recoveryEnd, 1300, "恢复完成时刻没有从事件结束后计时")
        near(data.basis, 0.12, "自我调节改写了冻结基础概率或统一倍率")
        near(data.initial, data.basis, "初次没有自然历史时被抑制")
        near(data.beforeEnd, data.basis * 0.2, "长事件尚未结束时提前恢复概率")
        near(data.atEnd, data.basis * 0.2, "自然天气结束时没有压到原概率20%")
        near(data.halfway, data.basis * 0.6, "150游戏秒时没有线性恢复到60%")
        near(data.recovered, data.basis, "300游戏秒时没有完全恢复")
        near(data.later, data.basis, "完全恢复后概率继续增大")
        near(data.high, data.low * 4, "相同历史和强度下基础概率比例没有保留")
        assert(data.high > data.low, "相同恢复进度时高概率没有保持更高")
        near(data.maximumAtEnd, data.maximumBasis * 0.2, "强度概率分段与恢复倍率没有叠加")
        if element_id == "wind" then near(data.maximumAtEnd, 0.0024, "十级风分段概率被自我调节覆盖") end
    end, "自然天气概率自我调节、默认配置和完整恢复：" .. element_id)

    test.system("environment", "natural_recovery_lifecycle", {id = element_id}, function(data)
        near(data.before.intensity, 0, "首次检查前出现自然事件")
        near(data.before.recoveryEnd, 0, "首次检查前已有自然恢复历史")
        near(data.first.started, 30, "首次自然事件没有走正式30秒检查")
        near(data.first.intensity, 0.5, "正式自然抽签没有启动要素")
        near(data.first["end"], 33, "非对齐事件结束时刻被改成天气步长")
        near(data.first.recoveryEnd, 333, "恢复完成时刻没有绑定本次自然结束时刻")
        near(data.neighbor.recoveryEnd, 333, "同一区域成员没有共享自然历史")
        near(data.otherRegion.recoveryEnd, 0, "自然历史串到了另一个区域")
        for _, sample in ipairs(data.otherElements) do
            near(sample.recoveryEnd, 0, "某要素触发后抑制了本区域另一要素")
        end
        near(data.stillActive.intensity, 0.5, "事件在实际结束前提前消散")
        near(data.expired.intensity, 0, "事件到期后仍持续")
        near(data.expired.recoveryEnd, 333, "发现到期的步长推迟了实际恢复起点")
        near(data.expired.probability, 0.2 + 0.8 / 300, "非对齐结束后经过1秒的概率恢复错误")
        near(data.restored.recoveryEnd, data.expired.recoveryEnd, "正式存档记录转换或Restore丢失自然历史")
        near(data.restored.probability, data.expired.probability, "正式恢复改变了当前概率")
        near(data.cloneChanged, 999, "深复制夹具没有改到复制对象")
        near(data.sourceAfterClone, 333, "Clone共享了可写自然历史")
        near(data.engineAfterExport, 333, "导出快照泄漏了内部可写自然历史")
    end, "自然天气结束计时、区域要素独立及保存克隆：" .. element_id)

    test.system("environment", "natural_recovery_draw", {id = element_id}, function(data)
        local regulated_count, low_count, high_count = 0, 0, 0
        assert(#data.trials == 64, "配对自然抽签样本缺失")
        for _, trial in ipairs(data.trials) do
            near(trial.history, 333, "配对抽签没有从同一自然历史开始")
            near(trial.basisIntensity, 0.5, "无历史的概率一对照没有成功触发")
            if trial.lowIntensity > 0 then
                low_count = low_count + 1
                assert(trial.highIntensity > 0, "相同抽样和历史时高概率漏掉了低概率的成功事件")
            end
            if trial.highIntensity > 0 then high_count = high_count + 1 end
            if trial.renewed.intensity > 0 then
                regulated_count = regulated_count + 1
                near(trial.renewed.started, 60, "恢复期间的成功没有走新检查时刻")
                near(trial.renewed["end"], 63, "恢复期间再次成功改变了原持续时长")
                near(trial.renewed.recoveryEnd, 363, "再次成功没有按新结束时刻重新开始恢复")
                near(trial.afterEnd.probability, 0.2 + 0.8 / 300, "第二次事件结束后没有重新从低概率恢复")
            else
                near(trial.renewed.recoveryEnd, 333, "失败抽签重新开始了恢复期")
            end
            near(trial.afterEnd.intensity, 0, "配对抽样的第二次事件没有到期")
        end
        assert(regulated_count > 0 and regulated_count < #data.trials, "正式自然抽签没有消费概率抑制或禁止了所有再次触发")
        assert(high_count > low_count, "相同历史、强度和时长下较高基础概率没有带来更多触发")
    end, "正式自然抽签消费抑制、恢复期重触发和基础概率单调：" .. element_id)
end

test.system("environment", "natural_recovery_lifecycle", {id = "rain", temperature = -10}, function(data)
    near(data.rain, 0, "寒冷自然降水没有分流为雪")
    near(data.snow, 0.5, "自然降雪没有复用降水事件")
    near(data.first.recoveryEnd, 333, "自然降雪没有复用rain的恢复历史")
end, "自然降雪共用降水恢复历史")

test.system("environment", "natural_recovery_clock", {}, function(data)
    near(data.started["end"], 1030, "长事件结束时刻错误")
    near(data.longActive.intensity, 0.5, "长事件在300秒前后被提前终止")
    near(data.longActive.recoveryEnd, 1330, "长事件按起始时刻提前消耗了恢复期")
    near(data.ended.probability, 0.2, "长事件刚结束时没有完整抑制")
    near(data.partlyRecovered.probability, 0.2 + 0.8 * 200 / 300, "长事件结束后恢复进度错误")
    near(data.nearZero.time, 0, "深回拨没有落在零时刻")
    near(data.nearZero["end"], 0, "深回拨夹具没有跨越旧事件结束时刻")
    near(data.nearZero.recoveryEnd, 100, "深回拨丢掉剩余恢复时间")
    near(data.nearZero.probability, data.partlyRecovered.probability, "回拨越过旧结束时刻后重启或消除了剩余抑制")
    near(data.beforeZero.recoveryEnd, 0, "恢复完成时刻回拨到零后没有归零")
    near(data.beforeZero.probability, 1, "恢复完成时刻到零后仍抑制")
    near(data.beforeRewind.probability, 1, "长事件恢复完成后仍抑制")
    near(data.afterRewind.probability, 1, "已完全恢复的历史在回拨后重新产生抑制")
    near(data.jumped.time, 5000, "大跳时没有推进到目标时刻")
    near(data.jumped.intensity, 0, "大跳时保留已结束天气")
    near(data.jumped.probability, 1, "大跳时按到达时间重启了旧事件恢复期")
end, "长事件、有限补算大跳时、回拨越零及已恢复历史")

test.system("environment", "natural_recovery_manual", {}, function(data)
    assert(#data.elements == 4, "手动天气夹具缺少自然要素")
    for _, sample in ipairs(data.elements) do
        near(sample.active.intensity, 0.5, "手动天气未生效：" .. sample.id)
        near(sample.active.recoveryEnd, 0, "手动天气开始时产生自然历史：" .. sample.id)
        near(sample.expired.recoveryEnd, 0, "手动天气结束时产生自然历史：" .. sample.id)
    end
    near(data.snowIntensity, 0.5, "手动雪夹具未生效")
    near(data.snowHistory, 0, "手动降雪产生自然历史")
    for _, history in ipairs(data.overrideHistories) do near(history, 0, "表现覆盖、手动雷击或清除产生自然历史") end
    near(data.replaced.recoveryEnd, 333, "手动替换改写了已有自然历史")
    near(data.manualEnded.recoveryEnd, 333, "手动事件结束后延长了已有自然恢复期")
    near(data.cleared.recoveryEnd, 333, "清天气入口改写了已有自然历史")
end, "手动天气、雪、雷击和清除不产生自然恢复历史")

test.system("environment", "natural_recovery_validation", {}, function(data)
    for _, name in ipairs({"negativeMultiplier", "largeMultiplier", "nanMultiplier", "infiniteMultiplier",
        "negativeSeconds", "nanSeconds", "infiniteSeconds"}) do
        assert(data[name] == "ArgumentOutOfRangeException", "接受非法自然恢复配置：" .. name)
    end
    for _, name in ipairs({"negativeHistory", "nanHistory", "infiniteHistory"}) do
        assert(data[name] == "ArgumentException", "恢复入口接受非法历史：" .. name)
    end
    assert(data.zeroSecondsValidation == "", "关闭恢复期的零时长配置被拒绝")
    near(data.zeroSecondsProbability, 1, "零恢复时长配置仍然抑制概率")
end, "自然恢复配置有限值、时长边界及保存历史拒绝")

for _, sample in ipairs({{current = 20, ambient = 120, seconds = 10},
    {current = 120, ambient = 20, seconds = 10}, {current = 20, ambient = -80, seconds = 10}}) do
    local input = sample
    test.system("environment", "thermal", input, function(data)
        assert(data.whole >= math.min(input.current, input.ambient) and data.whole <= math.max(input.current, input.ambient),
            "环境传热越过目标温度")
        assert(math.abs(data.whole - input.ambient) < math.abs(input.current - input.ambient), "环境传热没有趋近环境")
        near(data.whole, data.split, "一次和十次结算的传热不一致")
        near(data.zeroTime, input.current, "零时间仍改变温度")
        near(data.negativeTime, input.current, "负时间仍改变温度")
        near(data.invalidCurrent, input.ambient, "非法体温没有恢复有限环境值")
        near(data.invalidAmbient, input.current, "非法环境值污染了体温")
        near(data.limited, input.ambient, "固定速率传热没有在目标温度停下")
    end, "环境传热和步长一致性：" .. input.current .. "到" .. input.ambient)
end

test.system("environment", "heat_sources", {}, function(data)
    near(data.center, 1, "热源中心不是完整影响")
    near(data.middle, 0.5625, "热源距离衰减错误")
    near(data.edge, 0, "热源边界仍有影响")
    near(data.outside, 0, "热源范围外仍有影响")
    near(data.single, 40, "寒冷环境没有趋近热源目标")
    near(data.duplicate, data.single, "重叠相同热源叠加了目标温度")
    near(data.hotAmbient, 50, "更冷的普通热源降低了高温环境")
end, "热源距离、目标上限与重叠")

test.system("environment", "wind", {}, function(data)
    assert(data.belowLevel == 7 and data.thresholdLevel == 8, "风级边界错误")
    near(data.belowPush, 0, "低于八级仍推动物品")
    near(data.lightPush, 0.06, "起吹等级速度错误")
    near(data.strongPush, 0.2, "高一级的推动速度错误")
    near(data.maximumPush, 0.5, "最大推动速度错误")
    near(data.unreachablePush, 0, "不可达等级仍推动")
    assert(data.lightMassLevel == 8 and data.cutoffMassLevel == 11 and data.invalidMassLevel == 11,
        "质量边界或非法质量没有影响起吹资格")
    assert(data.zeroGravityLevel == 8, "零重力下起吹资格仍按地球重量计算")
    near(data.blendedPushX, -0.1125, "风区边缘混合速度X与原分档规则不一致")
    near(data.blendedPushY, -0.0075, "风区边缘混合速度Y与原分档规则不一致")
    near(data.unreachablePushX, 0, "超重物体不应有水平风速")
    near(data.unreachablePushY, 0, "超重物体不应有垂直风速")
    near(data.originPushX, 0.2, "零空间过渡权重未保留当前风区速度")
    near(data.originPushY, 0, "零空间过渡权重引入侧向风")
end, "风级、质量与重力推动门槛")

test.system("environment", "weather", {}, function(data)
    assert(data.firstReady and data.secondReady, "同一区域的天气快照缺失")
    near(data.firstRain, 0.8, "区域强制降雨没有进入正式快照")
    near(data.secondRain, data.firstRain, "相邻成员没有共享同一场雨")
    near(data.firstSnow, 0, "暖区普通降雨被误分成雪")
    assert(data.firstRegionX == data.secondRegionX, "同一区域成员归属不一致")
    assert(data.frozenReady and data.frozenTemperature == 10, "后登记区块改写了区域冻结气候")
    near(data.endedPrecipitation, 0, "持续时间结束后降水事件没有停止")
    assert(data.negativeRegionX == -128 and data.negativeRegionY == -128, "负坐标天气区域没有向下归整")
    near(data.rainProbability, 0.12, "自然天气统一概率倍率错误")
    near(data.wind8Probability, 0.06, "八级风附加概率倍率错误")
    near(data.wind9Probability, 0.03, "九级风附加概率倍率错误")
    near(data.wind10Probability, 0.012, "十级风附加概率倍率错误")
end, "区域天气共享、冻结气候、事件到期与强风概率")

test.system("environment", "wind_read_path", {}, function(data)
    local function same_wind(actual, expected, label)
        near(actual.x, expected.x, label .. "：水平分量")
        near(actual.y, expected.y, label .. "：垂直分量")
        near(actual.strength, expected.strength, label .. "：原始强度")
    end
    local function same_blend(actual, expected, label)
        assert(actual.found == expected.found, label .. "：存在状态")
        for _, name in ipairs({"origin", "horizontal", "vertical", "diagonal"}) do
            same_wind(actual[name], expected[name], label .. "：" .. name)
        end
        near(actual.horizontalWeight, expected.horizontalWeight, label .. "：横向权重")
        near(actual.verticalWeight, expected.verticalWeight, label .. "：纵向权重")
    end
    assert(data.event.found and data.eventSnapshot.found, "活动风事件采样缺失")
    same_wind(data.event.origin, data.eventSnapshot, "活动风单项/完整快照一致")
    near(data.event.origin.x, .54, "非单位气候风向未归一化")
    near(data.event.origin.y, .72, "非单位气候风向垂直分量错误")
    near(data.event.origin.strength, .9, "风事件强度错误")
    same_wind(data.forcedZero.origin, data.forcedZeroSnapshot, "强制零风单项/完整快照一致")
    near(data.forcedZero.origin.strength, 0, "强制零风未覆盖活动事件")
    assert(not data.forcedZeroHasPush and data.restoredHasPush, "零风覆盖及到期恢复未影响推动资格")
    same_wind(data.restored.origin, data.restoredSnapshot, "覆盖到期单项/完整快照一致")
    same_wind(data.restored.origin, data.event.origin, "覆盖到期未恢复原风事件")
    same_wind(data.fallback.origin, data.fallbackSnapshot, "无气候方向回退单项/完整快照一致")
    same_blend(data.fallback, data.fallbackRepeated, "随机回退方向未保持稳定")
    near(data.fallback.origin.x ^ 2 + data.fallback.origin.y ^ 2, .8 ^ 2, "随机回退方向未保留原始风强")

    for _, name in ipairs({"fourRegions", "wrapped"}) do
        local group = data[name]
        assert(group.authority.found and group.replica.found and #group.snapshots == 4, name .. "四区状态缺失")
        assert(group.authority.horizontalWeight > 0 and group.authority.horizontalWeight < .5
            and group.authority.verticalWeight > 0 and group.authority.verticalWeight < .5,
            name .. "边缘四区混合权重无效")
        for index, field in ipairs({"origin", "horizontal", "vertical", "diagonal"}) do
            assert(group.snapshots[index].found, name .. "完整天气快照缺失")
            same_wind(group.authority[field], group.snapshots[index], name .. "四区单项/完整快照一致：" .. field)
        end
        same_blend(group.replica, group.authority, name .. "客户端副本保留原始风字段和权重")
    end
    same_blend(data.wrapped.wrappedEquivalent, data.wrapped.authority, "世界环绕等价位置")
    assert(data.missingNeighbor.found and not data.missingSnapshot.found, "缺少邻区的风查询创建了天气状态")
    near(data.missingNeighbor.horizontal.strength, 0, "未加载邻区没有按无风处理")
    assert(not data.invalidPosition.found, "非有限位置仍返回风采样")
end, "单项风读取与完整快照一致、零风覆盖、四区、副本和环绕")

test.system("environment", "thermal_point_path", {}, function(data)
    assert(data.wrappedReady, "环绕热场夹具边界创建失败")
    for name, sample in pairs(data.localField) do
        near(sample.point, sample.batch, "局部热源单格/批量一致：" .. name)
    end
    near(data.localField.initial.point, 100, "局部热源中心目标温度")
    near(data.localField.targetChanged.point, 80, "原位目标温度更新没有立即生效")
    near(data.localField.strengthChanged.point, 50, "原位来源强度更新没有立即生效")
    assert(data.localField.movedOld.point < data.localField.strengthChanged.point, "来源移动后旧位置未衰减")
    near(data.localField.movedNew.point, 50, "来源移动后新中心强度错误")
    near(data.localField.radiusChanged.point, 20, "缩小半径后范围外仍有热量")
    near(data.localField.removed.point, 20, "移除局部来源后仍有热量")
    near(data.localField.cooling.point, -20, "制冷来源没有降低环境温度")
    near(data.localField.heatingOnly.point, 20, "普通热源错误降低更暖环境")
    assert(data.localField.wrapped.point > 20 and data.localField.wrapped.point < 100, "局部热源未跨环绕缝衰减")

    local liquid = data.liquid
    for _, name in ipairs({"initial", "sameRevisionRebound", "sameRevisionRestored", "soilChanged", "covered", "uncovered",
        "cleared", "unloaded", "rebound", "wrapped", "disposed"}) do
        local sample = liquid[name]
        assert(#sample.point == 16 and #sample.batch == 16, "液体采样未覆盖完整夹具：" .. name)
        for index, point in ipairs(sample.point) do
            near(point, sample.batch[index], "液体单格/批量一致：" .. name .. "格" .. index)
            assert(point >= 20 and point <= 100, "液体加热越过环境与目标边界：" .. name)
        end
    end
    near(liquid.initial.point[1], 100, "液体热源中心温度错误")
    assert(liquid.initial.point[2] < 100, "重叠同温液体热源被相加")
    assert(liquid.soilRevision == liquid.initialRevision, "普通土壤更新使液体热场版本失效")
    assert(liquid.replacementRevision == liquid.initialRevision, "同版本地形重绑夹具版本不相同")
    near(liquid.sameRevisionRebound.point[1], 20, "相同版本地形替换后仍使用旧来源")
    near(liquid.sameRevisionRebound.point[16], 100, "相同版本地形替换后未读取新来源")
    assert(liquid.coveredRevision > liquid.soilRevision, "地表支撑身份未使液体热场失效")
    for index, initial in ipairs(liquid.initial.point) do
        near(liquid.soilChanged.point[index], initial, "普通土壤更新改变热场")
        near(liquid.sameRevisionRestored.point[index], initial, "相同版本地形换回后未恢复来源")
        near(liquid.uncovered.point[index], initial, "移除支撑后没有恢复液面热源")
        near(liquid.cleared.point[index], 20, "抽干热液后仍有热量或普通水错误发热")
        near(liquid.unloaded.point[index], 20, "卸载液体来源后仍有热量")
        near(liquid.disposed.point[index], 20, "地形释放后仍有缓存热量")
    end
    assert(liquid.covered.point[1] < liquid.initial.point[1], "支撑遮住液面后旧热源仍在")
    near(liquid.rebound.point[1], 20, "同位置重绑地形后沿用了旧热源")
    near(liquid.rebound.point[16], 100, "同位置重绑地形后新热源缺失")
    assert(liquid.wrapped.point[13] > 20, "液体热源未跨环绕缝影响单格")
end, "局部与液体热源单格/批量一致、来源变化、地表版本和环绕")

local function values_equal(actual, expected, message)
    assert(#actual == #expected, message .. "：数量错误")
    for index, value in ipairs(expected) do
        near(actual[index], value, message .. "：第" .. index .. "项")
    end
end

local function layer_equal(actual, expected, message)
    assert(actual.exists and actual.length == #expected, message .. "：只读层长度或身份错误")
    values_equal(actual.values, expected, message)
end

test.system("environment", "environment_batch", {}, function(data)
    assert(data.immediateChanges == 2, "批次真实变化数包含了不变格")
    layer_equal(data.afterImmediate, {0.3, 0.4, 0.8, 0, 0.9, 0}, "即时批写数值")
    assert(data.immediateRevision == data.initialRevision + 1, "同层批写逐格递增了版本")
    assert(data.unchangedChanges == 0 and data.emptyChanges == 0, "不变或空批仍报告变化")
    assert(not data.emptyLayer.exists and data.emptyLayer.length == 0, "空批创建了无用环境层")
    assert(data.zeroChanges == 0 and data.firstRealChange == 1,
        "全零新层应保持无变化，首次非零写入必须正常触发")
    layer_equal(data.zeroLayer, {0, 0, 0, 0, 0, 0}, "全零新层仍应保留原有可读语义")
    layer_equal(data.afterFirstRealChange, {0, 0, 0.5, 0, 0, 0}, "首次真实变化应从原零层写入")
    assert(data.afterNoopNotices == 2 and data.afterNoopRevision == data.immediateRevision + 1,
        "不变或空批仍广播通知或递增版本")
    assert(data.waterChanges == 2 and data.moistureChanges == 3, "延迟批写真正变化格计数错误")
    assert(data.beforePublishNotices == 2, "多层数据写完前提前广播")
    layer_equal(data.beforePublishMoisture, {0.7, 0.4, 0.9, 0, 0.9, 0.5}, "发布前湿度可读")
    layer_equal(data.beforePublishWater, {30, 40, 80, 0, 90, 0}, "发布前水量可读")
    assert(data.afterPublishNotices == 4 and data.afterRepublishNotices == 4,
        "多层发布没有每层一次或重复发布再次通知")
    assert(data.afterPublishRevision == data.immediateRevision + 3, "延迟发布额外修改版本")
    assert(#data.notices == 5, "批量和后续单格通知数量错误")
    local immediate, initial, water, moisture, single = data.notices[1], data.notices[2], data.notices[3], data.notices[4], data.notices[5]
    assert(immediate.kind == "Environment" and immediate.layer == "moisture" and immediate.isBatch,
        "即时批次通知层或类型错误")
    values_equal(immediate.indices, {0, 4}, "即时批次只通知真实变化格")
    assert(immediate.borrowedIndexCount == 2, "批次借用索引长度错误")
    assert(initial.layer == "probe.zero" and initial.isBatch, "零值新层首次变化未收到正式通知")
    values_equal(initial.indices, {2}, "零值新层首次真实变化格")
    assert(water.layer == "agriculture.water" and moisture.layer == "moisture", "发布顺序未按层写入顺序")
    values_equal(water.indices, {0, 4}, "水量批次变化索引")
    values_equal(moisture.indices, {0, 2, 5}, "湿度批次变化索引")
    for _, notice in ipairs({water, moisture}) do
        assert(notice.isBatch and notice.kind == "Environment", "延迟通知没有保留批次身份")
        layer_equal(notice.water, {30, 40, 80, 0, 90, 0}, "通知回调读取完整水量")
        layer_equal(notice.moisture, {0.7, 0.4, 0.9, 0, 0.9, 0.5}, "通知回调读取完整湿度")
    end
    assert(not single.isBatch and single.borrowedIndexCount == 0 and single.layer == "moisture",
        "单格通知被改成了借用批次")
    values_equal(single.indices, {5}, "统一索引枚举保留单格真实区块宽度")
    assert(data.blockingRevision == 0 and data.surfaceLiquidRevision == 0, "普通土壤写入污染遮挡或液体版本")
end, "土壤环境批写、真实变化格、延迟合并通知与单格兼容")

test.system("environment", "environment_batch_rejection", {}, function(data)
    assert(#data.rejected == 10, "非法输入采样不完整")
    for _, rejected in ipairs(data.rejected) do
        assert(rejected.exception == "ArgumentException", rejected.name .. "没有整批拒绝")
        layer_equal(rejected.before, {0.2, 0.3, 0.4, 0.5}, rejected.name .. "前置数据")
        layer_equal(rejected.after, {0.2, 0.3, 0.4, 0.5}, rejected.name .. "留下部分写入")
        assert(rejected.afterRevision == rejected.beforeRevision and rejected.afterNotices == rejected.beforeNotices,
            rejected.name .. "拒绝后改变版本或通知")
    end
    assert(not data.rejectedLayer.exists, "非法新层批次留下了环境层")
    assert(data.recoveredChanges == 2 and data.noticesAfterRecovery == 1, "拒绝后的批次对象不能继续正常使用")
    layer_equal(data.recovered, {0.6, 0.3, 0.4, 0.7}, "失败后有效写回")
    assert(data.surfaceLiquidRevision == 0, "失败或普通土壤写入影响液体缓存版本")
end, "批次非法层名、越界、重复、逆序和非有限值整体拒绝")

test.system("environment", "environment_batch_lifecycle", {}, function(data)
    assert(data.firstPublish == "InvalidOperationException" and data.afterFailedNotices == 1,
        "观察者故障没有停止当前发布并暴露失败")
    assert(data.overwritePending == "InvalidOperationException" and data.singleOverwritePending == "InvalidOperationException",
        "未发布完成的同层批次被覆盖")
    layer_equal(data.afterFailedMoisture, {0.7, 0, 0, 0}, "发布失败后的正式数据")
    assert(data.retryPublish == "" and data.afterRetryNotices == 3, "失败层和后续层未保留或无法重试")
    assert(data.afterRepublishNotices == 3 and data.afterRetryRevision == data.beforePublishRevision,
        "重新发布再次发送成功层或改变数据版本")
    assert(data.reusedChanges == 1 and data.afterReuseNotices == 4, "重试完成后同层无法继续使用")
    assert(#data.notices == 4 and data.notices[1].layer == "moisture" and data.notices[2].layer == "moisture"
        and data.notices[3].layer == "agriculture.water", "失败保留后的通知顺序错误")
    values_equal(data.notices[1].indices, {0}, "故障时变化格")
    values_equal(data.notices[2].indices, {0}, "重试保留故障层变化格")
    values_equal(data.notices[3].indices, {1}, "重试保留后续层变化格")
    values_equal(data.notices[4].indices, {3}, "复用后的变化格")
    layer_equal(data.notices[1].moisture, {0.7, 0, 0, 0}, "先前通知的独立采样副本")
    layer_equal(data.notices[4].moisture, {0.7, 0, 0, 0.8}, "复用后的正式湿度")
    for _, notice in ipairs(data.notices) do
        assert(notice.nestedWrite == "InvalidOperationException" and notice.nestedSingleWrite == "InvalidOperationException",
            "回调期间允许改写借用批次")
        assert(notice.nestedPublish == "InvalidOperationException" and notice.nestedDispose == "InvalidOperationException",
            "回调期间允许嵌套发布或释放借用数据")
    end
    assert(data.disposed and data.afterDisposeNotices == data.afterReuseNotices, "释放时错误发布待发队列")
    assert(data.disposedWrite == "ObjectDisposedException" and data.disposedRead == "ObjectDisposedException"
        and data.disposedPublish == "ObjectDisposedException", "已释放地形仍允许批写、借用读取或发布")
end, "批次观察者故障重试、回调重入保护和释放边界")

test.system("environment", "environment_batch_revision", {}, function(data)
    assert(data.soilChanges == 2 and data.soilRevision == data.initialRevision, "土壤水量湿度使液体缓存失效")
    assert(data.costRevision == data.initialRevision, "仅支撑通行代价使液体缓存失效")
    assert(data.supportChanges == 2 and data.supportRevision == data.initialRevision + 1,
        "支撑面身份批量变化没有仅递增一次液体版本")
    assert(data.supportNoopChanges == 0 and data.supportNoopRevision == data.supportRevision,
        "支撑面身份不变仍使液体缓存失效")
    assert(data.liquidChanged and data.liquidRevision == data.supportRevision + 1, "真实液体变化未使缓存失效")
    assert(not data.repeatedLiquidChanged and data.repeatedLiquidRevision == data.liquidRevision,
        "液体深度身份不变仍使缓存失效")
    assert(data.blockingRevision == 0, "环境和液体写入错误改变视线遮挡版本")
end, "土壤、支撑成本、支撑身份和真实液体版本分离")

test.system("environment", "surface_work_queue", {}, function(data)
    assert(data.initialCount == 3 and data.initialFirstLeases == 2,
        "跨步地表积分未逐条入队或未保护待结算区块")
    assert(not data.dryBeforePending and data.dryWhilePending and not data.dryAfterFlush,
        "前笔降雪尚未写回时，后续晴暖融雪步被无雪快筛丢弃")
    assert(data.initialSimulation == "Dormant" and not data.evictedWhilePending,
        "待结算租约错误激活模拟或允许提前释放区块")
    assert(not data.nestedDrain, "正式结算回调中发生重入消费")
    assert(data.afterSliceCount == 2 and data.afterSliceApplied == 1 and data.afterSliceFirstLeases == 1,
        "零预算没有至少完成一块，或继续集中消费了所有区块")
    assert(data.afterSaveBoundaryCount == 1 and data.afterSaveBoundaryApplied == 3,
        "保存边界漏结旧账或等待了保存开始后新增的天气")
    assert(data.afterUnloadCount == 2 and data.afterUnloadFirstLeases == 0 and data.evictedAfterFlush,
        "卸载冲刷未结清指定块，或误消费其他区块")
    assert(data.failure == "InvalidOperationException" and data.afterFailureCount == 1
        and data.afterFailureSecondLeases == 1,
        "故障未报告、已执行事务重复留队或短租约泄漏")
    assert(data.afterDisposeCount == 0 and data.afterDisposeSecondLeases == 0 and data.evictedAfterDispose,
        "退出清理未释放剩余区块短租约")
    assert(data.oldTerrainDisposed and data.staleApplied == 0 and data.reboundLeases == 0,
        "同地址替换地形后旧积分污染了新地形")
    assert(data.rejected == 6 and data.afterRejectedCount == 0,
        "非法秒数或超出本步的降水积分进入队列")
    assert(data.disposedWorldApplied == 0 and data.finalCount == 0,
        "已释放世界仍应用旧天气或遗留队列")
    assert(data.saveBarrierActive and data.saveBarrierNested and data.saveBarrierReleased
        and data.saveBarrierAfterFailure,
        "保存屏障未保持嵌套状态，或取消/异常后仍阻止天气推进")
    local expected = {{0, 2, 1}, {2, 4, 3}, {0, 6, 2}, {0, 10, 5}, {2, 8, 4}}
    assert(#data.applied == #expected, "地表积分回调次数丢失或重复")
    for index, wanted in ipairs(expected) do
        local actual = data.applied[index]
        assert(actual.x == wanted[1], "分帧或卸载后同块结算顺序错误")
        near(actual.seconds, wanted[2], "分帧丢失或合并了本步游戏时间")
        near(actual.precipitation, wanted[3], "分帧丢失或缩减了本步降水积分")
    end
end, "天气地表分帧预算、保存边界、卸载保护、异常与世界释放")

test.system("environment", "surface_temperature_snapshot", {}, function(data)
    assert(#data.samples == 4, "跨帧温度快照采样不完整")
    local cold, warm, changed = data.samples[1], data.samples[2], data.samples[3]
    assert(cold.sampled and warm.sampled and changed.sampled, "天气队列未携带可用的冻结温度上下文")
    near(cold.first, -15, "入队后的调温把原本降雪改成了雨")
    near(cold.second, -15, "无热源的冷快照被后来的热源污染")
    near(warm.first, 40, "热源变温、降功率、移动或移除改变了已入队目标温度")
    near(warm.second, 33.9453125, "热源几何权重重建破坏了旧快照")
    near(changed.first, 52.5, "新入队快照没有独立冻结全局调温、天气和来源强度")
    assert(cold.first <= 0 and warm.first > 0, "延迟结算改变了本笔降水的雨雪相态")
    near(data.afterSourceRemoved, 25, "移除热源后的新快照没有读取当前环境")
    assert(data.samples[4].sampled, "液体温度快照未采样")
    near(data.samples[4].first, 17.5, "液体来源替换改变旧快照的目标值或距离权重")
    near(data.samples[4].second, 45, "液体来源旧快照未保留第二格权重")
    near(cold.seconds, 2, "冷快照丢失游戏时间")
    near(cold.precipitation, 1, "冷快照丢失降水积分")
    near(warm.precipitation, 3, "暖快照丢失降水积分")
end, "天气地表冻结温度、跨冷热变化、热源值与几何权重独立")

test.system("environment", "thermal_point_batch", {}, function(data)
    assert(#data.points == 64 and #data.reference == 64, "自然物同批64次温度采样缺失")
    for index, wanted in ipairs(data.reference) do
        near(data.points[index], wanted, "批次邻区选择改变正式单点热源结果：" .. index)
    end
    near(data.points[1], 100, "液体源所在格未达到目标环境温度")
    near(data.points[2], 90.3125, "相同目标温度的邻近液体热源错误叠加")
    near(data.points[9], 20, "液体热源影响越过真实半径")
    near(data.points[16], 90.3125, "漏掉相邻区块的辐射热源")
    assert(data.preparedChunks == 1 and data.dependencyLookups == 9,
        "同一目标区块64次单点采样重复准备邻区，或漏查完整邻区")
    near(data.nested, 100, "嵌套批次没有独立采样")
    near(data.outerAfterNested, 100, "释放内层批次清掉外层热源")
    assert(data.outerLookupsAfterNested == data.dependencyLookups,
        "嵌套批次使外层重复取得邻区")
    assert(data.releasedRejected == "InvalidOperationException", "批次释放后仍复用旧邻区结果")
    assert(data.soilRevisionUnchanged, "普通土壤变化使液体源缓存失效")
    near(data.soil, 100, "普通土壤变化改变辐射温度")
    near(data.covered, 65, "支撑遮盖后下一批仍使用被遮住的液体源")
    near(data.uncovered, 100, "移除支撑后下一批没有恢复液体源")
    near(data.drained, 20, "抽干后下一批保留旧热源")
    near(data.beforeReload, 100, "目录重载前采样不正确")
    near(data.afterReload, 180, "同目录新版本没有重建液体来源")
    assert(data.reloadRejectedOldScope == "InvalidOperationException", "目录切换后旧批次仍可复用")

    local identity = data.identity
    assert(identity.sameRevision and identity.oldDisposed, "同地址同版本替换夹具未成立")
    near(identity.initial, 100, "地形替换前温度错误")
    near(identity.rebound, 20, "同地址同版本的新地形复用旧液体源")
    near(identity.missingDimension, 20, "缺失维度误读其他维度的液体")
    near(identity.newDimension, 100, "新维度液体加载后未生效")
    near(identity.surfaceAfterCave, 20, "洞穴液体热源泄漏到地表")
    assert(identity.evicted, "来源区块无法卸载")
    near(identity.unloaded, 20, "卸载区块后仍残留热源")
    near(identity.reloaded, 100, "同地址重载后没有恢复当前热源")

    local topology = data.topology
    assert(topology.ready and topology.preparedChunks == 1 and topology.dependencyLookups == 4,
        "小环绕世界未按规范地址去重邻区")
    near(topology.seam, 90.3125, "环绕接缝未使用最短热源距离")
    near(topology.equivalent, topology.seam, "环绕等价格温度不同")
    near(topology.unwrapped, 20, "无环绕世界错误连接两边热源")

    local lifecycle = data.lifecycle
    near(lifecycle.beforeEpoch, 100, "旧世界初始温度错误")
    near(lifecycle.oldScopeAfterEpoch, 20, "世界代际更换后旧作用域仍读热源")
    assert(not lifecycle.oldFieldReopened, "旧代际温度场可重新进入采样批次")
    near(lifecycle.newEpoch, 100, "新代际温度场无法读取当前液体")
    near(lifecycle.afterDispose, 20, "世界释放后仍持有可用的旧热源")
    assert(lifecycle.remainingChunks == 0, "世界释放后仍有地形留存")
end, "自然物同步温度批次、邻区复用、热源刷新、维度环绕与嵌套释放")
