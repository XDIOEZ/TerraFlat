-- 管网拓扑与路由：组内检查各用自己的夹具，失败保留报告并继续其余检查。
test.check("world_ready")
test.check("player_ready")

local scenarios = {
    {"network_catalog", "本体工业气液目录完整性"},
    {"network_masks", "管道 16 种连接掩码"},
    {"network_rotation", "四向旋转与输入输出相态"},
    {"network_pressure_direction", "压差与有向输送规则"},
    {"network_valve", "阀门断边与恢复输送"},
    {"network_port_rejection", "端口物质筛选与出口相态"},
    {"network_receiver_routes", "按需接收路径与混气预算"},
    {"network_probe_hysteresis", "机械电子探针测压回差"},
}

for _, scenario in ipairs(scenarios) do
    test.fluid(scenario[1], scenario[2])
end

local function near(actual, expected, message)
    assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <=
        0.00000001 * math.max(1, math.abs(expected)), message)
end

-- 固定随机态验证实际预留和最终去向，断言失败时保留 C# 返回的完整原始载荷。
for _, seed in ipairs({1, 2, 7, 41, 91, 2026}) do
    for _, only_selected in ipairs({false, true}) do
        test.runtime("industrial_fluids", "selector_routes", {seed = seed, onlySelected = only_selected}, function(data)
            assert(data.scope == "live-isolated-world", "选择器没有使用真实隔离世界")
            assert(data.unselected.before == data.unselected.after and data.unselected.ports == 0
                and data.unselected.randomBefore == data.unselected.randomAfter,
                "未选择目标时改动了库存、预留或随机态")
            near(data.unselected.selectedTankMoles + data.unselected.otherTankMoles, 0, "未选择目标仍向两路输出")
            assert(data.selectionAccepted and not data.invalidSelection.accepted
                and data.invalidSelection.selectedBefore == "core:oxygen"
                and data.invalidSelection.selectedAfter == data.invalidSelection.selectedBefore
                and data.invalidSelection.before == data.invalidSelection.after,
                "合法气体选择失败或非法物质选择改变了原状态")
            local last = nil
            for _, step in ipairs(data.steps) do
                near(step.selectedNitrogen, 0, "所选出口输出了氮气，预留池没有满足选择器规则")
                near(step.otherOxygen, 0, "其他出口输出了氧气，预留池没有满足选择器规则")
                for _, reservation in ipairs(step.reservations) do
                    if string.find(reservation.PortId, "selected", 1, true) then
                        assert(reservation.FluidId == "core:oxygen", "所选出口预留了其他物质")
                    elseif string.find(reservation.PortId, "other", 1, true) then
                        assert(reservation.FluidId ~= "core:oxygen", "其他出口预留了所选物质")
                    end
                end
                last = step
            end
            assert(last ~= nil and last.selectedOxygen > 0, "所选出口没有向真实目标输送氧气")
            if data.onlySelected then
                near(last.otherNitrogen, 0, "仅有氧气时其他出口制造了物质")
            else
                assert(last.otherNitrogen > 0, "混气中存在氮气，但其他出口被无关首组分堵住")
            end
            near(data.initialGas, data.inputOxygen + data.inputNitrogen, "选择器夹具起始装料不一致")
            near(data.finalGas, data.initialGas, "实际双路输送破坏总物质量")
            near(data.finalEnergy, data.initialEnergy, "实际双路输送破坏总内能")
            assert(not data.rejectedPool.accepted and data.rejectedPool.ports == 0
                and data.rejectedPool.before == data.rejectedPool.after
                and data.rejectedPool.randomBefore == data.rejectedPool.randomAfter,
                "无合格物质的预留改动了库存、预留或随机态")
            assert(data.sticky.reserved and data.sticky.retried and data.sticky.firstFluid == "core:oxygen"
                and data.sticky.retryFluid == data.sticky.firstFluid
                and data.sticky.before == data.sticky.after
                and data.sticky.randomBefore == data.sticky.randomAfter,
                "已有预留被重新抽签或切换物质，堵塞重试不能保持原批次")
            near(data.sticky.firstMoles, 0.02, "首批预留数量错误")
            near(data.sticky.retryMoles, data.sticky.firstMoles, "重试把原批次数量改成新请求量")
            assert(data.switched.oldReserved and data.switched.selectedOxygen and data.switched.selectedNitrogen,
                "未能准备切换前的氧气预留与新的氮气选择")
            assert(data.switched.remainingReservationFluid ~= "core:oxygen" and data.switched.receiverNitrogen > 0,
                "切换选择器后旧氧气预留堵住了新的氮气输出")
            near(data.switched.remainingOxygen, 0.1, "切换选择器错误消费了已取消预留的氧气")
            near(data.switched.receiverOxygen, 0, "旧预留氧气仍流入了新的选择出口")
            near(data.switched.finalGas, data.switched.initialGas, "切换选择器破坏了气体守恒")
            near(data.switched.finalEnergy, data.switched.initialEnergy, "切换选择器破坏了内能守恒")
        end, "选择器原始输送与筛选预留，随机态 " .. seed .. (only_selected and "，只有所选气体" or "，氧氮混合"))
    end
end

for direction = 0, 3 do
    for _, mixed in ipairs({true, false}) do
        test.runtime("industrial_fluids", "phase_preference", {direction = direction, mixed = mixed}, function(data)
            local vertical = data.direction % 2 == 1
            local expect_liquid = data.mixed and vertical or (not data.mixed and not vertical)
            local target_water = data.targetWaterGas + data.targetWaterLiquid
            assert(data.scope == "live-isolated-world", "相态测试没有使用真实隔离世界")
            if expect_liquid then
                assert(data.flowPhase == "liquid" and target_water > 0, "首选或回退液相没有发生真实搬运")
                near(data.targetOxygenGas, 0, "选液相时错误搬运了氧气")
            else
                assert(data.flowPhase == "gas" and data.targetOxygenGas > 0, "首选或回退气相没有发生真实搬运")
                near(target_water, 0, "选气相时错误搬运了水")
            end
            near(data.targetOxygenGas + data.remainingOxygenGas, (data.mixed or vertical) and 0.05 or 0,
                "相态选择或正常相变破坏氧气物质量")
            near(target_water + data.remainingWaterGas + data.remainingWaterLiquid,
                (data.mixed or not vertical) and 1 or 0, "相态选择或正常相变破坏水的两相总物质量")
            near(data.finalGas + data.finalLiquid, data.initialGas + data.initialLiquid, "相态变化破坏全部组分总量")
            near(data.finalEnergy, data.initialEnergy, "输送和正常相变破坏总内能")
        end, "方向 " .. direction .. (mixed and "，双相首选" or "，单相回退"))
    end
end
