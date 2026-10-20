-- 地形生成、采挖与植被工作规则，不生成当前玩家世界，也不截图。
for _, scenario in ipairs({ "ocean", "freshwater", "cave", "frozen_ocean" }) do
    test.system("terrain", "natural_waterbed", { scenario = scenario }, function(data)
        local natural, river, lake = 0, 0, 0
        assert(#data.samples > 0, "天然底床夹具没有生成地形")
        for _, sample in ipairs(data.samples) do
            local depth = sample.naturalDepth
            if depth > 0 then
                natural = natural + 1
                assert(depth == 2 or depth == 3, "天然水体底床没有降低两到三层")
                if sample.kind == 1 then river = river + 1 end
                if sample.kind == 2 then lake = lake + 1 end
                if scenario == "frozen_ocean" then
                    assert(sample.liquid == 0, "冻结水体仍残留液体")
                else
                    assert(sample.liquid > 0, "未冻结的天然底床没有初始液体")
                    assert(depth == (sample.liquid <= 5 and 2 or 3), "浅深水没有生成对应底床层数")
                end
            else
                assert(sample.liquid == 0, "天然液体生成时漏写底床凹陷")
            end
            local expectedInitial = sample.liquid > 0 and sample.base - 1 or sample.base - depth
            assert(sample.initial == expectedInitial,
                "天然深浅水面没有统一低于同地势河岸一层")
            assert(sample.drained == sample.base - depth and
                sample.poured == (depth > 0 and sample.base - 1 or sample.base + 1),
                "抽水或倒水改坏了底床与液面层级")
            assert(sample.afterDepth == depth and sample.height == sample.afterHeight,
                "液体变化改写了天然底床或生成海拔")
        end
        assert(natural > 0, "夹具没有真实天然水域，不能证明底床生成正确")
        if scenario == "freshwater" then
            assert(river + lake > 0, "淡水夹具没有真实河流或湖泊")
        end
    end, "天然水域底床与抽水倒水层级：" .. scenario)
end

-- 基础生态夹具只给原始矿山与占格，逐格核对正式散矿阶段的概率和来源。
for _, case in ipairs({
    {scenario = "mixed", ore = 0.02, stone = 0.05},
    {scenario = "mixed", ore = 1, stone = 0},
    {scenario = "mixed", ore = 0, stone = 1},
    {scenario = "mixed", ore = 0, stone = 0},
    {scenario = "mixed", ore = 1, stone = 0, wrapped = true},
    {scenario = "mixed", ore = 0.02, stone = 0.05, wrapped = true},
    {scenario = "stone", ore = 0.02, stone = 0.05},
    {scenario = "stone", ore = 1, stone = 0},
    {scenario = "empty", ore = 1, stone = 0},
    {scenario = "occupied", ore = 0, stone = 1}
}) do
    local input = case
    test.system("terrain", "cave_loose_resources", input, function(data)
        assert(data.defaultOre == 0.02 and data.defaultStone == 0.05 and data.min == 5 and data.max == 12,
            "散矿默认概率或距离偏离 2%/5% 与 5～12 格")
        assert(data.oreChance == input.ore and data.stoneChance == input.stone, "散矿参数未进入正式快照")
        local function key(x, y) return x .. ":" .. y end
        local function distance(a, b)
            local dx, dy = math.abs(a.x - b.x), math.abs(a.y - b.y)
            if input.wrapped then dx, dy = math.min(dx, 32 - dx), math.min(dy, 32 - dy) end
            return dx * dx + dy * dy
        end
        local function closer(source, best, d, bestDistance)
            return not best or d < bestDistance or
                (d == bestDistance and (source.x < best.x or source.x == best.x and source.y < best.y))
        end
        local occupied, loose = {}, {}
        for _, source in ipairs(data.sources) do
            if source.x >= 0 and source.x < 16 and source.y >= 0 and source.y < 16 then
                occupied[key(source.x, source.y)] = true
            end
        end
        for _, placement in ipairs(data.placements) do
            if string.sub(placement.rule, 1, 11) == "cave.loose." then
                local id = key(placement.x, placement.y)
                assert(not loose[id] and not occupied[id], "散矿重复生成或覆盖矿山、植物、藤蔓")
                assert(placement.host == 0 and placement.offsetX == 0 and placement.offsetY == 0,
                    "散矿成为仅同块可见的伴生物，或偏移缩短了最小距离")
                loose[id] = placement
            end
        end
        local expectedCount, actualCount = 0, 0
        for _, cell in ipairs(data.cells) do
            local nearest, nearestOre, best, bestOre = nil, nil, math.huge, math.huge
            for _, source in ipairs(data.sources) do
                if source.mine then
                    local d = distance(cell, source)
                    if closer(source, nearest, d, best) then nearest, best = source, d end
                    if source.item ~= "Mine_Stone" and closer(source, nearestOre, d, bestOre) then
                        nearestOre, bestOre = source, d
                    end
                end
            end
            local expectedItem, expectedSource
            if not cell.blocked and not cell.wet and not cell.safe and not occupied[key(cell.x, cell.y)] and
                best >= 25 and best <= 144 then
                if cell.roll < input.ore then
                    if nearestOre and bestOre <= 144 then
                        expectedItem, expectedSource = string.gsub(nearestOre.item, "^Mine_", "Ore_"), nearestOre
                    end
                elseif cell.roll < math.min(1, input.ore + input.stone) then
                    expectedItem, expectedSource = "Ore_Stone", nearest
                end
            end
            local actual = loose[key(cell.x, cell.y)]
            assert((actual ~= nil) == (expectedItem ~= nil),
                "散矿概率、空格资格或 5～12 格距离不符合规则：" .. key(cell.x, cell.y))
            if expectedItem then
                expectedCount = expectedCount + 1
                assert(actual.item == expectedItem, "散矿没有对应真实来源矿山，或石头矿山吃到了额外 2%")
                assert(actual.rule == "cave.loose." .. expectedItem .. "@" .. expectedSource.x .. "," .. expectedSource.y,
                    "散矿来源坐标不稳定，或跨区块没有使用最近对应矿山")
            end
            if actual then actualCount = actualCount + 1 end
        end
        assert(expectedCount == actualCount, "散矿数量与候选格精确断言不一致")
        if input.scenario == "mixed" and input.ore == 1 then
            assert(loose[key(4, 8)] and loose[key(11, 8)], "5 格或 12 格边界没有包含在内")
        end
        assert(#data.placements == #data.repeated, "先解析邻块改变了当前块数量")
        for index, placement in ipairs(data.placements) do
            local repeated = data.repeated[index]
            assert(placement.guid == repeated.guid and placement.item == repeated.item and
                placement.rule == repeated.rule and placement.x == repeated.x and placement.y == repeated.y,
                "固定种子重生成或邻块解析顺序改变了结果")
        end
        local expanded = {}
        for _, placement in ipairs(data.expanded) do
            if placement.x < 16 then expanded[key(placement.x, placement.y)] = placement end
        end
        for _, placement in ipairs(data.placements) do
            local same = expanded[key(placement.x, placement.y)]
            assert(same and placement.guid == same.guid and placement.item == same.item and placement.rule == same.rule,
                "跨区块拼接与整片生成的同一格结果不一致")
            expanded[key(placement.x, placement.y)] = nil
        end
        assert(next(expanded) == nil, "扩大窗口凭空增加了公共区域散矿")
    end, "洞穴散矿真实来源、稀疏概率与跨块确定性：" .. input.scenario .. "/" .. input.ore .. "/" ..
        input.stone .. "/" .. tostring(input.wrapped))
end

for _, case in ipairs({{enabled = true, chance = 1}, {enabled = false, chance = 1}, {enabled = true, chance = 0}}) do
    local input = case
    test.system("terrain", "cave_dirt_ground", input, function(data)
        assert(data.waterCount > 0 and data.wallCount > 0, "洞穴夹具没有生成地下河与天然岩壁")
        assert(data.otherChanges == 0, "泥土地面改变了阻挡、寻路、岩壁或液体")
        if input.enabled and input.chance > 0 then
            assert(data.dirtCount > 0 and #data.samples > 0 and data.groundChanges == data.dirtCount,
                "河岸没有实际替换成泥土地面")
            for _, cell in ipairs(data.samples) do
                assert(cell.ground == 29 and cell.blocking == 0 and cell.walkable and cell.cost == 1,
                    "泥土仍然生成在阻挡层或阻断寻路")
                assert(cell.water == 0 and cell.adjacentWater, "泥土覆盖了水体或离开河岸")
            end
        else
            assert(data.dirtCount == 0 and data.groundChanges == 0, "关闭泥土或零概率仍然替换地面")
        end
    end, "洞穴河岸泥土只换底材：" .. tostring(input.enabled) .. "/" .. input.chance)
end

local function near(a, b, message, tolerance)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= (tolerance or 0.00001), message)
end

test.system("terrain", "climate_cache", {scenario = "identity"}, function(data)
    for _, name in ipairs({"macro", "expanded", "ecology", "minerals", "pairing"}) do
        assert(data[name].climate == data.base.climate and data[name].generation ~= data.base.generation,
            "临时输出范围或生态/矿脉/门户配置拆分了同一份气候缓存，或改变了完整生成指纹：" .. name)
    end
    for _, name in ipairs({"resizedGrid", "height", "temperature", "rainfall", "scale", "text", "profile", "signature"}) do
        assert(data[name].climate ~= data.base.climate,
            "真正的气候配置变化没有使缓存身份失效：" .. name)
    end
end, "气候缓存保留完整输入，忽略临时输出范围与生态身份")

for _, case in ipairs({{algorithm = "simple", wrapped = false}, {algorithm = "legacy", wrapped = true}}) do
    local input = case
    test.system("terrain", "climate_cache", input, function(data)
        assert(data.afterCold.precipitationBuilds > data.before.precipitationBuilds and
            data.afterCold.oceanChunkBuilds > data.before.oceanChunkBuilds, "夹具没有执行冷海洋与降水构建")
        for _, name in ipairs({"afterEcology", "afterExpanded", "afterRepeated"}) do
            assert(data[name].precipitationBuilds == data.afterCold.precipitationBuilds and
                data[name].oceanChunkBuilds == data.afterCold.oceanChunkBuilds,
                "相同气候输入重复扫描海洋或重建降水节点：" .. name)
        end
        assert(data.ecology.terrainHash == data.cold.terrainHash and
            data.repeated.terrainHash == data.cold.terrainHash, "改生态倍率或暖缓存改变了真实地形")
        assert(#data.cold.heightBits == 16 and #data.expanded.heightBits == 25,
            "真实生成没有保留完整的原始高度样本")
        for y = 0, 3 do
            for x = 0, 3 do
                local index = y * 4 + x + 1
                assert(data.cold.heightBits[index] == data.ecology.heightBits[index] and
                    data.cold.heightBits[index] == data.repeated.heightBits[index] and
                    data.cold.heightBits[index] == data.expanded.heightBits[y * 5 + x + 1],
                    "扩展输出或复用气候缓存改变了公共格的高度原始值")
            end
        end
    end, "正式气候冷暖缓存复用与高度不变：" .. input.algorithm .. "/" .. tostring(input.wrapped))
end

for _, scenario in ipairs({"seed", "epoch", "dimension", "topology", "parameter"}) do
    local selected = scenario
    test.system("terrain", "climate_cache", {scenario = selected}, function(data)
        assert(data.afterChanged.precipitationBuilds > data.afterCold.precipitationBuilds and
            data.afterChanged.oceanChunkBuilds > data.afterCold.oceanChunkBuilds,
            "不同世界上下文或气候参数错误复用了旧海洋/降水缓存：" .. selected)
    end, "气候缓存上下文隔离：" .. selected)
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

test.system("terrain", "river_diagnostic_aggregation", {}, function(data)
    local prefix = "river.local_refine."
    assert(data.firstCounts[prefix .. "invocations"] == 1 and
        data.repeatedCounts[prefix .. "invocations"] == 2,
        "河流细化次数没有跟随同一外层请求累计")
    for name, count in pairs(data.firstCounts) do
        if string.sub(name, 1, #prefix) == prefix then
            if name == prefix .. "output_padding_last" then
                assert(data.repeatedCounts[name] == count,
                    "最近输出外圈被误当作累计工作量")
            else
                assert(data.repeatedCounts[name] == count * 2,
                    "重复细化覆盖了前一次请求工作量，或改变了确定性候选：" .. name)
            end
        end
    end
    for _, suffix in ipairs({"centerline", "render", "channel_fill", "floodplain", "terminal_lakes", "height_compute"}) do
        local name = prefix .. suffix
        assert(type(data.firstStages[name]) == "number" and data.firstStages[name] >= 0 and
            type(data.repeatedStages[name]) == "number" and data.repeatedStages[name] >= 0,
            "正式河流生成缺少内部诊断阶段：" .. name)
    end
    assert(type(data.repeatedStages["river.local_refine"]) == "number" and
        data.repeatedCounts[prefix .. "region_nodes"] > 0,
        "父细化计时或真实宏观扫描没有进入诊断")
    local queries = data.firstCounts[prefix .. "floodplain_height_queries"]
    local merges = data.firstCounts[prefix .. "floodplain_merge_attempts"]
    local cells = data.firstCounts[prefix .. "floodplain_cells"]
    assert(type(queries) == "number" and queries > 0 and cells > 0 and queries == cells,
        "纯陆地河漫滩没有按唯一候选格只查询一次高度")
    assert(merges > queries and queries <= data.firstCounts[prefix .. "floodplain_candidates"],
        "河漫滩夹具没有覆盖重叠扩张，或重叠格仍重复查询高度")
end, "河流同请求多次细化累计与最近规模分离")

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
