-- 灯泡两条正式配方各自完成事务，逐种缺料和输出满时保留库存。
test.system("crafting", "handmade_catalog", {
    package = "crafting/electrical.json", definitionIds = {"Thread", "CarbonFilament", "Ingot_Tungsten"}
}, function(data)
    local enabled = false
    for _, path in ipairs(data.enabledPackages) do if path == "crafting/electrical.json" then enabled = true end end
    assert(enabled, "电气配方包未启用")
    local expected = { ["core:electrical_ElectricLightBulb"] = "CarbonFilament",
        ["core:electrical_ElectricLightBulb_Tungsten"] = "Ingot_Tungsten" }
    local count = 0
    local function sameSlots(before, after)
        assert(#before == #after, "灯泡拒绝制作改变了槽数")
        for i, slot in ipairs(before) do
            local other = after[i]
            assert(slot.id == other.id and slot.guid == other.guid and slot.amount == other.amount
                and slot.durability == other.durability and slot.maxDurability == other.maxDurability,
                "灯泡拒绝制作仍改变库存")
        end
    end
    for _, recipe in ipairs(data.recipes) do
        if recipe.expectedOutputs[1].id == "ElectricLightBulb_Summoner" then
            count = count + 1
            local filament = expected[recipe.id]
            assert(filament and recipe.station == "workbench" and #recipe.warnings == 0, "灯泡配方身份、工作台或物品引用错误")
            assert(#recipe.ingredients == 4 and #recipe.expectedOutputs == 1 and recipe.expectedOutputs[1].amount == 1,
                "灯泡应保留三种基础材料并额外消耗一种灯丝，产出一个灯泡")
            local materials = {}
            for _, input in ipairs(recipe.ingredients) do materials[input.id] = input.amount end
            assert(materials.Glass == 1 and materials.CopperPlate == 1 and materials.Ingot_WroughtIron == 1
                and materials[filament] == 1, "灯泡共同材料或灯丝扣量错误")
            assert(not (materials.CarbonFilament and materials.Ingot_Tungsten), "灯泡错误同时要求碳丝和钨")
            local normal = recipe.normal
            assert(normal.matched and normal.prepared and normal.committed, "灯泡替代配方不能独立制作")
            for _, slot in ipairs(normal.inputAfter) do assert(slot.amount == 0, "灯泡没有精确扣除材料") end
            assert(normal.outputAfter[1].id == "ElectricLightBulb_Summoner" and normal.outputAfter[1].amount == 1
                and normal.outputAfter[2].amount == 0, "灯泡产物或数量错误")
            assert(#recipe.missingMaterials == 4, "未逐种检查灯泡缺料")
            for _, rejected in ipairs(recipe.missingMaterials) do
                assert(rejected.removedOne and not rejected.matched and not rejected.prepared and not rejected.committed,
                    "缺少灯丝或共同材料仍能制作灯泡")
                sameSlots(rejected.inputBefore, rejected.inputAfter)
                sameSlots(rejected.outputBefore, rejected.outputAfter)
            end
            assert(recipe.full.matched and not recipe.full.prepared and not recipe.full.committed, "输出满仍提交灯泡制作")
            sameSlots(recipe.full.inputBefore, recipe.full.inputAfter)
            sameSlots(recipe.full.outputBefore, recipe.full.outputAfter)
            expected[recipe.id] = nil
        end
    end
    assert(count == 2 and next(expected) == nil, "灯泡必须恰好有碳丝、钨两种配方，不能保留无灯丝配方")
    local thread = data.definitions.Thread
    local carbon = data.definitions.CarbonFilament
    assert(carbon.id == "CarbonFilament" and carbon.gameName == "碳丝" and carbon.weight > 0
        and carbon.volume > 0 and carbon.stackable, "碳丝未作为可堆叠材料正式注册")
    local transition = thread.matter.transitions[1]
    assert(transition.minTemperature == 500 and transition.outputItemId == "CarbonFilament"
        and transition.outputAmountMultiplier == 1, "细线炭化获取碳丝的定义错误")
    assert(data.definitions.Ingot_Tungsten.id == "Ingot_Tungsten", "钨配方未复用正式钨锭")
end, "灯泡：碳丝与钨替代配方、逐种缺料、满输出和碳丝来源")

-- 机械材料走正式匹配、原子事务、实例封包和建筑品质转移。
test.system("crafting", "mechanical_materials", {}, function(data)
    assert(#data.recipes == 8, "锻锤与锯木机的四种板材/钉子组合不完整")
    local counts = {}
    for _, recipe in ipairs(data.recipes) do
        counts[recipe.output] = (counts[recipe.output] or 0) + 1
        local materials = {}
        for _, input in ipairs(recipe.inputs) do materials[input.id] = input.amount end
        assert(#recipe.inputs == 6 and (materials.IronPlate == 3 or materials.SteelPlate == 3)
            and (materials.Nail == 4 or materials.SteelNail == 4), "机械板材或钉子数量错误")
        local efficiency = materials.SteelPlate == 3 and materials.SteelNail == 4 and 1.2 or 1
        assert(recipe.matched and not recipe.withoutCabinet and not recipe.missing,
            "真实第六格未参加匹配，或缺工具柜/缺一枚钉子仍能制作")
        assert(not recipe.fullPrepared and recipe.afterFull == 4, "输出满仍扣除了柜内钉子")
        assert(recipe.prepared and recipe.committed and recipe.afterCraft == 0 and recipe.sameSlot,
            "共享库存没有真实扣量，或两个工作台看见的是物品副本")
        assert(recipe.afterRollback == 4 and recipe.detachedSlots == 5, "回滚或断开连接丢失柜内物品")
        assert(recipe.staleRejected and recipe.replacementAmount == 4, "换成同槽数的新工具柜后仍提交旧计划")
        assert(math.abs(recipe.efficiency - efficiency) < .00001
            and math.abs(recipe.halfReverse - efficiency / 2) < .00001 and recipe.stopped == 0,
            "全钢效率加成、反转半速或停转规则错误")
        assert(recipe.durabilityMultiplier == 1, "加工效率错误改变了机器耐久")
    end
    assert(counts.MechanicalHammer_Summoner == 4 and counts.Sawmill_Summoner == 4, "配方组合归属错误")
end, "机械材质：全钢加速20%、真实共享扣料、缺料/满输出/回滚与品质持久化")

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

-- 木盾读取正式配方，固定检查木板、绳子与三钉一次加固。
for _, row in ipairs({
    {name = "五木板五绳子", args = {}, success = true, planks = 0, ropes = 0},
    {name = "只扣配方所需材料", args = {planks = 6, ropes = 7}, success = true, planks = 1, ropes = 2},
    {name = "少一块木板拒绝", args = {planks = 4}, success = false, planks = 4, ropes = 5},
    {name = "少一根绳子拒绝", args = {ropes = 4}, success = false, planks = 5, ropes = 4},
    {name = "木条不能代替木板", args = {plankId = "Stick_Wood"}, success = false, planks = 5, ropes = 5},
    {name = "藤蔓不能代替绳子", args = {ropeId = "Twine"}, success = false, planks = 5, ropes = 5},
    {name = "满输出保留全部材料", args = {full = true}, success = false, planks = 5, ropes = 5}
}) do
    test.system("crafting", "wood_shield_recipe", row.args, function(data)
        assert(#data.warnings == 0 and data.success == row.success, "木盾正式配方产出或材料条件错误")
        assert(data.planks == row.planks and data.ropes == row.ropes, "木盾没有恰好扣除五木板五绳子，或失败仍扣料")
        if row.success then
            assert(data.outputId == "Shield_Wood" and data.outputAmount == 1, "木盾配方产物身份或数量错误")
        elseif row.args.full then
            assert(data.outputStillBlocked, "木盾制作失败覆盖输出阻塞物")
        else
            assert(not data.matched and data.outputAmount == 0, "木盾材料不足或错误仍出现候选或产物")
        end
    end, "木盾合成：" .. row.name)
end

for _, row in ipairs({
    {name = "满耐久同步提高", args = {durability = 100}, maximum = 150, durability = 150, quality = 1.5},
    {name = "破损比例保持", args = {}, maximum = 150, durability = 60, quality = 1.5},
    {name = "工作台加固", args = {workbench = true}, maximum = 150, durability = 60, quality = 1.5},
    {name = "保留已有胶黏品质和标记", args = {quality = 1.3, durability = 75, adhesive = true}, maximum = 195, durability = 112.5, quality = 1.95}
}) do
    test.system("crafting", "wood_shield_reinforcement", row.args, function(data)
        assert(#data.warnings == 0 and data.matched and data.preview and data.success, "木盾合法钉子加固失败")
        assert(data.previewUnchanged, "加固预览提前修改盾牌、标签或钉子")
        assert(data.shieldRemaining == 0 and data.nailsRemaining == 3 and data.outputAmount == 1 and data.outputId == "Shield_Wood",
            "钉子加固未恰好消耗三钉和一盾，或复制出额外盾牌")
        assert(math.abs(data.maximum - row.maximum) < 0.001 and math.abs(data.durability - row.durability) < 0.001
            and math.abs(data.quality - row.quality) < 0.001, "木盾加固没有同步增加50%耐久或覆盖已有品质")
        assert(data.identityPreserved and data.tagsPreserved and data.repeatRejected, "加固丢失原盾状态、标签或允许重复加固")
        assert(math.abs(data.restoredMaximum - row.maximum) < 0.001 and math.abs(data.restoredDurability - row.durability) < 0.001,
            "连续恢复快照后木盾加固倍率叠乘或耐久改变")
    end, "木盾加固：" .. row.name)
end

for _, row in ipairs({
    {name = "两钉不足", args = {nails = 2}},
    {name = "已有钉子加固标记", args = {reinforced = true}},
    {name = "其他盾牌不能套用", args = {otherShield = true}},
    {name = "异常多件盾牌不复制", args = {stacked = true}},
    {name = "满输出不扣材料", args = {full = true}}
}) do
    test.system("crafting", "wood_shield_reinforcement", row.args, function(data)
        assert(not data.success and data.previewUnchanged and data.outputAmount == 0, "非法木盾加固成功或预览修改原物")
        assert(data.shieldRemaining == (row.args.stacked and 2 or 1) and data.nailsRemaining == (row.args.nails or 6),
            "木盾加固拒绝仍消耗盾牌或钉子")
        if row.args.full then assert(data.outputStillBlocked, "满输出加固失败覆盖原物") end
    end, "木盾加固边界：" .. row.name)
end

test.system("crafting", "reinforcement_editor_roundtrip", {shield = true}, function(data)
    assert(data.excludeTag == "NailReinforced" and data.copyInputItemId == "Shield_Wood" and data.addTag == "NailReinforced",
        "内容工坊保存丢失木盾来源或一次加固标记")
    assert(data.durabilityMultiplier == 1.5 and data.outputAmount == 1 and not data.reinforce,
        "内容工坊保存改变钉子加固倍率或误用胶黏规则")
end, "内容工坊：木盾三钉加固字段往返")

test.system("crafting", "metal_tools_catalog", {}, function(data)
    local expected = {Needle_Bone = 10, Needle_Copper = 100, Needle_Iron = 200,
        CopperAnvil = 500, CopperAnvil_Summoner = 500, IronAnvil = 1000, IronAnvil_Summoner = 1000,
        CopperHammer = 100, IronHammer = 200, SteelHammer = 400}
    for _, item in ipairs(data.items) do
        assert(item.durability == expected[item.id] and item.maxDurability == expected[item.id], "针、锤子或砧耐久配置错误")
        if item.id:find("Anvil") then
            local config = item.modules["手动加工模块"].parameters
            assert(config.DurabilityPerProduct == 1 and config.BreakRecoveryFraction == .5, "砧未配置逐件磨损及一半返料")
        elseif item.id:find("Hammer") then
            local tags = {}
            for _, tag in ipairs(item.tags) do tags[tag] = true end
            local tool = item.modules.resourceTool.parameters
            assert(tags.Tool and tags.Metal and tool.toolKind == 4,
                "金属锤未组合金属标签和通用锤子能力")
            assert(not item.stackable and item.weight > 0 and item.volume > 0
                and item.visual.spriteAddress:find("Tools/MetalHammers/", 1, true), "金属锤物品或正式素材定义缺失")
        else
            local sewing = false
            for _, tag in ipairs(item.tags) do if tag == "SewingNeedle" then sewing = true end end
            assert(sewing, "缝纫针缺少通用能力标签")
        end
    end
    assert(#data.forging == 8, "锻造丢失原有六种金属板或新增两种针")
    local needles = 0
    for _, recipe in ipairs(data.forging) do
        assert(recipe.station == "hand_forge" and recipe.seconds == 12 and recipe.inputAmount == 1, "锻造站点或工作量改变")
        if recipe.output == "Needle_Copper" or recipe.output == "Needle_Iron" then
            needles = needles + 1
            assert(recipe.outputAmount == 10, "一锭应制作十根针")
            assert(recipe.input == (recipe.output == "Needle_Copper" and "Ingot_Copper" or "Ingot_WroughtIron"), "针使用了错误金属锭")
        else assert(recipe.outputAmount == 1, "金属板产量改变") end
    end
    assert(needles == 2 and #data.salvage == 2, "针或砧配方缺失")
    for _, row in ipairs(data.salvage) do
        local metal = row.output == "CopperAnvil_Summoner" and "Ingot_Copper" or "Ingot_WroughtIron"
        assert(row.materials[metal] == 5, "砧用坏应固定返五锭")
    end
end, "铜铁针、金属锤与砧：正式目录和固定一半返料")
-- 首把金属锤由普通材料制成，配方不得要求先有金属锤或铁砧。
test.system("crafting", "handmade_catalog", {package = "crafting/metal_hammers.json"}, function(data)
    local expected = {CopperHammer = {metal = "Ingot_Copper", durability = 100},
        IronHammer = {metal = "Ingot_WroughtIron", durability = 200},
        SteelHammer = {metal = "Ingot_Steel", durability = 400}}
    local enabled = false
    for _, path in ipairs(data.enabledPackages) do
        if path == "crafting/metal_hammers.json" then enabled = true end
    end
    assert(enabled and #data.recipes == 3, "三种金属锤配方包未完整启用")
    local function unchanged(before, after)
        assert(#before == #after, "金属锤制作拒绝改变了槽数")
        for i, slot in ipairs(before) do
            local other = after[i]
            assert(slot.id == other.id and slot.guid == other.guid and slot.amount == other.amount
                and slot.durability == other.durability and slot.maxDurability == other.maxDurability,
                "金属锤拒绝制作后仍改变库存")
        end
    end
    for _, recipe in ipairs(data.recipes) do
        assert(#recipe.expectedOutputs == 1 and recipe.expectedOutputs[1].amount == 1, "金属锤产量错误")
        local output = recipe.expectedOutputs[1].id
        local wanted = expected[output]
        assert(wanted and #recipe.warnings == 0 and recipe.station == "" and #recipe.missingTools == 0,
            "金属锤存在未知物品引用或要求先有工作站、锤子")
        assert(#recipe.ingredients == 3 and recipe.ingredients[1].id == wanted.metal
            and recipe.ingredients[1].amount == 3 and recipe.ingredients[2].id == "Stick_Wood"
            and recipe.ingredients[2].amount == 1 and recipe.ingredients[3].amount == 2,
            "金属锤未使用三锭、一木棍和两份绳类")
        local normal = recipe.normal
        assert(normal.matched and normal.prepared and normal.committed, "金属锤不能由普通材料制作")
        for _, slot in ipairs(normal.inputAfter) do assert(slot.amount == 0, "金属锤未精准扣料") end
        local product = normal.outputAfter[1]
        assert(product.id == output and product.amount == 1 and product.durability == wanted.durability
            and product.maxDurability == wanted.durability and normal.outputAfter[2].amount == 0,
            "金属锤产物或初始耐久错误")
        assert(#recipe.missingMaterials == 3, "金属锤没有逐种检查缺料")
        for _, rejected in ipairs(recipe.missingMaterials) do
            assert(rejected.removedOne and not rejected.matched and not rejected.prepared and not rejected.committed,
                "金属锤缺料仍完成制作")
            unchanged(rejected.inputBefore, rejected.inputAfter)
            unchanged(rejected.outputBefore, rejected.outputAfter)
        end
        assert(recipe.full.matched and not recipe.full.prepared and not recipe.full.committed, "金属锤满输出仍提交")
        unchanged(recipe.full.inputBefore, recipe.full.inputAfter)
        unchanged(recipe.full.outputBefore, recipe.full.outputAfter)
        expected[output] = nil
    end
    assert(next(expected) == nil, "金属锤配方重复或缺少一种材质")
end, "铜铁钢锤：正常制作、逐种缺料、满输出、初始耐久和无工具自锁")

for _, id in ipairs({"Needle_Bone", "Needle_Copper", "Needle_Iron"}) do
    test.system("crafting", "needle_sewing", {needle = id, durability = 2}, function(data)
        assert(data.matched and data.success and data.count == 1 and data.durability == 1 and data.guid == 9471,
            "缝纫未使用通用针标签或每件没有只扣一点耐久")
        assert(data.thread == 0 and data.leather == 0 and data.output == "Traveler_Gloves", "缝纫扣料或成品错误")
    end, "通用针缝纫磨损：" .. id)
    test.system("crafting", "needle_sewing", {needle = id}, function(data)
        assert(data.success and data.count == 0 and data.durability == 0, "针归零没有损毁当前一根")
    end, "针归零损毁：" .. id)
    test.system("crafting", "needle_sewing", {needle = id, full = true}, function(data)
        assert(not data.success and data.unchanged, "满输出仍扣材料或针耐久")
    end, "缝纫失败不磨损：" .. id)
end
for _, id in ipairs({"Needle_Copper", "Needle_Iron"}) do
    test.system("crafting", "needle_sewing", {needle = id, count = 10}, function(data)
        assert(data.success and data.count == 9 and data.durability == data.maximum,
            "一根针用坏后吞掉整堆或没有换成下一根满耐久")
    end, "针逐根消耗堆叠：" .. id)
end

for _, row in ipairs({
    {args = {durability = 500, products = 1}, accepted = true, after = 499},
    {args = {durability = 1000, maximum = 1000, products = 10}, accepted = true, after = 990},
    {args = {durability = 10, products = 10}, accepted = true, after = 0},
    {args = {durability = 9, products = 10}, accepted = false, after = 9},
    {args = {durability = 0, products = 1}, accepted = false, after = 0},
    {args = {durability = 10, products = 10, success = false}, accepted = true, after = 10}
}) do
    test.system("crafting", "manual_station_wear", row.args, function(data)
        assert(data.accepted == row.accepted and data.previewUnchanged and data.durability == row.after,
            "工作站逐件磨损、余量边界或失败不磨损错误")
        assert(data.stack == 1 and data.maximum == (row.args.maximum or 500), "加工磨损错误修改建筑数量或最大耐久")
        assert(math.abs(data.carrierDurability - row.after) < .001 and math.abs(data.replacedDurability - row.after) < .001,
            "拆回或重新放置将砧的剩余耐久补满")
        assert(data.genericRejected and data.workbenchRejected and data.forgeAccepted, "砧混入其他站点或普通手工配方")
    end, "工作站按成功产物数量磨损")
end


-- 手工切皮配方实际消耗皮革并保留刀具，缺刀与满输出均不扣材料。
for _, row in ipairs({
    {name = "刀具切皮", args = {}, success = true},
    {name = "缺皮革", args = {missing = true}},
    {name = "缺刀具", args = {missingKnife = true}},
    {name = "输出满", args = {full = true}}
}) do
    test.system("crafting", "leather_cutting", row.args, function(data)
        assert(data.committed == (row.success or false), "切皮成功或拒绝条件错误")
        if row.success then
            assert(data.outputAfter[1].id == "LeatherStrap" and data.outputAfter[1].amount == 4,
                "一份皮革未产出四根皮带")
            assert(data.inputAfter[2].amount == 0 and data.inputAfter[1].id == data.inputBefore[1].id
                and data.inputAfter[1].amount == data.inputBefore[1].amount
                and data.inputAfter[1].durability == data.inputBefore[1].durability, "切皮未精准扣料或改变刀具")
        else
            for i, before in ipairs(data.inputBefore) do
                local after = data.inputAfter[i]
                assert(after.id == before.id and after.guid == before.guid and after.amount == before.amount
                    and after.durability == before.durability, "拒绝切皮仍改变材料或刀具")
            end
        end
    end, "切割鞣制皮革：" .. row.name)
end

-- 正式配方按真实扣料结算品质，未消耗的绳子不影响整批皮带。
for _, row in ipairs({
    {name = "全皮带", args = {}, maximum = 130, quality = 1.3, reinforced = true},
    {name = "皮带跨堆叠", args = {straps = 2, secondStack = 3}, maximum = 130, quality = 1.3, reinforced = true},
    {name = "额外未用绳子不降品质", args = {ropes = 5}, maximum = 130, quality = 1.3, reinforced = true, ropes = 5},
    {name = "普通绳子", args = {straps = 0, ropes = 5}, maximum = 100, quality = 1, reinforced = false},
    {name = "皮带与绳子混用", args = {straps = 3, ropes = 2}, maximum = 100, quality = 1, reinforced = false},
    {name = "已有配方品质继续累乘", args = {quality = 1.5}, maximum = 195, quality = 1.95, reinforced = true}
}) do
    test.system("crafting", "binding_materials", row.args, function(data)
        assert(data.matched and data.ruleAccepted and data.success and data.previewUnchanged, "合法绳料配方失败或预检扣料")
        assert(math.abs(data.maximum - row.maximum) < .001 and math.abs(data.durability - row.maximum) < .001
            and math.abs(data.quality - row.quality) < .001 and data.reinforced == row.reinforced, "绳料品质、混料条件或一次标记错误")
        assert(data.remainingStraps == 0 and data.remainingRopes == (row.ropes or 0), "绳料未精准扣量")
        assert(math.abs(data.transferredMaximum - row.maximum) < .001 and data.transferredMarker == row.reinforced,
            "放置与拆回迁移品质时丢失耐久或一次标记")
    end, "绳料制作品质：" .. row.name)
end

for _, row in ipairs({
    {name = "缺少绳料", args = {straps = 4}},
    {name = "书本不能当绳料", args = {straps = 0, ropes = 5, ropeId = "MinersNote"}},
    {name = "命令手册不能当绳料", args = {straps = 0, ropes = 5, ropeId = "CommandManual"}},
    {name = "非法材质品质", args = {invalidQuality = true}},
    {name = "输出满保留材料", args = {full = true}}
}) do
    test.system("crafting", "binding_materials", row.args, function(data)
        assert(not data.success and data.previewUnchanged and data.inputsUnchanged, "拒绝绳料制作仍改动库存")
        if row.args.full then assert(data.outputStillBlocked, "满输出覆盖原物") end
    end, "绳料制作边界：" .. row.name)
end

for _, row in ipairs({
    {name = "保持破损比例", args = {}, maximum = 130, durability = 52, quality = 1.3},
    {name = "满耐久同步提高", args = {durability = 100}, maximum = 130, durability = 130, quality = 1.3},
    {name = "胶水钉子皮带独立叠加", args = {quality = 1.95, adhesive = true, nails = true}, maximum = 253.5, durability = 52, quality = 2.535}
}) do
    test.system("crafting", "binding_reinforcement", row.args, function(data)
        assert(data.preview and data.success and data.previewUnchanged and data.toolRemaining == 0 and data.strapsRemaining == 1,
            "皮带加固未原子消耗一件工具和一根皮带")
        assert(math.abs(data.maximum - row.maximum) < .001 and math.abs(data.durability - row.durability) < .001
            and math.abs(data.quality - row.quality) < .001, "皮带加固未保持破损比例或覆盖其他品质")
        assert(data.identityPreserved and data.tagsPreserved and data.repeatRejected and data.restoredMarker,
            "皮带加固丢失身份、实例标记或允许重复加固")
        assert(math.abs(data.restoredMaximum - row.maximum) < .001 and math.abs(data.restoredDurability - row.durability) < .001,
            "连续快照恢复叠乘皮带品质")
    end, "皮带后手加固：" .. row.name)
end

for _, row in ipairs({
    {name = "无皮带", args = {straps = 0}},
    {name = "已在合成或加固时使用皮带", args = {reinforced = true}},
    {name = "非工具", args = {notTool = true}},
    {name = "异常多件工具", args = {stacked = true}},
    {name = "输出满", args = {full = true}}
}) do
    test.system("crafting", "binding_reinforcement", row.args, function(data)
        assert(not data.success and data.previewUnchanged and data.inputsUnchanged, "非法皮带加固成功或扣除材料")
        if row.args.full then assert(data.outputStillBlocked, "皮带加固覆盖输出原物") end
    end, "皮带后手加固边界：" .. row.name)
end

for _, row in ipairs({
    {recipe = "core:petroleum_fuel_engine", output = "FuelEngine_Summoner"},
    {recipe = "core:petroleum_bitumen_floor", output = "Floor_Bitumen_Summoner"},
    {recipe = "core:petroleum_hot_bitumen_tool", output = "Axe_Stone", hot = true, tool = true},
    {recipe = "core:petroleum_hot_bitumen_barrel", output = "WoodenBarrel", hot = true},
    {recipe = "core:petroleum_hot_bitumen_trough", output = "WaterTrough", hot = true},
    {recipe = "core:petroleum_bitumen_glue", output = "Glue", hot = true}
}) do
    test.system("crafting", "petroleum_utilities", {recipe = row.recipe}, function(d)
        assert(#d.warnings == 0 and d.matched and d.committed, "石油用途正式配方未能提交：" .. row.recipe)
        assert(d.outputAfter[1].id == row.output and d.outputAfter[1].amount == 1, "石油用途产物或数量错误")
        for _, slot in ipairs(d.inputAfter) do assert(slot.amount == 0, "石油用途未精准消耗全部配方材料") end
        if row.tool then
            assert(d.reinforced and math.abs(d.maximum - 130) < 0.001 and math.abs(d.durability - 75) < 0.001,
                "热沥青没有复用树脂的工具强化规则")
        end
    end, "石油用途正式制作：" .. row.recipe)
    for _, refusal in ipairs({{missing = 0}, {full = true}}) do
        refusal.recipe = row.recipe
        test.system("crafting", "petroleum_utilities", refusal, function(d)
            assert(not d.committed and d.unchanged, "石油用途缺料或满输出仍扣料/产出")
        end, "石油用途拒绝保留库存：" .. row.recipe)
    end
    if row.hot then
        for _, refusal in ipairs({{temperature = 149.9}, {uninitialized = true}, {cooledBeforeCommit = true}}) do
            refusal.recipe = row.recipe
            test.system("crafting", "petroleum_utilities", refusal, function(d)
                assert(not d.committed and not d.qualifiedAtCommit and d.unchanged,
                    "冷沥青、未初始化或提交前冷却仍被当成粘合剂")
            end, "热沥青150度资格：" .. row.recipe)
        end
    end
end

test.system("crafting", "petroleum_utilities", {reinforced = true}, function(d)
    assert(not d.committed and d.unchanged, "热沥青可以重复强化同一工具")
end, "热沥青拒绝重复强化")

test.system("crafting", "petroleum_utilities", {recipe = "core:petroleum_fuel_engine", station = "handcraft"}, function(d)
    assert(not d.committed and d.unchanged, "燃料发动机绕过工作台资格")
end, "燃料发动机必须在工作台制作")
