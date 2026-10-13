-- 读取真实租约规则的逐帧输出，检查预加载、折返和清退。
test.space("streaming", function(data)
    assert(data.transitionMeters == 4096 and data.prefetchMeters == 8192, "星区过渡范围与现有规则不一致")
    local origin, left = "0:0", "-1:0"
    local expectations = {
        left = {{origin}, {origin, left}}, right = {{origin}, {origin, "1:0"}},
        down = {{origin}, {origin, "0:-1"}}, up = {{origin}, {origin, "0:1"}},
        corner = {{origin, "1:0", "0:-1", "1:-1"}},
        ["return-release"] = {{origin, left}, {origin, left}, {origin, left}, {left}, {origin, left}, {}},
        observers = {{origin}, {origin, left}, {left}, {}},
        ["fast-jump"] = {{origin}, {"1000000000000000:0"}},
        ["max-corner"] = {{"9223372036854775807:9223372036854775807"}},
        ["min-corner"] = {{"-9223372036854775808:-9223372036854775808"}}
    }
    assert(type(data.scenarios) == "table" and #data.scenarios == 10, "租约场景采样不完整")
    local seen, frameCount = {}, 0
    for _, scenario in ipairs(data.scenarios) do
        local expected = expectations[scenario.name]
        assert(expected and not seen[scenario.name], "未知或重复租约场景:" .. scenario.name)
        seen[scenario.name] = true
        assert(type(scenario.frames) == "table" and #scenario.frames == #expected, scenario.name .. "帧采样缺失")
        for index, frame in ipairs(scenario.frames) do
            local label = scenario.name .. "/第" .. index .. "帧"
            assert(type(frame.required) == "table" and #frame.required == #expected[index], label .. "租约数量错误")
            local required = {}
            for _, sector in ipairs(frame.required) do
                local key = sector.x .. ":" .. sector.y
                assert(not required[key], label .. "星区重复:" .. key)
                required[key] = true
            end
            for _, key in ipairs(expected[index]) do assert(required[key], label .. "缺少星区:" .. key) end
            if #frame.observations == 0 then assert(#frame.required == 0, label .. "无观察者时未清退") end
            frameCount = frameCount + 1
        end
    end
    return "覆盖 " .. #data.scenarios .. " 个租约场景、" .. frameCount .. " 帧及多人保留/折返清退"
end, "四向与角落预加载、多人租约、折返和极值清退")
