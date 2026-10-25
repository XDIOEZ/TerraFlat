-- 正式植物 Job 同时覆盖栽培和野生植物，耗水不依赖生长状态。
test.system("agriculture", "soil_commit_unchanged", {}, function(data)
    assert(data.uninitialized and data.geographyIndependent and data.geographicChangesIgnored,
        "耕地水肥提交错误依赖或改写地理湿润度和肥力")
    assert(data.equal and data.dryEqual and data.dryStillEqual,
        "已有水肥状态无变化时仍重复提交，包括耗尽后无效扣水/扣肥")
    assert(data.changedRemainder and data.changedFertility and data.wateringChanges,
        "耗水进度、肥力表现差异或有效补水没有触发新提交")
    assert(data.allocation == 0, "土壤幂等性热判断仍产生托管分配")
end, "植物水肥：真实变化提交、干土空操作跳过、初始化与表现完整性")

test.system("agriculture", "farmland_scope", {}, function(data)
    assert(data.initiallyEmpty and data.ordered and data.removed and data.remappedTile and data.environmentKeptIndex,
        "耕地索引遗漏真实地块变化、目录重映射或被水肥更新重建")
    assert(data.normalReadOnly and data.supplyRejected and data.geographicInitial,
        "普通地块被雨水创建农业状态，或新耕地错误继承动态水肥")
    assert(data.snowAdded and data.snowMelted and data.playerSnowAdded and data.playerSnowRemoved,
        "实际雪格索引没有跟随积雪、编辑标记或完全融化更新")
    assert(data.warmSkipped and data.coldKept and data.meltingKept and data.meltedSkipped,
        "暖雨普通地仍参与结算，或落雪/融雪被错误跳过")
end, "仅真实耕地更新水肥，普通地只读地理，暖雨稀疏处理并保留雨雪边界")

for _, row in ipairs({
    {label = "零水为干土", source = 1, water = 0, moisture = 1, expected = 0},
    {label = "五级水分", source = 1, water = 5, moisture = 0, expected = 0.5},
    {label = "十级为湿土", source = 1, water = 10, expected = 1},
    {label = "农业水量优先", source = 1, water = 2, moisture = 1, expected = 0.2},
    {label = "农业缺水层按零水", source = 1, moisture = 1, expected = 0},
    {label = "自然湿润度回退", moisture = 0.7, expected = 0.7},
    {label = "零来源仍读自然值", source = 0, water = 10, moisture = 0.3, expected = 0.3},
    {label = "自然湿润度按十级取整", moisture = 0.34, expected = 0.3},
    {label = "无环境数据用定义默认", defaultWater = 6, expected = 0.6},
    {label = "负水量截断", source = 1, water = -2, expected = 0},
    {label = "超量水截断", source = 1, water = 15, expected = 1}
}) do
    local expected = row
    test.system("agriculture", "soil_moisture_visual", row, function(data)
        assert(math.abs(data.ratio - expected.expected) < 0.00001,
            expected.label .. "没有给地块Shader传入正确的含水比例")
        assert(data.enabled == 1 and data.reserved, expected.label .. "的耕地标记或保留通道错误")
        assert(data.readOnly and data.allocatedBytes == 0, "水分表现查询写回土壤、触发灌溉或产生热分配")
        assert(data.waterLayer and data.sourceLayer and not data.generatedMoistureLayer,
            "耕地水分刷新没有只依赖农业权威层")
        assert(data.fertilityIgnored and data.progressIgnored and data.genericChangeIgnored,
            "肥力、累计进度或通用地形通知被错误当成仅刷新耕地水分")
    end, "耕地干湿Shader输入：" .. row.label)
end

test.system("agriculture", "soil_persistence_keys", {}, function(data)
    assert(data.worldKeyEquivalent and data.missingDoesNotCreate and data.reused and data.isolated,
        "农业差量键缓存改变当前世界、地表/洞穴/星区/船身份，或只读查询创建了空记录")
    assert(data.valuesPreserved and data.currentValues,
        "农业热查询复用缓存后没有读取实时水肥，或覆盖原有作物和差量对象")
    assert(data.bounded and data.cleared and data.replaced,
        "农业键缓存无界增长，或换档清理后返回了旧差量对象")
    assert(data.naturalCropOnly and data.naturalCropRemoved,
        "自然基质播种伪造了普通地块水肥，或作物移除后仍开放只读种植资格")
    assert(data.iterations == 8192 and data.allocatedBytes == 0,
        "已预热的逐格农业差量记录仍重复格式化区块名称和存档键")
end, "农业差量坐标键复用、世界隔离与热提交零分配")

