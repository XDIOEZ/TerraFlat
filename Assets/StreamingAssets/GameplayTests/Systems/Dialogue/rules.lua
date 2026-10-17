-- 正式条件求值器接收不同 Facts，不创建气泡或驱动真实角色。
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
    assert(data.issues == 0, "正式自言自语资源存在无效配置")
    assert(#data.incompleteIds == 0 and data.translatedLines == data.lines,
        "正式台词仍缺英文或中英文句数不一致：" .. table.concat(data.incompleteIds, ", "))
end, "正式角色台词完整覆盖中英文")

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
    assert(data.pages[1].title == "命令手册" and string.find(data.pages[1].body, "F4", 1, true), "手册简介缺失入口说明")
    local found = {}
    for index, entry in ipairs(data.entries) do
        assert(entry.category ~= "__dialogue_help_rule", "帮助提供方撤销后仍留有条目")
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
end, "命令目录稳定收集并完整生成独立书页")

test.system("dialogue", "help_command", {}, function(data)
    assert(data.handled and data.feedback == "当前本地玩家尚未就绪。", "帮助命令缺少本地玩家时未拒绝")
end, "帮助命令拒绝未就绪玩家")

test.system("dialogue", "help_command", { arguments = { "extra" } }, function(data)
    assert(data.handled and string.find(data.feedback, "用法：/help", 1, true), "帮助命令未拒绝多余参数")
end, "帮助命令仅接受零参数")

test.system("dialogue", "help_command", { command = "other" }, function(data)
    assert(not data.handled, "帮助处理器误吞了其它命令")
end, "帮助命令保留其它处理器入口")
