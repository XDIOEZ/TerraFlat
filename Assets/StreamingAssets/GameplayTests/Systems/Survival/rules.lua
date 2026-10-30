local function near(actual, expected, label)
    assert(type(actual) == "number" and math.abs(actual - expected) < .0001, label .. "错误")
end
local oxygenCases = {
    {name = "恢复上限与消耗", args = {}, add = 100, remove = 75, ratio = .75, supply = 7},
    {name = "耗尽不成负数", args = {current = 10, add = -20, remove = -1}, add = 0, remove = 0, ratio = 0, supply = 7},
    {name = "零容量氧气", args = {current = 0, maximum = 0}, add = 0, remove = 0, ratio = 0, supply = 7},
    {name = "零供给时间", args = {seconds = 0}, add = 100, remove = 75, ratio = .75, supply = 0}
}
for _, case in ipairs(oxygenCases) do
    test.system("survival", "oxygen", case.args, function(data)
        near(data.afterAdd, case.add, "氧气恢复上限")
        near(data.afterRemove, case.remove, "氧气消耗下限")
        near(data.ratio, case.ratio, "氧气比例")
        near(data.supplyAmount, case.supply, "外部供氧需求")
        assert(data.afterInvalid == data.afterRemove and data.invalidSupply == 0 and data.unloadedSupplyAccepted == 0, "非法输入或未加载供氧改变储备")
    end, "氧气储备：" .. case.name)
end
for _, case in ipairs({{name = "组合装备来源", args = {}, minimum = 20, maximum = 200}, {name = "真空下限钳制", args = {low = 100}, minimum = 0, maximum = 200}}) do
    test.system("survival", "pressure", case.args, function(data)
        near(data.minimum, case.minimum, "组合耐低压")
        near(data.maximum, case.maximum, "组合耐高压")
        assert(data.replacedMinimum == 35 and data.replacedMaximum == 175, "同来源更新被重复叠加")
        assert(data.restoredMinimum == 50 and data.restoredMaximum == 150, "移除耐压来源未恢复基线")
        assert(data.invalidModifierRejected and data.invalidRangeRejected and data.resetBuffer == 30, "气压参数保护或新生命缓冲错误")
    end, "气压安全范围：" .. case.name)
end
local nutritionCases = {
    {name = "碳水优先然后脂肪", args = {}, consumed = true, before = 18, after = 6, carbs = 0, fat = 3, protein = 3},
    {name = "最后才消耗蛋白质", args = {energy = 18}, consumed = true, before = 18, after = 0, carbs = 0, fat = 0, protein = 0},
    {name = "不足不部分扣除", args = {energy = 100}, consumed = false, before = 18, after = 18, carbs = 10, fat = 5, protein = 3},
    {name = "零能量无扣除", args = {energy = 0}, consumed = true, before = 18, after = 18, carbs = 10, fat = 5, protein = 3}
}
for _, case in ipairs(nutritionCases) do
    test.system("survival", "nutrition", case.args, function(data)
        assert(data.originalCarbohydrates == 10 and data.capacity == 10 and data.waterCapacity == 20, "进食覆盖接收者容量或改变原对象")
        assert(data.water == 20 and data.vitamins == 4, "营养恢复超过容量")
        assert(data.consumed == case.consumed and data.beforeEnergy == case.before and data.afterEnergy == case.after, "能量原子消耗错误")
        assert(data.carbohydrates == case.carbs and data.fat == case.fat and data.protein == case.protein, "能量消耗优先级错误")
        assert(not data.excessiveAccepted and data.afterRejected == data.afterEnergy, "失败能量请求扣了部分营养")
    end, "营养与能量：" .. case.name)
