test.system("item_runtime", "drop_motion_storage", {}, function(data)
    local function near(actual, expected)
        assert(type(actual) == "number" and math.abs(actual - expected) < .0001,
            "掉落物运动数值不符：" .. tostring(actual) .. "，预期 " .. tostring(expected))
    end
    local function item(stage, id)
        local value = stage.items[tostring(id)]
        assert(value, "补位后丢失掉落物：" .. id)
        return value
    end
    assert(data.removed.count == 6 and not data.removed.items["1"] and
        not data.removed.items["4"] and not data.removed.items["6"], "删除首位、中间及双活动物品后身份残留")
    near(item(data.removed, 9).x, 90)
    near(item(data.removed, 8).x, 80)
    local first = data.stepped
    assert(first.count == 6 and #first.changes == 5, "活动表补位后漏算或重复推进")
    near(item(first, 2).x, 24)
    assert(not item(first, 2).flying and not item(first, 3).enteringWater, "着地或浮起后临时状态没有结束")
    near(item(first, 3).depth, .8)
    for _, id in ipairs({8, 9}) do
        near(item(first, id).x, id * 10 + .5)
        near(item(first, id).flightElapsed, .25)
        near(item(first, id).rotation, 2.5)
    end
    near(item(first, 9).depth, .034375)
    near(item(first, 5).x, 50)

    local updated = data.updated
    assert(updated.count == 4 and #updated.changes == 4 and not updated.items["2"] and
        not updated.items["3"], "运行中删除或更新水态破坏物品集合")
    near(item(updated, 9).amount, 37)
    near(item(updated, 9).depth, .178125)
    near(item(updated, 8).x, 81)
    near(item(updated, 8).depth, 1)
    assert(item(updated, 8).waterKind == 2 and item(updated, 8).enteringWater, "完全浸没前提前结束下沉")
    assert(data.cancelled.count == 3 and not data.cancelled.items["9"] and
        not item(data.cancelled, 8).enteringWater and item(data.cancelled, 8).flying,
        "移除双活动物品或取消水态时串改了飞行状态")
    local sunk = item(data.sunk, 8)
    assert(data.sunk.count == 3 and sunk.waterKind == 3 and not sunk.enteringWater and sunk.flying,
        "补位后沉底删掉物品、遗留水态或错删飞行状态")
    near(sunk.x, 82)
    near(sunk.amount, 8)
    assert(#data.sunk.changes == 2 and data.sunk.changes[1].id == 8 and data.sunk.changes[1].kind == 0 and
        data.sunk.changes[2].id == 8 and data.sunk.changes[2].kind == 3, "飞行与水态没有保持原先的分阶段通知")
    local landed = item(data.landed, 8)
    near(landed.x, 84)
    near(landed.height, 0)
    assert(not landed.flying and not landed.enteringWater and data.landed.count == 4,
        "飞行结束后临时表残留或原物品数量改变")
    near(item(data.landed, 1).amount, 99)
    assert(not item(data.landed, 1).flying and not item(data.landed, 1).enteringWater, "重复使用 ID 继承旧运动状态")
    assert(data.clearedCount == 0 and not data.clearedContains and data.finalCount == 0, "清空或重复复用后残留身份")
    local reused = item(data.reused, 8)
    assert(data.reused.count == 1 and #data.reused.changes == 0 and not reused.flying and not reused.enteringWater,
        "工作区复用后静止物品继承上轮活动数据")
    near(reused.x, -3)
    near(reused.amount, 3)
    assert(data.motionAllocatedBytes == 0 and data.reuseAllocatedBytes == 0, "运动推进或已预热工作区复用产生托管分配")
end, "掉落物运动表补位、双活动状态、结束通知、身份复用与零分配")

test.system("processing", "cold_motion_workspace", {}, function(data)
    local function near(actual, expected)
        assert(type(actual) == "number" and math.abs(actual - expected) < .0001, "后台掉落物运动结果不符")
    end
    near(data.firstPosition, .5)
    near(data.firstElapsed, .25)
    near(data.secondPosition, 9)
    near(data.firstAfterSecond, .5)
    assert(not data.flyingPickable and data.landed and not data.floatingTransition, "后台飞行或浮起结束资格错误")
    near(data.landedPosition, 2)
    near(data.middleDepth, .25)
    near(data.finalDepth, .5)
    assert(data.sinkingCount == 1 and data.sinkingRetained, "后台沉底丢失实物或遗留活动状态")
    near(data.sinkingDepth, 1)
    near(data.sinkingAmount, 1)
    assert(data.duplicateError == "ArgumentException", "后台重复身份未拒绝")
    near(data.recoveredPosition, .5)
    assert(data.repeatedMotionBytes == 0, "后台工作区预热后重复分配")
    assert(data.pendingSettled and data.pendingBounded, "地表等待时长没有按固定步结清，或超过预算的余量没有保留")
end, "紧凑运动表经正式后台档案连续推进、跨世界隔离、异常清理与零分配")
