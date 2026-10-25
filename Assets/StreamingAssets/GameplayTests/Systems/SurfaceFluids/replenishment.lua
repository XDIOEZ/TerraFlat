local cases = {
    { level = 1, supplied = { 0, 0, 0, 0 }, remaining = 1 },
    { level = 2, supplied = { 1, 0, 0, 0 }, remaining = 1 },
    { level = 3, supplied = { 1, 1, 0, 0 }, remaining = 1 },
    { level = 4, supplied = { 2, 1, 0, 0 }, remaining = 1 },
    { level = 5, supplied = { 2, 1, 1, 0 }, remaining = 1 },
    { level = 6, supplied = { 3, 1, 1, 0 }, remaining = 1 },
    { level = 7, supplied = { 3, 2, 1, 0 }, remaining = 1 },
    { level = 8, supplied = { 4, 2, 1, 0 }, remaining = 1 },
    { level = 9, supplied = { 4, 2, 1, 1 }, remaining = 1 },
    { level = 10, supplied = { 5, 2, 1, 1 }, remaining = 1 }
}

for _, cells in ipairs({1, 2, 15, 16, 513, 4096}) do
    local count = cells
    test.system("surface_fluids", "connected_pump_supply", {cells = count}, function(data)
        local expected = 0
        for i = 1, count - 1 do expected = expected + 1 + i % 10 end
        assert(data.cells == count and data.initial == expected, "供水缓存漏格或初始总量错误")
        assert(data.valid and data.highestFirst, "供水缓存选择了空格、越界格或未优先抽高水位")
        assert(data.pumped == expected and data.selections == expected and data.exhausted,
            "抽空泵口后没有耗尽整组真实库存，或最后一级残水无法抽取")
        assert(data.restored and data.restoredTotal == 1, "抽干后补液无法重新供水，或重复更新造水")
    end, "连通供水缓存：空泵口、" .. count .. "格全量耗尽及重新注水")
end

for _, case in ipairs(cases) do
    local expected = case
    test.system("surface_fluids", "replenishment", { level = expected.level }, function(data)
        assert(data.remaining == expected.remaining and data.remaining >= 1, "供水格被搬空或水量差取整错误")
        local total = data.remaining
        for index, amount in ipairs(expected.supplied) do
            assert(data.supplied[index] == amount and data.neighbours[index] == amount,
                "邻居补水量没有按水量差的一半向下取整")
            total = total + data.neighbours[index]
        end
        assert(total == expected.level and data.total == expected.level, "补水增减了总水量")
        assert(data.groundUnchanged, "补水修改了底部地块")
    end, tostring(expected.level) .. "层水依次补四个空水格并保持水量守恒")
end

for source = 0, 10 do
    for target = 0, 10 do
        local beforeSource, beforeTarget = source, target
        test.system("surface_fluids", "replenishment_pair",
            { sourceLevel = beforeSource, targetLevel = beforeTarget }, function(data)
                assert(data.source + data.target == beforeSource + beforeTarget, "补水没有保持总水量")
                assert(data.amount >= 0 and data.amount == math.floor(data.amount), "补水拆分了整数单位")
                if beforeSource - beforeTarget < 2 then
                    assert(data.amount == 0 and data.source == beforeSource and data.target == beforeTarget,
                        "倒流、平水或仅差一级时仍搬水")
                else
                    assert(data.amount > 0 and data.source >= 1, "有水量差却没有补水或供水格被搬空")
                    assert(data.source >= data.target and data.source - data.target <= 1,
                        "补水后两格未均衡或发生反转")
                    assert(data.forwardAgain == 0 and data.reverseAgain == 0, "均衡后继续来回搬水")
                end
            end, tostring(source) .. "→" .. tostring(target) .. "层水量差、保底和停稳")
    end
end

test.system("surface_fluids", "replenishment_limits", {}, function(data)
    assert(data.emptyBasin == 5 and data.originalLevel == 1, "被抽干的原水域没有保留补水资格")
    assert(data.dryLand == 0 and data.pouredLand == 0, "补水蔓延到了普通陆地")
    assert(data.wetNeighbour == 4, "尚有少量水的原水域没有按水量差补水")
    assert(data.otherLiquid == 0 and data.changedLiquid == 0, "补水混入了其他种类的液体")
    assert(data.wall == 0 and data.blockedFlag == 0 and data.blockedSource == 0, "补水穿过了阻挡地块")
    assert(data.emptySource == 0 and data.sameCell == 0, "无水来源或自身格错误参与补水")
    assert(data.crossChunk == 5, "跨区块补水没有按稳定液体身份匹配")
end, "补水边界：原水域、陆地、异种液体、阻挡和跨区块")
