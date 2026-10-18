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
        assert(furnace.coldWoodLeft == 3 and math.abs(furnace.coldWoodTemperature - 109) < .0001,
            "炉温1000℃时错误跳过木头自身升温：" .. furnace.id)
        assert(furnace.hotCharcoalLeft == 0 and furnace.ash == 1,
            "产出槽木炭达到1000℃后没有继续受热变灰：" .. furnace.id)
    end
    assert(seen.Smelter and seen.BlastFurnace and seen.Bonfire, "正式熔炉、高炉或篝火没有进入全炉型回归")
end, "全部正式炉型只供热、材料自身温度与产出槽继续受热")

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
