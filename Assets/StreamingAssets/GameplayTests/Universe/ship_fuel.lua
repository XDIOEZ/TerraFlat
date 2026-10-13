-- 氢氧反应直接使用生产燃料和库存入口，管路供给另由真实机器流程覆盖。
local function near(actual, expected, label)
    assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <= 0.0000001, label)
end
test.space("ship_fuel", function(data)
    assert(data.operation == "ship_fuel" and data.scope == "production-domain", "缺少正式燃料领域结果")
    assert(data.hydrogenAdded == true and data.oxygenAdded == true and data.eligible == true,
        "真实二比一氢氧库存应可作航空燃料")
    near(data.availableReactionMoles, 10, "真实反应量应由氢氧限制组分共同决定")
    assert(data.burnAccepted == true, "合格实际燃料没有成功燃烧")
    near(data.hydrogenAfter, (data.availableReactionMoles - data.burnRequestedOxygen) * 2,
        "燃烧氢气扣量未满足二比一比例")
    near(data.oxygenAfter, data.availableReactionMoles - data.burnRequestedOxygen,
        "燃烧氧气扣量错误")
    near(data.energyJoules, data.burnRequestedOxygen * 483600, "生产燃烧能量不匹配实际反应量")
    assert(data.overdrawAccepted == false, "燃料不足时不能部分执行超额反应")
    near(data.overdrawEnergy, 0, "失败的反应不应产生能量")
    near(data.hydrogenAfterFailure, data.hydrogenAfter, "失败反应错误消耗了氢气")
    near(data.oxygenAfterFailure, data.oxygenAfter, "失败反应错误消耗了氧气")
    assert(data.impurityAdded == true and data.impureEligible == false, "过量杂气燃料必须被拒绝")
    assert(data.singleGasPrepared == true and data.singleGasEligible == false, "只有氢气的库存不能作为合格燃料")
    return "覆盖真实二比一燃料、限制组分、原子扣量、能量、超额请求回滚、杂气和缺氧拒绝。"
end, "飞船真实氢氧燃料反应")
