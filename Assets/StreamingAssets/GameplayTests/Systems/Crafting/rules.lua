local cases = {
    {name = "Tag先列不抢Exact材料", args = {}, matched = true, total = 2},
    {name = "Exact先列同样可行", args = {reverse = true}, matched = true, total = 2},
    {name = "同一份材料不可重复计数", args = {birch = 0}, matched = false, total = 0},
    {name = "跨堆叠提供总量", args = {oak = 2, birch = 3, tagDemand = 3, exactDemand = 2}, matched = true, total = 5},
    {name = "有标签但缺精确材料", args = {oak = 0, birch = 3}, matched = false, total = 0},
    {name = "工具必须存在", args = {tool = true}, matched = false, total = 0},
    {name = "零消耗工具不扣数量", args = {tool = true, toolPresent = true}, matched = true, total = 2},
    {name = "只要求工具身份", args = {tool = true, toolPresent = true, tagDemand = 0, exactDemand = 0}, matched = true, total = 0}
}
for _, case in ipairs(cases) do
    test.system("crafting", "allocation", case.args, function(data)
        assert(data.matched == case.matched and math.abs(data.total - case.total) < .00001, "全局扣料计划错误")
        assert(data.oakAfter == (case.args.oak or 1) and data.birchAfter == (case.args.birch or 1), "分配预检不应直接扣库存")
        local total = 0
        for _, entry in ipairs(data.plan) do
            assert(entry.amount > 0 and entry.slot < 2, "不消耗的工具进入了扣料计划")
            total = total + entry.amount
        end
        assert(math.abs(total - case.total) < .00001, "扣料计划明细不一致")
    end, "制作材料分配：" .. case.name)
end

-- 正式木板配方只消耗原木，满耐久或残余耐久的斧头在连续制作后都原样保留。
local plankCases = {
    {name = "满耐久斧头连续制作", args = {}, durability = 1, toolPresent = true, crafted = true},
    {name = "残余耐久斧头连续制作", args = {durability = .25}, durability = .25, toolPresent = true, crafted = true},
    {name = "没有斧头不得制作", args = {toolPresent = false}, toolPresent = false, crafted = false},
    {name = "输出满时保留斧头和原木", args = {fullOutput = true}, durability = 1, toolPresent = true, crafted = false}
}
for _, case in ipairs(plankCases) do
    test.system("crafting", "plank_tool", case.args, function(data)
        assert(data.recipeId == "core:木板_切削合成" and #data.attempts == 3, "没有使用正式木板配方连续制作")
        for index, attempt in ipairs(data.attempts) do
            assert(attempt.matched == case.toolPresent and attempt.prepared == case.crafted
                and attempt.committed == case.crafted, "木板制作条件或事务结果错误")
            assert(attempt.logs == (case.crafted and 3 - index or 3), "木板制作扣除了错误数量的原木")
            assert(attempt.outputAmount == (case.crafted and index * 2 or (case.args.fullOutput and 1 or 0)),
                "木板制作产量或失败后的输出数量错误")
            if case.crafted then
                assert(attempt.outputId == "Plank", "木板配方生成了错误产物")
            elseif case.args.fullOutput then
                assert(attempt.outputId == "test:blocker", "失败制作覆盖了原有输出物")
            end
            assert(attempt.toolPresent == case.toolPresent, "制作木板后斧头消失或凭空生成")
            if case.toolPresent then
                assert(attempt.toolGuid == 1701 and attempt.toolAmount == 1
                    and attempt.toolDurability == case.durability and attempt.toolMaxDurability == 1,
                    "制作木板改变了斧头的身份、数量或耐久")
            end
        end
    end, "木板工具保留：" .. case.name)
end

local transactions = {
    {name = "全产物一次提交", args = {}, prepared = true, committed = true, input = 3, output = 2, finalInput = 3, finalOutput = 2},
    {name = "满包不部分产出或扣料", args = {outputSlots = 1}, prepared = false, committed = false, input = 5, output = 0, finalInput = 5, finalOutput = 0},
    {name = "材料不足不修改库存", args = {amount = 1}, prepared = false, committed = false, input = 1, output = 0, finalInput = 1, finalOutput = 0},
    {name = "回滚恢复输入输出", args = {rollback = true}, prepared = true, committed = true, input = 3, output = 2, finalInput = 5, finalOutput = 0},
    {name = "结构变化拒绝过期计划", args = {changeShape = true}, prepared = true, committed = false, input = 5, output = 0, finalInput = 5, finalOutput = 0}
}
for _, case in ipairs(transactions) do
    test.system("crafting", "transaction", case.args, function(data)
        assert(data.inputBeforeCommit == (case.args.amount or 5) and data.outputBeforeCommit == 0, "预检直接扣料或产生部分产物")
        assert(data.prepared == case.prepared and data.committed == case.committed, "制作事务结果错误")
        assert(not data.duplicateCommitted, "同一制作事务重复提交")
        assert(data.inputAfterCommit == case.input and data.outputAfterCommit == case.output, "正式事务扣料产出错误")
        assert(data.inputFinal == case.finalInput and data.outputFinal == case.finalOutput, "事务最终结果或回滚错误")
    end, "制作原子事务：" .. case.name)
end
