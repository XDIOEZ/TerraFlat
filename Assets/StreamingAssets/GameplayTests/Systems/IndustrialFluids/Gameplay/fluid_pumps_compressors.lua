test.check("world_ready")
test.runtime("industrial_fluids", "combined_tank_pump_tick", {}, function(d)
    assert(d.powered and d.tankCount == 12, "双电泵或12格组合液罐现场未就绪")
    assert(d.stableGraph and d.stableBudget, "组合罐首次进水在Tick中重建拓扑或清空本轮预算")
    assert(d.notifications == d.tankCount, "同一步双泵进液重复分摊或发布组合罐快照")
    assert(d.rebuiltBetweenSteps, "液体身份变化没有在计算步之间更新流体拓扑")
    assert(math.abs(d.firstDelivered - d.expectedFirstDelivered) < .000001,
        "首次进液后第二台泵漏算、重复计算或丢失流量预算")
    assert(d.finalDelivered > d.firstDelivered and math.abs(d.finalDelivered + d.buffers - d.initialLiters) < .000001,
        "下一步未恢复抽液或泵和组合罐总量不守恒")
end, "双泵接12格组合罐：完整Tick拓扑稳定、通知合并、连续输送与守恒")
test.runtime("industrial_fluids", "pump", {}, function(d)
    assert(d.unpowered==0 and d.unpoweredSourceStable, "无动力气泵抽了环境")
    assert(math.abs(d.full-d.capacity)<.000001 and d.fullSourceStable, "普通气泵未充满或满后仍抽")
    assert(math.abs(d.sourceTaken-d.full)<.000001, "气泵未扣真实气源")
    assert(math.abs(d.oxygen/d.nitrogen-21/79)<.000001, "气泵把空气改成单气体")
end, "动力气泵充满固定容量、混气与守恒")
test.runtime("industrial_fluids", "surface_pump", {}, function(d)
    assert(d.unpoweredLevel==10, "无电水泵扣了地表水")
    assert(math.abs(d.targetLiters-d.rate*d.seconds*10)<.000001, "默认水泵整份尾量导致速率降低")
    assert(math.abs((d.initialLevel-d.level)*d.servingLiters-d.sourceLiters-d.targetLiters)<.000001, "地表水与泵尾量不守恒")
    assert(d.sourceLiters<d.servingLiters+.000001 and d.dirtyWaterMoles>0 and d.pureWaterMoles==0, "水泵超过一份余量或净化了原水")
end, "真实地表淡水抽取、每秒速率与设备尾量")
test.runtime("industrial_fluids", "connected_surface_pump", {}, function(d)
    assert(d.unpoweredLevel == d.initialLevel, "无电水泵消耗了连通水域")
    assert(d.remainingLevel <= 2 and d.inletLevel == 0, "空泵口未通过邻格扩散持续得到补水")
    assert(d.bodyFound and d.bodyCells == 3 and d.bodyLevel == d.remainingLevel, "真实连通统计与剩余水量不一致")
    assert(math.abs(d.pumpLiters + d.tankLiters + d.remainingLevel * .25 - 3) < .000001 and d.tankLiters > 0,
        "连通地表水与泵、储罐库存不守恒")
end, "空入口通过共享水域取余水、整数残水及泵罐守恒")
test.runtime("industrial_fluids", "shared_surface_pump_search", {}, function(d)
    assert(d.first == "Surplus" and d.second == "Surplus" and d.sameSource and d.shared,
        "同一水域的两处泵口没有复用候选格与连通缓存")
    assert(d.taken and d.sourceAfter == 1 and d.rejectedStale,
        "余水模式没有保留一级，或第二台泵使用旧查询重复扣水")
    assert(d.final == "LocalRemainder" and d.localTaken and d.inletAfter == 0 and d.middleAfter == 1,
        "确认余水耗尽后没有切换入口残水模式，或抽干了其它残水格")
    assert(d.refunded and d.reactivated == "Surplus" and d.remaining == 3,
        "退回真实来源后没有恢复候选资格，或两阶段抽液不守恒")
end, "共享候选、扣量时复验、余水保底、末格切换与来源退回")

test.runtime("industrial_fluids", "visible_surface_pump", {after=false}, function(d)
    assert(d.waterCells == 20 and d.wetCells == 20 and d.initialLevel == 200 and d.remainingLevel == 200,
        "抽前现场不是完整的5×4真实水池")
    assert(d.unpoweredLevel == d.initialLevel and d.pumpLiters == 0 and d.tankLiters[1] == 0 and d.tankLiters[2] == 0,
        "空电池状态下真实Tick消耗水或产生了储罐库存")
    assert(d.pumpCount == 2 and d.pipeCount == 4 and d.tankCount == 2 and d.wireCount == 8,
        "抽前现场缺少双电泵、正式液管、储罐或铜线")
    assert(d.sharedWaterSearch, "两台正式电泵未共享同一水域的寻源缓存")
    assert(d.inViewport and d.visibleMachineBridges == d.machineCount and d.machineCount == 18,
        "真实水池或正式设备表现没有完整进入镜头")
end, "抽前可见现场：5×4水池、共享水域双电泵和真实管罐电路")
