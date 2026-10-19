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

test.system("core_lifecycle", "startup_prefabs", {}, function(data)
    local present = {}
    for _, key in ipairs(data.keys) do
        assert(type(key) == "string" and #key > 0, "启动 UI 清单存在空键")
        assert(not present[key], "启动 UI 清单重复加载：" .. key)
        present[key] = true
    end
    assert(data.networkPanelKey == "UI_NetworkMode", "联机面板资源键偏离正式 Prefab 名称")
    for _, key in ipairs({
        "UI_SliderControl", "UI_MainMenuSettings", "UI_MainMenuExitConfirmation",
        "UI_ModManager", "UI_InputBindingRow", "UI_WorldLoading", "UI_NetworkMode",
        "UI_MobileControls", "UI_MobileControlLayoutEditor"
    }) do
        assert(present[key], "菜单提前就绪时缺少可立即访问的 UI：" .. key)
    end
end, "启动 UI 包含联机与手机面板且资源键唯一")

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

for _, mode in ipairs({ "slow", "instant", "budget", "empty", "failed_start", "failed_complete", "cancel" }) do
    test.system("core_lifecycle", "resource_queue", { mode = mode }, function(data)
        assert(data.maximum <= 4, "资源在途请求超过四个")
        assert(data.ownedBeforeDispose == data.started, "请求发出后没有立即登记会话所有权")
        assert(data.ownedAfterDispose == 0 and data.validAfterDispose == 0, "成功、失败或取消后遗留资源请求")
        if mode == "failed_start" or mode == "failed_complete" then
            assert(data.failure == "InvalidOperationException" and data.stage == "queue", "没有保留队列失败阶段和异常")
            if mode == "failed_start" then assert(data.started == 2, "发出失败后仍继续创建请求") end
        else
            assert(data.failure == nil, "合法资源队列执行失败")
            if mode == "empty" then
                assert(data.started == 0 and data.yielded == 0, "空队列仍发出请求或空等一帧")
            elseif mode == "cancel" then
                assert(data.started == 4 and #data.progress == 0, "取消后仍补发请求或收尾")
            else
                assert(data.started == 6 and #data.progress == 6 and data.progress[6] == 6, "队列遗漏请求或完成进度")
                for i = 1, 6 do
                    assert(data.results[i] == i - 1, "乱序完成改变了资源结果顺序")
                    assert(data.progress[i] == i, "队列完成进度倒退或跳过")
                end
                if mode == "slow" then
                    assert(data.maximum == 4 and data.startedAt[5] < data.completedAt[1], "慢请求堵住已释放的在途槽位")
                elseif mode == "instant" then
                    assert(data.yielded == 0, "已完成资源仍按批次空等下一帧")
                else
                    assert(data.yielded > 0, "请求队列没有归还主线程预算")
                end
            end
        end
    end, "资源队列并发、补槽、顺序与释放：" .. mode)
end

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
