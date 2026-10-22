-- 正式掉落条目及展开到 GO 的配置必须保持相同分布，不能只改变某一种后端。
test.system("combat", "discrete_probability", {}, function(data)
    assert(math.abs(data.originalPercent - 10) < .00001 and math.abs(data.changedPercent - 25) < .00001
        and math.abs(data.changedOtherPercent - 75) < .00001 and data.copiedWeight == 10,
        "运行态修正没有归一化，或污染了原表、结构体副本")
    assert(data.belowBoundary == 1 and data.atBoundary == 2 and data.atOne == 1 and data.atZero == 1,
        "概率累计边界或零概率档位抽样错误")
    assert(data.rangeSample == 1 and math.abs(data.restoredPercent - 25) < .00001,
        "范围约束或冻结生成参数往返改变了最终概率")
    assert(data.generationFingerprintChanged and data.ecologyFingerprintChanged,
        "修改概率后生成或生态缓存标识没有失效")
    assert(#data.productionTables == 5 and #data.patchProbabilities == 11, "正式五类迁移缺少概率表")
    local expected_counts = {20, 10, 54, 33, 33}
    for index, table in ipairs(data.productionTables) do
        assert(#table.entries == expected_counts[index], "正式概率表档位数量错误：" .. table.id)
        local total = 0
        for _, entry in ipairs(table.entries) do
            assert(entry.percent >= 0 and entry.percent == entry.percent, "概率非有限或为负")
            total = total + entry.percent
        end
        assert(math.abs(total - 100) < .00001, "最终概率没有合计100%：" .. table.id)
    end
    for _, probability in ipairs(data.patchProbabilities) do
        assert(probability >= 0 and probability <= 1, "聚落距离档位概率越界")
    end
    assert(data.negative == "ArgumentOutOfRangeException" and data.nan == "ArgumentOutOfRangeException"
        and data.wrongTotal == "ArgumentException" and data.duplicate == "ArgumentException"
        and data.zeroAfterAdjustment == "ArgumentException" and data.invalidRandom == "ArgumentOutOfRangeException"
        and data.invalidRange == "InvalidOperationException", "非法原表、修正结果或抽样输入没有明确拒绝")
end, "共用离散概率、修正隔离、最终百分比、五类配置和冻结缓存")

test.system("combat", "loot_amount", {}, function(data)
    assert(not data.rejected and data.backendEquivalent, "GO 与原生掉落数量抽样不一致：" .. (data.reason or "结果不同"))
    assert(data.minimum >= 1 and data.maximum <= 20 and data.minimum == data.goMinimum and data.maximum == data.goMaximum,
        "掉叶子范围越界或后端端点不同")
    assert(#data.counts == 20 and #data.percentages == 20, "掉落数量统计不完整")
    local total = 0
    for amount, count in ipairs(data.counts) do
        total = total + count
        assert(math.abs(count - data.percentages[amount] * 100) <= 2, "实际抽样偏离JSON最终概率：" .. amount)
    end
    assert(total == 10000, "抽样丢失数量")
end, "树叶读取JSON最终概率且GO/原生后端一致")

for _, row in ipairs({
    {minimum = 1, maximum = 4, distribution = "Uniform", peak = 0},
    {minimum = 3, maximum = 3, distribution = "Uniform"}
}) do
    test.system("combat", "loot_amount", row, function(data)
        assert(not data.rejected and data.backendEquivalent, "有效数量分布被拒绝或后端不一致")
        assert(data.minimum == row.minimum and data.maximum == row.maximum, "数量分布越界")
        if row.distribution == "Uniform" then
            for _, count in ipairs(data.counts) do assert(count == 10000 / #data.counts, "普通或固定掉落不再均匀") end
        elseif row.minimum == row.maximum then
            assert(data.counts[1] == 10000, "固定掉落数量变化")
        else
            for index = 2, #data.counts do
                assert((row.peak == row.minimum and data.counts[index] < data.counts[index - 1]) or
                    (row.peak == row.maximum and data.counts[index] > data.counts[index - 1]), "边界峰值分布方向错误")
            end
        end
    end, "掉落分布：均匀、固定与端点峰值")
end

for _, row in ipairs({{minimum = 0}, {minimum = 20, maximum = 1}, {minimum = 2}, {maximum = 19}, {distribution = "Unknown"}}) do
    test.system("combat", "loot_amount", row, function(data)
        assert(data.rejected, "无效掉落配置没有在加载时拒绝")
    end, "掉落分布：拒绝非法数量、峰值与分布")
end

test.system("combat", "tree_leaf_configuration", {}, function(data)
    local expected = {AppleTree = true, PeachTree = true, OrangeTree = true, Tree_Coconut = true,
        Tree_Pine = true, Tree_Oak = true, Tree_Willow = true}
    assert(#data.trees >= 7, "树木采样遗漏正式定义")
    for _, tree in ipairs(data.trees) do
        expected[tree.id] = nil
        assert(#tree.leaves == 1, tree.id .. " 缺失或重复掉叶子")
        local leaf = tree.leaves[1]
        assert(leaf.probability == 1 and leaf.minimum == 1 and leaf.maximum == 20 and
            leaf.distribution == "DiscreteTable" and leaf.tableId == "loot.tree.leaf.amount", tree.id .. " 没有统一树叶掉落配置")
    end
    assert(next(expected) == nil, "已知树种有遗漏")
end, "所有树种都必定掉叶子，包括柳树及继承定义")
