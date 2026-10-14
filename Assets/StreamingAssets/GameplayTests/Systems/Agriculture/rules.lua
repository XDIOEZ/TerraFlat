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
