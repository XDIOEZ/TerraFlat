-- 正式条件求值器接收不同 Facts，不创建气泡或驱动真实角色。
-- 事件化温度提醒只安排一次确认；阈值抖动不得抢跑，冷却期间不能丢失候选转换。
test.system("dialogue", "temperature_transition", { samples = {
    { time = 0, temperature = 25 },
    { time = 1, temperature = 9 },
    { time = 3.99, temperature = 9 },
    { time = 4, temperature = 9 },
    { time = 5, temperature = 10.4 },
    { time = 7, temperature = 10.6 },
    { time = 23, temperature = 10.6 },
    { time = 24, temperature = 10.6 }
} }, function(data)
    local samples = data.samples
    assert(not samples[2].changed and math.abs(samples[2].pendingAt - 4) < 0.001,
        "低温首次越界未预约 3 秒确认")
    assert(not samples[3].changed and samples[3].active == "", "低温稳定不足时提前发言")
    assert(samples[4].changed and samples[4].active == "ColdEnter", "稳定低温没有触发进入事件")
    assert(samples[5].active == "ColdEnter", "滞回范围内错误地退出低温")
    assert(samples[6].active == "" and math.abs(samples[6].pendingAt - 24) < 0.001,
        "开始恢复时未撤销旧提示或未遵守共享冷却")
    assert(not samples[7].changed and samples[8].changed and samples[8].active == "ColdExit",
        "恢复事件没有在冷却到期后触发")
end, "温度事件进入、滞回、撤销与冷却后恢复")

test.system("dialogue", "temperature_transition", { samples = {
    { time = 0, temperature = 25 },
    { time = 1, temperature = 41 },
    { time = 2, temperature = 39.4 },
    { time = 3, temperature = 25 },
    { time = 4, temperature = 41 },
    { time = 7, temperature = 41 },
    { time = 8, temperature = 39.4 },
    { time = 11, temperature = 39.4 },
    { time = 27, temperature = 39.4 }
} }, function(data)
    local s = data.samples
    assert(not s[3].changed and s[3].active == "" and s[3].pendingAt == -1,
        "未满足三秒的高温候选恢复正常后仍被保留")
    assert(s[4].pendingAt == -1 and s[4].active == "", "温度恢复正常后仍保留待确认的高温")
    assert(s[6].changed and s[6].active == "HotEnter", "持续高温未触发进入事件")
    assert(s[7].active == "" and not s[7].changed, "高温恢复候选期间旧事件未撤销")
    assert(not s[8].changed and s[9].changed and s[9].active == "HotExit",
        "高温退出应受去抖和冷却共同约束")
end, "温度事件抖动取消、再次确认与热恢复")

for _, row in ipairs({
    { actual = "0.5", expected = "0.3", operator = "Greater", matched = true },
    { actual = "0.5", expected = "0.5", operator = "Greater", matched = false },
    { actual = "0.5", expected = "0.5", operator = "GreaterOrEqual", matched = true },
    { actual = "0.3", expected = "0.5", operator = "Less", matched = true },
    { actual = "0.3", expected = "0.3", operator = "LessOrEqual", matched = true },
    { actual = "1.0", expected = "1", operator = "Equal", matched = true },
    { actual = "RAIN", expected = "rain", operator = "Equal", matched = true },
    { actual = "rain", expected = "snow", operator = "NotEqual", matched = true },
    { exists = false, operator = "Exists", matched = false },
    { exists = false, operator = "NotExists", matched = true },
    { exists = false, operator = "Equal", matched = false }
}) do
    test.system("dialogue", "condition", row, function(data)
        assert(data.matched == row.matched, "Fact 条件判断错误：" .. row.operator)
    end, "台词 Fact 条件：" .. row.operator)
end

