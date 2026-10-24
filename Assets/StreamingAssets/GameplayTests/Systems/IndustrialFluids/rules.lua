local function near(a, b, message, tolerance)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= (tolerance or 0.000001), message)
end

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
    assert(data.contentsReplacementRebound and data.readContentsUsesOwnedState, "Contents 换代后仍读旧库存")
    assert(data.unchangedFlushReusesBytes and data.unchangedClockFlushReusesBytes, "稳态气罐重复序列化")
    assert(data.clockChangeWritesBytes and data.persistedRuptured and data.rupturedTankRejected, "超压计时或破裂状态未持久化")
    near(data.persistedClock, 0.25, "真实超压计时写回错误")
    assert(data.payloadReplacementRebound and data.binaryReplacementRebound, "外部二进制或模块换代后仍复用旧库存")
    near(data.payloadReplacementClock, 0, "载荷替换后残留旧超压计时")
    assert(not data.payloadReplacementRuptured and data.loadedPayloadFlushReusesBytes, "新载荷状态错误或读取后重复序列化")
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

test.system("industrial_fluids", "portable_tank_air_fill", {}, function(data)
    assert(data.filled, "铁气罐未能从真实大气抽取气体")
    assert(data.oxygen > 0 and data.nitrogen > 0, "充气未保留空气中的氮氧混合组分")
    near(data.pressure, 101.325, "4 L 气罐未按真实容积温度达到 1 标准大气压", .0001)
    near(data.temperature, 293.15, "充装时没有保留气源温度和内能", .001)
    assert(data.sourceConserved, "充气过程造气或丢气")
    assert(not data.filledAgain and data.againSourceUnchanged, "已经达到目标压力仍继续扣除环境气体")
    assert(not data.vacuumFilled and data.vacuumTargetEmpty, "真空环境凭空生成了空气")
    assert(not data.thinFilled and data.thinSourceUnchanged and data.thinTargetEmpty,
        "无压缩机时从低压大气直接制造标准气压")
    assert(data.seededGas and data.seededLiquid and data.mixedFilled, "含已有气液的气罐无法正常补气")
    assert(data.carbonPreserved and data.liquidPreserved and data.liquidSpacePreserved,
        "补充空气清空了原有气体或液体")
    assert(data.mixedGasAdded and data.mixedConserved, "混气补充破坏物质量守恒")
    assert(data.mixedPressure <= 101.325 and data.mixedPressure > 101.324,
        "含液相和旧气体的目标压力没有按有效气相容积与内能解算")
    assert(data.existingEnergyPreserved, "混气覆盖了已有内能")
    assert(data.roomSeeded and data.roomFilled and data.roomMolesConserved and data.roomEnergyConserved,
        "密闭舱空气充装未保证物质量和内能守恒")
    near(data.roomMixedTemperature, 310, "密闭舱抽气没有保持真实气源温度", .001)
    assert(data.partialFilled and data.partialConserved and
        data.partialPressure > 0 and data.partialPressure < 101.325,
        "气源不足时未部分充装或错误创造气体补足目标")
    assert(data.ironCarrier and data.ironBuilding and data.steelExcluded,
        "一键空气功能错误扩展到钢制气罐")
end, "铁制气罐真实空气充装、守恒、真空拒绝、带气液补充和目标压力")
