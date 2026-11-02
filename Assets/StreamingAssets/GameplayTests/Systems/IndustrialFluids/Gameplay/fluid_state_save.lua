test.check("world_ready")
test.check("player_ready")
test.runtime("industrial_fluids", "liquid_network_temperature", {}, function(d)
    assert(math.abs(d.mixed - 83.3333333333) < .00001 and math.abs(d.pipe - d.mixed) < .00001,
        "正式液网没有在无流动时统一罐和管道温度")
    assert(math.abs(d.output - d.mixed) < .00001, "正式出液批次没有继承统一液网温度")
    assert(math.abs(d.closedFirst - d.mixed) < .00001 and math.abs(d.closedSecond - d.mixed) < .00001,
        "关闭阀门恢复了旧液温")
    assert(math.abs(d.separateFirst - (100 * d.mixed + 10 * 10) / 110) < .00001
        and math.abs(d.separateSecond - d.mixed) < .00001, "关阀后外部进液影响了另一子网")
    assert(math.abs(d.rejoined - 78.75) < .00001 and math.abs(d.saved - d.rejoined) < .00001,
        "重新接通或正式快照改变了混温结果")
    assert(math.abs(d.immediatelyRejoined - d.rejoined) < .00001, "打开阀门后仍等待下一Tick才混温")
    assert(math.abs(d.total - 160) < .00001 and d.empty and d.combinedShared
        and math.abs(d.combinedLiters - 100) < .00001 and d.combinedCells == 2,
        "总液量缓存、空网或组合罐的库存与覆盖格去重错误")
    assert(d.filtered and d.independentChambers, "温度分组穿过物质过滤或独立腔体")
    assert(d.gasUnaffected, "气相专用腔体被工业液温模拟接管")
end, "真实液网开关阀、无流动同温、分网进液、快照与组合罐去重")

test.fluid("core_serialization", "共用库存序列化")
test.runtime("industrial_fluids", "ownership", {}, function(d)
    assert(d.shared and d.restoredShared and d.capacity == 200, "同类相邻罐没有共享库存与总容量")
    assert(math.abs(d.a-90)<.000001 and math.abs(d.b-90)<.000001, "倒序恢复改变组合罐总量")
    assert(math.abs(d.dismantledAmount-45)<.000001 and math.abs(d.remainingAfterRemoval-45)<.000001, "拆回没有等比分出单格内容")
    assert(d.atmosphereDelta==0, "正常拆回把气体排到环境")
    assert(d.typesAndMaterialsSeparate and #d.combinations == 4, "不同罐型或材质错误组合")
    assert(d.differentLiquidsSeparate and math.abs(d.liquidsBeforeEmptying-90)<.000001,
        "空罐桥接把不同液体合并或丢失")
    assert(d.emptyTankJoined, "排空异种液体后没有自然加入相邻组合罐")
    for _, tank in ipairs(d.combinations) do
        assert(tank.shared and tank.memberCapacity > 0 and tank.capacity == 3 * tank.memberCapacity,
            tank.id .. "三格未累加当前定义的真实容量")
        assert(tank.leftMask == 2 and tank.middleMask == 10 and tank.rightMask == 8, "自然拼接四向掩码错误")
        assert(tank.split and tank.splitMask == 0, "拆掉中间格后仍共享或保留内部连边")
        assert(math.abs(tank.carried-50)<.000001 and math.abs(tank.leftAfterSplit-50)<.000001
            and math.abs(tank.rightAfterSplit-50)<.000001, "拆开三格组合罐丢料或造料")
        assert(math.abs(tank.restored-150)<.000001, "复装未恢复整组真实内容")
        assert(math.abs(tank.refilled-90)<.000001, "模板补给只保留首格或重复补给")
    end
end, "铁钢气液罐自然组合、断桥拆分、拆回份额与倒序恢复")
