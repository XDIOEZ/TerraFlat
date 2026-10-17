local cases = {
    { level = 1, supplied = { 1, 0, 0, 0 }, remaining = 0 },
    { level = 2, supplied = { 1, 1, 0, 0 }, remaining = 0 },
    { level = 3, supplied = { 1, 1, 1, 0 }, remaining = 0 },
    { level = 4, supplied = { 1, 1, 1, 1 }, remaining = 0 },
    { level = 5, supplied = { 1, 1, 1, 1 }, remaining = 1 },
    { level = 6, supplied = { 1, 1, 1, 1 }, remaining = 2 },
    { level = 7, supplied = { 1, 1, 1, 1 }, remaining = 3 },
    { level = 8, supplied = { 2, 1, 1, 1 }, remaining = 3 },
    { level = 9, supplied = { 2, 1, 1, 1 }, remaining = 4 },
    { level = 10, supplied = { 2, 2, 1, 1 }, remaining = 4 }
}

for _, case in ipairs(cases) do
    local expected = case
    test.system("surface_fluids", "replenishment", { level = expected.level }, function(data)
        assert(data.remaining == expected.remaining, "供水格没有按每次剩余层级扣水")
        local total = data.remaining
        for index, amount in ipairs(expected.supplied) do
            assert(data.supplied[index] == amount and data.neighbours[index] == amount,
                "邻居补水量没有向下取整或最低一层规则错误")
            total = total + data.neighbours[index]
        end
        assert(total == expected.level and data.total == expected.level, "补水增减了总水量")
        assert(data.groundUnchanged, "补水修改了底部地块")
    end, tostring(expected.level) .. "层水依次补四个空水格并保持水量守恒")
end

test.system("surface_fluids", "replenishment_limits", {}, function(data)
    assert(data.emptyBasin == 2 and data.originalLevel == 1, "被抽干的原水域没有保留补水资格")
    assert(data.dryLand == 0 and data.pouredLand == 0, "补水蔓延到了普通陆地")
    assert(data.wetNeighbour == 0, "仍有水的邻格被重复补水")
    assert(data.otherLiquid == 0, "补水混入了其他种类的液体")
    assert(data.wall == 0 and data.blockedFlag == 0 and data.blockedSource == 0, "补水穿过了阻挡地块")
    assert(data.emptySource == 0 and data.sameCell == 0, "无水来源或自身格错误参与补水")
    assert(data.crossChunk == 2, "跨区块补水没有按稳定液体身份匹配")
end, "补水边界：原水域、陆地、异种液体、阻挡和跨区块")
