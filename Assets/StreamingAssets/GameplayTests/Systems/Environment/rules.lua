local function near(a, b, message, tolerance)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= (tolerance or 0.0001), message)
end

for _, sample in ipairs({{current = 20, ambient = 120, seconds = 10},
    {current = 120, ambient = 20, seconds = 10}, {current = 20, ambient = -80, seconds = 10}}) do
    local input = sample
    test.system("environment", "thermal", input, function(data)
        assert(data.whole >= math.min(input.current, input.ambient) and data.whole <= math.max(input.current, input.ambient),
            "环境传热越过目标温度")
        assert(math.abs(data.whole - input.ambient) < math.abs(input.current - input.ambient), "环境传热没有趋近环境")
        near(data.whole, data.split, "一次和十次结算的传热不一致")
        near(data.zeroTime, input.current, "零时间仍改变温度")
        near(data.negativeTime, input.current, "负时间仍改变温度")
        near(data.invalidCurrent, input.ambient, "非法体温没有恢复有限环境值")
        near(data.invalidAmbient, input.current, "非法环境值污染了体温")
        near(data.limited, input.ambient, "固定速率传热没有在目标温度停下")
    end, "环境传热和步长一致性：" .. input.current .. "到" .. input.ambient)
end

test.system("environment", "heat_sources", {}, function(data)
    near(data.center, 1, "热源中心不是完整影响")
    near(data.middle, 0.5625, "热源距离衰减错误")
    near(data.edge, 0, "热源边界仍有影响")
    near(data.outside, 0, "热源范围外仍有影响")
    near(data.single, 40, "寒冷环境没有趋近热源目标")
    near(data.duplicate, data.single, "重叠相同热源叠加了目标温度")
    near(data.hotAmbient, 50, "更冷的普通热源降低了高温环境")
end, "热源距离、目标上限与重叠")

test.system("environment", "wind", {}, function(data)
    assert(data.belowLevel == 7 and data.thresholdLevel == 8, "风级边界错误")
    near(data.belowPush, 0, "低于八级仍推动物品")
    near(data.lightPush, 0.06, "起吹等级速度错误")
    near(data.strongPush, 0.2, "高一级的推动速度错误")
    near(data.maximumPush, 0.5, "最大推动速度错误")
    near(data.unreachablePush, 0, "不可达等级仍推动")
    assert(data.lightMassLevel == 8 and data.cutoffMassLevel == 11 and data.invalidMassLevel == 11,
        "质量边界或非法质量没有影响起吹资格")
    assert(data.zeroGravityLevel == 8, "零重力下起吹资格仍按地球重量计算")
end, "风级、质量与重力推动门槛")

test.system("environment", "weather", {}, function(data)
    assert(data.firstReady and data.secondReady, "同一区域的天气快照缺失")
    near(data.firstRain, 0.8, "区域强制降雨没有进入正式快照")
    near(data.secondRain, data.firstRain, "相邻成员没有共享同一场雨")
    near(data.firstSnow, 0, "暖区普通降雨被误分成雪")
    assert(data.firstRegionX == data.secondRegionX, "同一区域成员归属不一致")
    assert(data.frozenReady and data.frozenTemperature == 10, "后登记区块改写了区域冻结气候")
    near(data.endedPrecipitation, 0, "持续时间结束后降水事件没有停止")
    assert(data.negativeRegionX == -128 and data.negativeRegionY == -128, "负坐标天气区域没有向下归整")
    near(data.rainProbability, 0.12, "自然天气统一概率倍率错误")
    near(data.wind8Probability, 0.06, "八级风附加概率倍率错误")
    near(data.wind9Probability, 0.03, "九级风附加概率倍率错误")
    near(data.wind10Probability, 0.012, "十级风附加概率倍率错误")
end, "区域天气共享、冻结气候、事件到期与强风概率")
