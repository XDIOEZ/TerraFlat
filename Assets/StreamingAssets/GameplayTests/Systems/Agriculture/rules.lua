test.system("agriculture", "soil", {}, function(data)
    assert(data.fullWater == 100 and data.fullFertility == 1, "补充水肥超出耕地上限")
    assert(data.sourceWaterAfterClone == 100 and data.sourceFertilityAfterClone == 1,
        "副本水肥消耗污染原始耕地")
    assert(data.emptyWater == 0 and data.emptyFertility == 0 and data.cloneWater == 0 and data.cloneFertility == 0,
        "超额消耗水肥未保持下限")
    for _, key in ipairs({"negativeAddWater", "negativeConsumeWater", "negativeAddFertility", "negativeConsumeFertility"}) do
        assert(data[key] == "ArgumentOutOfRangeException", "水肥负数输入未拒绝：" .. key)
    end
end, "耕地水肥上下限、消耗与副本隔离")

for _, row in ipairs({
    {label = "正常补水", water = 20, supply = 5, maximum = 100, expected = 25, changed = true},
    {label = "超量截断", water = 98, supply = 5, maximum = 100, expected = 100, changed = true},
    {label = "满水跳过", water = 100, supply = 5, maximum = 100, expected = 100, changed = false},
    {label = "浮点增量太小", water = 100, supply = 0.0000000001, maximum = 1000, expected = 100, changed = false},
    {label = "零容量保底", water = 0, supply = 1, maximum = 0, expected = 0.01, changed = true},
    {label = "负容量保底", water = 0, supply = 1, maximum = -10, expected = 0.01, changed = true},
    {label = "零补水拒绝", water = 20, supply = 0, maximum = 100, expected = 20, changed = false},
    {label = "负补水拒绝", water = 20, supply = -5, maximum = 100, expected = 20, changed = false},
    {label = "NaN补水拒绝", water = 20, supplyKind = "nan", maximum = 100, expected = 20, changed = false},
    {label = "正无穷补水拒绝", water = 20, supplyKind = "positive_infinity", maximum = 100, expected = 20, changed = false},
    {label = "负无穷补水拒绝", water = 20, supplyKind = "negative_infinity", maximum = 100, expected = 20, changed = false}
}) do
    local input = row
    test.system("agriculture", "surface_water_rule", input, function(data)
        assert(data.changed == input.changed, input.label .. "的真实增加判断错误")
        assert(type(data.nextWater) == "number" and data.nextWater == data.nextWater
            and math.abs(data.nextWater - input.expected) <= 0.00001, input.label .. "的正式补水结果错误")
        if not input.changed then
            assert(data.nextWater == data.water, input.label .. "在拒绝或不变时改变了输出水量")
        end
    end, "单格与批量共用补水规则：" .. input.label)
end
