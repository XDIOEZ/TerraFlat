-- 地形生成、采挖与植被工作规则，不生成当前玩家世界，也不截图。
local function near(a, b, message, tolerance)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= (tolerance or 0.00001), message)
end

for _, case in ipairs({
    {scenario = "anchor", budget = 8, count = 1, found = true, x = 0, y = 0},
    {scenario = "mixed", budget = 8, count = 5, found = true, x = 0, y = 0},
    {scenario = "mixed", budget = 4, count = 4, found = false, x = 0, y = 0},
    {scenario = "empty", budget = 8, count = 8, found = false, x = 0, y = 0},
    {scenario = "wrapped", budget = 12, count = 5, found = true, x = 3, y = 1, wrapped = true},
    {scenario = "wrapped_empty", budget = 20, count = 8, found = false, x = 3, y = 1, wrapped = true},
    {scenario = "mixed", budget = 8, count = 5, found = true, x = -9, y = -6},
    {scenario = "distant", budget = 515, count = 514, found = true, x = 0, y = 0},
    {scenario = "cancelled", budget = 8, count = 0, found = false, x = 0, y = 0, cancelled = true},
    {scenario = "cancel_during", budget = 8, count = 3, found = false, x = 0, y = 0, cancelled = true}
}) do
    local input = case
    test.system("terrain", "spawn_line", input, function(data)
        assert(data.found == input.found and data.cancelled == (input.cancelled == true),
            "出生直线搜索的落地、预算或取消结果错误：" .. input.scenario)
        assert(#data.samples == input.count and data.contextMatches,
            "找到陆地后仍继续搜索、预算未生效、环绕重复或世界上下文改变")
        for index, cell in ipairs(data.samples) do
            local x, y = input.x + index - 1, input.y + index - 1
            if input.wrapped then x, y = (x + 4) % 8 - 4, (y + 2) % 4 - 2 end
            assert(cell.x == x and cell.y == y, "出生搜索偏离了固定斜线或跳过中间格")
        end
        if input.found then
            local last = data.samples[#data.samples]
            assert(data.x == last.x and data.y == last.y, "落点不是直线上第一个无水可走格")
        end
    end, "出生点单线逐格、首个陆地即停与取消：" .. input.scenario .. "/" .. input.budget)
end

for _, wrapped in ipairs({false, true}) do
    test.system("terrain", "contact_query_context", {wrapped = wrapped}, function(data)
        assert(data.initial.found and data.initial.top == 11 and data.initial.x == -4 and
            data.initial.y == -2 and data.initial.localX == 0 and data.initial.localY == 0,
            "负坐标接触寻址或区块内索引错误")
        assert(data.changed.found and data.changed.top == 44 and data.changed.liquid == "core:dirty_water" and
            math.abs(data.changed.depth - .6) < .00001, "单轮缓存寻址挡住了实时地表或水深修改")
        assert(data.covered.top == 90 and data.covered.ground == 90 and data.covered.depth == 0,
            "平台覆盖没有即时遮断真实液体接触")
        assert(data.uncovered.top == 44 and math.abs(data.uncovered.depth - .6) < .00001,
            "同轮移除平台后没有恢复底部地表和液体")
        assert(data.neighbour.found and data.neighbour.top == 22 and data.neighbour.x == -2,
            "跨区块查询错误复用了上一块")
        assert(data.seam.found == wrapped and data.negativeImage.found == wrapped,
            "循环镜像或无限世界的边界语义改变")
        if wrapped then
            assert(data.seam.top == 44 and data.seam.x == -4 and data.negativeImage.top == 33 and
                data.negativeImage.x == 3, "循环接缝没有命中规范权威格")
        end
        assert(not data.missing.found and data.loadedSameBatch.found and data.loadedSameBatch.top == 55 and
            data.newlyLoaded and data.loadedTop == 55, "缺失格触发了假加载或就绪后仍缓存缺失")
        assert(not data.removed.found and data.replaced.found and data.replaced.top == 66,
            "同轮回收或重建地形仍使用旧块缓存")
        assert(data.sameWorld and not data.foreignWorld and not data.oldEpochAccepted and
            data.newEpochAccepted and not data.disposedAccepted, "换世界、换代际或释放后仍接受旧接触查询")
    end, "脚下单轮寻址与实时液体覆盖：" .. (wrapped and "循环世界" or "无限世界"))
end

for _, kind in ipairs({ "valid", "positive_infinity", "negative_infinity", "nan_x", "nan_y", "outside", "disabled_camera", "missing_camera" }) do
    local input = { kind = kind }
    test.system("terrain", "environment_pointer", input, function(data)
        assert(data.accepted == (input.kind == "valid"), "环境面板没有正确拒绝无效指针或相机：" .. input.kind)
        assert(data.finiteWorld, "环境面板投影返回了非有限世界坐标")
        if input.kind == "valid" then
            near(data.worldX, 13, "偏移相机视口的中心没有落在正确世界 X")
            near(data.worldY, -7, "偏移相机视口的中心没有落在正确世界 Y")
        else
            near(data.worldX, 0, "投影失败后仍保留旧世界 X")
            near(data.worldY, 0, "投影失败后仍保留旧世界 Y")
        end
    end, "环境面板指针投影与无效输入拒绝：" .. kind)
end

test.system("terrain", "ground_cover_work", {}, function(data)
    assert(not data.unselectedCompleted and data.unselectedUses == 0, "未选中植被也累计了采集次数")
    assert(data.scissorsCompleted and data.scissorsUses == 1, "剪刀没有一次完成植被采集")
    for index, completed in ipairs(data.stoneCompleted) do
        assert(completed == (index == 4) and data.stoneUses[index] == index,
            "石器未按同一目标累计四次后完成：" .. index)
    end
    assert(#data.stoneCompleted == 4, "石器没有采样完整四次交互")
    assert(data.retryCompleted and data.retryUses == 4, "已完成进度重试时没有保持四次封顶")
    for _, sample in ipairs(data.targetChanges) do
        assert(sample.before == 1 and sample.afterSelect == 0 and sample.afterWork == 1 and not sample.completed,
            "更换目标身份继承了上一目标进度：" .. sample.kind)
    end
    assert(#data.targetChanges == 5, "未覆盖地格、草花、花朵身份和地形更换")
    assert(data.resetUses == 0 and not data.resetCompleted and data.afterResetUses == 0,
        "重置没有清除进度和目标身份")
    assert(data.zeroUsesRejected and data.negativeUsesRejected and data.afterInvalidUses == 0,
        "无效交互次数未拒绝或改变了进度")
    assert(not data.disposedCompleted and data.disposedUses == 1, "已释放地形仍能累计采集进度")
    assert(not data.emptyGrassConsumed and data.emptyGrassState == data.expectedEmptyGrass,
        "空草层被当成可采草消费")
    assert(data.presentGrassConsumed and not data.repeatGrassConsumed and
        data.consumedGrassState == data.expectedEmptyGrass, "真实草层没有只被消费一次")
end, "剪刀一次与石器四次植被工作、目标隔离和草层一次性消费")

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

-- 使用紧凑水文预算采样真正生成并提交的地形，覆盖冷暖缓存与跨区生成顺序。
for _, case in ipairs({
    {seed = 17413, origin = 0, wrapped = false, label = "正坐标"},
    {seed = 8321, origin = -32, wrapped = true, label = "负坐标环绕世界"}
}) do
    test.system("terrain", "cached_generation", case, function(data)
        local cold = data.cold
        assert(cold.cellCount == 1024 and cold.environmentValues == cold.cellCount and
            #cold.heightBits == cold.cellCount, "地形采样未包含完整的真实高度环境层")
        assert(cold.waterCells > 0 and cold.riverCells + cold.lakeCells > 0,
            "水文夹具没有真实河湖，空地形不能证明河湖生成稳定")
        for _, name in ipairs({"warmed", "independent"}) do
            local repeated = data[name]
            assert(cold.terrainHash == repeated.terrainHash and cold.waterCells == repeated.waterCells and
                cold.riverCells == repeated.riverCells and cold.lakeCells == repeated.lakeCells,
                "冷缓存、邻区预热后的缓存或独立生成器改变了正式地形：" .. name)
            for index, height in ipairs(cold.heightBits) do
                assert(height == repeated.heightBits[index], "高度原始值随缓存或生成顺序改变：" .. name)
            end
        end
    end, "河湖正式生成的冷暖缓存、独立实例与顺序稳定：" .. case.label)
end

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
        assert(data.tier == (case.args.tier or 1) and data.efficiency == (case.args.efficiency or 1), "JSON 参数没有写入真实工具等级或效率字段")
        assert(data.readOnlyParameterRejected, "只读计算属性被接受为可写 JSON 参数")
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
