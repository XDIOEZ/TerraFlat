local function near(a, b, message, tolerance)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= (tolerance or 0.0001), message)
end

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
