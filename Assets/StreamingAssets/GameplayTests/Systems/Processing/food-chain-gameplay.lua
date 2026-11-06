-- 使用正式物品加工与热反应入口检查扣料和产物。
local function amount(slots, id)
    local total = 0
    for _, slot in ipairs(slots) do
        if slot.id == id then total = total + slot.amount end
    end
    return total
end

for _, row in ipairs({
    {source = "Wheat", output = "Flour"},
    {source = "Corn", output = "Flour"},
    {source = "Soybean", output = "Tofu"},
    {source = "Tomato", output = "TomatoPaste"},
    {source = "Sugarcane", output = "Sugar"}
}) do
    for _, sample in ipairs({{count = 2, blocked = false}, {count = 1, blocked = false}, {count = 2, blocked = true}}) do
        test.runtime("processing", "material_cutting", {
            id = row.source, capability = "grind", amount = sample.count, full = sample.blocked
        }, function(data)
            assert(data.resolved and data.inputAmount == 2, row.source .. "未解析正式石臼配方")
            if sample.count == 2 and not sample.blocked then
                assert(data.preview and data.success and amount(data.input, row.source) == 0
                    and amount(data.output, row.output) == 1, row.source .. "加工扣料或产物错误")
            else
                assert(not data.preview and not data.success and data.unchanged,
                    row.source .. "缺料或输出已满时仍扣料或产物")
            end
        end, "食材加工：" .. row.source .. "，数量=" .. sample.count .. "，输出受阻=" .. tostring(sample.blocked))
    end
end

for _, row in ipairs({
    {source = "Flour", output = "Bread"},
    {source = "Corn", output = "RoastedCorn"},
    {source = "Potato", output = "BakedPotato"},
    {source = "Pumpkin", output = "CookedPumpkin"}
}) do
    for _, temperature in ipairs({99.9, 100}) do
        test.runtime("processing", "material_reaction", {
            materials = {{id = row.source, amount = 2, temperature = temperature}}
        }, function(data)
            local cooked = temperature >= 100
            assert(amount(data.input, row.source) == (cooked and 0 or 2)
                and amount(data.output, row.output) == (cooked and 2 or 0),
                row.source .. "加热温度边界、消耗或产物错误")
        end, "食材加热：" .. row.source .. "，温度=" .. temperature)
    end
end

for _, sample in ipairs({
    {mode = "normal"},
    {mode = "liquid_full"},
    {mode = "solid_full"},
    {mode = "effect_failure"}
}) do
    test.runtime("processing", "soybean_press", sample, function(data)
        if sample.mode == "normal" then
            assert(data.processed and data.soybeanOil == 1
                and amount(data.solids, "Soybean") == 0 and amount(data.solids, "SoybeanMeal") == 3,
                "大豆压榨没有原子产出豆油和豆粕")
        elseif sample.mode == "liquid_full" then
            assert(data.processed and data.water > 0 and data.soybeanOil == 0
                and amount(data.solids, "Soybean") == 4 and amount(data.solids, "SoybeanMeal") == 0,
                "液体满载仍扣大豆或生成固体副产物")
        elseif sample.mode == "solid_full" then
            assert(data.blockedWithoutMutation and amount(data.solids, "Soybean") == 5
                and amount(data.solids, "SoybeanMeal") == 0 and data.soybeanOil == 0,
                "固体满载时正式坩埚入口仍写入豆油或扣除大豆")
            assert(data.recovered and data.recoverySoybeanOil == 1
                and amount(data.recoverySolids, "Soybean") == 1
                and amount(data.recoverySolids, "SoybeanMeal") == 3,
                "清出固体空间后正式坩埚入口未恢复压榨")
        else
            assert(data.failed and data.effectRan and data.unchanged and data.soybeanOil == 0,
                "豆油副作用失败没有同时恢复液体和固体库存")
        end
        if sample.mode == "solid_full" or sample.mode == "effect_failure" then
            assert(data.exactSoybeanInput and not data.mealHasPressReaction,
                "压榨反应未精确绑定大豆，或豆粕错误继承大豆反应")
        end
    end, "大豆豆油与豆粕同一事务：" .. sample.mode)
end

for _, sample in ipairs({
    {mode = "normal"},
    {mode = "no_oil"},
    {mode = "reserved_oil"},
    {mode = "solid_full"},
    {mode = "partial_oil"}
}) do
    test.runtime("processing", "potato_oil_frying", {
        mode = sample.mode, temperature = 100
    }, function(data)
        assert(data.temperature == 100 and data.exactFryingInput,
            "油煎反应未精确配置为100℃、1 Potato + 1份豆油→1 FriedPotato")
        if sample.mode == "normal" then
            assert(data.processed and data.soybeanOil == 0 and data.water == 2
                and amount(data.solids, "Potato") == 0
                and amount(data.solids, "FriedPotato") == 1
                and amount(data.solids, "BakedPotato") == 0,
                "混合液坩埚应只消耗豆油并煎出土豆")
        elseif sample.mode == "no_oil" then
            assert(amount(data.solids, "Potato") == 0
                and amount(data.solids, "FriedPotato") == 0
                and amount(data.solids, "BakedPotato") == 1,
                "缺豆油时普通烤土豆热反应应继续执行")
        elseif sample.mode == "reserved_oil" then
            assert(data.reservedSoybeanOil == 1 and data.soybeanOil == 1 and data.water == 2
                and amount(data.solids, "Potato") == 0
                and amount(data.solids, "FriedPotato") == 0
                and amount(data.solids, "BakedPotato") == 1,
                "已预约豆油不可供油煎消耗，且不应阻挡烤土豆回退")
        elseif sample.mode == "solid_full" then
            assert(data.blockedWithoutMutation and amount(data.solids, "Potato") == 5
                and amount(data.solids, "FriedPotato") == 0
                and amount(data.solids, "BakedPotato") == 0
                and data.soybeanOil == 1 and data.water == 2,
                "固体输出满载时油煎不得扣土豆或豆油")
            assert(data.recovered and data.recoverySoybeanOil == 0
                and amount(data.recoverySolids, "Potato") == 4
                and amount(data.recoverySolids, "FriedPotato") == 1
                and amount(data.recoverySolids, "BakedPotato") == 0,
                "清出固体槽后坩埚没有恢复油煎")
        else
            assert(data.soybeanOil == 0 and data.water == 2
                and amount(data.solids, "Potato") == 1
                and amount(data.solids, "FriedPotato") == 1
                and amount(data.solids, "BakedPotato") == 0,
                "仅有一份豆油时首轮必须只煎一份并阻止剩余土豆同轮烘烤")
            assert(data.secondProcessed and data.secondSoybeanOil == 0 and data.secondWater == 2
                and amount(data.secondSolids, "Potato") == 0
                and amount(data.secondSolids, "FriedPotato") == 1
                and amount(data.secondSolids, "BakedPotato") == 1,
                "豆油耗尽后的下一轮应将剩余土豆交给普通烘烤")
        end
    end, "豆油煎土豆正式坩埚入口：" .. sample.mode)
end
