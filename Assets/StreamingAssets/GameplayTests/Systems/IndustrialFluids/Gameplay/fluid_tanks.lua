-- 储罐生命周期与充装：组内检查各用自己的夹具，失败保留报告并继续其余检查。
test.check("world_ready")
test.check("player_ready")

local scenarios = {
    {"tank_scope_transfer", "跨船整批迁移与重新组合"},
    {"tank_dismantle", "方块罐拆除份额与持久溢流"},
    {"tank_overpressure", "超压固定受损成员"},
    {"tank_rupture_once", "共享破裂预算与防重复释放"},
    {"device_portable_fill", "便携气罐限压及冻结充装"},
}

for _, scenario in ipairs(scenarios) do
    test.fluid(scenario[1], scenario[2])
end

local function near(actual, expected, message)
    assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <=
        0.00000001 * math.max(1, math.abs(expected)), message)
end

-- 直接读取正式组主身份和两份保存内容，避免把随机出生的第二个节点固定当作非主成员。
test.runtime("industrial_fluids", "tank_ownership", {}, function(data)
    assert(data.scope == "live-isolated-world", "储罐没有使用真实隔离世界")
    assert(data.shared and not data.foreignShared, "同材四邻没有共享库存或异材错误共享库存")
    near(data.volume, 200, "组合罐总容积错误")
    near(data.minimumGasSpace, 0.2, "组合罐最低气相空间错误")
    assert(data.maskA == 2 and data.maskB == 8 and data.maskC == 0, "储罐连接外形没有遵循真实共享组")
    assert((data.ownerId == data.aId or data.ownerId == data.bId)
        and data.ownerA == data.ownerId and data.ownerB == data.ownerId
        and data.savedOwnerId == data.ownerId and data.savedNonOwnerId ~= data.ownerId,
        "采样或保存没有遵循真实组主身份")
    assert(#data.primaryContents.Components == 2 and #data.secondaryContents.Components == 0
        and #data.secondaryContents.Ports == 0, "主成员没有唯一保存内容或非主成员保存了重复库存")
    local saved_gas, saved_liquid = 0, 0
    for _, component in ipairs(data.primaryContents.Components) do
        saved_gas = saved_gas + component.GasMoles
        saved_liquid = saved_liquid + component.LiquidMoles
    end
    near(saved_gas, data.initialGas, "主成员保存的气量与真实共享库存不一致")
    near(saved_liquid, data.initialLiquid, "主成员保存的液量与真实共享库存不一致")
    near(data.primaryContents.TotalInternalEnergyJoules, data.initialEnergy, "主成员保存内能错误")
    assert(data.oldInventoryEmpty and not data.splitShared, "拆分后旧库存还能重复提取或新罐仍共享库存")
    near(data.finalGas, data.initialGas, "跨船拆分丢失或复制气体")
    near(data.finalLiquid, data.initialLiquid, "跨船拆分丢失或复制液体")
    near(data.finalEnergy, data.initialEnergy, "跨船拆分破坏内能")
    near(data.firstGas, 1, "第一罐没有得到等容积气体份额")
    near(data.secondGas, 1, "第二罐没有得到等容积气体份额")
    near(data.firstLiquid, 10, "第一罐没有得到等容积液体份额")
    near(data.secondLiquid, 10, "第二罐没有得到等容积液体份额")
end, "正式储罐组主保存、唯一内容与跨船拆分守恒")

-- 两个方块罐、管段和真实石磨在地表与船本地格间往返，覆盖碰撞拒绝后的归属。
test.runtime("industrial_fluids", "ground_scope_roundtrip", {}, function(data)
    assert(data.scope == "live-isolated-world", "作用域迁移没有使用正式隔离世界")
    assert(type(data.groundKey) == "string" and string.sub(data.groundKey, 1, 5) ~= "ship:",
        "来源没有使用普通地表机器世界")
    assert(type(data.shipKey) == "string" and string.sub(data.shipKey, 1, 5) == "ship:",
        "飞行没有使用独立船体作用域")
    assert(#data.initial.nodes == 4 and #data.flight.nodes == 4 and #data.landed.nodes == 4,
        "两罐、管段和加工器夹具成员不完整")
    assert(data.initial.processorPresent and data.initial.sameProcessor, "地表石磨没有正式加工器")
    near(data.initial.gas, 4.1, "初始气量没有进入真实罐和管段")
    near(data.initial.liquid, 40, "初始液量没有进入真实罐")
    assert(data.initial.energy > 0 and data.initial.tanksShared, "初始两罐没有形成有内能的共享库存")

    local function check_members(stage, scope_key, expected, label)
        assert(stage.processorPresent and stage.sameProcessor, label .. "重建或丢失了真实加工器")
        assert(stage.tanksShared, label .. "两罐没有重建唯一共享库存")
        near(stage.gas, data.initial.gas, label .. "丢失或复制了气体")
        near(stage.liquid, data.initial.liquid, label .. "丢失或复制了液体")
        near(stage.energy, data.initial.energy, label .. "丢失或复制了内能")
        near(stage.pipeGas, 0.1, label .. "管段内容被排空或复制")
        for i, node in ipairs(stage.nodes) do
            assert(node.id == data.initial.nodes[i].id and node.sameNode and node.owned,
                label .. "替换了节点身份或没有提交节点归属")
            assert(node.scopeKey == scope_key, label .. "节点作用域错误")
            assert(node.cellX == expected[i].cellX and node.cellY == expected[i].cellY,
                label .. "节点格坐标错误")
            near(node.positionX, expected[i].positionX, label .. "节点横坐标错误")
            near(node.positionY, expected[i].positionY, label .. "节点纵坐标错误")
            assert(node.quarterTurns == expected[i].quarterTurns
                and node.stateQuarterTurns == expected[i].quarterTurns
                and node.savedQuarterTurns == expected[i].quarterTurns,
                label .. "没有使用绝对最终朝向或保存朝向不同步")
        end
    end

    local flight_targets = {
        {cellX = 0, cellY = 0, positionX = 0.5, positionY = 0.5, quarterTurns = 3},
        {cellX = 1, cellY = 0, positionX = 1.5, positionY = 0.5, quarterTurns = 2},
        {cellX = 2, cellY = 0, positionX = 2.5, positionY = 0.5, quarterTurns = 1},
        {cellX = 2, cellY = 0, positionX = 2.5, positionY = 0.5, quarterTurns = 0},
    }
    check_members(data.flight, data.shipKey, flight_targets, "地表装配后：")
    assert(data.flight.groundOwnedCount == 0, "装配后地表仍持有同一批机器")
    assert(data.afterRejected.rejected and data.afterRejected.sameBlocker,
        "目标同层冲突没有被拒绝或误动了原占用者")
    check_members(data.afterRejected, data.shipKey, flight_targets, "拒绝落地后：")
    assert(data.afterRejected.groundOwnedCount == 0, "拒绝后已移走部分机器形成双重归属")
    check_members(data.landed, data.groundKey, data.initial.nodes, "重新落地后：")
    assert(data.landed.shipOwnedCount == 0, "落地后船作用域仍持有机器")
end, "普通地表与船体整批移交、冲突拒绝和气液守恒")
