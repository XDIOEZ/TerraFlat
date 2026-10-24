-- 来源无关的战斗参数与死亡开关独立执行，保持正式数值和机制接口的原有断言。
local function near(actual, expected, label)
    assert(type(actual) == "number" and math.abs(actual - expected) < .00001, label .. "错误")
end

-- 直接采样正式等级公式，不以Lua重新实现概率规则。
local hitCases = {
    {name = "零命中等级", args = {accuracy = 0, size = 10}, hit = 0},
    {name = "一级命中", args = {accuracy = 1, size = 10}, hit = .1},
    {name = "七级打二级蜜蜂", args = {accuracy = 7, size = 2}, hit = .14},
    {name = "九级打二级目标", args = {accuracy = 9, size = 2}, hit = .18},
    {name = "七级打五级玩家", args = {accuracy = 7, size = 5}, hit = .35},
    {name = "十级打十级", args = {accuracy = 10, size = 10}, hit = 1},
    {name = "零体型", args = {accuracy = 10, size = 0}, hit = 0},
    {name = "低于零级钳制", args = {accuracy = -1, size = 10}, hit = 0},
    {name = "超过十级钳制", args = {accuracy = 11, size = 11}, hit = 1},
    {name = "武器和疲劳分别乘算", args = {accuracy = 7, size = 2, weapon = 1.5, state = .5}, hit = .105}
}
for _, case in ipairs(hitCases) do
    test.system("combat", "logical_hit_levels", case.args, function(data)
        near(data.baseHit, case.hit, "基础命中概率")
        near(data.finalHit, case.hit, "没有额外闪避时命中概率")
    end, "命中等级：" .. case.name)
end
local dodgeCases = {
    {name = "20%独立技能闪避", args = {accuracy = 7, size = 2, dodge = .2}, hit = .112},
    {name = "20%和30%分别乘算", args = {accuracy = 7, size = 2, dodge = .2, secondDodge = .3}, hit = .0784},
    {name = "额外闪避单项不允许100%", args = {accuracy = 7, size = 2, dodge = 1}, hit = .007},
    {name = "负额外闪避不奖励命中", args = {accuracy = 7, size = 2, dodge = -.5}, hit = .14}
}
for _, case in ipairs(dodgeCases) do
    test.system("combat", "logical_hit_levels", case.args, function(data)
        near(data.finalHit, case.hit, "闪避乘算结果")
        near(data.maximumDodge, .95, "单项闪避上限")
    end, "独立闪避：" .. case.name)
end

local damageCases = {
    {name = "只抵扣一次防御", args = {attack = 10, defense = 3}, loss = 7, attackMultiplier = 1},
    {name = "防御完全挡住", args = {attack = 10, defense = 12, received = 2}, loss = 0, attackMultiplier = 1},
    {name = "攻击修正先乘再扣防御", args = {attack = 10, defense = 3, attackMultiplier = 2, received = 2}, loss = 34, attackMultiplier = 1},
    {name = "负攻击无伤害", args = {attack = -1, defense = -3}, loss = 0, attackMultiplier = 1},
    {name = "负倍率钳制", args = {received = -1}, loss = 0, attackMultiplier = 1},
    {name = "怪物攻击玩家", args = {sourcePlayer = false, targetPlayer = true}, loss = 7, attackMultiplier = .5},
    {name = "怪物血量最小归一化", args = {creatureHealth = 0}, loss = 7, attackMultiplier = 20}
}
for _, case in ipairs(damageCases) do
    test.system("combat", "physical", case.args, function(data)
        near(data.physical, case.loss, "物理结算")
        near(data.resolvedTotal, case.loss, "四槽共用结算")
        near(data.attackMultiplier, case.attackMultiplier, "来源攻击参数")
        if case.loss > 0 then near(data.cut, case.loss * .5, "刃伤反馈比例") end
    end, "战斗结算：" .. case.name)
end
test.system("combat", "death_rule_override", {}, function(data)
    assert(data.defaultEnabled and data.explicitDisabled, "死亡模块未接收世界默认或外部公开开关")
    assert(data.retainedDisabled and data.retainedEnabled,
        "重新发布世界默认覆盖了角色的显式死亡开关")
    assert(data.clearedToWorld and data.clearedToDisabled, "清除角色覆盖后没有恢复当前世界默认")
    assert(data.restoredDisabled, "死亡开关覆盖未随模块二进制载荷恢复")
    assert(data.freshPayloadCleared and data.freshUsesWorld, "池壳绑定空载荷后残留上一角色的死亡覆盖")
end, "死亡规则：外部开关、默认更新、覆盖持久化与池壳隔离")

