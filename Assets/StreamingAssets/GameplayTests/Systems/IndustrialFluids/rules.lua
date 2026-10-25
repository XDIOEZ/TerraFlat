local function near(a, b, message, tolerance)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= (tolerance or 0.000001), message)
end

test.system("industrial_fluids", "liquid_block_tanks", {}, function(data)
    assert(#data.tanks == 2 and #data.recipeWarnings == 0, "铁钢液罐或正式制作配方缺失")
    for index, tank in ipairs(data.tanks) do
        local expected_id = index == 1 and "LiquidTankBlock_Iron" or "LiquidTankBlock_Steel"
        assert(tank.id == expected_id and tank.kind == "tank", "液罐没有复用正式储罐能力")
        assert(tank.material == (index == 1 and "iron" or "steel"), "液罐材质配置错误")
        near(tank.capacity, 100, "液罐容量不是100L")
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
        near(tank.filled, 100, "液罐没有按实际容量接液")
        near(tank.overflow, 0, "液罐满后仍接收液体")
        near(tank.drained, 25, "液罐不能供出实际液体")
        near(tank.mixed, 0, "液罐混入了另一种液体")
        near(tank.sourceLeft, 50, "液罐超量扣取来源")
        near(tank.stored, 75, "液罐保存恢复丢失内容")
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
