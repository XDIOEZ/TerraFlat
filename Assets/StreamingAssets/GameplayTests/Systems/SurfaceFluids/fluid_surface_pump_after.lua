test.check("world_ready")
test.runtime("industrial_fluids", "visible_surface_pump", {after=true}, function(d)
    assert(d.unpoweredLevel == d.initialLevel and d.initialLevel == 200,
        "独立抽后案例的空电池前置消耗了真实地表水")
    assert(d.remainingLevel > 0 and d.remainingLevel <= 20 and d.wetCells > 0,
        "双泵未抽到约1/10残水，或错误强制抽干整个水域")
    assert(d.poweredFirst and d.poweredSecond and d.firstEnergySpent > 0 and d.secondEnergySpent > 0,
        "两台泵未通过实际电网Tick获得供电并消耗真实电池储能")
    assert(d.tankLiters[1] > 0 and d.tankLiters[2] > 0 and d.sharedWaterSearch,
        "两台泵没有从共享水域向各自真实储罐供液")
    local total = d.remainingLevel * .25 + d.pumpLiters + d.tankLiters[1] + d.tankLiters[2]
    assert(math.abs(total - d.initialLiters) < .00001, "地表残水、泵尾量和两只储罐的真实液量不守恒")
    assert(d.pumpCount == 2 and d.pipeCount == 4 and d.tankCount == 2 and d.wireCount == 8,
        "抽后现场缺少正式双电泵、液管、储罐或铜线")
    assert(d.inViewport and d.visibleMachineBridges == d.machineCount and d.machineCount == 18,
        "抽后水面和实际设备表现没有完整进入结束截图镜头")
end, "抽后可见现场：真实供电双泵抽到保留底水、两罐进水和总量守恒")
