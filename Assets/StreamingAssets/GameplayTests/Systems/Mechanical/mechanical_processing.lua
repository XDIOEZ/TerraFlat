-- 材料自身决定热转化，机械加工继续使用对应的正式加工能力。
local function total(slots, id)
    local amount = 0
    for _, item in ipairs(slots) do
        if item.id == id then amount = amount + item.amount end
    end
    return amount
end

for _, input in ipairs({
    { amount = 1, charcoal = 0, ash = 1 },
    { amount = 2, charcoal = 0, ash = 2 },
    { amount = 3, charcoal = 1, ash = 0 },
    { amount = 4, charcoal = 1, ash = 1 },
    { amount = 6, charcoal = 2, ash = 0 },
    { amount = 7, charcoal = 2, ash = 1 },
}) do
    test.runtime("processing", "material_transition", { amounts = { input.amount }, temperature = 500 }, function(data)
        assert(#data.input == 0, "500℃木头没有完整消费同一堆材料")
        assert(total(data.output, "Charcoal") == input.charcoal and total(data.output, "WoodAsh") == input.ash,
            "500℃木头未按每三根一炭、余下一至两根每根一灰结算")
        for _, item in ipairs(data.output) do
            assert(item.initialized and item.temperature == 500, "热转化产物丢失材料自身温度")
        end
    end, string.format("木头500℃同堆分组：%d根→%d炭+%d灰", input.amount, input.charcoal, input.ash))
end

test.runtime("processing", "material_transition", { amounts = { 1, 2 }, temperature = 500 }, function(data)
    assert(#data.input == 0 and total(data.output, "WoodAsh") == 3 and total(data.output, "Charcoal") == 0,
        "不同槽位的一根和两根木头被跨槽凑成木炭")
end, "同炉不同槽位不跨堆凑三根木头")

for _, input in ipairs({
    { id = "Log", amount = 3, temperature = 499.99, expected = "Log", expectedAmount = 3 },
    { id = "Plank", amount = 3, temperature = 500, expected = "Charcoal", expectedAmount = 1 },
    { id = "Log", amount = 3, temperature = 999.99, expected = "Charcoal", expectedAmount = 1 },
    { id = "Log", amount = 3, temperature = 1000, expected = "WoodAsh", expectedAmount = 1 },
    { id = "Charcoal", amount = 2, temperature = 999.99, expected = "Charcoal", expectedAmount = 2 },
    { id = "Charcoal", amount = 2, temperature = 1000, expected = "WoodAsh", expectedAmount = 2 },
}) do
    test.runtime("processing", "material_transition", input, function(data)
        local untouched = input.expected == input.id
        assert(total(untouched and data.input or data.output, input.expected) == input.expectedAmount,
            "材料自身温度边界、木制品特质继承或木炭1000℃变灰错误")
        assert(#(untouched and data.output or data.input) == 0, "热转化重复保留原料或提前生成产物")
        for _, item in ipairs(data.output) do
            assert(item.initialized and math.abs(item.temperature - input.temperature) < .001,
                "链式热转化丢失实际材料温度")
        end
    end, string.format("%s自身温度边界：%g℃", input.id, input.temperature))
end

test.runtime("processing", "material_transition_atomic", {}, function(data)
    assert(not data.blockedSuccess and data.sameSource and #data.blockedOutput == 0,
        "单空槽放不下炭灰两种产物时部分提交或更换了来源")
    assert(total(data.blockedInput, "Log") == 4, "多产物输出空间不足仍扣除了木头")
    assert(data.resumedSuccess and #data.input == 0 and total(data.output, "Charcoal") == 1
        and total(data.output, "WoodAsh") == 1, "空出第二槽后未原子恢复一炭一灰")
    assert(not data.duplicateSuccess, "已经消费的木头再次提交产生重复产物")
    for _, item in ipairs(data.output) do
        assert(item.temperature == 500 and item.initialized, "库存事务丢失产物温度")
    end
end, "木头多产物空间不足整笔拒绝、恢复及重复提交拒绝")

test.runtime("processing", "meat_heat_and_dry", {}, function(data)
    assert(data.heatedOutput == "Meat_Cooked" and data.heatedAmount == 2
        and data.heatedSeconds > 0 and data.heatedSeconds <= 180 and data.heatedTemperature >= 200,
        "地面生肉受篝火炉心热量后未转成等量熟肉")
    assert(data.driedOutput == "Meat_Dehydrate" and data.driedAmount == 2
        and data.driedSeconds > 0 and data.driedSeconds <= 240 and data.driedTemperature == 20,
        "低温晾架没有把等量生肉变为肉干")
    assert(data.lowTemperature == "Meat_Dehydrate" and data.cookingBoundary == "Meat_Cooked"
        and data.veryHot == "Meat_Cooked" and data.freshUnchanged,
        "200℃临界值、过热保护或低温干燥优先级不正确")
    assert(#data.furnaceInput == 0 and #data.furnaceOutput == 1
        and data.furnaceOutput[1].id == "Meat_Cooked" and data.furnaceOutput[1].amount == 1,
        "生肉改为物质热转化后炉内烤肉生产链断开")
end, "生肉篝火烤制优先、晾架风干、200℃分界及炉内兼容")

for _, input in ipairs({ { temperature = 999.99, ingots = 0 }, { temperature = 1000, ingots = 1 }, { temperature = 1000, amount = 8, ingots = 4 } }) do
    test.runtime("processing", "material_reaction", input, function(data)
        assert(total(data.output, "Ingot_Copper") == input.ingots,
            "铜矿没有按材料自身1000℃的反应特质产铜")
        assert(total(data.input, "Ore_Copper") == (input.ingots == 0 and 2 or 0),
            "铜矿反应温度不足仍扣料，或反应后残留原料")
        for _, item in ipairs(data.output) do
            assert(item.temperature == input.temperature, "铜矿反应产物未继承消耗材料温度")
        end
    end, string.format("铜矿自身温度和整堆反应：%g℃，%d份", input.temperature, input.amount or 2))
end

test.runtime("processing", "material_reaction", { temperature = 1000, mixed = true }, function(data)
    assert(total(data.input, "Ore_Copper") == 2 and #data.input == 1
        and data.input[1].temperature < 1000, "铜矿反应错误消耗了未达温度的同类材料")
    assert(total(data.output, "Ingot_Copper") == 1 and data.output[1].temperature == 1000,
        "混合冷热同类材料没有只消费达温的一堆并保留产物温度")
end, "不同堆同种矿石逐堆核验自身温度")

for _, sourceId in ipairs({ "Ore_Iron", "Ingot_RawIron", "Ingot_WroughtIron" }) do
    for _, sample in ipairs({
        { amount = 2, coal = 2, temperature = 1489, steel = 1 },
        { amount = 4, coal = 4, temperature = 1489, steel = 2 },
        { amount = 2, coal = 2, temperature = 1488.99, steel = 0 },
        { amount = 2, coal = 2, temperature = 1489, coalTemperature = 1488.99, steel = 0 },
        { amount = 2, coal = 1, temperature = 1489, steel = 0 },
    }) do
        test.runtime("processing", "material_reaction", {
            materials = {
                { id = sourceId, amount = sample.amount, temperature = sample.temperature },
                { id = "Ore_Coal", amount = sample.coal, temperature = sample.coalTemperature or sample.temperature },
            },
        }, function(data)
            assert(total(data.output, "Ingot_Steel") == sample.steel,
                "炼钢路线、二铁二煤比例或材料自身1489℃门槛错误：" .. sourceId)
            assert(total(data.input, "Ore_Coal") == sample.coal - sample.steel * 2,
                "炼钢没有按实际产出消费煤矿石：" .. sourceId)
            if sample.steel > 0 then
                assert(#data.input == 0 and #data.output == 1,
                    "炼钢未优先于单料炼铁，或材料没有完整消费：" .. sourceId)
                assert(data.output[1].initialized and data.output[1].temperature == sample.temperature,
                    "钢锭没有继承参与反应材料的温度")
            end
        end, string.format("%s炼钢：%d铁+%d煤，铁%g℃/煤%g℃", sourceId, sample.amount,
            sample.coal, sample.temperature, sample.coalTemperature or sample.temperature))
    end
end

test.runtime("processing", "material_alloy_atomic", {}, function(data)
    assert(total(data.blockedInput, "Ore_Copper") == 9 and total(data.blockedInput, "Ore_Tin") == 1,
        "青铜输出受阻时降级执行纯铜反应，错误消耗合金原料")
    assert(total(data.blockedOutput, "Ingot_Copper") == 1 and #data.blockedOutput == 1,
        "合金输出受阻后仍向原有铜锭槽塞入低阶产物")
    assert(#data.input == 0 and total(data.output, "Ingot_Bronze") == 5,
        "空出输出后没有优先按九铜一锡反应成五青铜")
end, "合金优先、满输出禁止降级吞料、恢复后正式反应")

test.runtime("processing", "furnace_heat_only", {}, function(data)
    assert(#data.furnaces > 0, "没有从正式目录发现任何炉体")
    local seen = {}
    for _, furnace in ipairs(data.furnaces) do
        seen[furnace.id] = true
        assert(furnace.processorAbsent, "炉体仍创建配方加工器：" .. furnace.id)
        assert(furnace.woodLeft == 0 and furnace.charcoal == 1, "炉体500℃未调用材料自身转化：" .. furnace.id)
        local expectedCold = 1000 - 900 * .99 ^ (furnace.heatingRatePercent / 100)
        assert(furnace.coldWoodLeft == 3 and math.abs(furnace.coldWoodTemperature - expectedCold) < .0001,
            "炉温1000℃时错误跳过木头自身升温：" .. furnace.id)
        assert(furnace.hotCharcoalLeft == 0 and furnace.ash == 1,
            "产出槽木炭达到1000℃后没有继续受热变灰：" .. furnace.id)
        assert(furnace.panel.inputBound and furnace.panel.outputBound
            and furnace.panel.visibleInputs == furnace.inputSlots and furnace.panel.visibleOutputs == furnace.outputSlots,
            "炉体显示假槽或UI没有绑定真实库存：" .. furnace.id)
        if furnace.id == "Smelter" then
            assert(furnace.heatingRatePercent == 50 and furnace.inputSlots == 2 and furnace.temperatureUpSpeed == 10,
                "普通熔炉应为50%材料升温、2输入，炉体仍为10℃/秒")
        elseif furnace.id == "BlastFurnace" then
            assert(furnace.heatingRatePercent == 100 and furnace.inputSlots == 3 and furnace.temperatureUpSpeed == 10,
                "高炉应为100%材料升温、3输入，炉体仍为10℃/秒")
        end
    end
    assert(seen.Smelter and seen.BlastFurnace and seen.Bonfire, "正式熔炉、高炉或篝火没有进入全炉型回归")
end, "全部正式炉型只供热、材料自身温度与产出槽继续受热")

for _, multiplier in ipairs({ 0, .5, 1, 1.5 }) do
    test.runtime("processing", "material_heating_coefficient", { multiplier = multiplier }, function(data)
        local expected = 1000 - 900 * .99 ^ multiplier
        assert(math.abs(data.input - expected) < .0001 and math.abs(data.output - expected) < .0001
            and math.abs(data.crucibleMaterial - expected) < .0001,
            "输入、产出与坩埚内部固体未共用同一升温倍率")
        assert(math.abs(data.bucket - (20 + 10 * multiplier)) < .0001
            and math.abs(data.crucibleLiquid - data.bucket) < .0001,
            "普通容器与坩埚内液体换热倍率不一致")
        assert(math.abs(data.freshTemperature - (1000 - 965 * .99 ^ multiplier)) < .0001
            and data.freshAmount == 3 and data.freshOutputCount == 0,
            "新投材料被初始化为炉温而瞬间变炭灰，或覆盖了35℃真实环境初温")
        assert(math.abs(data.freshCrucibleMaterial - (1000 - 980 * .99 ^ multiplier)) < .0001,
            "坩埚新材料没有采用加热前的20℃容器内温度")
        assert(math.abs(data.liquidProcessingSeconds - 1) < .0001,
            "升温倍率加快了海水加工计时")
        assert(math.abs(data.leafMoisture - .798725) < .00001,
            "升温倍率错误缩放了相同温度下的一秒蒸发时间")
    end, string.format("共享供热%g倍：全部载荷、新材料初温与非温度计时", multiplier))
end

for _, input in ipairs({ { watts = 0, expected = 20 }, { watts = 600, expected = 25 },
    { watts = 1200, expected = 30 }, { watts = 2400, expected = 30 },
    { target = 1000, watts = 600, expected = 30 }, { target = 3500, watts = 1050, expected = 25 },
    { target = 3500, watts = 2100, expected = 30 } }) do
    test.runtime("processing", "induction_power", input, function(data)
        assert(data.inputSlots == 3 and data.outputSlots == 2 and data.inventoryCount == 2 and data.processorAbsent,
            "电磁炉不是3输入2输出、仍有燃料库存或配方加工器")
        local target = input.target or 2000
        local requested = target * .6
        assert(data.voltage == 120 and data.ratedWatts == 2100 and data.maximum == 3500
            and data.target == target and math.abs(data.requestedWatts - requested) < .001
            and data.temperatureUpSpeed == 10 and data.heatingRatePercent == 150,
            "电磁炉额定电力、炉温上限、10℃/秒炉体升温或150%材料换热配置错误")
        assert(data.zero == data.before and math.abs(data.after - input.expected) < .0001
            and math.abs(data.supplied - math.min(input.watts, requested)) < .001, "电磁炉供电比例/零时间/功率上限错误")
        assert(math.abs(data.material - .75) < .0001,
            "电磁炉材料150%被供电比例再次降额，或停电后没有利用炉体余热")
        assert(data.longStepMaximum == target and data.coasting < target and data.coasting > 20,
            "电磁炉超过设定目标温度，或断电后继续主动升温/丢失余热")
        assert(data.panel.inputBound and data.panel.outputBound and data.panel.visibleInputs == 3
            and data.panel.visibleOutputs == 2 and data.panel.visibleFuelSlots == 0 and not data.panel.ignitionVisible,
            "电磁炉正式面板槽位数量与真实绑定错误，或残留燃料/点火控件")
    end, string.format("电磁炉供电边界与余热：%dW", input.watts))
end

for _, sample in ipairs({{target = 1000, watts = 600}, {target = 2000, watts = 1200},
    {target = 3500, watts = 2100}}) do
    test.runtime("processing", "induction_grid", sample, function(data)
        assert(data.sameNetwork and math.abs(data.poweredWatts - sample.watts) < .01
            and math.abs(data.batteryConsumedJoules - sample.watts) < .05,
            "真实电池和电线没有经正式电网按目标温度交付和扣除真实电量")
        assert(data.poweredTemperature == 30 and data.emptyWatts == 0 and data.coastingTemperature < 30,
            "电池耗尽后电磁炉仍主动升温，或低目标满供电没有保持10℃/秒")
    end, string.format("真实电网随目标%g℃请求%gW并准确扣电", sample.target, sample.watts))
end

test.runtime("processing", "induction_target", {}, function(data)
    assert(data.default == 2000 and data.minimumAccepted and data.maximumAccepted,
        "电磁炉默认目标或20~3500℃边界命令错误")
    assert(#data.invalid == 6, "没有检查全部非法温度输入")
    for _, sample in ipairs(data.invalid) do
        assert(not sample.accepted and sample.target == 3500,
            "非法温度命令被接受或篡改了原目标：" .. sample.value)
    end
    assert(#data.receipts == 2 and data.acceptedToken > 0 and data.rejectedToken > 0
        and data.acceptedToken ~= data.rejectedToken, "真实机器命令缺少独立token回执")
    for index, receipt in ipairs(data.receipts) do
        assert(receipt.entityId == data.entityId and receipt.operation == "set-target-temperature"
            and receipt.confirmed and receipt.accepted == (index == 1)
            and receipt.token == (index == 1 and data.acceptedToken or data.rejectedToken)
            and receipt.argument == (index == 1 and "1500" or "NaN"),
            "机器目标温度回执的token、设备、参数或接受结果不属于本次真实命令")
        if index == 2 then assert(#receipt.reason > 0, "真实拒绝回执丢失原因") end
    end
    assert(data.lowered and data.hot == 3500 and data.afterCommand == data.hot
        and data.cooling < data.hot and data.cooling > 1000,
        "调低目标温度瞬间抹除余热，或继续主动升温")
    assert(data.savedTarget == 1000 and data.remoteApplied and data.remoteTarget == 1000
        and math.abs(data.requestedWatts - 600) < .001,
        "目标温度未进入正式机器存档或远程快照恢复")
    assert(data.disconnected < data.cooling and data.disconnected > 20,
        "断电后丢失余热或没有自然冷却")
end, "电磁炉目标范围、非法值拒绝、降低目标余热与正式存档恢复")

for _, material in ipairs({{id = "Ore_Tungsten", output = "Ingot_Tungsten"},
    {id = "Ore_Copper", output = "Ingot_Copper"}, {id = "Ore_Gold", output = "Ingot_Gold"}}) do
    for _, segments in ipairs({1, 4000}) do
        test.runtime("processing", "material_heating_window", {
            materials = {{id = material.id, amount = 2, temperature = 20}}, segments = segments
        }, function(data)
            assert(#data.input == 0 and total(data.output, material.output) == 1,
                "3500℃热源长步或小步跳过了矿物原有反应温窗：" .. material.id)
        end, string.format("%s在3500℃炉内分%d段经过自身熔炼温区", material.id, segments))
    end
end

test.runtime("processing", "material_heating_window", {full = true, materials = {
    {id = "Ore_Gold", amount = 2, temperature = 20},
    {id = "Ore_Stone", amount = 1, temperature = 20}
}}, function(data)
    assert(total(data.input, "Ore_Gold") == 2 and total(data.output, "StoneSlab") == 1,
        "金锭输出受阻时扣除了跨温窗的原料")
    for _, item in ipairs(data.input) do
        if item.id == "Ore_Gold" then
            assert(item.temperature >= 1064 and item.temperature <= 2000,
                "金矿跨过有效温窗后仍继续升温，永久跳过熔炼")
        end
    end
    assert(total(data.resumedInput, "Ore_Gold") == 0 and total(data.resumedOutput, "Ingot_Gold") == 1,
        "跨温窗等待的金矿保存恢复后没有在释放空间时继续熔炼")
end, "金矿升温到原温窗后满输出等待与保存恢复，不丢失反应资格")

test.runtime("processing", "material_heating_window", {full = true, materials = {
    {id = "Ingot_RawIron", amount = 2, temperature = 1489},
    {id = "Ore_Coal", amount = 2, temperature = 1489},
    {id = "Ore_Stone", amount = 1, temperature = 20}
}}, function(data)
    assert(total(data.input, "Ingot_RawIron") == 2 and total(data.input, "Ore_Coal") == 2
        and total(data.output, "StoneSlab") == 1,
        "钢锭输出被占满时扣掉原料或降级成其它金属")
    for _, item in ipairs(data.input) do
        if item.id == "Ore_Stone" then
            assert(item.temperature > 3000, "输出拒绝错误冻结了无关材料")
        else
            assert(item.temperature >= 1489 and item.temperature <= 2000,
                "输出拒绝后参与反应材料越过原有效温区")
        end
    end
    assert(total(data.resumedInput, "Ingot_RawIron") == 0 and total(data.resumedInput, "Ore_Coal") == 0
        and total(data.resumedInput, "Ore_Stone") == 1 and total(data.resumedOutput, "Ingot_Steel") == 1,
        "满槽等待的材料经过真实快照恢复后无法按二铁二煤原子产钢")
end, "高目标满输出只等待相关原料、无关料继续升温，保存后释放空间恢复")

for _, row in ipairs({
    {name = "硼与沙子", materials = {{id = "Boron", amount = 1, temperature = 20},
        {id = "Sand", amount = 2, temperature = 20}}, output = "Glass", amount = 2},
    {name = "石英单独制玻璃", materials = {{id = "Quartz", amount = 1, temperature = 20}}, output = "Glass", amount = 1},
    {name = "石英与煤优先还原硅", materials = {{id = "Quartz", amount = 1, temperature = 20},
        {id = "Ore_Coal", amount = 2, temperature = 20}}, output = "SiliconPlate", amount = 1}
}) do
    for _, segments in ipairs({1, 400}) do
        test.runtime("processing", "material_heating_window", {materials = row.materials, segments = segments}, function(data)
            assert(#data.input == 0 and total(data.output, row.output) == row.amount,
                "电子材料从20℃实际升温后没有完成指定反应：" .. row.name)
            if row.output == "SiliconPlate" then
                assert(total(data.output, "Glass") == 0, "石英在煤参与还原前被单料玻璃配方抢先消耗")
            end
        end, string.format("电子材料冷料真实供热%d段：%s", segments, row.name))
    end
    test.runtime("processing", "material_heating_window", {materials = row.materials, full = true}, function(data)
        for _, material in ipairs(row.materials) do
            assert(total(data.input, material.id) == material.amount,
                "电子材料热产物输出已满时仍扣料：" .. row.name)
            assert(total(data.resumedInput, material.id) == 0,
                "电子材料释放输出并保存恢复后无法继续反应：" .. row.name)
        end
        assert(total(data.output, "StoneSlab") == 1
            and total(data.resumedOutput, row.output) == row.amount,
            "电子材料满槽阻塞物被覆盖，或恢复后产物错误：" .. row.name)
    end, "电子材料满输出等待与快照恢复：" .. row.name)
end

for _, sample in ipairs({
    {name = "切割一矿两片", args = {}, resolved = true, success = true},
    {name = "低级切割能力", args = {level = 1}, resolved = true, success = true},
    {name = "研磨不能代替切割", args = {capability = "grind"}, resolved = false, success = false},
    {name = "空原料拒绝", args = {amount = 0}, resolved = false, success = false},
    {name = "满输出不扣矿石", args = {full = true}, resolved = true, success = false}
}) do
    test.runtime("processing", "material_cutting", sample.args, function(data)
        assert(data.resolved == sample.resolved and data.preview == sample.success and data.success == sample.success,
            "云母正式切割能力解析或事务资格错误")
        if sample.success then
            assert(data.work == 4 and data.inputAmount == 1 and #data.input == 0
                and total(data.output, "MicaSheet") == 2, "云母切割没有按一矿两片完成正式事务")
        else
            assert(data.unchanged, "云母拒绝切割仍改变原料或输出")
        end
    end, "云母正式物品加工：" .. sample.name)
end

for _, furnace in ipairs({ { id = "Smelter", capacity = 50 }, { id = "BlastFurnace", capacity = 150 },
    { id = "InductionStove", capacity = 300 } }) do
    for _, material in ipairs({ { id = "Ore_Gold", unit = 3 }, { id = "Plank", unit = 4 } }) do
        test.runtime("processing", "furnace_input_capacity", { id = furnace.id, itemId = material.id }, function(data)
            local expected = math.floor(furnace.capacity / material.unit)
            assert(data.capacityEnabled and not data.unlimitedCarry and not data.unlimitedSlots
                and data.maxWeight == furnace.capacity and data.maxAllowedWeight == furnace.capacity
                and data.maxVolume == furnace.capacity, "炉体输入未启用实际kg/L总容量，或错误借用玩家超重宽限")
            assert(data.firstTransfer and data.nonEmptySlots >= 2 and data.filled == expected
                and data.remaining + data.filled == 500, "不同输入槽没有累计总容量，或超大请求未按实际容量部分接纳")
            assert(not data.extraTransfer and not data.fullGrant and data.recoveredGrant and data.afterGrant == expected,
                "输入已满仍接受转移或制作产物，或释放空间后未恢复正式制作事务")
            assert(data.panel.inputBound and data.panelMaxWeight == furnace.capacity and data.panelMaxVolume == furnace.capacity,
                "重新绑定正式面板后丢失输入容量")
            assert(data.snapshotApplied and data.afterSnapshot == expected and not data.postSnapshotExtraTransfer,
                "远程快照清除了输入容量或突破上限")
            assert(not data.outputCapacityEnabled and not data.fuelCapacityEnabled,
                "只限制输入容量时错误给产出或燃料库存增加了总容量限制")
        end, string.format("%s输入%dkg/%dL跨槽容量：%s", furnace.id, furnace.capacity, furnace.capacity, material.id))
    end
end

test.runtime("processing", "furnace_ambient_heat", {}, function(data)
    assert(#data.samples == 3, "高炉升温、降温和小温差采样不完整")
    for _, sample in ipairs(data.samples) do
        assert(not sample.burning and math.abs(sample.ambient - 20) > 1,
            "停火高炉没有读取真实非20℃环境")
        local difference = sample.ambient - sample.before
        local delta = math.min(math.abs(difference), math.max(math.abs(difference) * .01, .5))
        local expected = sample.before + (difference >= 0 and delta or -delta)
        assert(sample.zero == sample.before and math.abs(sample.after - expected) < .0001,
            "停火高炉仍按固定速度趋近20℃，或零时间推进了炉温")
    end
end, "停火高炉按真实当地环境百分比升降温")

test.gm("mechanical_build")
test.gm("mechanical_start")
test.await_check("mechanical_output:loom:Cloth", 30, "棉花完成织布")
test.await_check("mechanical_output:sawmill:Plank", 30, "木头完成锯切")
test.await_check("mechanical_output:hammer:CopperPlate", 30, "铜锭完成机械锤加工")
test.await_check("mechanical_output:millstone:OrePowder_Copper", 30, "矿石完成机械研磨")
test.await_check("mechanical_pump_delivered", 5, "真实水车驱动气泵并将大气送入储罐")
test.check("mechanical_player_stationary", "机械工厂运行不应推动主角")

for _, sample in ipairs({
    {stage = "extraction", temperature = 99.99, oil = 4, crude = 0},
    {stage = "extraction", temperature = 100, oil = 0, crude = 1},
    {stage = "extraction", temperature = 100, full = true, oil = 4, crude = 0},
    {stage = "distillation", temperature = 350},
    {stage = "cracking", temperature = 750},
    {stage = "polymerization", temperature = 200},
    {stage = "polymerization", temperature = 200, missingCatalyst = true},
    {stage = "fuel", temperature = 20}
}) do
    test.runtime("processing", "petrochemical_crucible", sample, function(data)
        assert(data.processed, "坩埚正式加热入口未执行")
        if sample.stage == "extraction" then
            assert(data.oil == sample.oil and total(data.solids, "CrudeOil") == sample.crude,
                "石油液体提取、温度门槛或满槽回滚错误")
            if sample.crude == 1 then assert(data.solids[1].temperature == 100, "石油原料未继承液体温度") end
        elseif sample.stage == "distillation" then
            assert(total(data.solids, "CrudeOil") == 0 and total(data.solids, "FuelOil") == 2
                and total(data.solids, "Naphtha") == 1 and total(data.solids, "Bitumen") == 1,
                "坩埚石油蒸馏产物不完整")
        elseif sample.stage == "cracking" then
            assert(total(data.solids, "Naphtha") == 0 and total(data.solids, "Ethylene") == 2
                and total(data.solids, "Propylene") == 1, "坩埚石脑油裂解产物错误")
        elseif sample.stage == "polymerization" then
            local produced = sample.missingCatalyst and 0 or 7
            assert(total(data.solids, "Plastic") == produced
                and total(data.solids, "Ethylene") == 7 - produced
                and total(data.solids, "PolymerizationCatalyst") == (sample.missingCatalyst and 0 or 1),
                "坩埚乙烯聚合缺催化剂仍生产或消耗催化剂")
        else
            assert(data.fuel == 4 and total(data.solids, "FuelOil") == 0,
                "燃油物品没有转换为可供发动机的真实一升液体")
        end
    end, "坩埚石油化工真实入口：" .. sample.stage)
end

for _, sample in ipairs({{failures = 1}, {failures = 1, writeThenFail = true}, {failures = 2, writeThenFail = true}}) do
    test.runtime("processing", "petrochemical_persist_rollback", sample, function(data)
        assert(data.failed and data.persistCalls == 2 and data.originalPreserved,
            "液体热加工持久化异常未保留原始错误或未尝试恢复写入")
        assert(total(data.outputAfterFailure, "CrudeOil") == 0 and data.liquidAfterFailure == 4
            and data.savedAfterFailure == 4, "持久化异常没有同时恢复产物库存和石油液体")
        assert(total(data.retryOutput, "CrudeOil") == 1 and data.retryLiquid == 0 and data.retrySaved == 0,
            "异常恢复后的重试丢失物料或重复产出")
    end, "石油热加工持久化故障与双侧回滚")
end
