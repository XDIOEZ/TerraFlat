-- 复用正式创造背包填充与快捷栏切换；容量和扣料细节在独立规则用例检查。
test.check("player_ready", "真实玩家就绪")
test.runtime("inventory", "creative_begin", {}, function(data)
    assert(data.registered and data.originalSlotCount > 0 and data.originalWeight >= 0,
        "创造背包没有先登记原始玩家库存的恢复租约")
end, "登记原始玩家库存、创造状态与载荷恢复")
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

-- 在隔离世界使用正式物品定义与正式鼠标拖拽事务，测试槽位数据独立于玩家背包。
test.runtime("inventory", "direct_drag", {}, function(data)
    local d, m, b, p, stale = data.direct, data.merged, data.blocked, data.partial, data.stale
    assert(d.dragStarted and d.lockedBefore and not d.negativeAccepted,
        "无效目标下标错误提交拖拽或没有锁住来源槽")
    assert(d.afterNegative.id == "Twine" and d.afterNegative.amount == 6
        and d.afterNegative.guid == d.sourceBefore.guid, "越界拒绝修改了原有物品")
    assert(d.moved and d.sourceAfter.id == "" and d.targetAfter.id == "Twine"
        and d.targetAfter.amount == 6 and d.targetAfter.guid == d.sourceBefore.guid,
        "鼠标整组拖拽未移动原物品或丢失身份")
    assert(not d.reusedAccepted and not d.lockedAfter, "拖拽完成后仍可重复提交或来源锁未释放")
    assert(m.accepted and m.sourceBefore.amount == 3 and m.targetBefore.amount == 4
        and m.sourceAfter.id == "" and m.targetAfter.id == "Twine"
        and m.targetAfter.amount == 7 and m.targetAfter.guid == m.targetBefore.guid,
        "同类堆叠拖拽没有正确合并数量或保留目标身份")
    assert(not b.accepted and b.lockedDuring and not b.lockedAfter
        and b.sourceBefore.id == "Rope" and b.sourceAfter.id == "Rope"
        and b.sourceAfter.amount == b.sourceBefore.amount
        and b.sourceAfter.guid == b.sourceBefore.guid and b.targetAfter.id == "",
        "标签拒绝没有回滚，或取消拖拽后仍锁住物品")
    assert(p.accepted and p.sourceBefore.amount == 2 and p.sourceAfter.amount == 1
        and p.targetAfter.id == "Twine" and p.targetAfter.amount == 1,
        "受限单槽的部分接收不守恒")
    assert(not stale.accepted and not stale.lockedAfter and stale.sourceAfter.id == "Ore_Stone"
        and stale.sourceAfter.amount == 1 and stale.targetAfter.id == "Twine"
        and stale.targetAfter.amount == 6,
        "拖拽途中来源被替换后仍提交旧引用或改写目标物品")
end, "鼠标直接拖拽、合堆、限额、拒绝回滚、陈旧来源和清理")

test.runtime("inventory", "equipment_drag", {}, function(data)
    assert(data.headIndex >= 0 and data.torsoIndex >= 0
        and data.headIndex ~= data.torsoIndex and data.headAllowed
        and not data.torsoAllowedAtHead and not data.headAllowedAtTorso,
        "正式装备定义、模块和槽位标签接收规则错误")
    assert(not data.wrongAccepted and data.wrongSourceAfter.id == "Spacesuit_Torso"
        and data.wrongTargetAfter.id == "", "错误装备进入头部槽或拒绝时丢失来源")
    assert(data.equipAccepted and data.sourceAfterEquip.id == ""
        and data.headAfterEquip.id == "Spacesuit_Head"
        and data.headAfterEquip.guid == data.headBefore.guid,
        "头部装备拖入正式装备栏失败或身份丢失")
    assert(not data.reverseSwapAccepted and not data.invalidIndexAccepted
        and data.headAfterSwap.guid == data.headBefore.guid
        and data.swapSourceAfter.id == "Spacesuit_Head",
        "异类双向接收限制或越界拒绝后装备被错误交换")
    assert(data.unequipAccepted and data.sourceAfterUnequip.id == "Spacesuit_Head"
        and data.sourceAfterUnequip.guid == data.headBefore.guid
        and data.headAfterUnequip.id == "",
        "卸下装备未按正式拖拽链恢复原物品")
    assert(data.sourceDragReleased and data.equipmentDragReleased,
        "装备拖拽完成或失败后没有释放来源占用")
end, "装备槽标签拒绝、合法装卸、双向交换与拖拽收尾")

test.runtime("inventory", "equipment_configuration", {}, function(data)
    assert(data.genericConfigurationButtons == 1 and data.specializedConfigurationButtons == 0,
        "正式装备页应只有统一装备配置入口，不应含宇航服专用按钮")
    assert(data.emptyCount == 0 and data.ordinaryCount == 0,
        "空装备栏或只有普通装备时不应出现装备配置目标")
    assert(data.nullRejected and data.foreignRejected,
        "空目标或不属于实际装备栏的配置效果未被拒绝")
    assert(data.singleCount == 1 and data.singleItemMatched and data.singleOpened,
        "正式宇航服衣服没有通过通用装备能力提供自身配置入口")
    assert(data.unequipped and data.staleRejected and data.afterUnequipCount == 0,
        "装备卸下后仍可发现或打开旧配置目标")
    assert(data.multipleCount == 2 and data.firstItemCount == 1 and data.secondItemCount == 1
        and data.firstConfigurationEffects == 2 and data.secondOpened,
        "多件可配置装备没有分别收集，或同件装备的多个配置效果没有去重")
    assert(data.reequippedOldTargetRejected,
        "同一件物品重新穿戴后，旧装备效果引用仍能打开配置")
    assert(data.originalEquipmentRestored,
        "装备配置夹具没有归还隔离玩家原有装备引用或槽位")
end, "通用装备配置发现、同件去重、多目标、旧目标拒绝与原装备恢复")

test.runtime("inventory", "creative_restore", {}, function(data)
    assert(data.restored and data.slotCount == data.originalSlotCount,
        "创造背包测试结束后未恢复原有背包格数")
    assert(data.capacityRestored and data.playerStateRestored,
        "创造模式容量豁免或玩家持久状态未恢复")
    assert(math.abs(data.weight - data.originalWeight) <= .001,
        "创造背包留有额外物品载荷，可能导致后续飞船过重或跨系统测试失真")
end, "恢复原始库存、创造容量与角色载荷，禁止污染后续Lua")
