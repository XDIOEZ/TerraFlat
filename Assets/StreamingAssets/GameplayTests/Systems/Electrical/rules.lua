local function near(a, b, message)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= 0.00001, message)
end

local function finite(value, message)
    assert(type(value) == "number" and value == value and math.abs(value) < math.huge, message)
end

local function finite_sample(sample)
    for _, key in ipairs({ "generatedWatts", "deliveredWatts", "demandWatts", "chargingWatts",
        "dischargingWatts", "wastedWatts", "currentAmps", "suppliedWatts", "powerRatio", "batteryJoules" }) do
        finite(sample[key], "无限电源读数不是有限数字：" .. key)
    end
    for _, output in ipairs(sample.sourceWatts) do finite(output, "无限电源输出出现无穷数") end
    for _, energy in ipairs(sample.sourceJoules) do near(energy, 0, "无限电源开始储存或消耗电量") end
end

test.system("electrical", "battery_commit", {}, function(data)
    near(data.previewEnergy, 100, "反复探算扣除了真实电池储能")
    assert(data.previewNotifications == 0, "电池探算发布了储能变化")
    near(data.committedEnergy, 95, "一次0.1秒50瓦负载没有只扣5焦耳")
    assert(data.notifications == 1, "正式储能结算没有恰好通知一次")
    near(data.deliveredWatts, 50, "电池实际交付功率错误")
    near(data.suppliedWatts, 50, "用电器没有收到实际供电")
end, "电池探算隔离与单次功率结算")

test.system("electrical", "supply_limits", {}, function(data)
    near(data.limited.delivered, 50, "供给不足时产生额外功率")
    near(data.limited.demand, 100, "用电需求读数错误")
    near(data.limited.ratio, 0.5, "缺电没有按真实功率折减")
    near(data.limited.current, 50 / 120, "电流没有从实际功率和电压派生")
    assert(data.voltageConflict, "两个不兼容电压源没有冲突")
    near(data.conflictDelivered, 0, "电压冲突后电网仍交付功率")
    near(data.conflictSupplied, 0, "电压冲突后保留了旧供电")
end, "电网功率上限、电流与电压冲突")

