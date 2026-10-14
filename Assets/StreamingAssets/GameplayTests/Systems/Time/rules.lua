local function near(a, b, message, tolerance)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= (tolerance or 0.000001), message)
end

for _, seconds in ipairs({4321.25, 0, 98765.5}) do
    local value = seconds
    test.system("time", "calendar", {seconds = value}, function(data)
        near(data.beforeSeconds, value, "绝对时间写入丢失日内余数")
        near(data.afterDaySeconds, value, "修改日长改变了绝对秒数")
        near(data.afterYearSeconds, value, "修改年长改变了绝对秒数")
        near(data.afterCycles, data.beforeCycles, "修改年长跳变公转进度")
        near(data.dayLength, 100, "新日长没有生效")
        near(data.yearDays, 48, "公转周期没有按新日长换算")
        near(data.copySeconds, value + 500, "独立时钟副本没有独立推进")
        assert(data.firstSeason == "Spring" and data.firstYear == 1, "第零日的季节或年份错误")
        local sum, asymmetric = 0, false
        assert(#data.circular == 4 and #data.eccentric == 4, "缺少四季采样")
        for index = 1, 4 do
            near(data.circular[index], 0.25, "圆轨道四季没有等分")
            assert(data.eccentric[index] > 0 and data.eccentric[index] < 1, "椭圆轨道季节长度无效")
            sum = sum + data.eccentric[index]
            asymmetric = asymmetric or math.abs(data.eccentric[index] - 0.25) > 0.001
        end
        near(sum, 1, "椭圆轨道四季没有覆盖完整一年")
        assert(asymmetric, "椭圆轨道仍被平均分季")
        assert(data.invalidDay == "ArgumentOutOfRangeException" and data.invalidYear == "ArgumentOutOfRangeException"
            and data.negativeTime == "ArgumentOutOfRangeException", "非法周期或负时间未拒绝")
    end, "绝对时钟、周期修改与四季：" .. value .. "秒")
end