end
local spoilageCases = {
    {name = "未到期湿肉不替换", args = {elapsed = 1798}, target = "", elapsed = 1799, advanced = true},
    {name = "到期湿肉变腐肉", args = {moisture = .8}, target = "Meat_Rotten", elapsed = 1800, advanced = true},
    {name = "略高于百分之二十仍腐败", args = {moisture = .200001}, target = "Meat_Rotten", elapsed = 1800, advanced = true},
    {name = "恰好百分之二十变肉干", args = {moisture = .2}, target = "Meat_Dehydrate", elapsed = 1800, advanced = true},
    {name = "低于百分之二十变肉干", args = {moisture = .199999}, target = "Meat_Dehydrate", elapsed = 1800, advanced = true},
    {name = "未初始化读取初始含水量", args = {initialized = false, moisture = 0}, target = "Meat_Rotten", elapsed = 1800, advanced = true},
    {name = "非法含水量不算风干成功", args = {invalidMoisture = true}, target = "Meat_Rotten", elapsed = 1800, advanced = true},
    {name = "未配置干燥分流保留腐败规则", args = {noDrying = true, moisture = .1}, target = "Meat_Rotten", elapsed = 1800, advanced = true},
    {name = "关闭腐败不推进", args = {enabled = false}, target = "", elapsed = 1799, advanced = false, ineligible = true},
    {name = "无效周期不推进", args = {invalidInterval = true}, target = "", elapsed = 1799, advanced = false, ineligible = true},
    {name = "负时间不推进", args = {seconds = -1}, target = "", elapsed = 1799, advanced = false},
    {name = "非法时间不推进", args = {invalidSeconds = true}, target = "", elapsed = 1799, advanced = false},
    {name = "替换前保留到期状态可重试", args = {elapsed = 1800}, target = "Meat_Rotten", elapsed = 1800, advanced = false, beforeReady = true},
    {name = "大步进到期不重置计时", args = {seconds = 30}, target = "Meat_Rotten", elapsed = 1800, advanced = true}
}
for _, case in ipairs(spoilageCases) do
    test.system("survival", "food_spoilage_moisture", case.args, function(data)
        assert(data.eligible == (not case.ineligible) and data.beforeReady == (case.beforeReady == true),
            "腐败资格或到期前状态错误")
        assert(data.advanced == case.advanced and data.elapsed == case.elapsed, "腐败计时推进、钳制或失败重试错误")
        assert(data.ready == (case.target ~= "") and data.target == case.target, "到期含水分流产物错误")
        assert(data.restoredReady == data.ready and data.restoredTarget == data.target and not data.advancedOnZero,
            "读回状态改变到期结果，或零时间推进了腐败")
        near(data.configuredDryThreshold, .2, "到期干燥阈值")
        near(data.matterDryThreshold, data.configuredDryThreshold, "自然风干与到期判断阈值一致")
        assert(data.configuredDryTarget == "Meat_Dehydrate", "生肉缺少肉干分流配置")
        assert(data.worldChanged and data.worldReady and data.worldTarget == "Meat_Rotten" and data.worldElapsed == 1800,
            "世界落地食物未推进同一份腐败时钟或到期湿肉产物错误")
        near(data.worldTemperatureAtExpiry, 20.5, "大步进环境只推进到腐败时刻")
        near(data.worldExpiredTemperature, data.worldTemperatureAtExpiry, "替换等待期间停止额外风干与传热")
    end, "生肉腐败与含水量：" .. case.name)
end

