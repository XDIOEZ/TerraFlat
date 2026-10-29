local function near(a, b, message, tolerance)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= (tolerance or 0.000001), message)
end

test.system("industrial_fluids", "combined_tank_inventory", {}, function(data)
    near(data.before, 90, "组合罐没有合并真实气量")
    near(data.held, 45, "拆回没有按单格容量分出内容")
    near(data.remaining, 45, "拆回丢失或复制剩余气体")
    near(data.heldOxygen + data.remainingOxygen, 20, "混气拆分改变实际组分")
    near(data.remainingCapacity, 100, "拆回后容量未缩小")
    assert(data.remainingMembers == 1 and data.rejectMixedLiquid, "成员拆分或异种液体组合拒绝错误")
    assert(data.centerSlice == 4 and data.seamSlice == 9,
        "组合接缝拉伸了中央气流或液滴标志")
end, "组合气液罐真实库存、拆回守恒与标志接缝")

test.system("industrial_fluids", "liquid_block_tanks", {}, function(data)
    assert(#data.tanks == 2 and #data.recipeWarnings == 0, "铁钢液罐或正式制作配方缺失")
    for index, tank in ipairs(data.tanks) do
        local expected_id = index == 1 and "LiquidTankBlock_Iron" or "LiquidTankBlock_Steel"
        assert(tank.id == expected_id and tank.kind == "tank" and tank.combineAdjacent, "液罐没有启用自然组合储罐能力")
        assert(tank.material == (index == 1 and "iron" or "steel"), "液罐材质配置错误")
        near(tank.capacity, 800, "液罐每格没有扣除外壳体积后保留800L")
        assert(tank.combinedMembers == 3 and tank.combinedCapacity == 2400, "组合液罐没有按每块800L相加")
        assert(not tank.manualAirFill and tank.stackable, "液罐误启用补气或空罐不能堆叠")
        assert(#tank.ports == 4 and #tank.pairs == 2, "液罐接口或建筑配对缺失")
        local directions = {}
        for _, port in ipairs(tank.ports) do
            assert(port.Phase == "liquid" and port.Mode == "both" and port.Chamber == "main",
                "液罐没有使用双向独立液体接口")
            assert(not directions[port.Direction], "液罐端口方向重复")
            directions[port.Direction] = true
        end
        for direction = 0, 3 do assert(directions[direction], "液罐缺少四向液体接口") end
        for pair_index, pair in ipairs(tank.pairs) do
            assert(pair.role == (pair_index == 1 and 1 or 0) and pair.bodyId == tank.id
                and pair.carrierId == tank.id .. "_Summoner" and pair.machineId == tank.id,
                "液罐放置、机器或拆回配对错误")
            assert(pair.maxHp > 0 and pair.sprite == "Assets/6_Art/Generated/Chemistry/" .. tank.id .. "/" .. tank.id .. "_Idle.png",
                "液罐缺少受击生命或独立液滴贴图引用")
        end
        near(tank.filled, 800, "液罐没有按实际容量接液")
        near(tank.overflow, 0, "液罐满后仍接收液体")
        near(tank.drained, 25, "液罐不能供出实际液体")
        near(tank.mixed, 0, "液罐混入了另一种液体")
        near(tank.sourceLeft, 200, "液罐超量扣取来源")
        near(tank.stored, 775, "液罐保存恢复丢失内容")
        near(tank.delivered, 25, "出液没有交付真实数量")
        near(tank.otherStored, 5, "拒绝混液或保存影响了另一罐")
        assert(tank.storedFluid == "core:pure_water" and tank.otherFluid == "core:filtered_water"
            and tank.differentContentsCannotStack, "独立液罐串料或不同内容合堆")
        assert(tank.crafted.committed and not tank.missing.committed and not tank.full.committed,
            "液罐制作正常、缺料或输出满拒绝规则错误")
        assert(tank.crafted.outputAfter[1].id == tank.id .. "_Summoner", "液罐制作产物不是可放置物品")
        for _, refusal in ipairs({tank.missing, tank.full}) do
            for slot_index, before in ipairs(refusal.inputBefore) do
                local after = refusal.inputAfter[slot_index]
                assert(before.id == after.id and before.amount == after.amount, "液罐制作拒绝后扣了材料")
            end
        end
    end
end, "铁钢液罐配对、四向液网、固定容量、拒绝混液、独立保存与制作事务")

test.system("industrial_fluids", "block_tank_stacking", {}, function(data)
    assert(#data.tanks == 6, "气罐方块或便携气罐定义覆盖不完整")
    for _, tank in ipairs(data.tanks) do
        if string.sub(tank.id, 1, 13) == "GasTankBlock_" then
            assert(tank.combineAdjacent, "气罐方块没有启用自然组合")
            assert(tank.stackable and tank.canStack, tank.id .. "没有启用同类方块堆叠")
            assert(tank.independentPayloads and tank.differentContentsCannotStack and tank.sourceUnchanged,
                tank.id .. "独立罐内容保存混用、带不同气体合堆或改动原库存")
        else
            assert(not tank.stackable and not tank.canStack, tank.id .. "便携气罐被错误改为可堆叠")
        end
    end
end, "铁钢气罐方块可堆叠、各罐独立内容保存与便携气罐隔离")

test.system("industrial_fluids", "reservations", {}, function(data)
    assert(data.reserved and data.transferred, "正式预留或转移失败")
    near(data.reservedMoles, 3, "端口没有预留指定数量")
    near(data.availableAfterReservation, 7, "预留没有排除其它调用可用量")
    near(data.sourceMoles, 8, "源库存扣量错误")
    near(data.targetMoles, 2, "目标库存收到的数量错误")
    near(data.sourceMoles + data.targetMoles, 10, "转移破坏物质量守恒")
    near(data.reservedRemainder, 1, "部分转移没有保留端口余量")
    near(data.availableAfterRelease, 8, "释放预留没有恢复可用量")
    assert(data.portsAfterRelease == 0, "释放后残留端口预留")
end, "流体端口预留、部分转移与守恒")

test.system("industrial_fluids", "infinite_gas_supply", {}, function(data)
    near(data.sample, 100, "无限气源没有保留有限配比样本")
    near(data.sourceHydrogenShare, 2 / 3, "无限气源样本配比改变")
    near(data.first, 250, "单次供气不应受样本大小限制")
    near(data.second, 250, "持续供气没有重复输出")
    near(data.third, 250, "持续供气没有重复输出")
    near(data.full, 0, "目标满后仍然供气")
    near(data.target, 750, "目标未按容量接收实际气体")
    near(data.targetFirstGas, 500, "无限混气转移改变配比")
    near(data.targetTemperature, 320, "供气温度未使用来源配置")
    near(data.filtered, 30, "过滤没有保留原混气比例")
    near(data.filterRejectedGas, 0, "过滤输出了不允许的组分")
    assert(data.reservations and data.storedReservations == 0 and data.rejected,
        "无限预约被有限样本截断、写入存档或破坏拒绝事务")
    assert(data.liquidTaken and data.liquidLeft == 1, "无限气源错误改变液体守恒")
    assert(data.initiallyFinite and data.restoredInfinite, "来源能力没有由机器定义重建")
    near(data.restoredDelivery, 200, "恢复后的无限气源无法超过样本供气")
    near(data.emptyRestoreSample, 100, "恢复空库存丢失无限气源样本")
    near(data.liquidRestoreSample, 100, "恢复纯液体库存丢失无限气源样本")
    near(data.liquidRestoreShare, 2 / 3, "恢复库存后无限气源配比改变")
    near(data.lquidRestoreAmount, 2, "恢复无限气源时未保留真实液体")
    assert(data.pendingAfterRestore, "恢复库存时丢失未结算的无限供气预约")
end, "无限气源有限样本、持续供气、过滤、预约、恢复与液体守恒")

test.system("industrial_fluids", "atomic_recipe", {}, function(data)
    assert(not data.missingIngredientAccepted, "缺少第二反应物仍接受配方")
    near(data.afterFailure, 10, "配方失败扣掉第一反应物")
    assert(data.accepted, "合格原子配方没有提交")
    near(data.takenMoles, 5, "重复物质需求没有合并真实扣量")
    near(data.remainingMoles, 5, "原子配方剩余量错误")
    assert(not data.oversizedLiquidAccepted, "超容积液体被接收")
    near(data.liquidAfterRejectedAdd, 0, "容量拒绝后残留部分液体")
end, "流体配方原子扣量与容积拒绝")

test.system("industrial_fluids", "heat", {}, function(data)
    assert(data.transfer > 0, "冷热库存没有交换热量")
    near(data.coldKelvin, 350, "大步热交换越过平衡温度")
    near(data.hotKelvin, 351, "单独补热没有从真实内能更新温度")
    near(data.added, 68, "补热实际量错误")
    near(data.energyAfter, data.energyBefore + 68, "内部热交换破坏总能量")
    assert(data.massRevisionStable, "只加热错误地改变质量版本")
    near(data.hotMoles, 2, "加热改变热库存物质量")
    near(data.coldMoles, 2, "加热改变冷库存物质量")
    near(data.selfTransfer, 0, "同一库存自换热产生能量")
end, "流体内能、平衡温度与质量隔离")

for _, input in ipairs({
    {current = 300, ambient = 400, seconds = 1, gas = 2, liquid = 0},
    {current = 400, ambient = 300, seconds = 1, gas = 2, liquid = 0},
    {current = 300, ambient = 300.5, seconds = 1, gas = 2, liquid = 0, expected = 300.5},
    {current = 300.5, ambient = 300, seconds = 1, gas = 2, liquid = 0, expected = 300},
    {current = 300, ambient = 300.25, seconds = 1, gas = 2, liquid = 0, expected = 300.25},
    {current = 300, ambient = 301, seconds = .1, gas = 2, liquid = 0, expected = 300.05},
    {current = 250, ambient = 200, seconds = 10, gas = 0, liquid = 3, expected = 244.9993718548904},
    {current = 250, ambient = 350, seconds = 10, gas = 2, liquid = 3},
    {current = 300, ambient = 400, seconds = 0, gas = 2, liquid = 1},
    {current = 300, ambient = 400, seconds = -1, gas = 2, liquid = 1},
}) do
    test.system("industrial_fluids", "ambient_heat", input, function(data)
        local expected = input.current
        if input.seconds > 0 then
            expected = input.expected or (input.ambient + (input.current - input.ambient) * .99 ^ input.seconds)
        end
        near(data.whole, expected, "工业流体自然传热没有按温差百分比和0.5℃每秒保底推进")
        near(data.split, data.whole, "流体内能分步结算偏离相同总时长")
        near(data.energyAfter - data.energyBefore, data.heat, "环境换热回执与实际内能增减不一致")
        near(data.heat, (data.whole - input.current) * data.heatCapacity, "温度变化没有按实际热容换回内能")
        assert(data.massRevisionStable and data.gasMoles == input.gas and data.liquidMoles == input.liquid,
            "自然换热改写了质量版本或气液数量")
        near(data.zeroTemperature, input.current, "零时间改变了流体温度")
        assert(data.zeroHeat == 0 and data.negativeHeat == 0
            and data.invalidHeat == 0 and data.invalidTimeHeat == 0, "非正时间或非法参数仍改变流体内能")
        assert(data.emptyHeat == 0 and data.emptyEnergy == 0, "空库存自然换热产生内能")
    end, string.format("流体自然温差比例与内能：%gK→%gK，%g秒", input.current, input.ambient, input.seconds))
end

test.system("industrial_fluids", "phase_change", {}, function(data)
    assert(data.converted > 0 and data.gasMoles > 0 and data.liquidMoles > 0, "低压两相样本没有实际相变")
    near(data.gasMoles + data.liquidMoles, 10, "相变破坏总物质量")
    near(data.energyAfter, data.energyBefore, "同腔相变凭空增加或损失内能")
    assert(data.kelvin > 0 and data.kelvin < 300, "蒸发没有消费潜热或温度无效")
end, "两相变换的真实物质量与内能守恒")

test.system("industrial_fluids", "cold_wind_cache", {}, function(data)
    assert(data.initial and data.replaced and data.evicted and data.missing and data.bounded,
        "冷区块逐出后的固定风力缓存未复用、更新或限制容量")
end, "冷地表固定风力汇总值复用与有界逐出")

test.system("industrial_fluids", "cold_temperature_path", {}, function(data)
    assert(#data.groups == 4, "没有采样普通、新旧气候及两类星体覆盖")
    for _, group in ipairs(data.groups) do
        assert(group.committed and #group.samples == 16, "独立完整地表生成失败：" .. group.mode)
        for _, sample in ipairs(group.samples) do
            assert(sample.found, "完整地表缺少真实温度层：" .. group.mode)
            near(sample.point, sample.full, "冷机器点读偏离完整地表温度：" .. group.mode, .0001)
            near(sample.wrapped, sample.point, "跨环绕查询改变温度：" .. group.mode, .0001)
            if group.mode == "space" or group.mode == "uniform" then
                assert(sample.point >= -57 and sample.point <= -23, "星体点读没有使用覆盖温度和波动范围")
            end
        end
        assert(group.pointReadBytes == 0, "冷机器点温度仍创建区块或生成工作数据：" .. group.mode)
    end
end, "冷机器正式地理温度、星体覆盖、环绕与完整生成一致且点读无分配")

test.system("industrial_fluids", "portable_tank_cache", {}, function(data)
    assert(data.found and data.wrapperReused and data.wrapsOwnedContents, "便携气罐没有复用真实内容的库存包装")
    assert(data.configCopies and data.itemsIsolated, "便携气罐把可变配置或真实库存共享给不同调用或物品")
    near(data.volumeAfterExternalEdit, 4, "外部修改返回配置污染后续容量")
    near(data.otherVolume, 4, "修改气罐配置污染另一物品容量")
    assert(data.supportsSpacesuitAfterExternalEdit and data.otherSupportsSpacesuit, "外部修改返回配置污染供氧资格")
    assert(not data.manualFillAfterExternalEdit, "外部修改返回配置开启了未声明的手动充气能力")
    assert(data.contentsReplacementRebound and data.readContentsUsesOwnedState, "Contents 换代后仍读旧库存")
    assert(data.unchangedFlushReusesBytes, "稳态气罐重复序列化")
    assert(data.payloadReplacementRebound and data.binaryReplacementRebound, "外部二进制或模块换代后仍复用旧库存")
    assert(data.loadedPayloadFlushReusesBytes, "新载荷状态错误或读取后重复序列化")
end, "便携气罐包装复用、配置隔离与载荷换代")

test.system("industrial_fluids", "portable_tank_payload", {}, function(data)
    assert(data.writesOwnedState, "便携气罐写入没有保留正式库存的真实内容")
    near(data.initialGasMoles, 2, "便携气罐气相保存数量错误")
    near(data.initialLiquidMoles, 1, "便携气罐液相保存数量错误")
    near(data.initialKelvin, 300, "便携气罐保存后温度改变")
    near(data.initialPressure, data.pressureBefore, "便携气罐保存后压力改变")
    assert(data.unchangedWriteReusesBytes, "没有变化的正式库存写入重复序列化")
    assert(data.taken and data.contentChangeWritesBytes, "真实扣量没有使缓存写回新载荷")
    near(data.takenGasMoles, 0.5, "正式气相扣量错误")
    near(data.remainingGasMoles, 1.5, "扣量后保存的真实气相错误")
    near(data.remainingLiquidMoles, 1, "扣气误改液相数量")
    near(data.remainingGasMoles + data.takenGasMoles, data.initialGasMoles, "扣量与保存破坏物质量守恒")
    near(data.remainingKelvin, 300, "等温提取后的内能保存错误")
    near(data.remainingPressure, data.expectedPressure, "缓存写回后压力不是实际内容读数")
    assert(data.remainingPressure > 0 and data.remainingPressure < data.initialPressure, "扣气后压力没有下降")
    assert(data.unchangedContentFlushReusesBytes, "内容变更写回后再次 Flush 仍重复序列化")
end, "便携气罐真实组分、内能、压力与按需序列化")

test.system("industrial_fluids", "portable_tank_placement", {}, function(data)
    assert(#data.tanks == 4, "铁制或钢制气罐缺少载体或落地本体")
    for index, tank in ipairs(data.tanks) do
        local placed = index % 2 == 0
        local carrier = index <= 2 and "PortableGasTank_Iron" or "PortableGasTank_Steel"
        assert(tank.enabled and tank.role == (placed and 1 or 0) and not tank.stackable,
            "便携气罐的建筑角色、启用状态或单件配置错误：" .. tank.id)
        assert(tank.carrierId == carrier and tank.bodyId == carrier .. "_Building",
            "便携气罐放置和拆回未使用同一配对")
        assert(tank.canBePickedUp ~= placed and (placed or tank.explicitPlacement),
            "气罐绕过了面板放置入口或建筑拆回流程")
        local shared = {}
        for _, id in ipairs(tank.sharedModules) do shared[id] = true end
        assert(shared["流体气罐模块"] and shared["生命值系统模块"], "放置或拆回气罐没有保留气液与耐久")
        local steel = index > 2
        assert(tank.maxHp == (steel and 160 or 80), "气罐落地后最大耐久改变")
        assert(tank.configuration.VolumeLiters == 4
            and tank.configuration.GasCapacityStandardLiters == (steel and 60 or 20)
            and tank.configuration.NaturalFillFraction == .2
            and tank.configuration.MaterialId == (steel and "core:steel" or "core:iron"),
            "气罐放置前后的容量、自然上限或材质不一致")
        assert(tank.configuration.AllowManualAirFill, "铁制或钢制便携气罐没有声明手动充气能力")
    end
end, "铁制和钢制便携气罐放置配对、拆回、共享内容与材质配置")

test.system("industrial_fluids", "portable_tank_air_fill", {}, function(data)
    assert(data.filled and data.conserved, "自然补气没有使用真实来源或破坏守恒")
    near(data.standardLiters, 4, "自然补气未到20%固定数量上限", .00001)
    near(data.oxygenRatio, .21, "混合空气组分比例被改写", .00001)
    assert(data.repeatRejected and data.highPreserved, "达到或高于自然上限仍改动内容")
    assert(data.vacuumRejected and data.partial, "真空造气或有限来源未部分充装")
    assert(data.tinyFilled and data.tinyConserved and data.tinyLiters <= .00002, "微小固定容量失败或超上限")
    assert(data.rollback and data.nullTankExcluded, "拒绝充装未完整回源或空物品获得能力")
end, "便携罐20%自然补气、混合比例、有限来源、微容量与拒绝回滚")

-- 组合罐碰撞合并回归：业务断言留在Lua，C#只调用正式矩形合并与物理容器。
local function check_merged_obstacle_boxes(data, expected_count, expected_area, blocked_points, empty_points)
    assert(data.rebuilt and not data.unchangedRebuilt and not data.unchangedSourceChanged,
        "没有真实几何变化仍重复构建组合罐碰撞")
    assert(#data.boxes == expected_count and #data.boxes <= data.inputCount,
        "组合罐碰撞没有减少到预期矩形数量")
    local area = 0
    for _, box in ipairs(data.boxes) do
        assert(box.width > 0 and box.height > 0, "合并产生无效碰撞矩形")
        area = area + box.width * box.height
    end
    near(area, expected_area, "合并扩大或漏掉原本的罐体阻挡面积", .00001)
    local function covered(point)
        for _, box in ipairs(data.boxes) do
            if point[1] > box.x and point[1] < box.x + box.width
                and point[2] > box.y and point[2] < box.y + box.height then return true end
        end
        return false
    end
    for _, point in ipairs(blocked_points or {}) do assert(covered(point), "罐体原有阻挡消失") end
    for _, point in ipairs(empty_points or {}) do assert(not covered(point), "合并填平了空洞、凹口或真实间隙") end
end

local tank_rectangle = {}
for y = 0, 15 do
    for x = 0, 31 do table.insert(tank_rectangle, {x = x, y = y, width = 1, height = 1}) end
end
test.system("industrial_fluids", "combined_tank_collision_geometry", {boxes = tank_rectangle}, function(data)
    check_merged_obstacle_boxes(data, 1, 512, {{.5,.5},{31.5,15.5}}, {{-.1,.5},{32.1,.5}})
end, "整片512格储罐只保留一个实体碰撞矩形")

for _, scenario in ipairs({
    {name = "凹形", count = 2, area = 5,
        boxes = {{x=0,y=0,width=1,height=1},{x=1,y=0,width=1,height=1},{x=2,y=0,width=1,height=1},
            {x=0,y=1,width=1,height=1},{x=0,y=2,width=1,height=1}},
        blocked = {{.5,.5},{2.5,.5},{.5,2.5}}, empty = {{1.5,1.5},{2.5,2.5}}},
    {name = "空心", count = 4, area = 8,
        boxes = {{x=0,y=0,width=1,height=1},{x=1,y=0,width=1,height=1},{x=2,y=0,width=1,height=1},
            {x=0,y=1,width=1,height=1},{x=2,y=1,width=1,height=1},
            {x=0,y=2,width=1,height=1},{x=1,y=2,width=1,height=1},{x=2,y=2,width=1,height=1}},
        blocked = {{.5,1.5},{2.5,1.5},{1.5,.5},{1.5,2.5}}, empty = {{1.5,1.5}}},
    {name = "真实间距", count = 2, area = 1.8,
        boxes = {{x=0,y=0,width=.9,height=1},{x=1,y=0,width=.9,height=1}},
        blocked = {{.45,.5},{1.45,.5}}, empty = {{.95,.5}}},
    {name = "对角接触", count = 2, area = 2,
        boxes = {{x=0,y=0,width=1,height=1},{x=1,y=1,width=1,height=1}},
        blocked = {{.5,.5},{1.5,1.5}}, empty = {{1.5,.5},{.5,1.5}}},
    {name = "极小正间隙", count = 2, area = 2,
        boxes = {{x=0,y=0,width=1,height=1},{x=1.000003814697265625,y=0,width=1,height=1}},
        blocked = {{.5,.5},{1.5,.5}}, empty = {{1.0000019073486328125,.5}}},
    {name = "不等高错位", count = 2, area = 3,
        boxes = {{x=0,y=0,width=1,height=2},{x=1,y=0,width=1,height=1}},
        blocked = {{.5,1.5},{1.5,.5}}, empty = {{1.5,1.5}}},
}) do
    test.system("industrial_fluids", "combined_tank_collision_geometry", {boxes = scenario.boxes}, function(data)
        check_merged_obstacle_boxes(data, scenario.count, scenario.area, scenario.blocked, scenario.empty)
    end, "组合罐碰撞保留" .. scenario.name .. "的真实范围")
end

local screenshot_tank_boxes = {}
for _, row in ipairs({{0,0,7},{1,0,7},{2,5,7},{3,5,6},{4,4,6},{5,4,5}}) do
    for x = row[2], row[3] do table.insert(screenshot_tank_boxes, {x=x,y=row[1],width=1,height=1}) end
end
test.system("industrial_fluids", "combined_tank_collision_geometry", {boxes=screenshot_tank_boxes}, function(data)
    check_merged_obstacle_boxes(data, 5, 26, {{.5,.5},{7.5,1.5},{5.5,5.5}}, {{3.5,2.5},{7.5,3.5},{6.5,5.5}})
end, "截图同形26格组合罐合并到5个矩形并保留台阶凹口")

test.system("industrial_fluids", "combined_tank_collision_geometry",
    {boxes={{x=-1,y=0,width=3,height=1},{x=31,y=0,width=2,height=1}}}, function(data)
    check_merged_obstacle_boxes(data, 2, 5, {{-.75,.5},{1.75,.5},{31.25,.5},{32.75,.5}}, {{2.25,.5},{33.25,.5}})
end, "通用矩形保持负坐标和跨批次完整形状，不再按数据区块裁断")

test.system("industrial_fluids", "combined_tank_collision_capabilities", {}, function(data)
    assert(#data.builtInTanks == 4, "没有覆盖铁钢水罐和气罐")
    for _, tank in ipairs(data.builtInTanks) do assert(tank.eligible, "已有罐体未走共用合并能力：" .. tank.id) end
    assert(data.futureTank and data.futureStorageStrategy, "新增罐体或储存策略仍要硬编码名称才能合并")
    assert(not data.disabledCombination and not data.nonBlocking and not data.pipe and not data.ordinaryMachine
        and not data.missingNode, "普通机器、管道或未开启组合能力的设备误入罐体合并")
end, "水气罐与未来储存能力共用碰撞合并资格")

test.system("industrial_fluids", "combined_tank_collision_lifecycle", {}, function(data)
    assert(data.initialCount == 1 and data.initialArea == 3 and data.unchangedChanges == 0,
        "连续罐没有合并，或库存通知重复写回物理组件")
    assert(data.splitCount == 2 and data.splitArea == 2 and not data.middleStillBlocked,
        "拆掉中间罐后旧大碰撞体仍阻挡缺口")
    assert(data.mirrorInitialCount == 1 and data.mirrorSplitCount == 2 and data.mirrorSourcesValid
        and data.mirrorStableIdentity and data.mirrorEmptyCount == 0,
        "环绕镜像没有复用、拆分、追踪真实来源或清空旧合并碰撞")
    assert(data.independentRetained and data.emptyMergedCount == 0 and data.clearedCount == 0,
        "合并池清理误删普通机器/自然物，或保留旧罐体碰撞")
    assert(data.genericProxyRegistry and data.batchBoundaryKeepsWholeShape and data.crossingArea == 2,
        "矩形未登记到通用实体代理，或跨物理批次时被错误裁断")
end, "组合罐拆中间、原生形状复用、独立来源保留、跨批次完整形状与镜像收尾")


test.system("industrial_fluids", "fuel_oil_engine_definition", {}, function(d)
    assert(d.kind == "combustion-engine" and d.ambientCombustion and d.torque > 0 and d.rpm > 0,
        "燃料发动机没有通过环境助燃策略接入机械动力")
    assert(#d.ports == 1 and d.ports[1].Phase == "liquid" and d.ports[1].Mode == "input"
        and d.ports[1].FluidId == "core:fuel_oil", "燃油机未使用独立真实燃油液体口")
    assert(#d.inputs == 1 and d.inputs[1].FluidId == "core:fuel_oil" and d.inputs[1].Phase == "liquid"
        and d.joules == 8600000 and d.efficiency == 0.35, "燃油机反应或功量定义错误")
    for i, pair in ipairs(d.pairs) do
        assert(pair.role == (i == 1 and 1 or 0) and pair.body == "FuelEngine" and pair.carrier == "FuelEngine_Summoner"
            and pair.machine == "FuelEngine" and pair.health > 0, "燃油机放置/拆回身份或受击定义缺失")
    end
end, "燃油机真实液体供料、环境助燃、机械动力与建筑配对")
