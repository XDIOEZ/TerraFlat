-- 复用正式创造背包填充与快捷栏切换；容量和扣料细节在独立规则用例检查。
test.check("player_ready", "真实玩家就绪")
test.gm("enable_admin")
test.gm("initialize_creative_inventory")
test.check("gm_creative_inventory_enabled", "正式创造背包已开启并填充目录物品")
test.select_hotbar(0)
test.check("hotbar_slot:0", "库存初始化后快捷栏仍可选择")

-- 两次正式 GM 填充之间只读取真实背包，确保同一种物品不重复新增槽位。
local first
test.runtime("inventory", "creative_snapshot", {}, function(data)
    assert(data.unlimitedSlots and data.unlimitedCarry and data.unlimitedStack,
        "创造背包未同时开放动态槽、堆叠及携带容量")
    assert(data.slotCount > 20 and #data.items > 20 and data.slotCount > #data.items,
        "正式物品没有入包，或扩容后没有保留空槽")
    local seenSlots = {}
    for _, item in ipairs(data.items) do
        assert(type(item.slot) == "number" and item.slot >= 0 and item.slot < data.slotCount
            and not seenSlots[item.slot], "实际背包槽位无效或重复")
        assert(type(item.id) == "string" and #item.id > 0 and item.hasStack
            and type(item.amount) == "number" and item.amount > 0,
            "创造背包包含失效物品或非正数数量")
        seenSlots[item.slot] = true
    end
    first = data
end, "真实创造背包内容、容量策略及空槽")

test.gm("initialize_creative_inventory")
test.runtime("inventory", "creative_snapshot", {}, function(data)
    assert(first and #data.items == #first.items and data.slotCount == first.slotCount,
        "重复填充创造背包额外生成了物品或槽位")
    assert(data.unlimitedSlots and data.unlimitedCarry and data.unlimitedStack,
        "重复填充丢失创造背包容量豁免")
    local bySlot, firstOccurrence = {}, {}
    for _, item in ipairs(data.items) do bySlot[item.slot] = item end
    for _, original in ipairs(first.items) do
        local current = bySlot[original.slot]
        assert(current and current.id == original.id and current.guid == original.guid,
            "重复填充改变了原有槽位的物品定义或身份")
        local increase = firstOccurrence[original.id] and 0 or 100
        firstOccurrence[original.id] = true
        assert(math.abs(current.amount - original.amount - increase) < 0.001,
            "重复填充未按定义准确补充100个，或错误累加了重复槽位：" .. original.id)
    end
end, "重复创造填充的同槽身份、物品数量与无重复扩容")

test.runtime("inventory", "slot_bounds", {}, function(data)
    assert(data.validIndexPreserved, "有效索引没有返回真实背包槽位")
    assert(data.negativeRejected and data.beyondRejected,
        "越界索引错误映射到第0格，可能误读或误扣物品")
    assert(data.emptyRejected, "空背包取槽抛错或返回无效引用")
end, "真实背包与空背包的槽位越界安全")
