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