test.system("survival", "food_timer_state", {}, function(data)
    assert(data.converted, "食物计时没有转入强类型状态，或丢失扩展负载")
    near(data.initialSpoilage, 2, "首轮腐败进度")
    near(data.initialMelting, 2, "首轮融化进度")
    near(data.sharedSpoilage, 2, "共享模块跨库存腐败时间去重")
    near(data.sharedMelting, 2, "共享模块跨库存融化时间去重")
    near(data.afterSpoilage, 4, "同一宿主继续腐败")
    near(data.afterMelting, 1, "融化周期后的剩余时间")
    near(data.afterAmount, 4, "融化实际扣减库存数量")
    near(data.restoredSpoilage, 2, "快照恢复腐败进度")
    near(data.restoredMelting, 2, "快照恢复融化进度")
    assert(data.independentClone and data.fingerprintChanged, "食物复制不独立或计时变化未更新快照指纹")
    near(data.disabledSpoilage, 4, "禁用时不推进")
    near(data.resumedSpoilage, 5, "启用后恢复计时")
    assert(data.disabledTimersConverted and data.worldDisabledConverted,
        "关闭的库存或世界食物仍在反复解析字符串，或被错误推进")
    near(data.spoilageAtMelt, 1, "同物品先腐败后融化")
    near(data.firstAmount, 1, "第一格融化事务")
    assert(data.replacementStayed, "事务期间替换的槽位被旧条目覆盖")
    near(data.staleSecondElapsed, 0, "被替换的旧物品未继续计时")
    near(data.retryElapsed, 1.5, "融化事务失败保留已累计时间")
    near(data.retryAmount, 2, "融化事务失败不扣数量")
    assert(data.indexUpdated and data.typedGenericWrite and data.genericStringWrite,
        "观察者替换、重命名或通用接口写入后读取错误")
end, "食物计时强类型状态、顺序事务及快照")

test.system("survival", "food_observer_index", {}, function(data)
    assert(data.registered and data.sortedCount and data.sortedTick and data.sortedState,
        "食物规则 Tick 或状态观察者索引遗漏、重复或改变优先级顺序")
    assert(data.dynamicEligibility and data.lateRegistered and data.lateTick and data.lateState,
        "食物规则动态调度或初始化后新增扩展机制未生效")
    assert(data.gatedPause and data.gatedResume and data.disposed,
        "食物规则休眠、恢复或卸载后观察者引用生命周期不正确")
end, "食物高频规则只遍历观察者：排序、休眠及热添加回归")

local temperatureCases = {
    {name = "有限增温取最强", args = {}, ambient = 25, first = 35, strongest = 50, restored = 30},
    {name = "基础体温超过上限不降温", args = {current = 60, ambient = 60}, ambient = 60, first = 60, strongest = 60, restored = 60},
    {name = "多来源不累加", args = {current = 0, ambient = 0}, ambient = 0, first = 10, strongest = 20, restored = 0},
    {name = "环境温度向上趋近", args = {current = 10, ambient = 30}, ambient = 15, first = 20, strongest = 30, restored = 10}
}
for _, case in ipairs(temperatureCases) do
    test.system("survival", "thermal", case.args, function(data)
        near(data.ambientNext, case.ambient, "环境热传导")
        near(data.splitNext, data.ambientNext, "分帧热传导一致性")
        near(data.firstWarming, case.first, "首来源增温")
        near(data.strongestWarming, case.strongest, "最强来源增温")
        near(data.afterFirstRemoval, case.strongest, "移除未生效来源")
        near(data.restored, case.restored, "恢复基础体温")
        assert(data.invalidSecondsNext == case.restored and data.savedTemperature == case.restored and data.saveIsIndependent, "非法时间或临时增温污染基础快照")
        near(data.tickNatural, data.ambientNext, "角色真实热传导入口与原算法一致")
        near(data.tickWarmed, data.expectedWarmed, "有增温来源时仅对基础体温进行热传导")
        near(data.tickAfterRemoval, data.ambientNext, "移除热源恢复本轮基础体温")
    end, "角色体温：" .. case.name)
end

