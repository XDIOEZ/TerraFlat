local function near(actual, expected, label)
    assert(type(actual) == "number" and math.abs(actual - expected) < .0001, label .. "错误")
end

for level = 0, 20 do
    local expectedDrain = level == 0 and .5 or 1 + level * .04
    local expectedReceived = level == 0 and .5 or 1
    test.system("core_lifecycle", "difficulty_rules", {level = level}, function(data)
        near(data.hungerDrain, expectedDrain, "食物与水分共用的消耗倍率")
        near(data.received, expectedReceived, "怪物攻击玩家的受伤倍率")
        near(data.environmentalDamage, 1 + level * .04, "环境伤害倍率保持原规则")
        near(data.creatureAttack, 1 + level * .06, "怪物自身攻击倍率保持原规则")
        near(data.playerAttack, 1 - level * .0175, "玩家攻击倍率保持原规则")
        near(data.damage, 7 * expectedReceived, "受伤减半在扣除防御之后")
    end, "预设难度生存与战斗倍率：" .. level)
end

for _, case in ipairs({
    {name = "玩家攻击", args = {sourcePlayer = true}, damage = 7, received = 1},
    {name = "非玩家目标", args = {targetPlayer = false}, damage = 7, received = 1},
    {name = "环境攻击", args = {environmental = true}, damage = 7, received = 1},
    {name = "未破防", args = {defense = 12}, damage = 0, received = .5},
    {name = "自定义难度", args = {custom = true}, damage = 7, received = 1}
}) do
    test.system("core_lifecycle", "difficulty_rules", case.args, function(data)
        near(data.received, case.received, "受伤倍率适用范围")
        near(data.damage, case.damage, "难度减伤与防御边界")
        if case.args.custom then near(data.hungerDrain, 1, "自定义消耗保持原规则") end
    end, "难度 0 减伤边界：" .. case.name)
end

-- 只执行独立资源计划，阶段依赖和失败清理由生产管线处理。
test.system("core_lifecycle", "resource_plan", { mode = "ordered" }, function(data)
    assert(data.failure == nil, "合法资源依赖被拒绝")
    assert(#data.order == 2 and data.order[1] == "startup" and data.order[2] == "content", "资源没有按依赖执行")
    assert(data.finished == 2, "正常阶段没有释放迭代器")
    local previous = -1
    for _, value in ipairs(data.progress) do
        assert(value >= previous and value >= 0 and value <= 1, "加载进度倒退或越界")
        previous = value
    end
    assert(previous == 1, "全部阶段完成后进度没有达到一")
end, "资源依赖顺序与单调进度")

for _, mode in ipairs({ "missing", "cycle" }) do
    test.system("core_lifecycle", "resource_plan", { mode = mode }, function(data)
        assert(data.failure == "InvalidOperationException" and #data.order == 0, "非法依赖没有在执行前拒绝")
        assert(data.finished == 0, "错误依赖仍执行了资源阶段")
    end, "资源拒绝非法依赖：" .. mode)
end

test.system("core_lifecycle", "resource_plan", { mode = "throw" }, function(data)
    assert(data.failure == "InvalidOperationException" and data.stage == "content", "没有保留失败阶段和异常")
    assert(#data.order == 2 and data.finished == 2, "异常阶段没有执行 finally 清理")
end, "资源阶段异常与清理")

for _, row in ipairs({ { fail = false, commit = true }, { fail = true, commit = true }, { fail = false, commit = false } }) do
    test.system("core_lifecycle", "resource_candidate", row, function(data)
        assert(#data.betweenFrames == 1 and data.betweenFrames[1], "候选加载让出帧时没有恢复正式目录")
        assert(data.disposed == 1 and data.preservedBeforeCommit and data.officialPreserved, "候选释放或正式目录隔离错误")
        if row.fail then
            assert(data.error == "InvalidOperationException" and not data.committed and not data.hasCandidate, "失败候选仍被发布")
        elseif row.commit then
            assert(data.error == nil and data.committed and data.hasCandidate and data.hasSecond and data.hasCleanup, "完成候选没有完整发布或 comparer 丢失")
        else
            assert(data.error == nil and not data.committed and not data.hasCandidate, "尚未提交的候选污染正式目录")
        end
    end, "资源候选跨帧恢复、释放与原子发布")
end