test.system("agriculture", "crop_health_catalog", {}, function(data)
    assert(#data.crops > 0, "未读取到正式作物定义")
    for _, crop in ipairs(data.crops) do
        assert(crop.hp == 20 and crop.maximum == 20, "作物基础生命未统一为20：" .. crop.id)
    end
end, "作物最终继承定义统一20点生命")

test.system("agriculture", "tilling_visual_schedule", {}, function(data)
    assert(data.noEarlyRefresh and data.idleFramesSkipped == 600 and data.startsOnTime,
        "锄地完成后的十秒保持阶段仍逐帧读取，或退回开始时间被推迟")
    assert(data.additionalWorkExtends and data.decayContinues and data.emptyIgnored,
        "再次施工没有延期、退回阶段未保持逐帧推进或空格被错误轮询")
    assert(data.unnecessaryRefreshes == 0 and data.steadyAllocatedBytes == 0,
        "锄地保持期仍触发大量无效扫描或生成 GC 分配")
end, "锄地十秒保持期跳过覆盖层扫描、按时退回及零分配")

for _, row in ipairs({
    {label = "明亮生长", light = 1, grows = true, water = 8},
    {label = "低于门槛停长", light = 0.199, grows = false, water = 8},
    {label = "等于门槛生长", light = 0.2, grows = true, water = 8},
    {label = "黑暗仍耗水", light = 0, grows = false, water = 8},
    {label = "零门槛也不能在全黑生长", minimumLight = 0, light = 0, grows = false, water = 8},
    {label = "自定义光照需求", light = 0.4, minimumLight = 0.5, grows = false, water = 8},
    {label = "成熟仍耗水", mature = true, light = 0, grows = false, water = 8},
    {label = "短日长每天两格", dayLength = 720, light = 0, grows = false, water = 8},
    {label = "长日长每天两格", dayLength = 2880, light = 0, grows = false, water = 8},
    {label = "耗水两倍", waterMultiplier = 2, light = 0, grows = false, water = 6},
    {label = "耐旱十分之一", waterMultiplier = 0.1, light = 0, grows = false, water = 10, remainder = 0.2},
    {label = "缺肥仍耗水", fertility = 0, grows = false, water = 8},
    {label = "缺水停止生长", initialWater = 0, grows = false, water = 0},
    {label = "耗尽后不欠水", initialWater = 1, light = 0, grows = false, water = 0},
    {label = "补算保留耗尽前的生长", initialWater = 1, grows = true, water = 0},
    {label = "半天消耗一格", seconds = 720, light = 0, grows = false, water = 9},
    {label = "温度停长仍耗水", temperatureStress = true, grows = false, water = 8},
    {label = "野生植物黑暗停长耗水", wild = true, light = 0, grows = false, water = 8},
    {label = "野生植物明亮生长耗肥", wild = true, grows = true, water = 8},
    {label = "成熟野生植物仍耗水", wild = true, mature = true, grows = false, water = 8},
    {label = "同一时刻不重复耗水", repeatSameTime = true, light = 0, grows = false, water = 8}
}) do
    local input = {}
    for key, value in pairs(row) do input[key] = value end
    -- water 是预期结果，夹具默认从十格水开始。
    input.water = row.initialWater or 10
    local expected = row
    test.system("agriculture", "plant_growth", input, function(data)
        assert(data.cultivated == data.expectedCultivated and data.soilX == data.expectedSoilX
            and data.soilY == data.expectedSoilY, expected.label .. "的种植身份或地块坐标被 Job 改写")
        assert(data.growWild == 1 and data.scaleByStage == 1 and data.harvested == 0,
            expected.label .. "的野生成长、阶段缩放或收获身份被 Job 改写")
        assert(data.waterInitialized == 1
            and math.abs(data.lastWaterWorldTime - data.expectedWaterWorldTime) < 0.0001,
            expected.label .. "的独立耗水时钟没有推进到当前世界时刻")
        if not expected.temperatureStress then
            assert(data.climateStress == data.initialClimateStress,
                expected.label .. "在没有气候模块时错误改写了气候压力")
        end
        assert((data.progress > data.before) == expected.grows, expected.label .. "的成长门禁错误")
        assert(data.water == expected.water, expected.label .. "的每日耗水错误")
        assert(math.abs(data.waterRemainder - (expected.remainder or 0)) < 0.0001,
            expected.label .. "的小数耗水进度丢失")
        assert((data.usedFertility > 0) == expected.grows, expected.label .. "的肥力消耗错误")
        if expected.repeatSameTime then
            assert(data.repeatedUsedWater == 0, "重复调度同一世界时刻重复耗水")
        end
    end, "植物光照与全天耗水：" .. row.label)
end

test.system("agriculture", "soil", {}, function(data)
    assert(data.fullWater == 10 and data.fullFertility == 1, "补充水肥超出耕地十级上限")
    assert(data.sourceWaterAfterClone == 10 and data.sourceFertilityAfterClone == 1,
        "副本水肥消耗污染原始耕地")
    assert(data.emptyWater == 0 and data.emptyFertility == 0 and data.cloneWater == 0 and data.cloneFertility == 0,
        "超额消耗水肥未保持下限")
    for _, key in ipairs({"negativeAddWater", "negativeConsumeWater", "negativeAddFertility", "negativeConsumeFertility"}) do
        assert(data[key] == "ArgumentOutOfRangeException", "水肥负数输入未拒绝：" .. key)
    end
    assert(data.waterAfterPartialSupply == 2 and data.partialSupply == 0.25 and data.waterAfterWholeSupply == 3,
        "连续补水没有累计满一份后才增加一级")
    assert(data.waterAfterPartialConsumption == 3 and data.partialConsumption == 0.25
        and data.waterAfterWholeConsumption == 2, "连续耗水没有累计满一份后才减少一级")
    assert(data.fullSupplyRemainder == 0 and data.emptyConsumptionRemainder == 0,
        "满水或耗尽后保留了可在后续凭空兑换的进度")
    assert(data.frozenConsumptionBudget == 1.25 and data.emptyConsumptionBudget == 0,
        "作物大时间片的耗水超过冻结预算，会吞掉后续补入的水")
    assert(data.halfWaterMultiplier == 0.5, "五级水分计算成长倍率时发生了整数除法")
end, "耕地水肥上下限、消耗与副本隔离")

for _, row in ipairs({
    {label = "正常补水", water = 2, supply = 1, expected = 3, pending = 0, changed = true},
    {label = "超量截断", water = 9, supply = 5, expected = 10, pending = 0, changed = true},
    {label = "满水跳过", water = 10, supply = 1, expected = 10, pending = 0, changed = false},
    {label = "小量只累计进度", water = 2, supply = 0.25, expected = 2, pending = 0.25, changed = true},
    {label = "累计后增加一级", water = 2, remainder = 0.75, supply = 0.5, expected = 3, pending = 0.25, changed = true},
    {label = "满水丢弃多余进度", water = 9, remainder = 0.75, supply = 0.5, expected = 10, pending = 0, changed = true},
    {label = "浮点增量太小", water = 2, remainder = 0.5, supply = 0.0000000001, expected = 2, pending = 0.5, changed = false},
    {label = "零补水拒绝", water = 2, supply = 0, expected = 2, pending = 0, changed = false},
    {label = "负补水拒绝", water = 2, supply = -1, expected = 2, pending = 0, changed = false},
    {label = "NaN补水拒绝", water = 2, supplyKind = "nan", expected = 2, pending = 0, changed = false},
    {label = "正无穷补水拒绝", water = 2, supplyKind = "positive_infinity", expected = 2, pending = 0, changed = false},
    {label = "负无穷补水拒绝", water = 2, supplyKind = "negative_infinity", expected = 2, pending = 0, changed = false}
}) do
    local input = row
    test.system("agriculture", "surface_water_rule", input, function(data)
        assert(data.changed == input.changed, input.label .. "的真实增加判断错误")
        assert(type(data.nextWater) == "number" and data.nextWater == data.nextWater
            and math.abs(data.nextWater - input.expected) <= 0.00001, input.label .. "的正式补水结果错误")
        assert(data.nextWater == math.floor(data.nextWater) and data.nextWater >= 0 and data.nextWater <= 10,
            input.label .. "产生了小数水分或超出十级范围")
        assert(math.abs(data.nextRemainder - input.pending) <= 0.00001, input.label .. "的连续补水进度错误")
        if not input.changed then
            assert(data.nextWater == data.water, input.label .. "在拒绝或不变时改变了输出水量")
        end
    end, "单格与批量共用补水规则：" .. input.label)
end
test.system("agriculture", "manual_harvest_work", {}, function(data)
    assert(data.defaultSteps == 5, "徒手默认采集次数不是五次")
    assert(data.staminaCost == 1 and data.shakeDuring ~= 0 and data.shakeBefore == 0 and data.shakeAfter == 0,
        "采摘基础体力或抖动时限错误")
    for i = 1, 10 do
        assert(data.completed[i] == (i == 5 or i == 10), "未完成五次就采出物品，或完成后未重置")
        assert(data.progress[i] == i % 5, "徒手进度没有按有效交互累积")
    end
end, "徒手采集：作物和两个实体后端共用五次工作规则")

test.system("agriculture", "manual_harvest_work", { steps = 0 }, function(data)
    for _, complete in ipairs(data.completed) do assert(complete, "非法采集配置没有钳制到至少一次") end
end, "徒手采集：工作次数配置下限")
