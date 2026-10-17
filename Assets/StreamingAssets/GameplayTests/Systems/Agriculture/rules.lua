test.system("agriculture", "soil", {}, function(data)
    assert(data.fullWater == 10 and data.fullFertility == 1, "补充水肥超出耕地十级上限")
    assert(data.sourceWaterAfterClone == 10 and data.sourceFertilityAfterClone == 1,
        "副本水肥消耗污染原始耕地")
    assert(data.emptyWater == 0 and data.emptyFertility == 0 and data.cloneWater == 0 and data.cloneFertility == 0,
        "超额消耗水肥未保持下限")
    for _, key in ipairs({"negativeAddWater", "negativeConsumeWater", "negativeAddFertility", "negativeConsumeFertility"}) do
        assert(data[key] == "ArgumentOutOfRangeException", "水肥负数输入未拒绝：" .. key)
    end
    assert(data.waterAfterPartialSupply == 2 and data.partialSupply == 0.25 and data.waterAfterWholeSupply == 3,
        "连续补水没有累计满一份后才增加一级")
    assert(data.waterAfterPartialConsumption == 3 and data.partialConsumption == 0.25
        and data.waterAfterWholeConsumption == 2, "连续耗水没有累计满一份后才减少一级")
    assert(data.fullSupplyRemainder == 0 and data.emptyConsumptionRemainder == 0,
        "满水或耗尽后保留了可在后续凭空兑换的进度")
    assert(data.frozenConsumptionBudget == 1.25 and data.emptyConsumptionBudget == 0,
        "作物大时间片的耗水超过冻结预算，会吞掉后续补入的水")
    assert(data.halfWaterMultiplier == 0.5, "五级水分计算成长倍率时发生了整数除法")
end, "耕地水肥上下限、消耗与副本隔离")

for _, row in ipairs({
    {label = "正常补水", water = 2, supply = 1, expected = 3, pending = 0, changed = true},
    {label = "超量截断", water = 9, supply = 5, expected = 10, pending = 0, changed = true},
    {label = "满水跳过", water = 10, supply = 1, expected = 10, pending = 0, changed = false},
    {label = "小量只累计进度", water = 2, supply = 0.25, expected = 2, pending = 0.25, changed = true},
    {label = "累计后增加一级", water = 2, remainder = 0.75, supply = 0.5, expected = 3, pending = 0.25, changed = true},
    {label = "满水丢弃多余进度", water = 9, remainder = 0.75, supply = 0.5, expected = 10, pending = 0, changed = true},
    {label = "浮点增量太小", water = 2, remainder = 0.5, supply = 0.0000000001, expected = 2, pending = 0.5, changed = false},
    {label = "零补水拒绝", water = 2, supply = 0, expected = 2, pending = 0, changed = false},
    {label = "负补水拒绝", water = 2, supply = -1, expected = 2, pending = 0, changed = false},
    {label = "NaN补水拒绝", water = 2, supplyKind = "nan", expected = 2, pending = 0, changed = false},
    {label = "正无穷补水拒绝", water = 2, supplyKind = "positive_infinity", expected = 2, pending = 0, changed = false},
    {label = "负无穷补水拒绝", water = 2, supplyKind = "negative_infinity", expected = 2, pending = 0, changed = false}
}) do
    local input = row
    test.system("agriculture", "surface_water_rule", input, function(data)
        assert(data.changed == input.changed, input.label .. "的真实增加判断错误")
        assert(type(data.nextWater) == "number" and data.nextWater == data.nextWater
            and math.abs(data.nextWater - input.expected) <= 0.00001, input.label .. "的正式补水结果错误")
        assert(data.nextWater == math.floor(data.nextWater) and data.nextWater >= 0 and data.nextWater <= 10,
            input.label .. "产生了小数水分或超出十级范围")
        assert(math.abs(data.nextRemainder - input.pending) <= 0.00001, input.label .. "的连续补水进度错误")
        if not input.changed then
            assert(data.nextWater == data.water, input.label .. "在拒绝或不变时改变了输出水量")
        end
    end, "单格与批量共用补水规则：" .. input.label)
end