-- 采集奖励复用正式工具专精与掉落选择，不把死亡掉落的数量变成奖励数量。
test.system("combat", "harvest_hit_bonus", {}, function(data)
    near(data.chance, .05, "采集奖励概率")
    assert(data.eligible and data.triggered, "匹配镐子的有效伤害应可获奖")
    assert(data.triggersPer10000 == 500, "5% 抽样区间不正确")
    assert(data.backendEquivalent and data.lootTableUnchanged and data.goItem == "Stone" and data.nativeItem == "Stone",
        "GO 与原生资源必须共用掉落选择")
    assert(data.invalidRollRejected and data.invalidDamageRejected and data.invalidAffinityRejected and
        data.invalidSelectionRejected and data.nullTablesEmpty, "非法参数或空表不应产出奖励")
end, "采集奖励：5% 区间、匹配资格、双后端与非法参数")

local harvestCases = {
    {name = "概率下边界", args = {roll = 0}, eligible = true, triggered = true},
    {name = "概率内侧", args = {roll = .049999}, eligible = true, triggered = true},
    {name = "概率上边界排除", args = {roll = .05}, eligible = true, triggered = false},
    {name = "随机值一不中奖", args = {roll = 1}, eligible = true, triggered = false},
    {name = "斧头砍树", args = {tool = 3, preferred = 3}, eligible = true, triggered = true},
    {name = "小刀1.5倍专精", args = {tool = 5, preferred = 5}, eligible = true, triggered = true},
    {name = "工具不匹配", args = {tool = 3}, eligible = false, triggered = false},
    {name = "没有工具", args = {tool = 0}, eligible = false, triggered = false},
    {name = "目标没有弱点", args = {preferred = 0}, eligible = false, triggered = false},
    {name = "没有实际扣血", args = {damage = 0}, eligible = false, triggered = false},
    {name = "拒绝命中", args = {damage = -1}, eligible = false, triggered = false},
    {name = "客户端不重复掉落", args = {authority = false}, eligible = false, triggered = false},
    {name = "动物不获得玩家奖励", args = {sourcePlayer = false}, eligible = false, triggered = false},
    {name = "环境伤害", args = {environment = true}, eligible = false, triggered = false},
    {name = "真实伤害不套工具奖励", args = {trueDamage = true}, eligible = false, triggered = false},
    {name = "无效来源", args = {validSource = false}, eligible = false, triggered = false}
}
for _, case in ipairs(harvestCases) do
    test.system("combat", "harvest_hit_bonus", case.args, function(data)
        assert(data.eligible == case.eligible and data.triggered == case.triggered, "采集奖励资格或概率不正确")
    end, "采集奖励：" .. case.name)
end

local rewardSelections = {
    {name = "石头区间", args = {selection = .499999}, item = "Stone"},
    {name = "铜矿区间边界", args = {selection = .5}, item = "CopperOre"},
    {name = "末端点", args = {selection = 1}, item = "CopperOre"},
    {name = "空掉落表", args = {entries = {}}, item = ""},
    {name = "零概率零数量空ID跳过", args = {entries = {
        {id = "Disabled", chance = 0}, {id = "Empty", maximum = 0}, {id = "", chance = 1},
        {id = "CopperOre", chance = 1}}}, item = "CopperOre"},
    {name = "全部不可掉落", args = {entries = {{id = "Stone", chance = 0}}}, item = ""},
    {name = "稀有产物按权重选择", args = {selection = .75, entries = {
        {id = "Stone", chance = .75}, {id = "CopperOre", chance = .25}}}, item = "CopperOre"},
    {name = "稀有产物不被等概率放大", args = {selection = .74, entries = {
        {id = "Stone", chance = .75}, {id = "CopperOre", chance = .25}}}, item = "Stone"}
}
for _, case in ipairs(rewardSelections) do
    test.system("combat", "harvest_hit_bonus", case.args, function(data)
        assert(data.goItem == case.item and data.nativeItem == case.item and data.backendEquivalent,
            "单件奖励没有按目标掉落表选择，或两后端结果不同")
    end, "采集奖励掉落表：" .. case.name)
end
