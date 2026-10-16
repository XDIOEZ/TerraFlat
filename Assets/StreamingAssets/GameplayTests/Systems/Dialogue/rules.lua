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
