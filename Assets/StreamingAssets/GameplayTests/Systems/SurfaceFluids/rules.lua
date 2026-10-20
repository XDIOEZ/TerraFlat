local function near(a, b, message)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= 0.00001, message)
end

test.system("surface_fluids", "notification_dispatch", {}, function(data)
    assert(data.iterations == 1024 and data.hits == 2048,
        "正式取水通知在重复发送时丢失或重复调用监听器")
    assert(data.firstSnapshot and data.nextSnapshot and data.duplicates,
        "监听器在通知途中退订没有保持本轮快照，或重复订阅顺序变化")
end, "取水事件投递顺序、重入退订与稳态分配")

test.system("surface_fluids", "liquid_layer", {}, function(data)
    assert(data.added and not data.repeated and data.repeatedRevisionStable, "液深重复写入错误地产生变更")
    assert(data.wetChanged and data.wetId == "core:dirty_water", "液体独立身份或差量未保存")
    assert(data.wetLevel == 6, "权威水量不是整数六级")
    near(data.wetDepth, 0.6, "表现液深没有按等级派生")
    assert(data.ground == 17 and data.cost == 5, "写液体篡改底层地形或导航成本")
    assert(data.cleared and data.dryType == 0 and not data.dryChanged, "抽干后身份未清除或恢复基线仍有差量")
    assert(data.dryLevel == 0 and data.negativeLevel == 0 and data.excessLevel == 10, "水量没有限制在零到十级")
    assert(data.restoredLevel == 6 and data.restoredId == data.wetId, "存档序列化和恢复丢失了整数水量或身份")
    assert(data.minimumGeneratedLevel == 1 and data.roundedGeneratedLevel == 3, "生成水量没有向上取完整等级")
    assert(data.exactGeneratedLevels[1] == 1 and data.exactGeneratedLevels[2] == 6, "生成器的浮点边界误差多升了一级水量")
    assert(data.missingId == "ArgumentException" and data.invalidGeneratedDepth == "ArgumentOutOfRangeException",
        "无身份液体或非法生成深度没有拒绝")
    local flow = data.flow
    assert(#flow.samples > 1, "整数流动没有推进")
    for _, step in ipairs(flow.samples) do
        local total = 0
        for _, level in ipairs(step.levels) do
            assert(level >= 0 and level <= 10 and level == math.floor(level), "流动写出了碎份或越界水量")
            total = total + level
        end
        assert(total == 8, "整数流动丢水或造水")
    end
    local final = flow.samples[#flow.samples]
    assert(final.transfers == 0, "整数均衡后仍来回震荡")
    for index = 2, #final.levels do
        assert(math.abs(final.levels[1] - final.levels[index]) <= 1, "整数液面没有达到允许的一层误差")
    end
    assert(flow.blocked.left == 10 and flow.blocked.right == 0 and flow.blocked.transfers == 0, "阻挡边界漏水")
    assert(flow.foreign.left == 10 and flow.foreign.right == 2 and flow.foreign.rightType == 2, "不同液体被混入")
    assert(flow.full.left == 10 and flow.full.right == 10, "满格溢出十级")
    assert(flow.single.left == 1 and flow.single.right == 0 and flow.single.transfers == 0, "单份水被拆开或来回移动")
    assert(flow.edge.left < 6 and flow.edge.right > 0 and flow.edge.left + flow.edge.right == 6, "有效邻边没有整份转移")
end, "整数十级液体、存档恢复、守恒流动与边界停稳")

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
