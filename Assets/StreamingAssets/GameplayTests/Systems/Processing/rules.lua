local function near(a, b, message)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= 0.00001, message)
end

test.system("processing", "mineral_content", {}, function(data)
    local mine, iron = data.mine.modules["生命值系统模块"].parameters.Data,
        data.iron.modules["生命值系统模块"].parameters.Data
    assert(mine.MaxHp == 80 and mine.MaxHp > iron.MaxHp and mine.DefenseValues.Physical == 24
        and mine.DefenseValues.Physical > iron.DefenseValues.Physical, "钨矿硬度没有高于正式铁矿")
    assert(data.mine.modules.harvestRequirement.parameters.requiredTool == 1
        and data.mine.entityRuntime == data.iron.entityRuntime, "钨矿未复用镐专精与矿物实体后端")
    assert(#data.loot == 1 and data.loot[1].LootPrefabName == "Ore_Tungsten"
        and data.loot[1].DropChance == 1 and data.loot[1].MinAmount > 0,
        "钨矿没有通过正式掉落表必掉钨矿石")
    local order, tungsten = {}, nil
    for index, rule in ipairs(data.caveRules) do
        order[rule.itemId] = index
        if rule.itemId == "Mine_Tungsten" then tungsten = rule end
    end
    assert(tungsten and math.abs(tungsten.threshold - .94) < .00001 and math.abs(tungsten.scale - .03) < .00001
        and order.Mine_Diamond < order.Mine_Tungsten and order.Mine_Tungsten < order.Mine_Gold,
        "钨矿未登记地下稀有矿脉或破坏了矿物优先顺序")
    assert(data.surfaceCount == 0, "钨矿被错误接入地表自然生成")
    for _, item in ipairs({ data.ore, data.ingot }) do
        assert(item.stackable and item.canBePickedUp and item.weight > 0 and item.volume > 0
            and #item.gameName > 0 and #item.description > 0 and #item.visual.spriteAddress > 0,
            "钨矿石或钨锭缺少库存、名称、描述或贴图定义")
    end
    for _, sample in ipairs({ {id = "Ore_Tungsten", state = data.oreState},
        {id = "Ingot_Tungsten", state = data.ingotState} }) do
        local state = sample.state
        assert(state.added and state.addedAmount == 3 and state.amount == 5,
            "钨物品没有通过正式库存合并为一堆：" .. sample.id)
        assert(state.savedId == sample.id and state.savedAmount == 5 and state.savedTemperature == 1800,
            "钨物品实例快照丢失稳定身份、数量或材料温度：" .. sample.id)
        assert(state.serialized and state.identified and state.networkId == sample.id and state.networkGuid == 9681,
            "钨物品不能复用正式网络负载身份：" .. sample.id)
    end
end, "钨矿地下目录、硬度、镐专精、掉落及钨材料堆叠和快照身份")

for _, sample in ipairs({
    {temperature = 3421.99, matched = false, committed = false},
    {temperature = 3422, matched = true, committed = true},
    {temperature = 3500, matched = true, committed = true},
    {temperature = 3422, amounts = {Ore_Tungsten = 1}, matched = false, committed = false},
    {temperature = 3422, full = true, matched = true, committed = false}
}) do
    test.system("processing", "mineral_reaction", sample, function(data)
        assert(data.minimum == 3422 and data.maximum == 9999 and data.matched == sample.matched
            and data.committed == sample.committed, "钨熔炼的温区、缺料或满槽判定错误")
        if sample.committed then
            assert(data.input[1].amount == 0 and data.output[1].id == "Ingot_Tungsten"
                and data.output[1].amount == 1, "钨熔炼没有按两矿换一锭原子提交")
        else
            assert(data.unchanged, "钨熔炼拒绝后仍扣料或改写输出")
        end
    end, string.format("钨矿正式反应事务：%g℃，满槽%s", sample.temperature, tostring(sample.full == true)))
end

for _, sample in ipairs({{target = 20, watts = 12}, {target = 1000, watts = 600},
    {target = 2000, watts = 1200}, {target = 3500, watts = 2100}}) do
    test.system("processing", "induction_temperature", sample, function(data)
        assert(data.minimum == 20 and data.maximum == 3500 and data.default == 2000 and data.maximumWatts == 2100,
            "电磁炉目标温区、默认值或最高功率错误")
        assert(math.abs(data.requestedWatts - sample.watts) < .001 and data.clamped == sample.target,
            "电磁炉没有按目标温度线性请求电量")
        assert(data.boundarySeconds > 0 and data.boundarySeconds < 400,
            "钨熔点的解析传热到界时间不在正式供热可达范围")
    end, string.format("电磁炉目标%g℃请求%gW", sample.target, sample.watts))
