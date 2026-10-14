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
