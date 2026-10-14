local function near(actual, expected, label)
    assert(type(actual) == "number" and math.abs(actual - expected) < .00001, label .. "错误")
end
local damageCases = {
    {name = "只抵扣一次防御", args = {attack = 10, defense = 3}, loss = 7, difficulty = 1},
    {name = "防御完全挡住", args = {attack = 10, defense = 12, received = 2}, loss = 0, difficulty = 1},
    {name = "难度先乘再扣防御", args = {attack = 10, defense = 3, difficulty = 2, received = 2}, loss = 34, difficulty = 1},
    {name = "负攻击无伤害", args = {attack = -1, defense = -3}, loss = 0, difficulty = 1},
    {name = "负倍率钳制", args = {received = -1}, loss = 0, difficulty = 1},
    {name = "怪物攻击玩家", args = {sourcePlayer = false, targetPlayer = true}, loss = 7, difficulty = .5},
    {name = "怪物血量最小归一化", args = {creatureHealth = 0}, loss = 7, difficulty = 20}
}
for _, case in ipairs(damageCases) do
    test.system("combat", "physical", case.args, function(data)
        near(data.physical, case.loss, "物理结算")
        near(data.resolvedTotal, case.loss, "四槽共用结算")
        near(data.difficulty, case.difficulty, "来源难度")
        if case.loss > 0 then near(data.cut, case.loss * .5, "刃伤反馈比例") end
    end, "战斗结算：" .. case.name)
end
local bleedCases = {
    {name = "过量伤害裁剪", args = {}, total = 10, tier = 2},
    {name = "无血目标", args = {blood = false}, total = 10, tier = 0},
    {name = "死亡不新加出血", args = {alive = false}, total = 10, tier = 0},
    {name = "纯钝击", args = {cut = 0, pierce = 0, chop = 0, blunt = 20}, total = 10, tier = 0},
    {name = "随机未命中", args = {random = 1}, total = 10, tier = 0},
    {name = "零生命损失", args = {loss = 0}, total = 0, tier = 0},
    {name = "轻刃伤", args = {cut = 2, pierce = 0, chop = 0, blunt = 0, loss = 2}, total = 2, tier = 1},
    {name = "重刃伤", args = {loss = 100}, total = 20, tier = 3}
}
for _, case in ipairs(bleedCases) do
    test.system("combat", "loss_bleeding", case.args, function(data)
        near(data.actualTotal, case.total, "实际生命损失")
        assert(data.bleeding == case.tier, "出血资格或分级错误")
    end, "实际损失与出血：" .. case.name)
end
for _, change in ipairs({"none", "generation", "world", "dimension", "backend", "value", "invalid"}) do
    test.system("combat", "identity", {change = change}, function(data)
        assert(data.originalValid, "基准身份失效")
        assert(data.sameIdentity == (change == "none"), "完整身份没有区分实例来源")
        assert(data.sameAttack == (change == "none"), "攻击去重身份遗漏字段")
        assert(data.changedValid == (change ~= "invalid"), "零编号身份被接受")
    end, "战斗身份：" .. change)
end
test.system("combat", "identity", {changePulse = true}, function(data)
    assert(data.sameIdentity and not data.sameAttack, "不同脉冲被当成同一攻击")
end, "同一来源不同脉冲独立结算")
