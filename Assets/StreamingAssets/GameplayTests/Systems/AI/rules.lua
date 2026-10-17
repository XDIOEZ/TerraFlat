test.system("ai", "hive_resident_schedule", {}, function(data)
    assert(data.preparedCount == 3 and #data.intended == 3, "分帧前没有保存完整的三名初始成员意图")
    for index, guid in ipairs({101, 102, 103}) do
        assert(data.intended[index].guid == guid and data.intended[index].pending, "成员意图缺少唯一身份或待初始化状态")
    end
    assert(data.generatedCount == 5 and data.originalHoney == 6, "重复维护重新分配了初始成员或重抽蜂蜜")
    local expected = {true, false, true, true, false, true}
    for index, accepted in ipairs(expected) do
        assert(data.admissions[index] == accepted, "普通出巢没有按帧限额或护巢被限额阻止")
    end
    assert(data.pendingAfterReload and data.sleepingAfterReload and data.firstGuidAfterReload == 101 and data.resumedCount == 3,
        "创建未完成时保存重载丢失成员意图、睡眠状态或身份")
    assert(data.failedAttemptAdmitted and not data.sameFrameRetry and data.nextFrameRetry,
        "失败尝试在同帧重复执行或不能在下帧重试")
    assert(not data.completedPending and not data.completedSleeping and data.completedSatiety == 321 and data.secondStillPending,
        "成功提交没有清待生成/睡眠状态或误改其它待出巢成员")
    assert(data.remainingCount == 2 and data.survivors[1] == 102 and data.survivors[2] == 103,
        "已登记初始成员死亡后被免费补足或改变剩余成员身份")
end, "蜂巢分帧出巢的持久意图、配额与即时护巢")

-- 正式水生线路保持原小段采样，连续相同液体格只查询一次。
for _, case in ipairs({
    { name = "同格移动", args = { targetX = .9 }, accepted = true, cells = {{x=0,y=0}} },
    { name = "跨三格连续水域", args = {}, accepted = true, cells = {{x=0,y=0},{x=1,y=0},{x=2,y=0}} },
    { name = "不能跳过岸边或未知格", args = { blockedCells = {{x=1,y=0}} }, accepted = false, cells = {{x=0,y=0},{x=1,y=0}} },
    { name = "起点未知立即拒绝", args = { blockedCells = {{x=0,y=0}} }, accepted = false, cells = {{x=0,y=0}} },
    { name = "负坐标向下取格", args = { originX = -.9, targetX = -.1 }, accepted = true, cells = {{x=-1,y=0}} },
    { name = "负坐标跨零格", args = { originX = -.1, targetX = .1 }, accepted = true, cells = {{x=-1,y=0},{x=0,y=0}} },
    { name = "零距离仍读起点", args = { targetX = .1 }, accepted = true, cells = {{x=0,y=0}} },
    { name = "环绕接缝取最短水路", args = { originX = 3.8, targetX = -3.8, wrapped = true }, accepted = true, cells = {{x=3,y=0},{x=-4,y=0}} },
    { name = "环绕接缝不能穿未知水格", args = { originX = 3.8, targetX = -3.8, wrapped = true, blockedCells = {{x=-4,y=0}} }, accepted = false, cells = {{x=3,y=0},{x=-4,y=0}} },
    { name = "镜像起点归一化", args = { originX = 8.1, targetX = 8.9, wrapped = true }, accepted = true, cells = {{x=0,y=0}} }
}) do
    test.system("ai", "aquatic_traversal", case.args, function(data)
        assert(data.accepted == case.accepted, "水生线路可达结果错误：" .. case.name)
        assert(data.queries == #case.cells and #data.visits == #case.cells, "水生线路重复查询同格或漏查跨格：" .. case.name)
        for index, cell in ipairs(case.cells) do
            assert(data.visits[index].x == cell.x and data.visits[index].y == cell.y, "水生线路采样规范格错误：" .. case.name)
        end
        assert(not data.acceptedAfterChange and data.queriesAfterChange > 0, "水域变化后仍复用上一次线路缓存：" .. case.name)
    end, "水生线路查询去重：" .. case.name)
end

-- 自然刷新读取正式目录，独立概率落空也要消耗排队机会。
test.system("ai", "ecology_config", {}, function(data)
    assert(data.fishSafeMin == -26.5 and data.fishSafeMax == 63.5, "鱼的冷热安全范围没有按确认值扩大")
    assert(data.fishWaterOnly, "小鱼自然生成不再限制水域")
    assert(data.chickenChance == .5 and data.chickenWeight == 2.5, "小鸡减半概率改变了原物种权重")
    assert(data.boarChance == 1 and math.abs(data.boarWeight - .2631579) < .000001,
        "小鸡减半影响了野猪默认概率或原权重")
    assert(data.defaultChance == 1, "未配置独立概率的物种没有保持全通过")
end, "自然刷新：小鸡独立机会减半，野猪与权重保持原样")
for _, case in ipairs({
    {chance = .5, sample = 0, accepted = true}, {chance = .5, sample = .4999, accepted = true},
    {chance = .5, sample = .5, accepted = false}, {chance = .5, sample = 1, accepted = false},
    {chance = 0, sample = 0, accepted = false}, {chance = 0, sample = 1, accepted = false},
    {chance = 1, sample = 0, accepted = true}, {chance = 1, sample = 1, accepted = true}
}) do
    test.system("ai", "spawn_chance", {chance = case.chance, sample = case.sample}, function(data)
        assert(data.accepted == case.accepted, "正式独立概率判定边界错误")
    end, "自然刷新概率边界：" .. case.chance .. "/" .. case.sample)
end
for _, case in ipairs({
    {replacement = false, spawned = false, pending = 1, replacements = 2, lifetime = 4},
    {replacement = true, spawned = false, pending = 0, replacements = 1, lifetime = 4},
    {replacement = false, spawned = true, pending = 1, replacements = 2, lifetime = 5},
    {replacement = true, spawned = true, pending = 0, replacements = 1, lifetime = 4}
}) do
    test.system("ai", "spawn_attempt", {replacement = case.replacement, spawned = case.spawned}, function(data)
        assert(data.pending == case.pending and data.replacement == case.replacements,
            "生成机会结束后没有消耗对应普通队列或补员队列")
        assert(data.lifetime == case.lifetime and data.budget == 7 and data.lastDay == 12,
            "概率落空计入出生数、误扣预算或未记录处理日期")
    end, "自然刷新队列：补员=" .. tostring(case.replacement) .. "，出生=" .. tostring(case.spawned))
end

-- 调用真实动物产蛋日历，不把离线时间变成积压产蛋。
local cases = {
    {name = "最短两天", args = {day = 10, minimum = 0, delay = 0, checkDay = 11.99}, deadline = 12, due = false, next = 13.99},
    {name = "截止当天", args = {day = 10, delay = 4, sample = .5, checkDay = 14}, deadline = 14, due = true, next = 18},
    {name = "随机值下界", args = {day = 10, delay = 4, sample = -1, checkDay = 12}, deadline = 12, due = true, next = 14},
    {name = "随机值上界", args = {day = 10, delay = 4, sample = 2, checkDay = 16}, deadline = 16, due = true, next = 22},
    {name = "提高冷却立即生效", args = {day = 10, delay = 0, checkDay = 12, updatedMinimum = 5}, deadline = 12, due = false, next = 14},
    {name = "离线不补发", args = {day = 10, delay = 0, checkDay = 100}, deadline = 12, due = true, next = 102},
    {name = "负延迟归零", args = {day = 10, delay = -4, checkDay = 12}, deadline = 12, due = true, next = 14}
}
for _, case in ipairs(cases) do
    test.system("ai", "egg_schedule", case.args, function(data)
        assert(not data.initiallyDue and data.initialized and not data.rejected, "日历初始化失败")
        assert(math.abs(data.deadline - case.deadline) < .000001, "产蛋截止时间错误")
        assert(data.due == case.due, "冷却资格错误")
        assert(math.abs(data.nextDeadline - case.next) < .000001, "下轮应从实际成功日重新计时")
    end, "产蛋日历：" .. case.name)
end
for _, field in ipairs({"day", "minimum", "delay", "sample"}) do
    test.system("ai", "egg_schedule", {invalid = field}, function(data)
        assert(data.rejected and not data.initiallyDue, "非法非有限时间没有被拒绝")
    end, "产蛋日历拒绝非有限参数：" .. field)
end

for _, blocked in ipairs({false, true}) do
    test.system("ai", "state_machine", {blocked = blocked}, function(data)
        local expected = blocked
            and {"idle.enter", "idle.tick", "idle.tick", "idle.exit", "idle.enter", "idle.tick"}
            or {"idle.enter", "idle.tick", "idle.exit", "transition", "chase.enter", "chase.tick", "chase.exit", "idle.enter", "idle.tick"}
        assert(#data.events == #expected, "状态切换额外执行了节点回调")
        for index, value in ipairs(expected) do assert(data.events[index] == value, "旧节点退出、宿主切换、新节点进入顺序错误") end
        local state = blocked and "idle" or "chase"
        assert(data.afterEvaluation == state and data.afterRejected == state, "禁止或未知状态切换破坏当前节点")
        assert(data.repeatedTransitions == 0 and data.duplicateRejected and data.missingRejected and data.uninitializedRejected, "状态机身份或生命周期保护失效")
        assert(not data.resetInitialized and data.reinitialized and data.chaseAnimation == "Moving", "回收重入或动画语义错误")
    end, blocked and "AI状态机：被拦截时保持旧节点" or "AI状态机：正式评估切换与回收重入")
end
for _, case in ipairs({
    {name = "目标暂时缺失", args = {}, firstMoves = 1, firstArrivals = 0},
    {name = "非有限目标失效", args = {invalid = "nonfinite"}, firstMoves = 1, firstArrivals = 0},
    {name = "到达阈值最少五厘米", args = {targetX = .04, arrivalDistance = -1}, firstMoves = 0, firstArrivals = 1}
}) do
    test.system("ai", "advance_target", case.args, function(data)
        assert(data.firstMovements == case.firstMoves and data.firstArrivals == case.firstArrivals, "有效目标推进或到达阈值错误")
        assert(data.invalidMovements == case.firstMoves and data.invalidStops >= 1, "失效目标继续提交移动")
        assert(data.repeatedArrivalCount == case.firstArrivals + 1, "停在目标处重复通知到达")
        assert(data.returnArrivalCount == case.firstArrivals + 2 and data.arrivals == case.firstArrivals + 3, "离开再到达或节点重入不重新通知")
        assert(data.enters == 2 and data.exits == 2 and data.animation == "Moving", "推进节点生命周期错误")
    end, "AI目标推进：" .. case.name)
end
for _, case in ipairs({
    {name = "前摇与攻击窗口独立", args = {}, immediate = false, restored = 1},
    {name = "零前摇立即进入窗口", args = {windup = 0}, immediate = true, restored = 1},
    {name = "负配置钳制", args = {windup = -1, restore = -1}, immediate = true, restored = 0}
}) do
    test.system("ai", "attack_window", case.args, function(data)
        assert(data.startLocked and data.startActive == case.immediate and data.firstActive == case.immediate, "攻击提前生效或前摇没有锁定")
        assert(data.afterWindupActive, "前摇结束没有开启真实伤害窗口")
        assert(not data.closedActive and data.recoveryActive and data.cooldownAfterClose == 1, "伤害窗口关闭后没有进入后摇冷却")
        assert(not data.recoveryRestartActive, "后摇中重复起手重新开启窗口")
        assert(not data.afterRecoveryLocked and not data.afterRecoveryDone and data.cooldownDone, "后摇和冷却时钟被混为一项")
        assert(data.restored == case.restored and not data.stoppedLocked, "冷却恢复或取消窗口残留临时锁")
    end, "AI攻击时序：" .. case.name)
end
for _, case in ipairs({
    {name = "受击记忆跨状态保留", args = {}, ordinary = 2, previous = false, remembered = true},
    {name = "无来源只登记本状态受击", args = {hasOrigin = false}, ordinary = 2, previous = false, remembered = false},
    {name = "零记忆不造威胁", args = {memory = 0}, ordinary = 2, previous = false, remembered = false},
    {name = "负时间不推进", args = {step = -1}, ordinary = 4, previous = true, remembered = true}
}) do
    test.system("ai", "behavior_memory", case.args, function(data)
        assert(data.ordinaryAfter == case.ordinary and data.worldAfter == 10 and data.previousThreat == case.previous, "模块计时重复推进世界计时或负时间")
        assert(data.damaged and data.remembered == case.remembered, "受击事实或来源记忆错误")
        assert(not data.newStateDamaged and data.newStateThreat == case.remembered and data.stateElapsed == 0, "切换状态未清瞬时事实或丢失持续记忆")
        if case.remembered then assert(data.originX == 7 and data.originY == 8, "实际受击来源位置没有冻结") end
        assert(data.worldAfterWorldTick == 5 and data.originalAfterExportMutation == case.ordinary, "世界计时或导出快照污染原数据")
        assert(data.ordinaryElapsed and not data.finalThreat, "到期计时或威胁记忆不清退")
    end, "AI行为图记忆：" .. case.name)
end
for _, case in ipairs({
    {name = "目标体型扩大感知", args = {}, effective = 8},
    {name = "负半径", args = {radius = -1}, effective = 0},
    {name = "负目标倍率", args = {multiplier = -1}, effective = 0},
    {name = "非法半径", args = {invalidRadius = true}, effective = 0},
    {name = "非法目标倍率回到一", args = {invalidMultiplier = true}, effective = 4},
    {name = "零目标倍率", args = {multiplier = 0}, effective = 0}
}) do
    test.system("ai", "perception_radius", case.args, function(data)
        assert(data.effective == case.effective, "正式目标体型感知规则错误")
    end, "AI感知范围：" .. case.name)
end
