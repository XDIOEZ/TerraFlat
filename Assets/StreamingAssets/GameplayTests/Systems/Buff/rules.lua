local cases = {
    {name = "逐层过期", args = {}, initial = 3, stacks = 2, remaining = 1.5, expired = false, ticks = 5},
    {name = "跨多层结束", args = {elapsed = 6}, initial = 3, stacks = 1, remaining = 0, expired = true, ticks = 12},
    {name = "层数上限", args = {stacks = 20, elapsed = .5}, initial = 4, stacks = 4, remaining = 1.5, expired = false, ticks = 1},
    {name = "层数下限", args = {stacks = 0, elapsed = .5}, initial = 1, stacks = 1, remaining = 1.5, expired = false, ticks = 1},
    {name = "持续暴露不脱层", args = {exposed = true}, initial = 3, stacks = 3, remaining = 2, expired = false, ticks = 5},
    {name = "永久状态不按时间到期", args = {permanent = true, elapsed = 3.5}, initial = 3, stacks = 3, remaining = 0, expired = false, ticks = 7},
    {name = "普通到期一次结束", args = {decay = false, elapsed = 3}, initial = 3, stacks = 3, remaining = 0, expired = true, ticks = 4},
    {name = "续时不重播开始", args = {extend = 1}, initial = 3, stacks = 2, remaining = 1.5, expired = false, ticks = 5},
    {name = "零时间不推进", args = {elapsed = 0}, initial = 3, stacks = 3, remaining = 2, expired = false, ticks = 0}
}
for _, case in ipairs(cases) do
    test.system("buff", "lifecycle", case.args, function(data)
        assert(data.initialized and data.initialStacks == case.initial and data.stacks == case.stacks, "状态层数错误")
        assert(math.abs(data.initialScale - (1 + (case.initial - 1) * .25)) < .00001, "叠层视觉倍率错误")
        assert(math.abs(data.remaining - case.remaining) < .00001 and data.expired == case.expired, "持续时间错误")
        assert(data.startCount == 1 and data.stopCount == 1, "Start或Stop重复执行")
        assert(data.tickCount == case.ticks, "周期效果未按实际存活时长执行")
        assert(not data.invalidDurationAccepted, "接受负剩余持续时间")
        assert(data.extended == (case.args.extend == 1), "续时结果错误")
    end, "Buff生命周期：" .. case.name)
end

local function near(actual, expected, label)
    assert(math.abs(actual - expected) < .0001, label .. "：" .. tostring(actual) .. " != " .. tostring(expected))
end
local function attributes(sample, attack, ratio, label, baseAttack)
    near(sample.attack, attack, label .. "固定攻击")
    near(sample.speed, 12 * ratio, label .. "移速")
    near(sample.speedMultiplier, 1.5 * ratio, label .. "未保留原移动倍率")
    near(sample.baseAttack, baseAttack or 10, label .. "污染武器基础攻击")
    assert(sample.ownerCredit and sample.weaponSource, label .. "武器多级Owner归因错误")
end
test.system("buff", "infection_attributes", {}, function(data)
    assert(data.unregisteredHost, "规则夹具意外加载或注册了世界实体")
    assert(data.duration == 300 and data.interval == 10 and data.maximum == 3 and data.mode == "AddStacks" and not data.decay,
        "正式感染目录的时长、周期、三层上限或到期策略错误")
    local effects = {}
    for _, effect in ipairs(data.effects) do effects[effect.type .. "/" .. effect.phase] = effect end
    local damage = effects["core:true_damage/Tick"]
    local attack = effects["core:attack_flat_modifier/Start"]
    local move = effects["core:move_speed_reduction/Start"]
    assert(damage and damage.scaled and damage.value == 1 and attack and attack.scaled and attack.value == -1 and
        move and move.scaled and move.value == .25, "感染掉血、固定减攻或线性减速没有按层配置")
    assert(effects["core:attack_flat_modifier/Stop"] and effects["core:move_speed_reduction/Stop"], "感染缺少属性撤销阶段")
    attributes(data.baseline, 10, 1, "基线")
    attributes(data.one, 9, .75, "一层")
    attributes(data.two, 8, .5, "二层")
    attributes(data.three, 7, .25, "三层")
    for index = 1, 4 do near(data.three.damage[index], index * .7, "四类伤害比例") end
    attributes(data.minimum, 0, .25, "低攻击钳制", 2)
    for index = 1, 4 do near(data.minimum.damage[index], 0, "低攻击没有归零") end
    attributes(data.downTwo, 8, .5, "三降二")
    attributes(data.downOne, 9, .75, "二降一")
    attributes(data.twoSources, 8, .75 * .75, "同角色双来源")
    attributes(data.otherReceiver, 7, .25, "另一角色")
    attributes(data.oneSourceLeft, 9, .75, "删除一个来源")
    attributes(data.otherRestored, 10, 1, "另一角色恢复")
    attributes(data.firstUnchanged, 9, .75, "跨角色隔离")
    attributes(data.restored, 10, 1, "全部清除")
    attributes(data.beforeStart, 10, 1, "开始前设三层")
    attributes(data.startThree, 7, .25, "直接三层开始")
    attributes(data.startThreeRestored, 10, 1, "直接三层结束")
    for stacks = 1, 3 do near(data.periodicLosses[stacks], stacks, "正式周期真实掉血") end
    attributes(data.periodicRestored, 10, 1, "周期状态结束")
end, "正式感染三层属性、武器上下文、周期扣血与来源隔离")

for _, case in ipairs({
    {name = "正常三层减速", args = {}, accepted = true},
    {name = "拒绝三层达到负移速", args = {value = .5}, accepted = false},
    {name = "拒绝周期减速登记", args = {phase = "tick"}, accepted = false},
    {name = "拒绝周期攻击登记", args = {type = "core:attack_flat_modifier", phase = "tick", value = -1}, accepted = false},
    {name = "旧倍率不能伪装按层效果", args = {type = "core:move_speed_multiplier", value = .75}, accepted = false},
    {name = "拒绝负减速值", args = {value = -.25}, accepted = false},
    {name = "固定减攻登记", args = {type = "core:attack_flat_modifier", value = -1}, accepted = true}
}) do
    test.system("buff", "attribute_validation", case.args, function(data)
        assert(data.accepted == case.accepted, "属性定义校验错误：" .. case.name)
    end, "Buff属性定义：" .. case.name)
end
