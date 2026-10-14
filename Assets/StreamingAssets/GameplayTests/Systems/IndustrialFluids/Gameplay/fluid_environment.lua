-- 地表泵和出口只采样真实阶段，两相总量、冻存输送和拒收规则由 Lua 断言。
test.check("world_ready")
test.check("player_ready")

local scenarios = {
    {"environment_atmosphere", "有限大气抽排与逸散"},
    {"environment_outlet_vent", "环境排气与管段显式放气"},
    {"container_liquid_transfer", "工业液体端口原子事务"},
    {"surface_liquid_transactions", "真实地表抽液倾倒与拒收"},
}

for _, scenario in ipairs(scenarios) do
    test.fluid(scenario[1], scenario[2])
end

local function near(a, b, tolerance)
    return a == a and b == b and math.abs(a - b) <= (tolerance or 0.00001)
end

local function water_moles(inventory)
    return inventory.waterGasMoles + inventory.waterLiquidMoles
end

local function assert_inventory(inventory, water_id, label)
    assert(inventory.gasMoles >= 0 and inventory.liquidMoles >= 0 and inventory.energy >= 0,
        label .. "出现负物质量或负内能")
    assert(inventory.temperatureKelvin > 0 and inventory.temperatureKelvin < math.huge,
        label .. "温度不是有效有限数")
    assert(inventory.maximumPhaseMolesPerSecond > 0, label .. "测试关闭了正常相变")
    assert(near(inventory.gasMoles, inventory.waterGasMoles)
        and near(inventory.liquidMoles, inventory.waterLiquidMoles), label .. "出现凭空换出的其他物质")
    local reserved_gas, reserved_liquid = 0, 0
    for _, port in ipairs(inventory.state.Ports) do
        assert(port.FluidId == water_id and port.GasMoles >= 0 and port.LiquidMoles >= 0,
            label .. "出口预留的身份或数量无效")
        reserved_gas = reserved_gas + port.GasMoles
        reserved_liquid = reserved_liquid + port.LiquidMoles
    end
    assert(reserved_gas <= inventory.waterGasMoles + 0.000000001
        and reserved_liquid <= inventory.waterLiquidMoles + 0.000000001, label .. "出口预留超卖真实库存")
end

local function assert_restored(data)
    assert(data.restoredCell.depth == data.originalCell.depth
        and data.restoredCell.liquidId == data.originalCell.liquidId
        and data.restoredCell.ground == data.originalCell.ground, "地表液体测试没有归还原格")
end

local function assert_world_balance(data, before, after, label)
    local emitted_gas = after.outlet.emittedGasMoles - before.outlet.emittedGasMoles
    local removed_moles = water_moles(before.outlet) - water_moles(after.outlet) - emitted_gas
    local inventory_depth = removed_moles / data.molesPerServing * data.depthPerServing
    -- 世界液深使用 float，守恒误差按一个液深存储刻度检查，库存内部仍按严格 mol 检查。
    assert(near(after.cell.depth - before.cell.depth, inventory_depth, 0.00000012),
        label .. "库存实扣量与世界实际接纳深度不守恒")
    assert(after.outlet.emittedGasMoles == before.outlet.emittedGasMoles, label .. "选择排液后仍沿出口漏出气相")
end

local function assert_phase_command(before, after, expected_phase, accepted, label)
    assert(accepted and after.outlet.outletPhase == expected_phase, label .. "正式切相命令未生效")
    assert(after.outlet.waterGasMoles == before.outlet.waterGasMoles
        and after.outlet.waterLiquidMoles == before.outlet.waterLiquidMoles
        and after.outlet.energy == before.outlet.energy and after.outlet.randomState == before.outlet.randomState,
        label .. "只改出口选择就扣改了真实组分、内能或随机态")
    assert(after.cell.depth == before.cell.depth
        and after.outlet.emittedGasMoles == before.outlet.emittedGasMoles, label .. "切相命令提前提交了输送")
end

