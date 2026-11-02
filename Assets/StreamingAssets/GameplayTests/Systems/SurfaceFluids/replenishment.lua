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
    { level = 10, supplied = { 5, 2, 1, 1 }, remaining = 1 },
    { level = 100, supplied = { 50, 25, 12, 6 }, remaining = 7 }
}

test.system("surface_fluids", "diffusion_batch", {}, function(data)
    assert(data.transferred and data.pendingLeft == 50 and data.unchangedBeforeCommit,
        "天然水域转移没有先用内存最终值计算，或过早写入真实地形")
    assert(data.allBalanced and data.typesIntact and data.total == 6400,
        "批量补水破坏了整数水量守恒、50/50均衡或液体身份")
    assert(data.singleNotifications == 0 and data.batchNotifications == 1
        and data.changedCells == 128 and data.visualCells == 128,
        "64次补水仍逐格刷新表现，或可视脏格没有按最终水位合并")
    assert(data.chainTransferred and data.chainStaged and data.chainBalanced and data.batchReused,
        "连续补水没有读取暂存水量，或下一轮继续使用了上一轮的缓存")
end, "天然水域：64组补水按区块一次提交、守恒并去掉逐格刷新")

test.system("surface_fluids", "diffusion_data_retention_lease", {}, function(data)
    assert(data.dormant and data.hidden, "只读地形和水量却唤醒了模拟或表现")
    assert(data.initial == 2 and data.protectedData and data.remaining == 1,
        "数据租约没有保护区块、引用计数错误或重复归还破坏计数")
    assert(data.independent and data.evicted, "释放液体缓存撤销了其它系统租约，或区块不能正常回收")
end, "扩散边界只保留数据、独立租约与最后使用者释放")

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
    end, tostring(expected.level) .. "份水依次补四个空水格并保持水量守恒")
end

local levels = {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 49, 50, 51, 90, 99, 100}
for _, source in ipairs(levels) do
    for _, target in ipairs(levels) do
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
