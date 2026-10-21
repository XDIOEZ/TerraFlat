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

-- 正式新增手工配方逐条走匹配与事务，拒绝路径完整对照库存身份、数量和耐久。
test.system("crafting", "handmade_catalog", {}, function(data)
    local enabled = {}
    for _, path in ipairs(data.enabledPackages) do enabled[path] = true end
    assert(enabled["crafting/handmade_equipment.json"] and enabled["crafting/handmade_utilities.json"], "新增手工配方包未正式启用")
    local expected = {
        Axe_Copper = true, Axe_Bronze = true, Axe_RawIron = true, Axe_Iron = true, Dagger_Copper = true,
        Knife_Flint = true, Spear_Stone = true, Spear_Copper = true, Spear_Iron = true, Scissors = true,
        Crucible = true, Paper = true, Nail = true, Door_Wood_Summoner = true, Meatrack_Summoner = true,
        Scarecrow_Summoner = true, CompostBin_Summoner = true, BlastFurnace_Summoner = true,
        Generator_Basic_Summoner = true, Battery_Basic_Summoner = true
    }
    local seen = {}
    local function sameSlots(before, after)
        assert(#before == #after, "失败制作改变了库存槽数")
        for i, slot in ipairs(before) do
            local other = after[i]
            assert(slot.id == other.id and slot.guid == other.guid and slot.amount == other.amount
                and slot.durability == other.durability and slot.maxDurability == other.maxDurability,
                "拒绝制作或保留工具时改变了原物品")
        end
    end
    local function rejected(attempt, matched)
        assert(attempt.matched == matched and not attempt.prepared and not attempt.committed, "制作拒绝条件错误")
        sameSlots(attempt.inputBefore, attempt.inputAfter)
        sameSlots(attempt.outputBefore, attempt.outputAfter)
    end
    assert(#data.recipes == 20, "本轮新增的二十条手工配方数量不完整")
    for _, recipe in ipairs(data.recipes) do
        assert(#recipe.warnings == 0 and recipe.inputKinds <= 5, "配方引用不存在的物品或超出工作台五种输入")
        local normal = recipe.normal
        assert(normal.matched and normal.prepared and normal.committed, "正式手工配方无法完整产出：" .. recipe.id)
        assert(#normal.inputAfter == 5 and #normal.outputAfter == 2, "没有使用工作台正式五输入两输出容量")
        for i, ingredient in ipairs(normal.expectedInput) do
            local slot = normal.inputAfter[i]
            assert(slot.amount == ingredient.after and (ingredient.after == 0 or slot.id == ingredient.id), "新增手工配方扣料数量错误")
            if ingredient.before == ingredient.after then
                sameSlots({normal.inputBefore[i]}, {slot})
            end
        end
        local actual = {}
        for _, slot in ipairs(normal.outputAfter) do
            if slot.amount > 0 then actual[slot.id] = (actual[slot.id] or 0) + slot.amount end
        end
        for _, output in ipairs(recipe.expectedOutputs) do
            assert(output.id ~= "Charcoal" and not string.match(output.id, "^Ingot_"), "温度转变产物误加进手工配方")
            assert(expected[output.id] and not seen[output.id], "手工配方产物重复或超出本轮清单")
            seen[output.id] = true
            assert(actual[output.id] == output.amount, "新增手工配方产物数量错误")
            actual[output.id] = nil
        end
        for id, _ in pairs(actual) do assert(false, "新增手工配方生成额外产物：" .. id) end
        assert(recipe.missing.removedOne, "缺料案例未实际减少消耗材料")
        rejected(recipe.missing, false)
        rejected(recipe.full, true)
        for _, attempt in ipairs(recipe.missingTools) do rejected(attempt, false) end
    end
    for id, _ in pairs(expected) do assert(seen[id], "应补齐的手工产物仍缺配方：" .. id) end
end, "正式新增手工配方：二十项产物、缺料、工具保留及满输出")

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
-- 强化复用正式配方、候选预检、实例克隆、原子扣料和快照恢复。
test.system("crafting", "tool_reinforcement_catalog", {}, function(data)
    assert(#data.tools == data.expectedCount, "正式工具目录缺少强化覆盖样本")
    for _, row in ipairs(data.tools) do
        assert(row.tool and not row.stackable and row.maximum > 0, "子定义标签覆盖导致工具无法强化：" .. row.id)
    end
end, "工具强化：正式斧镐刀弓铲锄鱼竿的标签继承与单件身份")

for _, args in ipairs({ {}, { workbench = true }, { adhesive = true, temperature = 20 } }) do
    test.system("crafting", "tool_reinforcement", args, function(data)
        assert(#data.warnings == 0 and data.matched and data.preview and data.success, "合法工具强化失败")
        assert(data.sourceUnchangedAfterPreview, "强化预览提前修改工具或消耗胶水")
        assert(data.toolRemaining == 0 and data.glueRemaining == 2 and data.outputAmount == 1, "强化没有恰好消耗工具和一份胶水")
        assert(data.outputGuid == 17001 and math.abs(data.maximum - 130) < 0.001 and math.abs(data.durability - 75) < 0.001,
            "强化复制了新工具身份，或提高上限后修复一半缺损的计算错误")
        assert(data.snapshotTags and data.snapshotIdentity and data.repeatRejected, "强化或原实例标签/身份未保存，或允许再次强化")
        assert(math.abs(data.restoredMaximum - 130) < 0.001 and math.abs(data.restoredDurability - 75) < 0.001,
            "强化读档后倍率叠乘或耐久改变")
    end, "工具强化：" .. (args.adhesive and "其他胶水" or args.workbench and "合成台" or "手工"))
end

for _, row in ipairs({
    { name = "低于200度隐藏", args = { temperature = 199.9 }, matched = false },
    { name = "未初始化温度拒绝", args = { uninitialized = true }, matched = false },
    { name = "非法温度拒绝", args = { invalidTemperature = true }, matched = false },
    { name = "已强化工具拒绝", args = { reinforced = true }, matched = false },
    { name = "堆叠工具不复制", args = { stackedTool = true }, matched = true },
    { name = "满输出原子保留材料", args = { full = true }, matched = true },
    { name = "预览后降温提交拒绝", args = { coolBeforeCommit = true }, matched = true }
}) do
    test.system("crafting", "tool_reinforcement", row.args, function(data)
        assert(data.matched == row.matched and not data.success, "非法强化被提交或候选条件错误")
        assert(data.toolRemaining == (row.args.stackedTool and 2 or 1) and data.glueRemaining == 3,
            "强化拒绝仍扣工具或胶水")
        assert(data.outputAmount == 0 and data.sourceUnchangedAfterPreview, "拒绝或预览产生了强化工具")
        if row.args.coolBeforeCommit then
            assert(data.preview and not data.matchedBeforeCommit and data.dataNotifications == 1,
                "降温后的材料状态未通知候选或提交未复核温度")
        end
        if row.args.full then assert(data.outputStillBlocked, "满输出失败改变原槽位") end
    end, "工具强化边界：" .. row.name)
end

test.system("crafting", "reinforcement_editor_roundtrip", {}, function(data)
    assert(data.temperature == 200 and data.excludeTag == "AdhesiveReinforced", "编辑保存丢失温度或重复强化限制")
    assert(data.copyInputTag == "Tool" and data.reinforce and data.outputAmount == 1, "编辑保存破坏实例强化输出")
end, "内容工坊：强化配方字段往返")