local function reservation_ids(inventory, suffix)
    local ids, count = {}, 0
    for _, port in ipairs(inventory.state.Ports) do
        if string.sub(port.PortId, -#suffix) == suffix then
            ids[port.PortId] = true
            count = count + 1
        end
    end
    return ids, count
end

local function assert_old_slot_released(inventory, old_ids, label)
    -- Complete 末尾仍允许合法子槽正常相变；只检查切相前的对向子槽没有继续锁住物料。
    for _, port in ipairs(inventory.state.Ports) do
        assert(not old_ids[port.PortId], label .. "旧的另一相子槽仍占用真实库存")
    end
end

test.runtime("industrial_fluids", "surface_pump_flow", {}, function(data)
    assert(data.scope == "live-isolated-world", "液泵采样没有进入真实隔离世界")
    assert(data.depthPerServing > 0 and data.molesPerServing > 0, "纯水换算数据缺失")
    local before, off, pumped = data.before, data.unpowered, data.pumped
    local same, next_beginning, next_step = data.sameStep, data.nextBeginning, data.nextStep
    for _, stage in ipairs({before, off, data.beginning, pumped, same, next_beginning, next_step}) do
        assert_inventory(stage.pump, data.waterId, "地表液泵")
        assert_inventory(stage.tank, data.waterId, "接收水罐")
        assert(stage.cell.ground == before.cell.ground, "抽液改变了地面身份")
    end
    assert(off.cell.depth == before.cell.depth and water_moles(off.pump) == 0
        and water_moles(off.tank) == 0 and off.pump.energy == 0, "断电液泵扣了地表水或凭空进料")
    assert(pumped.pump.suppliedWatts > 0 and pumped.cell.depth < before.cell.depth
        and pumped.pump.waterLiquidMoles > 0 and water_moles(pumped.tank) == 0,
        "供电后液泵没有真实抽水，或在输送阶段之前直接送入水罐")
    local pumped_depth = water_moles(pumped.pump) / data.molesPerServing * data.depthPerServing
    assert(near(before.cell.depth - pumped.cell.depth, pumped_depth, 0.00000012),
        "液泵缓冲总 mol 与地表真实扣深度不守恒")
    assert(data.beginning.budget.known and data.beginning.budget.gasMoles == 0
        and data.beginning.budget.liquidMoles == 0 and pumped.budget.gasMoles == 0
        and pumped.budget.liquidMoles == 0, "本轮新抽入的水污染了起始冻结库存")
    assert(water_moles(same.tank) == 0 and near(water_moles(same.pump), water_moles(pumped.pump)),
        "新抽液体同轮穿透了出口，或正常相变丢失水总量")
    assert(near(same.pump.energy, pumped.pump.energy), "同轮正常相变没有保持内能")
    assert(#same.pump.state.Ports == 0, "本轮冻结库存为空时仍提前占用了新抽液体")
    assert(next_beginning.budget.known and near(next_beginning.budget.gasMoles, same.pump.gasMoles)
        and near(next_beginning.budget.liquidMoles, same.pump.liquidMoles), "下一轮没有冻结真实两相库存")
    assert(water_moles(next_step.tank) > 0 and next_step.pump.waterLiquidMoles < same.pump.waterLiquidMoles,
        "下一轮液体出口被正常相变卡死，或没有输送到真实接收罐")
    assert(near(water_moles(next_step.pump) + water_moles(next_step.tank), water_moles(pumped.pump)),
        "后续输送没有保持泵和水罐的水两相总 mol")
    assert(near(next_step.pump.energy + next_step.tank.energy, pumped.pump.energy),
        "后续输送没有保持泵和水罐的总内能")
    assert(next_step.cell.depth == pumped.cell.depth and next_step.pump.emittedGasMoles == 0,
        "纯输送步骤继续偷抽地表水或向环境漏气")
    assert(data.wetGasPump.submerged and not data.wetGasPump.canOutput,
        "气泵浸在真实无支撑液体格仍允许抽气输出")
    assert_restored(data)
end, "真实液泵：断电、同轮冻结、正常相变、下一轮守恒与湿阻")

test.runtime("industrial_fluids", "surface_outlet_flow", {}, function(data)
    assert(data.scope == "live-isolated-world", "地表出口采样没有进入真实隔离世界")
    local before, emitted = data.before, data.emitted
    local before_blocked, blocked = data.beforeBlocked, data.blocked
    local before_partial, partial = data.beforePartial, data.partial
    local before_retry, retried = data.beforeRetry, data.retried
    for _, stage in ipairs({before, emitted, before_blocked, blocked, before_partial, partial, before_retry, retried}) do
        assert_inventory(stage.outlet, data.waterId, "地表液体出口")
        assert(stage.outlet.outletPhase == "liquid", "测试排液阶段没有保留真实出口相位选择")
        assert(stage.cell.ground == before.cell.ground, "排液改变了地面身份")
    end
    assert(emitted.cell.depth > before.cell.depth and water_moles(emitted.outlet) < water_moles(before.outlet),
        "出口没有把真实液体放入世界 Liquid 层")
    assert_world_balance(data, before, emitted, "初次排液")
    assert(blocked.cell.depth == 1 and water_moles(blocked.outlet) == water_moles(before_blocked.outlet),
        "满世界格拒收后没有保持库存中的水两相总 mol")
    assert(near(blocked.outlet.energy, before_blocked.outlet.energy), "满世界格拒收后丢失了出口内能")
    assert(blocked.outlet.waterGasMoles > before_blocked.outlet.waterGasMoles
        and blocked.outlet.waterLiquidMoles < before_blocked.outlet.waterLiquidMoles,
        "温水满格用例没有经过正常蒸发，不能证明两相断言有效")
    assert(next(blocked.outlet.blockedPorts) ~= nil, "世界拒收原因没有进入真实端口报告")
    assert(water_moles(before_partial.outlet) == water_moles(blocked.outlet)
        and before_partial.outlet.energy == blocked.outlet.energy, "只腾出地表空间就改变了出口库存")
    assert(partial.cell.depth > before_partial.cell.depth and partial.cell.depth <= 1
        and water_moles(partial.outlet) > 0 and water_moles(partial.outlet) < water_moles(before_partial.outlet),
        "仅腾出少量空间后没有按真实接纳量部分提交，或把整批库存直接丢掉")
    assert_world_balance(data, before_partial, partial, "部分接纳")
    assert(next(partial.outlet.blockedPorts) == nil, "部分接纳成功后仍保留旧的端口拒收状态")
    assert(retried.cell.depth > before_retry.cell.depth
        and water_moles(retried.outlet) < water_moles(before_retry.outlet), "清空地表格后原液体批次没有继续输出")
    assert_world_balance(data, before_retry, retried, "解除堵塞续送")
    assert(next(retried.outlet.blockedPorts) == nil, "解除堵塞后端口报告没有恢复")
    local switch = data.phaseSwitch
    for _, stage in ipairs({switch.beforeBlocked, switch.blocked, switch.beforeGasCommand, switch.gasCommand,
        switch.gasBeginning, switch.gasStep, switch.liquidCommand, switch.beforeRetry,
        switch.liquidBeginning, switch.liquidStep}) do
        assert_inventory(stage.outlet, data.waterId, "动态切相出口")
        assert(stage.cell.ground == before.cell.ground, "切相抽排改变了地面身份")
    end
    assert(near(water_moles(switch.blocked.outlet), water_moles(switch.beforeBlocked.outlet), 0.000000001)
        and switch.blocked.cell.depth == 1, "切相前满格没有保留真实温水库存")
    local old_liquid_ids, liquid_count = reservation_ids(switch.blocked.outlet, ":liquid")
    assert(liquid_count > 0 and switch.blocked.outlet.waterGasMoles > 0,
        "切相前没有形成真实液体预留与正常水蒸气")
    assert(near(switch.beforeGasCommand.outlet.waterGasMoles - switch.blocked.outlet.waterGasMoles, 0.1, 0.000000001)
        and switch.beforeGasCommand.outlet.waterLiquidMoles == switch.blocked.outlet.waterLiquidMoles,
        "水蒸气前提没有通过真实加料建立")
    assert_phase_command(switch.beforeGasCommand, switch.gasCommand, "gas", switch.gasCommandAccepted, "液切气")
    assert(switch.gasBeginning.budget.known
        and near(switch.gasBeginning.budget.gasMoles, switch.gasCommand.outlet.gasMoles)
        and near(switch.gasBeginning.budget.liquidMoles, switch.gasCommand.outlet.liquidMoles),
        "切气后没有使用真实起始冻结库存")
    assert_old_slot_released(switch.gasStep.outlet, old_liquid_ids, "切气后的输送")
    local gas_emitted = switch.gasStep.outlet.emittedGasMoles - switch.gasCommand.outlet.emittedGasMoles
    assert(gas_emitted > 0 and switch.gasStep.cell.depth == switch.gasCommand.cell.depth,
        "切气后没有真实排出水蒸气，或绕过满世界格输出了液体")
    assert(near(water_moles(switch.gasStep.outlet) + gas_emitted,
        water_moles(switch.gasCommand.outlet), 0.000000001), "切气后两相总 mol 加实际排气量不守恒")
    local old_gas_ids, gas_count = reservation_ids(switch.gasStep.outlet, ":gas")
    assert(gas_count > 0, "真实吞吐没有留下气子槽批次，未覆盖气切液的旧预留边界")
    assert_phase_command(switch.gasStep, switch.liquidCommand, "liquid", switch.liquidCommandAccepted, "气切液")
    assert(switch.liquidBeginning.budget.known
        and near(switch.liquidBeginning.budget.gasMoles, switch.beforeRetry.outlet.gasMoles)
        and near(switch.liquidBeginning.budget.liquidMoles, switch.beforeRetry.outlet.liquidMoles),
        "切液续送没有使用真实起始冻结库存")
    assert_old_slot_released(switch.liquidStep.outlet, old_gas_ids, "切液后的输送")
    assert(switch.liquidStep.cell.depth > 0
        and water_moles(switch.liquidStep.outlet) < water_moles(switch.beforeRetry.outlet),
        "切回液体并腾出空间后仍被旧气子槽卡死")
    assert_world_balance(data, switch.beforeRetry, switch.liquidStep, "切液后的真实续送")
    local final_gas = switch.liquidStep.outlet.emittedGasMoles - switch.beforeGasCommand.outlet.emittedGasMoles
    local expected_depth = (water_moles(switch.beforeGasCommand.outlet)
        - water_moles(switch.liquidStep.outlet) - final_gas) / data.molesPerServing * data.depthPerServing
    assert(near(switch.liquidStep.cell.depth, expected_depth, 0.00000012),
        "切相全链路的库存水、世界接纳水和实际排气量不守恒")
    assert_restored(data)
end, "真实排液：拒收守恒、部分提交、原批次续送及动态切相")