test.system("electrical", "unlimited_supply", { steps = 32, seconds = 1, battery = true, batteryJoules = 1000 }, function(data)
    assert(#data.samples == 32, "没有采样连续大负载供电")
    assert(data.generatorValidation == "", "无限发电节点不能使用零额定功率")
    assert(data.consumerValidation == "ArgumentException", "普通用电器错误获得了无限发电能力")
    for _, sample in ipairs(data.samples) do
        finite_sample(sample)
        near(sample.generatedWatts, 12000, "无限电源仍被普通电池2400瓦上限限制")
        near(sample.suppliedWatts, 12000, "连续大负载没有得到完整供电")
        near(sample.deliveredWatts, 12000, "无限电源实际交付读数错误")
        near(sample.powerRatio, 1, "连续大负载仍然缺电")
        near(sample.dischargingWatts, 0, "无限电源供电仍消耗旁边电池")
        near(sample.batteryJoules, 1000, "连续供电消耗了普通电池储能")
        near(sample.sourceWatts[1], 12000, "无限电源单节点输出读数错误")
        near(sample.currentAmps, 100, "无限电源实际电流错误")
        assert(sample.notifications == 0, "没有储能变化仍发出了电池通知")
    end
end, "创造电源连续32秒完整供应12000瓦且不耗储能")

test.system("electrical", "unlimited_supply", { demandFactor = 0, steps = 3 }, function(data)
    for _, sample in ipairs(data.samples) do
        finite_sample(sample)
        near(sample.generatedWatts, 0, "闲置无限电源仍凭空发电")
        near(sample.sourceWatts[1], 0, "闲置无限电源的节点读数没有归零")
        near(sample.currentAmps, 0, "闲置无限电源仍显示有电流")
        near(sample.wastedWatts, 0, "闲置无限电源产生了无限余电")
    end
end, "创造电源闲置时输出和电流均为有限零值")

test.system("electrical", "unlimited_supply", { battery = true, steps = 3 }, function(data)
    for index, sample in ipairs(data.samples) do
        finite_sample(sample)
        near(sample.suppliedWatts, 12000, "给普通电池充电挤占了负载供电")
        near(sample.generatedWatts, 12100, "无限电源没有只补负载和真实充电需求")
        near(sample.chargingWatts, 100, "普通电池充电越过了100瓦限制")
        near(sample.batteryJoules, 250 + index * 50, "普通电池没有按实际功率充电")
        near(sample.wastedWatts, 0, "按需发电留下了多余功率")
        assert(sample.notifications == index, "每步充电没有只通知一次")
    end
end, "创造电源供负载的同时按普通电池上限充电")

test.system("electrical", "unlimited_supply", { battery = true, steps = 3, commitStorage = false }, function(data)
    for _, sample in ipairs(data.samples) do
        finite_sample(sample)
        near(sample.generatedWatts, 12100, "充电探算没有取得真实按需出力")
        near(sample.chargingWatts, 100, "充电探算丢失了实际充电读数")
        near(sample.batteryJoules, 250, "反复探算写入了普通电池储能")
        assert(sample.notifications == 0, "无限电源探算发布了储能变化")
    end
end, "创造电源反复探算充电不写储能也不发通知")

test.system("electrical", "unlimited_supply", { sources = 2, battery = true, batteryJoules = 900, steps = 3 }, function(data)
    for index, sample in ipairs(data.samples) do
        finite_sample(sample)
        local charging = index <= 2 and 100 or 0
        near(sample.generatedWatts, 12000 + charging, "多个无限电源重复补足了同一功率缺口")
        near(sample.sourceWatts[1], (12000 + charging) / 2, "第一个无限电源没有分摊缺口")
        near(sample.sourceWatts[2], (12000 + charging) / 2, "第二个无限电源没有分摊缺口")
        near(sample.chargingWatts, charging, "普通电池满电后仍继续充电")
        near(sample.batteryJoules, math.min(900 + index * 50, 1000), "多源充电超过了普通电池容量")
        near(sample.wastedWatts, 0, "多无限源产生了多余功率")
    end
end, "多个创造电源分摊出力且电池充满后停止补充电功率")

local blocked_cases = {
    { args = { connected = false }, label = "创造电源断线后不能跨电网供电" },
    { args = { providerFactor = 0 }, label = "创造电源停用后不能供电" },
    { args = { sourceConnection = "right", sourceRotation = 1 }, label = "创造电源转错方向后不能越过端口供电" },
    { args = { sources = 2, secondSourceVoltage = 24 }, conflict = true, label = "创造电源仍遵守电压源冲突规则" },
    { args = { consumerVoltage = 24 }, incompatible = true, label = "创造电源不能给电压不兼容设备供电" },
    { args = { wireMaximumVoltage = 24 }, incompatible = true, label = "创造电源不能越过电线耐压限制" }
}
for _, case in ipairs(blocked_cases) do
    local expected_conflict = case.conflict == true
    local expected_incompatible = case.incompatible == true
    test.system("electrical", "unlimited_supply", case.args, function(data)
        local sample = data.samples[1]
        finite_sample(sample)
        near(sample.generatedWatts, 0, "拒绝供电后仍产生了无限出力")
        near(sample.suppliedWatts, 0, "拒绝供电后设备仍收到功率")
        near(sample.deliveredWatts, 0, "拒绝供电后电网仍有交付")
        for _, output in ipairs(sample.sourceWatts) do near(output, 0, "未连接或停用的创造电源仍有输出") end
        assert(sample.voltageConflict == expected_conflict, "无限电源电压冲突标记错误")
        assert(sample.voltageIncompatible == expected_incompatible, "无限电源电压不兼容标记错误")
    end, case.label)
end

test.system("electrical", "unlimited_supply", { sourceConnection = "right" }, function(data)
    local sample = data.samples[1]
    finite_sample(sample)
    near(sample.suppliedWatts, 12000, "创造电源正确朝向邻格电线后没有供电")
end, "创造电源通过正式方向端口接入邻格电线")
