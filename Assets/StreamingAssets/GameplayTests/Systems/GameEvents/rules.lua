local cases = {
    {name = "跨三天收集", args = {}, days = {2, 3, 4}, times = {125, 225, 325}},
    {name = "单次日程", args = {["repeat"] = 0}, days = {2}, times = {125}},
    {name = "隔两天", args = {["repeat"] = 2, new = 525}, days = {2, 4, 6}, times = {125, 325, 525}},
    {name = "尚未到时刻", args = {new = 124}, days = {}, times = {}},
    {name = "加载不补放过去事件", args = {old = 325, new = 425}, days = {5}, times = {425}},
    {name = "已评估日不重复", args = {lastDay = 3}, days = {4}, times = {325}},
    {name = "概率为零不触发", args = {chance = 0}, days = {}, times = {}},
    {name = "日内时间按天长归一化", args = {timeOfDay = 125}, days = {2, 3, 4}, times = {125, 225, 325}}
}
for _, case in ipairs(cases) do
    test.system("game_events", "day_schedule", case.args, function(data)
        assert(data.valid and #data.days == #case.days and #data.times == #case.times, "日程触发数量错误")
        for index, day in ipairs(case.days) do
            assert(data.days[index] == day and data.times[index] == case.times[index], "事件发生日或时间错误")
        end
        assert(data.repeatCount == 0, "同一推进重复触发日程")
    end, "世界事件日历：" .. case.name)
end
for _, args in ipairs({{minimumDay = 0}, {["repeat"] = -1}, {timeOfDay = -1}, {chance = 2}}) do
    test.system("game_events", "day_schedule", args, function(data)
        assert(not data.valid and #data.days == 0 and type(data.error) == "string" and #data.error > 0, "非法日程配置被接受")
    end, "拒绝非法事件日程配置")
end