end

for _, input in ipairs({
    { roll = 0, expected = true },
    { roll = 0.0999, expected = true },
    { roll = 0.1, expected = false },
    { roll = 1, expected = false },
    { roll = 0, chance = 0, expected = false, hits = 0 },
    { roll = 1, chance = 1, expected = true, hits = 100 },
}) do
    test.system("processing", "flint_strike", input, function(data)
        near(data.chance, input.chance or 0.1, "打火石默认成功率不是百分之十")
        assert(data.accepted == input.expected, "打火石单次概率判定边界错误")
        assert(data.hits == (input.hits or 10), "打火石连续判定改变概率或出现点击累积保底")
    end, string.format("打火石独立概率与端点：抽样值 %g", input.roll))
end

test.system("processing", "recipe_selection", {}, function(data)
    near(data.sameProgress, 4, "重复绑定同配方清除了真实加工进度")
    near(data.sameRatio, 0.4, "加工进度比例错误")
    near(data.changedProgress, 0, "更换配方保留旧进度")
    assert(data.invalidWork == "ArgumentOutOfRangeException", "零加工工时被接受")
    assert(data.clearedId == "" and data.clearedRatio == 0, "清配方后残留配方或进度显示")
    assert(not data.zeroAdvance and not data.negativeAdvance, "零或负时间仍提交加工")
end, "同配方进度保持、换配方清零与非法工时")

test.system("processing", "slot_identity", {}, function(data)
    near(data.unchangedElapsed, 5, "未变来源清除了整槽计时")
    assert(data.unchangedPending == 3, "未变来源清除了已抽定产量")
    assert(data.changed.elapsed == 0 and data.changed.pending == -1 and data.changed.pendingItem == "",
        "真实来源数量变化没有清除旧计时和待产物")
    assert(data.emptySignature == "" and data.slots == 2 and data.secondElapsed == 0 and data.secondPending == -1,
        "清空来源或新槽默认状态错误")
end, "整槽来源身份变化与已抽定产量重置")

test.system("processing", "atomic_output", {}, function(data)
    assert(not data.blocked.advanced and data.blocked.commits == 0, "满输出时加工仍提交库存")
    assert(data.blocked.progress == 5 and data.blocked.input == 4 and data.blocked.output == 1,
        "输出无法同时放下全部产物时丢进度、扣原料或部分产出")
    assert(data.resumed.advanced and data.resumed.commits == 1, "清出空间后没有恢复加工事务")
    assert(data.resumed.input == 2 and data.resumed.output == 2 and data.resumed.progress == 1,
        "恢复加工没有按实际工时一次扣料并提交两个产物")
    assert(not data.fullAdvanced and data.finalProgress == 1 and data.finalInput == 2 and data.finalOutput == 2
        and data.finalCommits == 1, "再次满输出后继续推进或重复产出")
end, "真实加工进度、满输出原子拒绝与恢复产出")

for _, input in ipairs({
    { current = 100, furnace = 1000, seconds = 1, expected = 109, second = 117.91 },
    { current = 900, furnace = 1000, seconds = 1, expected = 901, second = 901.99 },
    { current = 999, furnace = 1000, seconds = 1, expected = 999.5 },
    { current = 499, furnace = 500, seconds = 2, expected = 500 },
    { current = 499.75, furnace = 500, seconds = 1, expected = 500 },
    { current = 499, furnace = 500, seconds = .1, expected = 499.05 },
    { current = 501, furnace = 500, seconds = 1, expected = 500.5 },
    { current = 500.25, furnace = 500, seconds = 1, expected = 500 },
    { current = 450, furnace = 500, seconds = 10, expected = 455.00062814511 },
    { current = 400, furnace = 500, seconds = 100, expected = 465.51684617685 },
    { current = 100, furnace = 800, seconds = 1, expected = 107 },
    { current = 1100, furnace = 1000, seconds = 1, expected = 1099 },
    { current = 1000, furnace = 1000, seconds = 1, expected = 1000 },
    { current = 100, furnace = 1000, seconds = 0, expected = 100 },
    { current = 100, furnace = 1000, seconds = -1, expected = 100 },
    { current = 100, furnace = 1000, seconds = 100000, expected = 1000 },
}) do
    test.system("processing", "material_heat", input, function(data)
        assert(math.abs(data.next - input.expected) <= 0.0001,
            "炉内材料没有按温差百分比和每游戏秒0.5℃保底升降温")
        assert(math.abs(data.split - data.next) <= 0.001,
            "分次更新和后台累计结算相同时长后材料温度不一致")
        assert(data.next >= math.min(input.current, input.furnace)
            and data.next <= math.max(input.current, input.furnace), "材料温度越过了实际炉温")
        assert(data.amount == 4, "升温扣除了材料数量")
        if input.second then
            assert(math.abs(data.second - input.second) <= 0.0001, "连续升温没有按最新剩余温差计算")
            assert(data.second - data.next < data.next - input.current, "材料越接近炉温没有升得越慢")
        end
        if input.seconds <= 0 or input.current == input.furnace then
            assert(not data.changed, "无温差或非正时间仍报告材料状态变化")
        end
    end, string.format("材料温差百分比传热：%g℃→%g℃，%g秒", input.current, input.furnace, input.seconds))
