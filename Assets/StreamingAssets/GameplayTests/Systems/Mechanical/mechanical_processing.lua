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
            assert(item.initialized and item.temperature == input.temperature, "链式热转化丢失实际材料温度")
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
    { watts = 1200, expected = 30 }, { watts = 2400, expected = 30 } }) do
    test.runtime("processing", "induction_power", input, function(data)
        assert(data.inputSlots == 3 and data.outputSlots == 2 and data.inventoryCount == 2 and data.processorAbsent,
            "电磁炉不是3输入2输出、仍有燃料库存或配方加工器")
        assert(data.voltage == 120 and data.ratedWatts == 1200 and data.maximum == 2000
            and data.temperatureUpSpeed == 10 and data.heatingRatePercent == 150,
            "电磁炉额定电力、炉温上限、10℃/秒炉体升温或150%材料换热配置错误")
        assert(data.zero == data.before and math.abs(data.after - input.expected) < .0001
            and data.supplied == math.min(input.watts, 1200), "电磁炉供电比例/零时间/功率上限错误")
        assert(math.abs(data.material - .75) < .0001,
            "电磁炉材料150%被供电比例再次降额，或停电后没有利用炉体余热")
        assert(data.longStepMaximum == 2000 and data.coasting < 2000 and data.coasting > 20,
            "电磁炉超过2000℃，或断电后继续主动升温/丢失余热")
        assert(data.panel.inputBound and data.panel.outputBound and data.panel.visibleInputs == 3
            and data.panel.visibleOutputs == 2 and data.panel.visibleFuelSlots == 0 and not data.panel.ignitionVisible,
            "电磁炉正式面板槽位数量与真实绑定错误，或残留燃料/点火控件")
    end, string.format("电磁炉供电边界与余热：%dW", input.watts))
end

test.runtime("processing", "induction_grid", {}, function(data)
    assert(data.sameNetwork and data.poweredWatts == 1200 and data.batteryConsumedJoules == 1200,
        "真实电池和电线没有经正式电网向电磁炉交付并扣除1200J")
    assert(data.poweredTemperature == 30 and data.emptyWatts == 0 and data.coastingTemperature < 30,
        "电池耗尽后电磁炉仍主动升温，或满供电未按10℃/秒升温")
end, "真实双电池电网供热与断电自然冷却")

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
        local expected = sample.before + (sample.ambient - sample.before) * .01
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