local temperaturePresentationCases = {
    {name = "正常体温保持原色", temperature = 36.5, coldWarning = 0, heatWarning = 0, coldDanger = 0, heatDanger = 0},
    {name = "低温预警起点", temperature = 8, coldWarning = 0, heatWarning = 0, coldDanger = 0, heatDanger = 0},
    {name = "低温预警四分之一", temperature = 7.25, coldWarning = .15625, heatWarning = 0, coldDanger = 0, heatDanger = 0},
    {name = "低温预警一半", temperature = 6.5, coldWarning = .5, heatWarning = 0, coldDanger = 0, heatDanger = 0},
    {name = "低温预警四分之三", temperature = 5.75, coldWarning = .84375, heatWarning = 0, coldDanger = 0, heatDanger = 0},
    {name = "低温安全线只变蓝不结冰", temperature = 5, coldWarning = 1, heatWarning = 0, coldDanger = 0, heatDanger = 0},
    {name = "低温刚越界无强度跳变", temperature = 4.999, coldWarning = 1, heatWarning = 0, coldDanger = 0, heatDanger = 0},
    {name = "结冰四分之一", temperature = 2.5, coldWarning = 1, heatWarning = 0, coldDanger = .15625, heatDanger = 0},
    {name = "结冰一半", temperature = 0, coldWarning = 1, heatWarning = 0, coldDanger = .5, heatDanger = 0},
    {name = "结冰四分之三", temperature = -2.5, coldWarning = 1, heatWarning = 0, coldDanger = .84375, heatDanger = 0},
    {name = "结冰满强度", temperature = -5, coldWarning = 1, heatWarning = 0, coldDanger = 1, heatDanger = 0},
    {name = "极冷强度钳制", temperature = -100, coldWarning = 1, heatWarning = 0, coldDanger = 1, heatDanger = 0},
    {name = "高温预警起点", temperature = 37, coldWarning = 0, heatWarning = 0, coldDanger = 0, heatDanger = 0},
    {name = "高温预警四分之一", temperature = 37.75, coldWarning = 0, heatWarning = .15625, coldDanger = 0, heatDanger = 0},
    {name = "高温预警一半", temperature = 38.5, coldWarning = 0, heatWarning = .5, coldDanger = 0, heatDanger = 0},
    {name = "高温预警四分之三", temperature = 39.25, coldWarning = 0, heatWarning = .84375, coldDanger = 0, heatDanger = 0},
    {name = "高温安全线只变红无热浪", temperature = 40, coldWarning = 0, heatWarning = 1, coldDanger = 0, heatDanger = 0},
    {name = "高温刚越界无强度跳变", temperature = 40.001, coldWarning = 0, heatWarning = 1, coldDanger = 0, heatDanger = 0},
    {name = "热浪四分之一", temperature = 42.5, coldWarning = 0, heatWarning = 1, coldDanger = 0, heatDanger = .15625},
    {name = "热浪一半", temperature = 45, coldWarning = 0, heatWarning = 1, coldDanger = 0, heatDanger = .5},
    {name = "热浪四分之三", temperature = 47.5, coldWarning = 0, heatWarning = 1, coldDanger = 0, heatDanger = .84375},
    {name = "热浪满强度", temperature = 50, coldWarning = 0, heatWarning = 1, coldDanger = 0, heatDanger = 1},
    {name = "极热强度钳制", temperature = 100, coldWarning = 0, heatWarning = 1, coldDanger = 0, heatDanger = 1}
}
for _, sample in ipairs(temperaturePresentationCases) do
    local input = sample
    test.system("survival", "temperature_presentation", {temperature = input.temperature}, function(data)
        for _, key in ipairs({"coldWarning", "heatWarning", "coldDanger", "heatDanger"}) do
            near(data[key], input[key], "体温表现强度：" .. key)
            assert(data[key] >= 0 and data[key] <= 1, "体温表现强度没有钳制到零至一")
        end
        near(data.signedWarning, data.heatWarning - data.coldWarning, "体温条冷热方向")
        near(data.signedDanger, data.heatDanger - data.coldDanger, "屏幕冷热方向")
        assert(data.coldWarning * data.heatWarning == 0 and data.coldDanger * data.heatDanger == 0,
            "同一体温同时生成冷热预警")
    end, "体温条与屏幕边缘：" .. input.name)
end