end

for _, input in ipairs({
    { current = 500, furnace = 501, seconds = 1, heatingRateMultiplier = 0, expected = 500 },
    { current = 500, furnace = 501, seconds = 1, heatingRateMultiplier = .5, expected = 500.25 },
    { current = 500, furnace = 501, seconds = 1, heatingRateMultiplier = 1, expected = 500.5 },
    { current = 500, furnace = 501, seconds = 1, heatingRateMultiplier = 1.5, expected = 500.75 },
    { current = 500, furnace = 501, seconds = 1, heatingRateMultiplier = 10, expected = 501 },
    { current = 100, furnace = 1000, seconds = 1, heatingRateMultiplier = .5, expected = 1000 - 900 * math.sqrt(.99) },
    { current = 100, furnace = 1000, seconds = 1, heatingRateMultiplier = 1.5, expected = 1000 - 900 * .99 ^ 1.5 },
    { current = 1100, furnace = 1000, seconds = 1, heatingRateMultiplier = 0, expected = 1099 },
    { current = 1100, furnace = 1000, seconds = 1, heatingRateMultiplier = .5, expected = 1099 },
    { current = 1100, furnace = 1000, seconds = 1, heatingRateMultiplier = 1.5, expected = 1099 },
    { current = 500.25, furnace = 500, seconds = 1, heatingRateMultiplier = 1.5, expected = 500 },
    { current = 20, furnace = 1000, seconds = 1, heatingRateMultiplier = .5, fixedTransfer = 10, expected = 25 },
    { current = 20, furnace = 1000, seconds = 1, heatingRateMultiplier = 1.5, fixedTransfer = 10, expected = 35 },
    { current = 100, furnace = 20, seconds = 1, heatingRateMultiplier = 1.5, fixedTransfer = 10, expected = 90 },
    { current = 100, furnace = 1000, seconds = 0, heatingRateMultiplier = 1.5, expected = 100 },
}) do
    test.system("processing", "material_heat", input, function(data)
        assert(math.abs(data.next - input.expected) < .0001, "材料换热倍率未只作用于升温，或冷却被错误加速")
        assert(math.abs(data.split - data.next) < .001, "换热倍率分次推进与累计时长不同")
        assert(data.amount == 4, "材料换热倍率改变了数量")
        if input.heatingRateMultiplier == 0 and input.current < input.furnace or input.seconds == 0 then
            assert(not data.changed, "零升温倍率或零时间仍改变材料状态")
        end
    end, string.format("升温倍率%g，仅传热不加快冷却：%g℃→%g℃", input.heatingRateMultiplier, input.current, input.furnace))
end

test.system("processing", "fuel_absorption", {}, function(data)
    assert(not data.premature and data.beforeAmount == 3, "燃料遮罩长满前提前扣了物品")
    near(data.beforeProgress, 2.9 / 3, "燃料遮罩未按三秒一份推进")
    near(data.unchangedElapsed, 2.9, "同一份燃料被观察时重置了进度")
    assert(data.completed and data.consumedId == "test:absorption-fuel" and data.afterAmount == 2,
        "吸收完成后没有通过真实库存准确扣掉一份燃料")
    assert(data.afterProgress == 0 and not data.duplicate, "完成后残留进度或重复扣料")
    assert(not data.removedConsumed and data.returnedElapsed == 0, "中途拿走燃料后仍扣料，或放回后继承了进度")
    assert(data.replacedElapsed == 0 and not data.staleConsumed and data.replacementAmount == 4,
        "同种燃料更换实例后沿用了旧进度，或完成时错误消费了替换物")
    assert(data.invalidElapsed == 0, "零、负数或非有限时间仍推进燃料吸收")
    assert(data.restoredElapsed == 1.5 and data.restoredMatches and data.restoredConsumed,
        "燃料吸收状态序列化后丢进度、槽位身份或无法完成扣料")
    assert(data.clampedProgress == 1 and data.longStepConsumed and data.finalAmount == 0 and data.finalSlotEmpty,
        "单份进度越界，或长步完成后未按真实余量清空槽位")
end, "三秒吸收、完成才扣一份、中途取回与换料重置、重复扣料拒绝与状态保存")