local valid = [[{"schemaVersion":1,"entries":[{"id":"probe","topic":"weather","priority":"Ambient","triggers":["Idle"],"conditions":[{"fact":"weather.isRaining","operator":"Equal","value":"true"}],"lines":["下雨了"]}]}]]
test.system("dialogue", "config", { json = valid }, function(data)
    assert(data.count == 1 and data.issues == 0 and data.ids[1] == "probe", "合法台词配置加载失败")
end, "台词正式配置加载")
for _, json in ipairs({ "{}", "{", (string.gsub(valid, "weather.isRaining", "probe.unknown")), (string.gsub(valid, '"schemaVersion":1', '"schemaVersion":99')) }) do
    test.system("dialogue", "config", { json = json }, function(data)
        assert(data.count == 0 and data.issues > 0, "错误台词配置未隔离或未报告问题")
    end, "台词配置拒绝错误 schema、Fact 或 JSON")
end

test.system("dialogue", "builtin_locales", {}, function(data)
    assert(data.entries > 0 and data.lines > 0, "没有加载到正式自言自语资源")
    assert(data.prewarmSlices > 0 and data.cacheReused and data.startupPrewarmSupported,
        "台词配置没有在玩家出生前分帧预热并共享缓存")
    assert(data.issues == 0, "正式自言自语资源存在无效配置")
    assert(#data.incompleteIds == 0 and data.translatedLines == data.lines,
        "正式台词仍缺英文或中英文句数不一致：" .. table.concat(data.incompleteIds, ", "))
    local exhausted = nil
    for _, entry in ipairs(data.externalEntries) do
        if entry.id == "stamina.work.exhausted" then exhausted = entry end
    end
    assert(exhausted and exhausted.externalOnly and exhausted.conditions == 0,
        "筋疲力尽台词必须只由失败的工作主动触发")
    assert(exhausted.priority == "Player" and exhausted.cooldown == 8,
        "体力不足反馈的优先级或防刷屏冷却不正确")
    assert(#exhausted.lines == 4 and exhausted.lines[1] == "我已经筋疲力尽了"
        and exhausted.lines[2] == "让我歇会……", "体力不足台词缺少指定文案或随机变化")

    local expectedFoodIds = {
        "Apple", "Beeswax", "Berry", "BirdEgg", "CactusPiece", "Chili", "Coconut_Half", "CoconutMeat",
        "CookedRice", "Egg", "Egg_Cooked", "Fat", "Flour", "FoxtailSpike", "HerbPoultice", "IceBlock",
        "Jam", "Leaf", "Meat", "Meat_Cooked", "Meat_Cured", "Meat_Dehydrate", "Meat_Rotten", "Mushroom",
        "Orange", "Peach", "Radish", "Rice", "Salt", "Seed_Apple", "Seed_Berry", "Seed_Cotton",
        "Seed_Foxtail", "Seed_Herb", "Seed_Mushroom", "Seed_Radish", "Seed_Willow", "Tea", "WildDogMilk",
        "Corn", "Potato", "Tomato", "Pumpkin", "Watermelon", "Strawberry", "RoastedCorn", "BakedPotato", "FriedPotato", "CookedPumpkin", "Tofu", "TomatoPaste", "Bread", "Seed_Wheat", "Seed_Corn", "Seed_Soybean", "Seed_Potato", "Seed_Tomato", "Seed_Pumpkin", "Seed_Watermelon", "Seed_Strawberry", "Sugarcane", "Seed_Sugarcane", "Seed_TeaLeaf"
    }
    local expectedFoodSet, tasteEntries, spoilageEntries = {}, {}, {}
    local foodEntryCount = 0
    for _, itemId in ipairs(expectedFoodIds) do expectedFoodSet[itemId] = true end
    for _, entry in ipairs(data.externalEntries) do
        local tastePrefix, spoilagePrefix = "food.taste.", "food.spoilage."
        if string.sub(entry.id, 1, #tastePrefix) == tastePrefix then
            foodEntryCount = foodEntryCount + 1
            tasteEntries[string.sub(entry.id, #tastePrefix + 1)] = entry
        elseif string.sub(entry.id, 1, #spoilagePrefix) == spoilagePrefix then
            foodEntryCount = foodEntryCount + 1
            spoilageEntries[string.sub(entry.id, #spoilagePrefix + 1)] = entry
        end
    end
    assert(#expectedFoodIds == 63, "可食用内置物品清单意外变化")
    assert(foodEntryCount == #expectedFoodIds * 2, "食物口味/临期台词存在重复或多余条目")
    local uniqueSpoilageLines, uniqueEnglishSpoilageLines = {}, {}
    for _, itemId in ipairs(expectedFoodIds) do
        for _, entry in ipairs({tasteEntries[itemId] or false, spoilageEntries[itemId] or false}) do
            assert(entry and entry.externalOnly and entry.conditions == 0,
                itemId .. " 缺少外部触发的口味或临期台词")
            assert(entry.once and entry.completionFlag == entry.id,
                itemId .. " 的一次性完成标记未按条目独立保存")
            assert(#entry.lines == 1 and #entry.englishLines == 1,
                itemId .. " 必须各有且只有一条中英文台词")
        end
        local chineseSpoilageLine = spoilageEntries[itemId].lines[1]
        local englishSpoilageLine = spoilageEntries[itemId].englishLines[1]
        assert(not uniqueSpoilageLines[chineseSpoilageLine], "多种食物共用了同一条中文临期台词")
        assert(not uniqueEnglishSpoilageLines[englishSpoilageLine], "多种食物共用了同一条英文临期台词")
        uniqueSpoilageLines[chineseSpoilageLine] = true
        uniqueEnglishSpoilageLines[englishSpoilageLine] = true
    end
    for itemId, _ in pairs(tasteEntries) do assert(expectedFoodSet[itemId], "出现未登记食物的口味台词：" .. itemId) end
    for itemId, _ in pairs(spoilageEntries) do assert(expectedFoodSet[itemId], "出现未登记食物的临期台词：" .. itemId) end
    assert(string.find(spoilageEntries.Berry.lines[1], "酒味", 1, true), "浆果临期台词没有酒味描述")
end, "正式角色台词完整覆盖中英文，体力不足只由工作失败触发")

for _, row in ipairs({
    { itemId = "Berry", remaining = 20, interval = 100, expected = "food.spoilage.Berry", nearExpiry = true },
    { itemId = "Berry", remaining = 20.001, interval = 100, expected = "food.taste.Berry", nearExpiry = false },
    { itemId = "Berry", remaining = 0, interval = 100, expected = "food.spoilage.Berry", nearExpiry = true },
    { itemId = "Berry", remaining = 20, interval = 100, spoilageCompleted = true, expected = "food.taste.Berry", nearExpiry = true },
    { itemId = "Berry", remaining = 20, interval = 100, tasteCompleted = true, expected = "food.spoilage.Berry", nearExpiry = true },
    { itemId = "Berry", remaining = 20, interval = 100, tasteCompleted = true, spoilageCompleted = true, expected = "", nearExpiry = true },
    { itemId = "Apple", remaining = 0, interval = 0, expected = "food.taste.Apple", nearExpiry = false },
    { itemId = "Apple", remaining = 0, interval = -1, expected = "food.taste.Apple", nearExpiry = false },
    { itemId = "", remaining = 0, interval = 100, expected = "", nearExpiry = true }
}) do
    test.system("dialogue", "food_consumption_speech", row, function(data)
        assert(data.entryId == row.expected, "完整食用台词优先级或食物身份错误：" .. row.itemId)
        assert(data.nearExpiry == row.nearExpiry, "腐败剩余时间20%边界判定错误")
    end, "食物自言自语选择与临期边界")
end

for _, row in ipairs({
    { hasState = false, remaining = 0, interval = 0 },
    { hasState = true, enabled = false, elapsed = 80, interval = 100, remaining = 0, capturedInterval = 0 },
    { hasState = true, enabled = true, elapsed = 80, interval = 100, remaining = 20, capturedInterval = 100 },
    { hasState = true, enabled = true, elapsed = 0, interval = 0, remaining = 0, capturedInterval = 0 }
}) do
    test.system("dialogue", "food_consumption_snapshot", row, function(data)
        assert(data.remaining == row.remaining and data.interval == (row.capturedInterval or 0),
            "食用完成快照误用了不存在、关闭或无效的腐败计时")
    end, "食物腐败计时快照")
end

local external = [[{"schemaVersion":1,"entries":[{"id":"probe.external","topic":"stamina","priority":"Player","triggers":["External"],"conditions":[],"lines":["让我歇会"]}]}]]
for _, json in ipairs({external, (string.gsub(external, '"conditions":%[%]', '"conditions":null'))}) do
    test.system("dialogue", "config", { json = json }, function(data)
        assert(data.count == 1 and data.issues == 0 and data.ids[1] == "probe.external",
            "外部事件台词不能使用空条件")
    end, "外部事件台词接受空条件并由点名请求触发")
end
for _, trigger in ipairs({'"External","Idle"', '"External","StateChanged"', '"Debug"'}) do
    test.system("dialogue", "config", { json = string.gsub(external, '"External"', trigger) }, function(data)
        assert(data.count == 0 and data.issues > 0,
            "自动台词仍需条件，Debug 也不能混入正式配置")
    end, "台词拒绝无条件自动触发及调试枚举：" .. trigger)
end

for _, row in ipairs({
    { stamina = 0, cost = 1, accepted = false, remaining = 0, rejected = 1, consumed = 0 },
    { stamina = .5, cost = 1, accepted = false, remaining = .5, rejected = 1, consumed = 0 },
    { stamina = .999, cost = 1, accepted = false, remaining = .999, rejected = 1, consumed = 0 },
    { stamina = 1, cost = 1, accepted = true, remaining = 0, rejected = 0, consumed = 1 },
    { stamina = 2, cost = 1, accepted = true, remaining = 1, rejected = 0, consumed = 1 },
    { stamina = 0, cost = 0, accepted = true, remaining = 0, rejected = 0, consumed = 0 },
    { stamina = 0, cost = -1, accepted = true, remaining = 0, rejected = 0, consumed = 0 },
    { stamina = 0, cost = 1, continuous = true, accepted = true, remaining = 0, rejected = 0, consumed = 0 }
}) do
    test.system("dialogue", "stamina_rejection", row, function(data)
        assert(data.accepted == row.accepted and math.abs(data.stamina - row.remaining) < .00001,
            "体力拒绝反馈改变了正式扣费结果")
        assert(data.rejected == row.rejected and data.consumed == row.consumed,
            "体力不足时未报告拒绝，或成功、免费和持续消耗时误报")
        assert(data.source == (row.rejected == 1 and "flatworld.harvest.manual" or ""),
            "体力不足事件未保留工作的稳定来源")
    end, "体力工作拒绝：" .. row.stamina .. "/" .. row.cost .. (row.continuous and " 持续消耗" or ""))
end

test.system("dialogue", "stamina_rejection", { source = " ", stamina = 0, cost = 1 }, function(data)
    assert(not data.accepted and data.rejected == 1 and data.source == "flatworld.stamina.unspecified",
        "无来源的体力拒绝未归入默认来源")
end, "体力拒绝保留未分类来源")

-- 两种正式容量命令共用边界案例，拒绝时重量和体积均保持原值。
for _, command in ipairs({ "carryweight", "carryvolume" }) do
    for _, row in ipairs({
        { arguments = { "5000" }, parsed = true, capacity = 5000 },
        { arguments = { "100.5" }, parsed = true, capacity = 100.5 },
        { arguments = {}, parsed = false },
        { arguments = { "5000", "kg" }, parsed = false },
        { arguments = { "0" }, parsed = false },
        { arguments = { "-1" }, parsed = false },
        { arguments = { "NaN" }, parsed = false },
        { arguments = { "Infinity" }, parsed = false },
        { arguments = { "1e10000" }, parsed = false },
        { arguments = { "五千" }, parsed = false }
    }) do
        row.command = command
        test.system("dialogue", "carry_weight_command", row, function(data)
            assert(data.parsed == row.parsed, "容量命令的合法数字判断错误：" .. command)
            if row.parsed then
                assert(data.capacity == row.capacity, "容量命令未按原数值解析：" .. command)
                assert(data.feedback == "当前本地玩家尚未就绪。", "缺少玩家时未明确拒绝")
            else
                assert(string.find(data.feedback, "/" .. command .. " 5000", 1, true), "非法参数未返回对应命令示例")
            end
            assert(data.handled and data.playerWeight == 100 and data.playerVolume == 100,
                "拒绝命令后仍修改了玩家重量或体积")
        end, command .. " 参数与未就绪拒绝：" .. (row.arguments[1] or "缺参"))
    end

    test.system("dialogue", "carry_weight_command", { command = command, arguments = { "5000" }, withPlayer = true }, function(data)
        assert(data.handled and data.feedback == "玩家主背包尚未初始化。", "缺少真实背包时未明确拒绝")
        assert(data.playerWeight == 100 and data.playerVolume == 100, "背包未就绪时提前改了玩家容量")
    end, command .. " 拒绝未初始化的主背包")
end

test.system("dialogue", "carry_weight_command", { command = "other", arguments = { "5000" } }, function(data)
    assert(not data.handled, "容量处理器误处理了其它命令")
    assert(data.playerWeight == 100 and data.playerVolume == 100, "其它命令修改了玩家容量")
end, "容量命令保留其它处理器入口")

test.system("dialogue", "help_catalog", {}, function(data)
    assert(#data.probeUsages == 2 and data.probeUsages[1] == "/a" and data.probeUsages[2] == "/z",
        "帮助目录注册替换、去重或稳定排序错误")
    assert(#data.entries >= 3 and #data.pages == #data.entries + 1, "命令手册未按简介与每条一页生成")
    assert(data.pages[1].title == "命令手册" and string.find(data.pages[1].body, "聊天", 1, true)
        and string.find(data.pages[1].body, "木炭", 1, true), "手册简介缺失聊天或自带材料说明")
    assert(not string.find(data.pages[1].body, "F4", 1, true)
        and not string.find(data.pages[1].body, "调试快捷键", 1, true), "手册简介仍包含非聊天入口")
    local found = {}
    for index, entry in ipairs(data.entries) do
        assert(entry.category == "聊天命令" and string.sub(entry.usage, 1, 1) == "/",
            "手册混入了聊天斜杠命令以外的操作")
        local page = data.pages[index + 1]
        assert(page.index == index and page.title == entry.title, "手册页码或标题没有对应操作")
        assert(page.customTitle and page.customBody, "书页未保存为独立物品的实例内容")
        assert(string.find(page.body, entry.category, 1, true) and string.find(page.body, entry.usage, 1, true)
            and string.find(page.body, entry.description, 1, true), "手册遗漏或截断了用法与条件")
        found[entry.usage] = entry.description
    end
    assert(found["/help"] and found["/carryweight 数值"] and found["/carryvolume 数值"], "正式聊天命令未全部登记到手册")
    assert(string.find(found["/carryweight 数值"], "1.5", 1, true)
        and string.find(found["/carryweight 数值"], "kg", 1, true), "重量说明遗漏单位或超额规则")
    assert(string.find(found["/carryvolume 数值"], "L", 1, true)
        and string.find(found["/carryvolume 数值"], "硬上限", 1, true), "体积说明遗漏单位或硬上限")
end, "命令目录只收聊天斜杠命令并完整生成独立书页")

test.system("dialogue", "help_command", {}, function(data)
    assert(data.handled and data.feedback == "当前本地玩家尚未就绪。", "帮助命令缺少本地玩家时未拒绝")
end, "帮助命令拒绝未就绪玩家")

test.system("dialogue", "help_command", { arguments = { "extra" } }, function(data)
    assert(data.handled and string.find(data.feedback, "用法：/help", 1, true), "帮助命令未拒绝多余参数")
end, "帮助命令仅接受零参数")

test.system("dialogue", "help_command", { command = "other" }, function(data)
    assert(not data.handled, "帮助处理器误吞了其它命令")
end, "帮助命令保留其它处理器入口")
