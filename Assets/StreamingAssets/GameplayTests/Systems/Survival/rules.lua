local function near(actual, expected, label)
    assert(type(actual) == "number" and math.abs(actual - expected) < .0001, label .. "错误")
end
local oxygenCases = {
    {name = "恢复上限与消耗", args = {}, add = 100, remove = 75, ratio = .75, supply = 7},
    {name = "耗尽不成负数", args = {current = 10, add = -20, remove = -1}, add = 0, remove = 0, ratio = 0, supply = 7},
    {name = "零容量氧气", args = {current = 0, maximum = 0}, add = 0, remove = 0, ratio = 0, supply = 7},
    {name = "零供给时间", args = {seconds = 0}, add = 100, remove = 75, ratio = .75, supply = 0}
}
for _, case in ipairs(oxygenCases) do
    test.system("survival", "oxygen", case.args, function(data)
        near(data.afterAdd, case.add, "氧气恢复上限")
        near(data.afterRemove, case.remove, "氧气消耗下限")
        near(data.ratio, case.ratio, "氧气比例")
        near(data.supplyAmount, case.supply, "外部供氧需求")
        assert(data.afterInvalid == data.afterRemove and data.invalidSupply == 0 and data.unloadedSupplyAccepted == 0, "非法输入或未加载供氧改变储备")
    end, "氧气储备：" .. case.name)
end
for _, case in ipairs({{name = "组合装备来源", args = {}, minimum = 20, maximum = 200}, {name = "真空下限钳制", args = {low = 100}, minimum = 0, maximum = 200}}) do
    test.system("survival", "pressure", case.args, function(data)
        near(data.minimum, case.minimum, "组合耐低压")
        near(data.maximum, case.maximum, "组合耐高压")
        assert(data.replacedMinimum == 35 and data.replacedMaximum == 175, "同来源更新被重复叠加")
        assert(data.restoredMinimum == 50 and data.restoredMaximum == 150, "移除耐压来源未恢复基线")
        assert(data.invalidModifierRejected and data.invalidRangeRejected and data.resetBuffer == 30, "气压参数保护或新生命缓冲错误")
    end, "气压安全范围：" .. case.name)
end
local nutritionCases = {
    {name = "碳水优先然后脂肪", args = {}, consumed = true, before = 18, after = 6, carbs = 0, fat = 3, protein = 3},
    {name = "最后才消耗蛋白质", args = {energy = 18}, consumed = true, before = 18, after = 0, carbs = 0, fat = 0, protein = 0},
    {name = "不足不部分扣除", args = {energy = 100}, consumed = false, before = 18, after = 18, carbs = 10, fat = 5, protein = 3},
    {name = "零能量无扣除", args = {energy = 0}, consumed = true, before = 18, after = 18, carbs = 10, fat = 5, protein = 3}
}
for _, case in ipairs(nutritionCases) do
    test.system("survival", "nutrition", case.args, function(data)
        assert(data.originalCarbohydrates == 10 and data.capacity == 10 and data.waterCapacity == 20, "进食覆盖接收者容量或改变原对象")
        assert(data.water == 20 and data.vitamins == 4, "营养恢复超过容量")
        assert(data.consumed == case.consumed and data.beforeEnergy == case.before and data.afterEnergy == case.after, "能量原子消耗错误")
        assert(data.carbohydrates == case.carbs and data.fat == case.fat and data.protein == case.protein, "能量消耗优先级错误")
        assert(not data.excessiveAccepted and data.afterRejected == data.afterEnergy, "失败能量请求扣了部分营养")
    end, "营养与能量：" .. case.name)
end
local temperatureCases = {
    {name = "有限增温取最强", args = {}, ambient = 28.087641500176, first = 35, strongest = 50, restored = 30},
    {name = "基础体温超过上限不降温", args = {current = 60, ambient = 60}, ambient = 60, first = 60, strongest = 60, restored = 60},
    {name = "多来源不累加", args = {current = 0, ambient = 0}, ambient = 0, first = 10, strongest = 20, restored = 0},
    {name = "环境温度向上趋近", args = {current = 10, ambient = 30}, ambient = 11.912358499824, first = 20, strongest = 30, restored = 10}
}
for _, case in ipairs(temperatureCases) do
    test.system("survival", "thermal", case.args, function(data)
        near(data.ambientNext, case.ambient, "环境热传导")
        near(data.splitNext, data.ambientNext, "分帧热传导一致性")
        near(data.firstWarming, case.first, "首来源增温")
        near(data.strongestWarming, case.strongest, "最强来源增温")
        near(data.afterFirstRemoval, case.strongest, "移除未生效来源")
        near(data.restored, case.restored, "恢复基础体温")
        assert(data.invalidSecondsNext == case.restored and data.savedTemperature == case.restored and data.saveIsIndependent, "非法时间或临时增温污染基础快照")
    end, "角色体温：" .. case.name)
end
local treatmentCases = {
    {name = "等待完成仅提交一次", args = {}, began = true, first = "Running", state = "Completed", commits = 1},
    {name = "主动取消不提交", args = {cancel = true}, began = true, first = "Running", state = "Cancelled", commits = 0},
    {name = "条件失效撤销", args = {invalidate = true}, began = true, first = "Running", state = "Cancelled", commits = 0},
    {name = "库存提交失败", args = {commit = false}, began = true, first = "Running", state = "Failed", commits = 1},
    {name = "初始条件拒绝", args = {valid = false}, began = false, first = "Ready", state = "Cancelled", commits = 0},
    {name = "零等待立即完成", args = {duration = 0, first = 0, second = 0}, began = true, first = "Completed", state = "Completed", commits = 1}
}
for _, case in ipairs(treatmentCases) do
    test.system("survival", "treatment", case.args, function(data)
        assert(data.began == case.began and data.firstState == case.first and data.state == case.state, "治疗会话状态错误")
        assert(data.commits == case.commits and not data.earlyComplete and not data.duplicateComplete, "治疗提前或重复提交")
        if case.first == "Running" then near(data.firstProgress, .5, "治疗等待进度") end
    end, "治疗会话：" .. case.name)
end
