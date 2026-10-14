-- 纯噪声和群系选择，不生成当前世界，也不截图。
local function near(a, b, message, tolerance)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= (tolerance or 0.00001), message)
end

for _, sample in ipairs({{height = 0.75, strength = 1, seed = 17413}, {height = 0.25, strength = 1, seed = 8321}}) do
    local input = sample
    test.system("terrain", "noise", input, function(data)
        assert(data.valid and not data.invalidOctavesAccepted, "噪声配置没有拒绝零八度")
        assert(data.sample >= 0 and data.sample <= 1, "噪声样本超出归一化范围")
        near(data.repeated, data.sample, "同种子同坐标的噪声不确定")
        assert(math.abs(data.otherSeed - data.sample) > 0.000001, "不同种子没有改变此噪声采样")
        near(data.unboosted, input.height, "关闭高度强化仍改变高度")
        if input.height > 0.5 then assert(data.boosted >= data.unboosted, "高地强化降低了高度")
        else assert(data.boosted <= data.unboosted, "低地强化提高了高度") end
        assert(data.boosted >= 0 and data.boosted <= 1, "高度强化越界")
        near(data.lowClamped, 0, "负高度没有归零")
        near(data.highClamped, 1, "过高高度没有封顶")
    end, "确定性噪声与高度强化：" .. input.height)
end

test.system("terrain", "biome", {}, function(data)
    assert(data.lowerBoundary == 3 and data.upperBoundary == 3, "群系没有包含上下边界")
    assert(data.overlap == 3, "重叠区间没有按正式配置顺序选择")
    assert(data.fallback == 7, "未满足首个群系时没有继续匹配")
    assert(data.outside == 255 and data.empty == 255, "未匹配群系未保留独立标记")
end, "群系区间边界、优先顺序与未匹配")

local harvestCases = {
    {name = "一级普通工具", args = {}, tool = true, rule = true, uses = 6},
    {name = "三级品质提高工作量", args = {tier = 3}, tool = true, rule = true, uses = 3},
    {name = "独立效率加成", args = {efficiency = 2}, tool = true, rule = true, uses = 3},
    {name = "品质上限仍至少操作两次", args = {tier = 100, efficiency = 100}, tool = true, rule = true, uses = 2},
    {name = "不整除时次数向上取整", args = {baseUses = 7, efficiency = 2}, tool = true, rule = true, uses = 4},
    {name = "配置最低次数生效", args = {minimumUses = 3, efficiency = 100}, tool = true, rule = true, uses = 3},
    {name = "无采集工具类别拒绝", args = {toolKind = "None"}, tool = false, rule = true, uses = -1},
    {name = "零工具等级拒绝", args = {tier = 0}, tool = false, rule = true, uses = -1},
    {name = "零工作效率拒绝", args = {efficiency = 0}, tool = false, rule = true, uses = -1},
    {name = "负连续使用间隔拒绝", args = {interval = -1}, tool = true, rule = false, uses = -1},
    {name = "过长连续使用间隔拒绝", args = {interval = 11}, tool = true, rule = false, uses = -1},
    {name = "多层天然状态独立恢复", args = {layers = 4, depth = 3}, tool = true, rule = true, uses = 6},
    {name = "零天然层数拒绝", args = {layers = 0}, tool = true, rule = false, uses = -1}
}
for _, case in ipairs(harvestCases) do
    test.system("terrain", "harvest_rules", case.args, function(data)
        assert(data.toolValid == case.tool and data.ruleValid == case.rule and data.requiredUses == case.uses, "正式采挖工具、规则校验或品质次数错误")
        assert(data.restoredDepth == (case.args.depth or 2) and data.restoredGrass == 4 and data.invalidDepthRejected, "地层深度快照恢复或非法层数保护错误")
        assert(math.abs(data.progress - .4) < .00001 and data.highProgress == 1 and data.lowProgress == 0, "权威采挖进度读取未钳制")
        if case.rule then
            assert(math.abs(data.interval - .2) < .00001 and data.layers == (case.args.layers or 1), "采挖间隔或天然层数配置丢失")
        else
            assert(type(data.ruleError) == "string" and #data.ruleError > 0, "无效采挖配置没有拒绝原因")
        end
        if not case.tool then assert(type(data.toolError) == "string" and #data.toolError > 0, "无效工具没有拒绝原因") end
    end, "地表采挖规则：" .. case.name)
end