for _, sample in ipairs({
    {name = "窄安全区中间不变色", args = {minimum = 10, maximum = 12, temperature = 11}, cold = 0, hot = 0},
    {name = "窄安全区低温渐变缩至四分之一宽度", args = {minimum = 10, maximum = 12, temperature = 10.25}, cold = .5, hot = 0},
    {name = "窄安全区高温渐变缩至四分之一宽度", args = {minimum = 10, maximum = 12, temperature = 11.75}, cold = 0, hot = .5},
    {name = "零宽安全线仍安全", args = {minimum = 10, maximum = 10, temperature = 10}, cold = 0, hot = 0},
    {name = "零宽安全线低温不同时变红", args = {minimum = 10, maximum = 10, temperature = 9}, cold = 1, hot = 0},
    {name = "零宽安全线高温不同时变蓝", args = {minimum = 10, maximum = 10, temperature = 11}, cold = 0, hot = 1}
}) do
    local input = sample
    test.system("survival", "temperature_presentation", input.args, function(data)
        near(data.coldWarning, input.cold, "窄安全区低温预警")
        near(data.heatWarning, input.hot, "窄安全区高温预警")
        assert(data.coldDanger * data.heatDanger == 0, "窄安全区冷热屏幕效果冲突")
    end, "体温表现边界：" .. input.name)
end

for _, sample in ipairs({
    {name = "倒置安全线", args = {minimum = 40, maximum = 5}},
    {name = "非数体温", args = {invalid = "temperature_nan"}},
    {name = "无穷体温", args = {invalid = "temperature_infinity"}},
    {name = "非数低温安全线", args = {invalid = "minimum_nan"}},
    {name = "无穷高温安全线", args = {invalid = "maximum_infinity"}}
}) do
    local input = sample
    test.system("survival", "temperature_presentation", input.args, function(data)
        for _, key in ipairs({"coldWarning", "heatWarning", "coldDanger", "heatDanger", "signedWarning", "signedDanger"}) do
            near(data[key], 0, "非法体温表现输入归零：" .. key)
        end
    end, "体温表现保护：" .. input.name)
end

test.system("survival", "temperature_presentation_range", {}, function(data)
    near(data.initial.minimum, 5, "默认耐寒下限")
    near(data.initial.maximum, 40, "默认耐热上限")
    near(data.initial.cold.coldDanger, .028, "默认安全线下的结冰强度")
    near(data.initial.hot.heatDanger, .352, "默认安全线上的热浪强度")
    near(data.equipped.minimum, -5, "装备耐寒修正")
    near(data.equipped.maximum, 50, "装备耐热修正")
    near(data.equipped.cold.signedWarning, 0, "耐寒装备后体温条归正常色")
    near(data.equipped.cold.signedDanger, 0, "耐寒装备后屏幕结冰目标归零")
    near(data.equipped.hot.signedWarning, 0, "耐热装备后体温条归正常色")
    near(data.equipped.hot.signedDanger, 0, "耐热装备后屏幕热浪目标归零")
    near(data.restored.minimum, 5, "卸下装备后恢复耐寒线")
    near(data.restored.maximum, 40, "卸下装备后恢复耐热线")
    near(data.restored.cold.coldDanger, data.initial.cold.coldDanger, "卸装后结冰目标恢复")
    near(data.restored.hot.heatDanger, data.initial.hot.heatDanger, "卸装后热浪目标恢复")
end, "体温表现读取正式装备安全范围修正与撤销")

