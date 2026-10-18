test.system("player", "interaction_completion", {}, function(data)
    assert(not data.wrongTargetAccepted and data.originalPreserved, "不匹配的完成请求清掉当前交互")
    assert(data.accepted and data.currentCleared and data.onlyCompletedRemoved, "完成交互没有撤掉自身目标，或误删其他候选")
    assert(not data.repeatedAccepted and data.completedCancelCount == 0, "重复完成或后续取消再次回调已结束目标")
    assert(not data.staleAccepted and data.nextPreserved and data.nextCancelCount == 1, "旧完成请求污染新交互，或新目标取消失效")
end, "交互完成清理、重复调用与迟到取消隔离")

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

-- 翻滚采样实走模块的发动、体力事务、碰撞缩放和收尾，不推进场景物理。
local function roll_near(actual, expected, label)
    assert(type(actual) == "number" and math.abs(actual - expected) < .0001, label)
end
local function roll_restored(sample)
    assert(not sample.rolling and not sample.invulnerable and not sample.motionLease, "翻滚结束后仍残留动作或租约")
    roll_near(sample.width, .4, "身体宽度没有恢复")
    roll_near(sample.height, .3, "身体高度没有恢复")
    roll_near(sample.visualAngle, 0, "贴图旋转没有归零")
    roll_near(sample.offsetY, -.07, "翻滚改动了碰撞偏移")
end

for _, case in ipairs({
    {args = {x = 3, y = 4}, x = .6, y = .8},
    {args = {x = 1, cursorX = -1, facingX = -1}, x = 1, y = 0},
    {args = {x = 0, y = -1, cursorY = 1}, x = 0, y = -1},
    {args = {cursorX = 3, cursorY = 4, facingX = -1}, x = .6, y = .8},
    {args = {cursorY = -1}, x = 0, y = -1},
    {args = {facingX = -1}, x = -1, y = 0},
    {args = {x = 0, y = -1, facingX = -1}, x = 0, y = -1},
    {args = {facingX = 0}, x = 1, y = 0}
}) do
    test.system("player", "roll_direction", case.args, function(data)
        roll_near(data.x, case.x, "翻滚横向没有遵循移动、准星、朝向的优先级")
        roll_near(data.y, case.y, "翻滚纵向没有遵循移动、准星、朝向的优先级")
    end, "翻滚方向：移动优先、静止朝准星、准星重合回退朝向、对角归一化")
end

test.system("player", "roll_input", {}, function(data)
    assert(data.path == "<Keyboard>/space" and data.actionType == "Button", "翻滚默认空格按钮没有接入输入资产")
    assert(data.editableCount == 1 and not data.initialStateCheck, "翻滚按键未进入重绑或会在启用时重复触发")
end, "翻滚输入：默认空格、可重绑、按住不连续触发")

for _, running in ipairs({false, true}) do
    test.system("player", "roll_action", {running = running, duplicateBody = true, steps = {
        {action = "start", x = 3, y = 4}, {action = "start"}, {seconds = .2}, {seconds = .2},
        {action = "start"}, {action = "cancel"}
    }}, function(data)
        local s = data.samples
        assert(s[1].accepted and s[1].rolling and s[1].invulnerable and s[1].motionLease, "正式翻滚没有同时建立移动和无敌")
        roll_near(s[1].requestedCost, 10, "翻滚基础体力消耗不是10")
        assert(s[1].transactions == 1 and s[1].costSource == "flatworld.movement.roll", "翻滚体力没有使用独立来源事务")
        roll_near(s[1].drivenSpeed, 20, "翻滚没有按步行两倍速度，不应叠加奔跑倍率")
        roll_near(s[1].width, .2, "碰撞宽度没有缩为50%或重复缩放")
        roll_near(s[1].height, .15, "碰撞高度没有缩为50%")
        assert(not s[1].isWalking and not s[1].bodySimulated, "翻滚冒充步行或开启了夹具物理")
        assert(not s[2].accepted and s[2].stamina == s[1].stamina and s[2].transactions == 1, "重复按键扣费或重开了动作")
        roll_near(s[3].visualAngle, 180, "翻滚中点贴图没有转半圈")
        roll_near(s[3].directionX, .6, "中途输入改变了翻滚方向")
        roll_near(s[3].rootAngle, 0, "贴图旋转污染角色物理根节点")
        assert(s[3].spriteFacing < 0 and s[3].invulnerable, "贴图翻面丢失或无敌提前结束")
        roll_restored(s[4])
        roll_near(s[4].cooldown, 0, "翻滚结束后仍有冷却")
        assert(s[5].accepted and s[5].transactions == 2 and s[5].invulnerable and s[5].motionLease,
            "上一轮结束后不能立即再次翻滚，或没有独立支付体力和建立租约")
        roll_restored(s[6])
    end, "翻滚正式动作：尺寸、旋转、无敌、体力、防重入、零冷却接续和奔跑隔离")
end

for _, args in ipairs({{stamina = 0}, {missingBody = true}, {triggerOnly = true}}) do
    args.steps = {{action = "start"}}
    test.system("player", "roll_action", args, function(data)
        local s = data.samples[1]
        assert(not s.accepted and s.transactions == 0 and s.cooldown == 0, "拒绝翻滚仍然扣费或进入冷却")
        roll_restored(s)
    end, "翻滚拒绝：体力不足或缺少真实身体碰撞")
