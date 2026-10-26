local function near(a, b, message)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= 0.00001, message)
end

test.system("surface_fluids", "bottom_recovery", {}, function(data)
    assert(data.stillSinking and data.completed, "完全浸没后没有保留视觉下沉阶段，或沉底过渡没有结束")
    assert(data.retainedCount == 1 and data.onBottom, "沉底后物品被删除，或保存恢复后重新开始下沉")
    near(data.amount, 7, "沉底或存档往返吞掉了堆叠数量")
    near(data.scale, .5, "沉底隐藏修改了物品真实尺寸")
    near(data.visualScale, 0, "沉底物重载后重新显示")
    near(data.x, -.9, "沉底物重新推进后水平位置改变")
    near(data.y, -.9, "沉底物重新推进后垂直位置改变")
    assert(data.sameCell and not data.shoreCell and not data.adjacentWaterCell,
        "沉底物未允许同格拾取，或从岸边、相邻水格被吸走")
    assert(data.floatingReach, "新增摸底限制改变了水面物品的原拾取规则")
    assert(data.itemStateSaved and data.itemStateCleared and data.otherState == 42 and data.rawStateRestored,
        "完整物品没有保存沉底状态、拾取时未清理，或覆盖了其他特殊数据")
end, "沉底保留、视觉隐藏、存档往返、负坐标同格拾取与完整物品状态清理")

test.system("surface_fluids", "notification_dispatch", {}, function(data)
    assert(data.iterations == 1024 and data.hits == 2048,
        "正式取水通知在重复发送时丢失或重复调用监听器")
    assert(data.firstSnapshot and data.nextSnapshot and data.duplicates,
        "监听器在通知途中退订没有保持本轮快照，或重复订阅顺序变化")
end, "取水事件投递顺序、重入退订与稳态分配")

