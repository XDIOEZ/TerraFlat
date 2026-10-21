-- 采样使用附近合格空格执行正式耕作与灌溉，返回前恢复现场，因此只做数据断言。
test.check("player_ready", "农业现场测试玩家就绪")
test.runtime("agriculture", "crop_visual_reenable", {}, function(data)
    local function same(a, b, label)
        assert(math.abs(a.scaleX - b.scaleX) < .00001 and math.abs(a.scaleY - b.scaleY) < .00001
            and a.sprite == b.sprite and a.material == b.material and a.clip == b.clip, label)
    end
    assert(#data.samples == 2, "缺少连续缩放或阶段图作物样本")
    for _, row in ipairs(data.samples) do
        local expectedScale = row.discrete and 1 or .25
        assert(math.abs(row.initial.scaleX - expectedScale) < .00001 and row.initial.clip == .5,
            row.id .. "首次激活时重复捕获了已修改的外壳基线")
        same(row.disabled, row.whileDisabled, row.id .. "停用后仍响应成长事件")
        assert(row.restored.sprite == row.matureSprite and row.restored.clip == .5
            and row.restored.material == row.initial.material
            and math.abs(row.restored.scaleX - 1) < .00001,
            row.id .. "恢复后未显示停用期间更新的成熟状态")
        same(row.initial, row.afterGrowthEvent, row.id .. "恢复后未重新监听成长事件")
        same(row.initial, row.repeated, row.id .. "多次启停累计改变了缩放或原始图片")
        assert(row.generation == row.restoredGeneration, "临时恢复不应重新加载模块")
        same(row.disabled, row.unloaded, row.id .. "卸载未恢复原始外壳")
        same(row.unloaded, row.afterUnloadEnable, row.id .. "正式卸载后被重新启用偷偷恢复表现")
        assert(not row.loadedAfterUnloadEnable, "正式卸载后改变了模块加载状态")
    end
end, "农作物表现停用恢复：当前成长、事件恢复、基线与正式卸载")
test.runtime("agriculture", "tillage_irrigation", {}, function(data)
    assert(data.scope == "live-isolated-world", "农业测试没有进入隔离世界")
    assert(data.partialAccepted and not data.partialCompleted and data.partialGroundPreserved,
        "未完成耕作提前改变正式地形")
    assert(data.partialVisualIndexed and data.completedVisualRemoved,
        "锄地临时格索引未及时增删，区块重绑可能遗漏或留下旧覆盖层")
    assert(data.completionAccepted and data.tillCompleted and data.groundAfter == "Tile_Farmland",
        "完成耕作没有提交正式耕地")
    assert(data.afterIrrigation == 3 and data.afterConsumption == 2,
        "正式土壤灌溉与消耗没有提交到真实区块")
    assert(data.sampledCommit, "已读取地块的批量提交没有通过正式采样入口")
    assert(data.savedSource > 0 and data.savedSource == data.currentSource and data.savedCropAbsent
        and math.abs(data.savedWater - data.afterConsumption) < 0.0001, "农业独立差量与真实水肥不一致")
    assert(data.savedSupplyRemainder == 0.25 and data.supplyRemainder == 0.25
        and data.savedConsumptionRemainder == 0.5 and data.consumptionRemainder == 0.5,
        "不足一份的连续补水或耗水进度没有随同一格保存并读回")
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

test.runtime("agriculture", "pour_absorption", {}, function(data)
    assert(data.targetPourAccepted and data.firstAdded == 3 and data.targetWater == 3 and data.targetLiquid == 0,
        "没有作物的耕地未立即按一份水加一级吸收倒入水")
    assert(data.untouchedWater == 0 and data.untouchedLiquid == 2,
        "向目标格倒水触发了其他耕地的吸水检测")
    assert(data.overflowPourAccepted and data.overflowAdded == 3 and data.fullWater == 10 and data.overflowLiquid == 2,
        "耕地达到十级后没有把剩余整数份水留在地表")
    assert(data.fullPourAccepted and data.fullAdded == 1 and data.fullWaterAfterExtra == 10 and data.extraLiquid == 3,
        "已满水的耕地吞掉了继续倒入的整份水")
    assert(data.lavaPourAccepted and data.lavaAdded == 1 and data.waterAfterLava == 0 and data.lavaLiquid == 1,
        "非水类液体被错误当成耕地水分吸收")
    assert(data.restored, "倒水吸收现场的液体、土壤或农业差量没有恢复")
end, "裸耕地即时吸水、十级容量、余水守恒与目标格隔离")
