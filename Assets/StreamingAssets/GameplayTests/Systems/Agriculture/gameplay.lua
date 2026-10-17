-- 采样使用附近合格空格执行正式耕作与灌溉，返回前恢复现场，因此只做数据断言。
test.check("player_ready", "农业现场测试玩家就绪")
test.runtime("agriculture", "tillage_irrigation", {}, function(data)
    assert(data.scope == "live-isolated-world", "农业测试没有进入隔离世界")
    assert(data.partialAccepted and not data.partialCompleted and data.partialGroundPreserved,
        "未完成耕作提前改变正式地形")
    assert(data.completionAccepted and data.tillCompleted and data.groundAfter == "Tile_Farmland",
        "完成耕作没有提交正式耕地")
    assert(math.abs(data.afterIrrigation - 30) < 0.0001 and math.abs(data.afterConsumption - 25) < 0.0001,
        "正式土壤灌溉与消耗没有提交到真实区块")
    assert(data.savedSource > 0 and data.savedSource == data.currentSource and data.savedCropAbsent
        and math.abs(data.savedWater - data.afterConsumption) < 0.0001, "农业独立差量与真实水肥不一致")
    assert(data.restored, "农业现场或自有农业差量没有恢复")
end, "真实分段耕作、灌溉提交与农业差量")

-- 资格查询直接使用正式种子模块，确认播种后的占格不会让食用重新接管。
for _, row in ipairs({
    {water = 0, fertility = 0, label = "缺水缺肥"},
    {water = 0, fertility = 1, label = "仅缺水"},
    {water = 4, fertility = 0, label = "仅缺肥"},
    {water = 4, fertility = 1, label = "水肥充足"}
}) do
    local input = row
    test.runtime("agriculture", "planting_target", input, function(data)
        assert(data.scope == "live-isolated-world" and data.water == input.water
            and data.fertility == input.fertility, "播种回归未准备指定水肥的真实耕地")
        assert(not data.ordinaryGroundAvailable and not data.ordinaryGroundPriority,
            "普通地面错误占用播种或食用通道")
        assert(data.emptyFarmlandAvailable and data.emptyFarmlandPriority,
            input.label .. "的空耕地仍被拒绝播种")
        assert(data.cropPresent and not data.occupiedAvailable and data.occupiedPriority,
            "作物占格后未拒绝重复播种，或错误把本次使用让给食用")
        assert(data.availableAfterCropRemoved and not data.outOfReachAvailable and not data.outOfReachPriority,
            "移除作物后未恢复播种资格，或超距离目标占用使用通道")
        assert(data.waterAfter == input.water and data.fertilityAfter == input.fertility
            and data.seedAmountAfter == 3, "只读播种资格查询修改了水肥或种子数量")
        assert(data.restored, "播种资格现场的土壤、作物或农业差量没有恢复")
    end, "真实播种资格和食用优先级：" .. input.label)
end