test.system("processing", "cold_fuel_candidates", {}, function(data)
    assert(data.sourceCount == 834 and #data.candidateIds == 2, "后台炉体把全世界掉落物送去排序，或漏掉本格候选")
    assert(data.candidateIds[1] == 2 and data.candidateIds[2] == 1, "本格燃料未按近先顺序，或飞行、邻格、重复身份和不可拾取记录混入候选")
    assert(data.originalPresent and data.shiftedPresent, "候选列表下标移动后错误拒绝仍存在的实物")
    assert(not data.removedPresent and not data.replacedArchivePresent, "已删除实物或被替换的世界档案仍允许扣料")
    assert(data.wrappedCount == 1 and data.wrappedGuid == 90 and data.unwrappedCount == 0,
        "后台炉体借用了其它世界的环绕尺寸或格子")
    assert(data.farRecordStillCold, "筛选本格时补热了远处掉落物")
end, "后台炉体只排序本格候选、距离顺序、身份失效与独立世界环绕")

test.system("processing", "cold_fuel_transfer", {}, function(data)
    near(data.blocked, 0, "拒绝存入的燃料槽仍接收了物品")
    near(data.afterBlocked, 5, "燃料槽拒绝后扣除了地面实物")
    near(data.partial, 2, "容量不足时没有按实际接纳量搬运")
    near(data.sourceAfterPartial, 3, "部分接纳后地面剩余数量错误")
    near(data.targetAfterPartial + data.sourceAfterPartial, 5, "燃料转移前后数量不守恒")
    assert(data.splitIdentity, "部分搬运复用了地面剩余物品的身份")
    near(data.full, 0, "满燃料槽仍重复吸料")
    near(data.sourceAfterFull, 3, "满燃料槽吞掉了地面剩余燃料")
    near(data.resumed, 2, "空出燃料槽后没有恢复正式转移")
    near(data.finalSource + data.finalTarget, 3, "恢复后没有按本次真实余量守恒")
end, "后台燃料实际库存接纳、满槽拒绝、分堆身份与恢复守恒")

for _, sample in ipairs({
    { available = 7, required = 2, expected = 3 },
    { available = 5.5, required = 2, expected = 2 },
    { available = 1.5, required = 2, expected = 0 },
    { available = 7, required = 2, maximum = 2, expected = 2 },
    { available = 7, required = 2, maximum = 0, expected = 0 },
    { available = 7, required = 2, blocked = true, expected = 0 },
    { available = 7, required = 2, temperature = 999, expected = 0 },
    { available = 3000000000, required = 3, expected = 715827882 }
}) do
    local expected, available, required = sample.expected, sample.available, sample.required
    test.system("processing", "reaction_batch", sample, function(data)
        assert(data.matched == (expected > 0) and data.batches == expected,
            "反应批次没有遵守整份材料、温度、锁槽或整数安全上限")
        assert(data.remaining == available, "匹配阶段提前扣除了材料")
        if available < 100 then
            near(data.consumed, expected * required, "批量扣料计划数量错误")
        end
    end, "反应整数批次边界：材料" .. available .. "、每批" .. required .. "、预期" .. expected)
end

test.system("processing", "cold_motion_workspace", {}, function(data)
    near(data.firstPosition, .5, "冷物没有按档案轨迹推进")
    near(data.firstElapsed, .25, "冷物没有保存实际推进时间")
    assert(not data.flyingPickable, "未着地的冷物提前允许拾取")
    near(data.secondPosition, 9, "复用工作区把上个世界的同身份轨迹带入新世界")
    near(data.firstAfterSecond, .5, "推进其它冷世界篡改原世界档案")
    assert(data.landed, "冷物着地后没有结束飞行并恢复拾取")
    near(data.landedPosition, 2, "重新读取档案后没有连续完成原轨迹")
    near(data.middleDepth, .25, "漂浮物水线中点错误")
    near(data.finalDepth, .5, "漂浮物水线终点错误")
    assert(not data.floatingTransition, "浮起过渡没有结束")
    assert(data.sinkingCount == 1 and data.sinkingRetained, "沉底物被删除，或没有结束过渡并保留拾取资格")
    near(data.sinkingDepth, 1, "沉底物后台推进后丢失完全浸没状态")
    near(data.sinkingAmount, 1, "沉底物后台推进后改变了数量")
    assert(data.duplicateError == "ArgumentException", "同世界重复实物身份没有被拒绝")
    near(data.recoveredPosition, .5, "失败后残留工作区污染下一轮冷物推进")
    assert(data.repeatedMotionBytes == 0, "已预热的冷物推进仍逐步重建模拟器或工作列表")
end, "冷物真实轨迹、跨世界工作区复用、浮沉结束、异常清理与热循环分配")
