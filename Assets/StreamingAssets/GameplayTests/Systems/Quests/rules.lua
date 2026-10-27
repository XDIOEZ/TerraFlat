-- 分包路径同时检查任务与配方的正式入口，不读取或写入文件。
for _, kind in ipairs({"quest", "recipe"}) do
    for _, scenario in ipairs({"normal", "dot", "escape", "absolute", "empty", "empty_root", "url", "url_escape"}) do
        local selectedKind, selectedScenario = kind, scenario
        test.system("quests", "catalog_package_path", {kind = selectedKind, scenario = selectedScenario}, function(data)
            local name = selectedKind == "quest" and "任务" or "配方"
            local folder = selectedKind == "quest" and "Quests" or "Recipes"
            if selectedScenario == "normal" or selectedScenario == "url" then
                assert(data.error == "" and data.resolved == data.root .. "/nested/starter.json", "正常分包路径改变")
            elseif selectedScenario == "dot" and not data.web then
                assert(data.error == "" and data.resolved == data.root .. "/starter.json", "本地目录内规范化改变")
            elseif selectedScenario == "empty_root" then
                assert(data.error == "ArgumentException" and data.parameter == (selectedKind == "quest" and "questRoot" or "recipeRoot"),
                    "空根目录的错误类型或参数名改变")
            else
                local prefix
                if selectedScenario == "dot" or selectedScenario == "url_escape" or (selectedScenario == "escape" and data.web) then
                    prefix = "StreamingAssets 相对路径无效："
                elseif selectedScenario == "escape" then
                    prefix = name .. "分包路径越出 " .. folder .. " 目录："
                else
                    prefix = name .. "分包路径无效："
                end
                assert(data.error == "InvalidDataException" and string.find(data.message, prefix, 1, true) == 1,
                    "分包路径拒绝或原错误上下文改变：" .. selectedScenario)
            end
        end, "分包路径：" .. selectedKind .. "/" .. selectedScenario)
    end
end

local cases = {
    {name = "信号推进并钳制目标", args = {}, first = 2, second = 3, valid = true},
    {name = "不同信号类型不推进", args = {signalType = "craft.succeeded"}, first = 0, second = 0, valid = true},
    {name = "不同目标不推进", args = {signalTarget = "test:wood"}, first = 0, second = 0, valid = true},
    {name = "不同维度不推进", args = {signalDimension = "cave"}, first = 0, second = 0, valid = true},
    {name = "不指定目标维度", args = {target = "", dimension = "", signalTarget = "anything", signalDimension = "cave"}, first = 2, second = 3, valid = true},
    {name = "负信号不倒扣进度", args = {current = 1, amount = -2}, first = 1, second = 1, valid = true},
    {name = "大信号不超过目标", args = {amount = 100}, first = 3, second = 3, valid = true},
    {name = "空事件配置拒绝", args = {eventType = ""}, first = 0, second = 0, valid = false}
}
for _, case in ipairs(cases) do
    test.system("quests", "signal_progress", case.args, function(data)
        assert(data.valid == case.valid and data.first == case.first and data.second == case.second, "任务信号过滤或进度上限错误")
        if not case.valid then assert(type(data.error) == "string" and #data.error > 0, "无效配置没有原因") end
    end, "任务信号目标：" .. case.name)
end

for _, case in ipairs({{slots = 2, success = true, amount = 2}, {slots = 1, success = false, amount = 0}, {slots = 0, success = false, amount = 0}}) do
    test.system("quests", "reward_plan", {slots = case.slots}, function(data)
        assert(data.committed == case.success and data.amount == case.amount and data.occupied == case.amount, "正式任务奖励部分发放或未提交")
        assert(data.deferredCount == 1 and data.deferredType == "test:deferred_reward", "任务奖励丢失延迟信号计划")
        if case.success then
            assert(data.firstItem == "test:quest_reward_0", "真实奖励物品没有写入库存")
        else
            assert(type(data.error) == "string" and #data.error > 0, "奖励空间不足没有明确拒绝原因")
        end
    end, "任务原子奖励：可用槽位" .. case.slots)
end
for _, case in ipairs({
    {name = "库存状态目标钳制", args = {}, valid = true, progress = 5},
    {name = "同一库存重复暴露不重复计数", args = {required = 10}, valid = true, progress = 6},
    {name = "不同物品不计数", args = {itemId = "test:other"}, valid = true, progress = 0},
    {name = "空物品参数拒绝", args = {itemId = ""}, valid = false, progress = 0}
}) do
    test.system("quests", "inventory_objective", case.args, function(data)
        assert(data.valid == case.valid and data.progress == case.progress, "真实库存条件目标计数错误")
        assert(data.firstUnchanged == 2 and data.secondUnchanged == 4, "只读任务目标修改了库存")
    end, "任务状态目标：" .. case.name)
end
test.system("quests", "progress_store", {}, function(data)
    assert(data.caseInsensitive and data.readyStatus == "ReadyToClaim" and data.readyProgress == 3, "待领奖状态或大小写身份在存储往返中丢失")
    assert(data.completedStatus == "Completed" and data.rewardsClaimed and data.completionCount == 1, "领取标记或完成次数未保留")
    assert(data.otherValue == 42, "写任务进度覆盖其它系统命名空间")
    assert(data.higherVersionRejected and data.rejectedReadUnchanged, "拒绝读取高版本时改写了原数据")
end, "任务状态存储与领取标记的独立往返")