test.system("surface_fluids", "liquid_layer", {}, function(data)
    assert(data.added and not data.repeated and data.repeatedRevisionStable, "液深重复写入错误地产生变更")
    assert(data.wetChanged and data.wetId == "core:dirty_water", "液体独立身份或差量未保存")
    assert(data.maximumLevel == 100 and data.litersPerUnit == 10, "满格不是100份或一份不是10升")
    assert(data.wetLevel == 60, "原六级水没有按比例变成60份")
    near(data.wetDepth, 0.6, "表现液深没有按等级派生")
    assert(data.ground == 17 and data.cost == 5, "写液体篡改底层地形或导航成本")
    assert(data.cleared and data.dryType == 0 and not data.dryChanged, "抽干后身份未清除或恢复基线仍有差量")
    assert(data.dryLevel == 0 and data.negativeLevel == 0 and data.excessLevel == 100, "水量没有限制在零到100份")
    assert(data.restoredLevel == 60 and data.restoredId == data.wetId, "存档序列化和恢复丢失了整数水量或身份")
    assert(data.minimumGeneratedLevel == 10 and data.roundedGeneratedLevel == 30, "生成水量没有按原十档乘10")
    assert(data.exactGeneratedLevels[1] == 10 and data.exactGeneratedLevels[2] == 60, "生成器的浮点边界误差多升了一级水量")
    for index, expected in ipairs({0, 1, 1, 1, 2, 9, 10, 10, 10}) do
        assert(data.visualLevels[index] == expected, "水面没有向上取整到十档，或不足100升被画成干格")
    end
    local changes = data.visualNotifications
    for index, expected in ipairs({false, true, false, true, true}) do
        assert(changes.singleFlags[index] == expected, "单格每份扣水错误刷新水面，或跨档和干湿变化没有刷新")
    end
    assert(#changes.batches == 2 and #changes.batches[1].changed == 3 and #changes.batches[1].visual == 1
        and changes.batches[1].visual[1] == 2, "批量同档扣水没有保留全部数据通知或多刷新了水面")
    assert(#changes.batches[2].changed == 3 and #changes.batches[2].visual == 3 and changes.remaining == 101,
        "批量跨档或由干变湿遗漏水面通知，或丢失真实水量")
    assert(data.missingId == "ArgumentException" and data.invalidGeneratedDepth == "ArgumentOutOfRangeException",
        "无身份液体或非法生成深度没有拒绝")
    local flow = data.flow
    assert(#flow.samples > 1, "整数流动没有推进")
    for _, step in ipairs(flow.samples) do
        local total = 0
        for _, level in ipairs(step.levels) do
            assert(level >= 0 and level <= 100 and level == math.floor(level), "流动写出了碎份或越界水量")
            total = total + level
        end
        assert(total == 80, "整数流动丢水或造水")
    end
    local final = flow.samples[#flow.samples]
    assert(final.transfers == 0, "整数均衡后仍来回震荡")
    for index = 2, #final.levels do
        assert(math.abs(final.levels[1] - final.levels[index]) <= 1, "整数液面没有达到允许的一层误差")
    end
    assert(flow.blocked.left == 100 and flow.blocked.right == 0 and flow.blocked.transfers == 0, "阻挡边界漏水")
    assert(flow.foreign.left == 100 and flow.foreign.right == 20 and flow.foreign.rightType == 2, "不同液体被混入")
    assert(flow.full.left == 100 and flow.full.right == 100, "满格溢出100份")
    assert(flow.single.left == 1 and flow.single.right == 0 and flow.single.transfers == 0, "单份水被拆开或来回移动")
    assert(flow.edge.left < 6 and flow.edge.right > 0 and flow.edge.left + flow.edge.right == 6, "有效邻边没有整份转移")
    assert(#flow.canalPairs == 10201, "跨水渠0到100份整数边界没有全组合回归")
    for _, pair in ipairs(flow.canalPairs) do
        local source, target, amount = pair[1], pair[2], pair[3]
        local sourceAfter, targetAfter = source - amount, target + amount
        assert(amount >= 0 and amount == math.floor(amount) and amount == math.floor(math.max(0, source - target) / 2),
            "连通水域转移量错误或出现了碎份")
        assert(sourceAfter >= 0 and targetAfter <= 100 and sourceAfter + targetAfter == source + target,
            "水渠交换超容量、造水或丢水")
        assert(source <= target or sourceAfter >= targetAfter,
            "水渠两端交换后液位逆转")
    end
end, "100份液体、向上十档水面、存档恢复、守恒流动与边界停稳")

-- 正式目录决定液体浮力，单件密度与堆叠数量无关。
for _, row in ipairs({
    {liquidId = "core:dirty_water", weight = .99, volume = 1, threshold = 1, sinks = false},
    {liquidId = "core:drinkable_water", weight = 1, volume = 1, threshold = 1, sinks = true},
    {liquidId = "core:pure_water", weight = 1.01, volume = 1, threshold = 1, sinks = true},
    {liquidId = "core:filtered_water", weight = .99, volume = 1, threshold = 1, sinks = false},
    {liquidId = "core:salt_water", weight = 1, volume = 1, threshold = 1, sinks = true},
    {liquidId = "core:sea_water", weight = 1.49, volume = 1, threshold = 1.5, sinks = false},
    {liquidId = "core:lava", weight = 2.5, volume = .75, threshold = 6, sinks = false},
    {liquidId = "core:lava", weight = 2.5, volume = .75, amount = 64, threshold = 6, sinks = false},
    {liquidId = "core:lava", weight = 5.5, volume = 1, threshold = 6, sinks = false},
    {liquidId = "core:lava", weight = 5.999, volume = 1, threshold = 6, sinks = false},
    {liquidId = "core:lava", weight = 6, volume = 1, threshold = 6, sinks = true},
    {liquidId = "core:lava", weight = 6.001, volume = 1, threshold = 6, sinks = true}
}) do
    test.system("surface_fluids", "buoyancy", row, function(data)
        near(data.threshold, row.threshold, "正式液体目录的浮沉门槛错误")
        near(data.ratio, row.weight / row.volume, "单件密度计算受到堆叠数量影响")
        assert(data.sinks == row.sinks, "水或岩浆的浮沉边界错误")
    end, "液体浮沉：" .. row.liquidId .. "，密度 " .. row.weight / row.volume .. " kg/L，数量 " .. (row.amount or 1))
end