local temperatureTransitionCases = {
    {name = "半秒渐入一秒到满", args = {steps = {{target = -1, seconds = .5}, {target = -1, seconds = .5}}}, expected = {-.5, -1}},
    {name = "分帧不改变真实时间进度", args = {steps = {{target = -1}, {target = -1}, {target = -1}, {target = -1}}}, expected = {-.25, -.5, -.75, -1}},
    {name = "恢复安全后平滑退去", args = {current = 1, steps = {{target = 0, seconds = .5}, {target = 0, seconds = .5}}}, expected = {.5, 0}},
    {name = "冷热切换经过零点", args = {current = -1, steps = {{target = 1, seconds = .5}, {target = 1, seconds = .5}, {target = 1, seconds = .5}, {target = 1, seconds = .5}}}, expected = {-.5, 0, .5, 1}},
    {name = "过渡中反向从当前值续接", args = {steps = {{target = 1, seconds = .4}, {target = -1, seconds = .2}}}, expected = {.4, .2}},
    {name = "零负时间不推进", args = {current = .5, steps = {{target = 1, seconds = 0}, {target = -1, seconds = -1}}}, expected = {.5, .5}},
    {name = "长帧不越过目标", args = {steps = {{target = .3, seconds = 10}}}, expected = {.3}},
    {name = "零时长立即落到合法目标", args = {duration = 0, steps = {{target = 3}}}, expected = {1}},
    {name = "强度上下限", args = {current = -5, steps = {{target = -3}, {target = 3, seconds = 5}}}, expected = {-1, 1}},
    {name = "非法旧强度从零恢复", args = {invalid = "current_nan", steps = {{target = 1}}}, expected = {.25}},
    {name = "非法目标平滑退回零", args = {current = 1, invalid = "target_infinity", steps = {{target = 1}}}, expected = {.75}},
    {name = "非法时间不推进", args = {current = .5, invalid = "seconds_nan", steps = {{target = 1}}}, expected = {.5}},
    {name = "非法时长采用立即落点", args = {invalid = "duration_nan", steps = {{target = -1}}}, expected = {-1}}
}
for _, sample in ipairs(temperatureTransitionCases) do
    local input = sample
    test.system("survival", "temperature_presentation_transition", input.args, function(data)
        assert(#data.samples == #input.expected, "体温表现过渡采样缺失")
        for index, expected in ipairs(input.expected) do
            near(data.samples[index], expected, "真实秒体温表现过渡：" .. index)
            assert(data.samples[index] >= -1 and data.samples[index] <= 1, "冷热有符号强度越界")
        end
        near(data.final, input.expected[#input.expected], "体温表现过渡终点")
    end, "体温表现时间渐变：" .. input.name)
end

test.system("survival", "temperature_presentation_color", {}, function(data)
    local expected = {
        normal = {.8, .7, .6, .35}, coldHalf = {.52, .655, .785, .35}, coldFull = {.24, .61, .97, .35},
        heatHalf = {.9, .5, .4, .35}, heatFull = {1, .3, .2, .35}
    }
    for name, channels in pairs(expected) do
        for index, value in ipairs(channels) do near(data[name][index], value, "体温条颜色：" .. name .. "/" .. index) end
    end
    assert(data.coldFull[3] > data.coldFull[1] and data.heatFull[1] > data.heatFull[3], "低温没有变蓝或高温没有变红")
    for index = 1, 4 do
        near(data.coldClamped[index], data.coldFull[index], "过冷颜色封顶")
        near(data.heatClamped[index], data.heatFull[index], "过热颜色封顶")
        near(data.invalid[index], data.normal[index], "非法颜色强度恢复原主题色")
    end
end, "体温条主题正常色、蓝红渐变、透明度保留和强度钳制")

local treatmentCases = {
    {name = "等待完成仅提交一次", args = {}, began = true, first = "Running", state = "Completed", commits = 1},
    {name = "主动取消不提交", args = {cancel = true}, began = true, first = "Running", state = "Cancelled", commits = 0},
    {name = "条件失效撤销", args = {invalidate = true}, began = true, first = "Running", state = "Cancelled", commits = 0},
    {name = "库存提交失败", args = {commit = false}, began = true, first = "Running", state = "Failed", commits = 1},
    {name = "初始条件拒绝", args = {valid = false}, began = false, first = "Ready", state = "Cancelled", commits = 0},
    {name = "零等待立即完成", args = {duration = 0, first = 0, second = 0}, began = true, first = "Completed", state = "Completed", commits = 1}
}
for _, case in ipairs(treatmentCases) do
    test.system("survival", "treatment", case.args, function(data)
        assert(data.began == case.began and data.firstState == case.first and data.state == case.state, "治疗会话状态错误")
        assert(data.commits == case.commits and not data.earlyComplete and not data.duplicateComplete, "治疗提前或重复提交")
        if case.first == "Running" then near(data.firstProgress, .5, "治疗等待进度") end
    end, "治疗会话：" .. case.name)
end
