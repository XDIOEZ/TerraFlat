-- 由真实输入物品和正式配方产出产品，覆盖研磨、锯木、织布与锻压。
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
