test.check("world_ready")
test.runtime("industrial_fluids", "routes", {}, function(d)
    assert(d.oxygenReachable and not d.nitrogenReachable, "过滤管未按组分限制")
    assert(not d.closedReachable and not d.reverseReachable, "阀门或方向被搜索绕过")
    assert(not d.liquidReachable and not d.afterBreakReachable, "跨相态或断管仍可达")
    assert(d.pipeChambers==0 and d.beforeRemoval==d.afterRemoval, "管道存物或拆管改量")
    assert(d.infinite and math.abs(d.infiniteSample - 100) < .000001
        and math.abs(d.infiniteSampleAfter - 100) < .000001, "创造气源初始化或供气改变了有限样本")
    assert(#d.infiniteDirections == 4, "无限气源未覆盖四向端口")
    for _, reachable in ipairs(d.infiniteDirections) do assert(reachable, "无限气源某一方向不能通过正式管网供气") end
    assert(d.infiniteBudget > 0 and d.infiniteAvailable >= d.infiniteBudget - .000001,
        "无限气源未在冻结的管网预算中提供气体")
    assert(math.abs(d.infiniteDelivered * 3 - d.infiniteBudget) < .000001 and
        math.abs(d.infiniteRouteCharge - d.infiniteBudget) < .000001 and d.infiniteRemaining < .000001,
        "过滤后的无限气源没有按原混气和共用管路吞吐计费")
    assert(d.infiniteFilterRejectsHydrogen and d.infiniteValveClosed, "无限气源绕过过滤或关闭阀门")
end, "无库存气液分网、阀门、方向与过滤")
