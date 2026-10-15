-- 移动、奔跑与营养水分的职责沿用正式移动动作配置。
local cases = {
    {name = "静止", args = {moving = false, running = false}, nutrition = 1, water = 1},
    {name = "静止时奔跑开关", args = {moving = false, running = true}, nutrition = 1, water = 1},
    {name = "步行", args = {moving = true}, nutrition = 1.6, water = 1},
    {name = "奔跑", args = {moving = true, running = true}, nutrition = 3.2, water = .25},
    {name = "关闭动作", args = {enabled = false, moving = true, running = true}, nutrition = 1, water = 1},
    {name = "负配置钳制", args = {move = -2, run = -3, water = -4, running = true}, nutrition = 0, water = 0},
    {name = "配置组合", args = {move = 2, run = 3, water = .5, running = true}, nutrition = 6, water = .5}
}
for _, case in ipairs(cases) do
    test.system("player", "movement_cost", case.args, function(data)
        assert(math.abs(data.nutrition - case.nutrition) < .00001, "营养消耗倍率错误")
        assert(math.abs(data.water - case.water) < .00001, "水分消耗倍率错误")
        assert(data.moveConfiguration >= 0 and data.runConfiguration >= 0, "配置未钳制")
    end, "移动消耗：" .. case.name)
end

test.system("player", "trauma_modifiers", {}, function(data)
    local function check(sample, attack, inverted, label)
        assert(type(sample.attack) == "number" and math.abs(sample.attack - attack) < .00001,
            label .. "的创伤攻速倍率错误")
        assert(sample.moveX == (inverted and -2 or 2) and sample.moveY == (inverted and 3 or -3),
            label .. "的创伤移动方向错误")
    end
    check(data.baseline, 1, false, "无 Buff")
    check(data.stacked, .75, true, "同一角色双来源叠加")
    check(data.isolated, 1.8, true, "另一角色独立来源")
    check(data.oneConfusionRemoved, .75, true, "只移除一个混乱")
    check(data.bothConfusionsRemoved, .75, false, "移除全部混乱")
    check(data.oneAttackRemoved, .5, false, "只移除一个攻速修正")
    check(data.replacedAttack, .8, false, "替换同来源攻速修正")
    check(data.otherUnchanged, 1.8, true, "其它角色不受本角色变更影响")
    check(data.cleared, 1, false, "本角色全部修正撤销")
    check(data.otherCleared, 1, false, "另一角色全部修正撤销")
    assert(data.nullAttack == 1, "空角色不应带创伤加成")
end, "创伤 Buff 汇总缓存：多来源叠加、单独移除、更新及角色隔离")
for _, offset in ipairs({{x = 10, y = -20}, {x = -128, y = 256}}) do
    test.system("player", "physics_source", {offsetX = offset.x, offsetY = offset.y}, function(data)
        assert(data.ordinaryResolved and data.imageResolved, "普通碰撞体或镜像碰撞体没有归到真实源")
        assert(data.sourceHierarchyActive and data.collidersDisabled, "物理归源夹具没有遵守活动源层级或启用了实际碰撞")
        assert(data.ownerResolved, "镜像命中无法找到真实Item")
        assert(data.siblingResolved, "镜像命中无法找到真实Item的兄弟模块能力")
        assert(data.offsetX == offset.x and data.offsetY == offset.y, "镜像偏移绑定丢失")
        assert(data.absentCapability and data.nullRejected, "缺失能力或空碰撞源产生了虚假目标")
        assert(data.destroyedRejected and data.clearedRejected, "失效源被镜像自有组件冒充成新的命中目标")
    end, "物理命中镜像归源与兄弟能力：" .. offset.x .. "," .. offset.y)
end
