-- 磷酸铁锂电池沿用现有电网结算，独立检查正式配置和储能守恒。
local function near(a, b, message)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= 0.00001, message)
end

for _, row in ipairs({
    {name = "放电功率上限", args = {joules = 1000}, after = 400, discharge = 600, charge = 0},
    {name = "余电耗尽守恒", args = {joules = 50}, after = 0, discharge = 50, charge = 0},
    {name = "空电池无输出", args = {joules = 0}, after = 0, discharge = 0, charge = 0},
    {name = "充电功率上限", args = {charge = true, joules = 0}, after = 600, discharge = 0, charge = 600},
    {name = "即将充满按容量截断", args = {charge = true, joules = 59950}, after = 60000, discharge = 0, charge = 50},
    {name = "满电停止充电", args = {charge = true, joules = 60000}, after = 60000, discharge = 0, charge = 0}
}) do
    test.system("electrical", "lithium_iron_phosphate_battery", row.args, function(data)
        assert(data.role == "battery" and data.voltage == 120 and data.capacity == 60000
            and data.maxCharge == 600 and data.maxDischarge == 600, "磷酸铁锂电池没有复用现有蓄电池参数")
        near(data.preview, data.before, "磷酸铁锂电池预览提前改变储能")
        near(data.after, row.after, "磷酸铁锂电池正式充放电储能不守恒")
        near(data.discharging, row.discharge, "磷酸铁锂电池实际放电超出功率或余量")
        near(data.charging, row.charge, "磷酸铁锂电池实际充电超出功率或容量")
        assert(data.bodyDefinitionId == "Battery_LithiumIronPhosphate"
            and data.summonerDefinitionId == "Battery_LithiumIronPhosphate", "磷酸铁锂电池绑定了其他机器定义")
        for _, pair in ipairs({data.bodyPair, data.summonerPair}) do
            assert(pair.BuildingPrefabId == "Battery_LithiumIronPhosphate"
                and pair.SummonerPrefabId == "Battery_LithiumIronPhosphate_Summoner",
                "磷酸铁锂电池放置或拆回会变成基础电池")
        end
    end, "磷酸铁锂电池正式电网：" .. row.name)
end
