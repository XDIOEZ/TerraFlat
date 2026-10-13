-- 领域推进只断言方向分配和惯性算法，供油输入不代表场景机器已经供油。
local function near(actual, expected, label)
    assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <= 0.0000001, label)
end
test.space("ship_propulsion", function(data)
    assert(data.operation == "ship_propulsion" and data.scope == "production-domain", "缺少正式推进领域结果")
    assert(#data.cardinal == 4, "必须返回四个实际安装方向的推进结果")
    for direction, sample in ipairs(data.cardinal) do
        assert(#sample.throttle == 4, "四台喷嘴的出力索引丢失")
        for nozzle, throttle in ipairs(sample.throttle) do
            near(throttle, nozzle == direction and 1 or 0, "方向请求选择了错误的喷嘴")
        end
        near(sample.actualChange.x, sample.request.x, "四向推进横轴实际变化错误")
        near(sample.actualChange.y, sample.request.y, "四向推进纵轴实际变化错误")
    end
    near(data.partialOutputChange.x, 0, "没有横向供油时不能产生横向推进")
    near(data.partialOutputChange.y, 1.6 * 0.5, "纵向半供油未按比例限制推进")
    near(data.pureTurnTranslation.x, 0, "纯转向不应附加横向推进")
    near(data.pureTurnTranslation.y, 0, "纯转向不应附加纵向推进")
    near(data.turnAuthority, 1, "完整转向供油没有形成完整转向能力")
    near(data.coastAfterTenSeconds.x, data.coastInput.x, "松键十秒后横向惯性丢失")
    near(data.coastAfterTenSeconds.y, data.coastInput.y, "松键十秒后纵向惯性丢失")
    near(data.sideways.x, data.coastInput.x, "横向推力错误清除了已有侧向漂移")
    near(data.sideways.y, data.coastInput.y + 60 * 0.02, "横向输入没有施加对应速度变化")
    near(data.reverse.x, data.reverseInput.x - 100 * 0.02, "反向输入必须逐步制动")
    near(data.unpoweredOverspeed.x, 180, "无动力不能擅自限制碰撞后的超速惯性")
    near(data.unpoweredOverspeed.y, 0, "无动力不应新增纵向漂移")
    assert(#data.arrivals == 4 and type(data.arrivalDistance) == "number" and type(data.arrivalSpeed) == "number",
        "缺少多步长导航结果或正式到达阈值")
    for _, arrival in ipairs(data.arrivals) do
        assert(arrival.seconds > 0 and arrival.steps < 10000, "领域导航未在预算步数内到达")
        assert(arrival.distance >= 0 and arrival.distance <= data.arrivalDistance,
            "多步长导航未进入到达距离")
        assert(arrival.speed >= 0 and arrival.speed <= data.arrivalSpeed,
            "多步长导航进入范围时尚未停稳")
    end
    return "覆盖四向喷嘴、缺油比例、纯转向、松键惯性、侧向漂移、反向制动和多步长到点停稳。"
end, "喷嘴推进惯性与自动导航")
