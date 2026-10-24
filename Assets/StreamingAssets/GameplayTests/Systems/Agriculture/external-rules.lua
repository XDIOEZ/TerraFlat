-- 外部最终成长配置直接进入正式植物 Job，和资源目录的既有检查分别报告。
local cases = {
    {label = "外部关闭成长仍耗水", multiplier = 0, seconds = 1440, progress = 0, water = 8, remainder = 0, usedWater = 2, usedFertility = 0},
    {label = "外部半速成长", multiplier = 0.5, seconds = 1, progress = 0.00005, water = 10, remainder = 2 / 1440, usedWater = 2 / 1440, usedFertility = 0.0001},
    {label = "外部双速成长", multiplier = 2, seconds = 1, progress = 0.0002, water = 10, remainder = 2 / 1440, usedWater = 2 / 1440, usedFertility = 0.0001}
}

for _, row in ipairs(cases) do
    local expected = row
    test.system("agriculture", "plant_growth", {
        growthMultiplier = row.multiplier, seconds = row.seconds, dayLength = 1440,
        water = 10, fertility = 1, light = 1
    }, function(data)
        assert(data.before == 0 and math.abs(data.progress - expected.progress) < 0.0000001,
            expected.label .. "没有消费外部提供的最终成长倍率")
        assert(data.water == expected.water and math.abs(data.waterRemainder - expected.remainder) < 0.00001,
            expected.label .. "改变了独立于成长的水量或耗水余量")
        assert(math.abs(data.usedWater - expected.usedWater) < 0.00001,
            expected.label .. "的实际耗水不符合世界秒时钟")
        assert(math.abs(data.usedFertility - expected.usedFertility) < 0.0000001,
            expected.label .. "的肥力消耗与是否成长不一致")
        assert(data.waterInitialized == 1 and math.abs(data.lastWaterWorldTime - expected.seconds) < 0.00001,
            expected.label .. "没有推进独立耗水时钟")
        assert(data.cultivated == 1 and data.growWild == 1 and data.scaleByStage == 1 and data.harvested == 0
            and data.soilX == 17 and data.soilY == -29 and data.climateStress == data.initialClimateStress,
            expected.label .. "改写了种植身份、地块坐标或气候状态")
    end, "外部成长规则输入：" .. row.label)
end