end

for _, action in ipairs({"cancel", "disable", "unload", "locked", "dead"}) do
    test.system("player", "roll_action", {steps = {{action = "start"}, {action = action}}}, function(data)
        assert(data.samples[1].accepted, "取消边界夹具未能发动")
        roll_restored(data.samples[2])
    end, "翻滚中断立即还原：" .. action)
end

test.system("player", "roll_action", {serverRemote = true, reentrant = true, steps = {
    {action = "start"}, {seconds = .4}
}}, function(data)
    local first = data.samples[1]
    assert(first.accepted and first.invulnerable and not first.motionLease, "服务端远端动作没有保持生命权威与位置归属")
    assert(first.moverLocked and not first.bodySimulated and first.transactions == 1 and not first.reentrantAccepted,
        "服务端远端动作改了移动锁/物理或允许扣费回调重入")
    roll_restored(data.samples[2])
end, "翻滚联机权威：远端不加载本地模块，不接管刚体，事务回调不能重入")

test.system("player", "roll_action", {steps = {
    {action = "presentation", sequence = 2}, {action = "presentation", active = false, sequence = 1},
    {action = "presentation", active = false, sequence = 2}, {action = "presentation", sequence = 2}
}}, function(data)
    local s = data.samples
    assert(s[1].rolling and not s[1].invulnerable and s[1].transactions == 0 and not s[1].motionLease,
        "远端视觉错误扣费或建立本地移动/无敌")
    assert(s[2].rolling, "旧停止包取消了新翻滚")
    roll_restored(s[3])
    roll_restored(s[4])
end, "翻滚远端表现：绝对进度、不叠缩放、旧包拒绝、停止后不复活")

test.system("player", "roll_action", {stamina = 0, steps = {
    {action = "owned", sequence = 8, elapsed = .2}, {action = "owned", sequence = 8, elapsed = .1},
    {seconds = .2}, {action = "owned", sequence = 8, elapsed = .1}
}}, function(data)
    local s = data.samples
    assert(s[1].rolling and s[1].motionLease and s[1].invulnerable and s[1].stamina == 0 and s[1].transactions == 0,
        "服务器许可没有使用正式移动，或客户端重复扣费")
    roll_near(s[2].elapsed, .2, "同序号快照倒退或重开动作")
    roll_restored(s[3])
    roll_restored(s[4])
end, "翻滚客户端许可：复用移动租约，不重复扣费，同序号不能续无敌")

test.system("player", "interaction_query_context", {}, function(data)
    assert(data.wrapped == 1 and data.infinite == 0, "交互空间查询没有复用正确拓扑或循环重复了同一目标")
    assert(data.pointerHit == 1 and data.pointerMiss == 0, "空间交互的精确落点范围改变")
    assert(data.denied == 0 and data.inactive == 0, "同轮新查询没有读取最新准入或活动状态")
    assert(data.moved == 0 and data.removed == 0, "移动或注销目标仍残留在交互候选")
end, "交互单轮拓扑：循环接缝、重复视觉、落点与实时失效")

-- 手机准线必须使用有限距离，零力度、推满和异常数值都不能生成无效坐标。
local cursorCases = {
    {name = "普通交互回中", args = {strength = 0}, distance = 0},
    {name = "普通交互半推", args = {strength = .5}, distance = 1},
    {name = "普通交互推满", args = {}, distance = 2},
    {name = "建筑放置推满", args = {maximum = 4}, distance = 4},
    {name = "较大有限距离半推", args = {maximum = 24, strength = .5}, distance = 12},
    {name = "最大距离正无穷", args = {invalid = "maximum_infinity"}, distance = 10},
    {name = "最大距离负无穷", args = {invalid = "maximum_negative_infinity"}, distance = 10},
    {name = "最大距离非数", args = {invalid = "maximum_nan"}, distance = 10},
    {name = "无穷距离回中", args = {invalid = "maximum_infinity", strength = 0}, distance = 0},
    {name = "最小距离无穷", args = {invalid = "minimum_infinity"}, distance = 2},
    {name = "最小距离非数", args = {invalid = "minimum_nan"}, distance = 2},
    {name = "力度非数", args = {invalid = "strength_nan"}, distance = 0},
    {name = "力度超出上限", args = {strength = 2}, distance = 2},
    {name = "负力度", args = {strength = -1}, distance = 0},
    {name = "负距离", args = {minimum = -1, maximum = -2}, distance = 0},
    {name = "最小距离大于最大距离", args = {minimum = 3, maximum = 2}, distance = 3},
    {name = "零方向回退", args = {zeroDirection = true}, distance = 2}
}
for _, case in ipairs(cursorCases) do
    test.system("player", "aim_cursor", case.args, function(data)
        assert(data.finite, "手机准线产生了无效世界坐标")
        assert(type(data.distance) == "number" and math.abs(data.distance - case.distance) < .00001,
            "手机准线没有遵守当前距离与力度")
        assert(math.abs(data.x - case.distance) < .00001 and data.y == 0 and data.z == 0,
            "手机准线方向或二维平面坐标错误")
    end, "手机准线距离：" .. case.name)
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
