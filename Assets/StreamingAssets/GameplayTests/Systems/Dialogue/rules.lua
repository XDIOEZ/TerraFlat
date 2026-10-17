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
