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
