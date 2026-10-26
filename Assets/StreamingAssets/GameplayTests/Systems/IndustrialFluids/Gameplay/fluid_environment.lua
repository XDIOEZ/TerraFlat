test.check("world_ready")
test.fluid("surface_liquid_transactions", "共用正式地表抽排拒绝与回滚")
test.runtime("industrial_fluids", "surface_outlet", {}, function(d)
    assert(d.stoppedLevel==0, "液体出口零速率仍排放")
    assert(d.partialLevel == 0 and d.partialSource == d.initialLiters, "出口不足十升时提前扣料或增加地表水")
    assert(d.emittedLevel == 1 and d.litersPerUnit == 10 and math.abs(d.sourceAfter - 15) < .000001,
        "液体出口没有一次排十升到正式地表水层")
    assert(math.abs(d.sourceAfter+d.emittedLevel*10-d.initialLiters)<.000001, "液体输出破坏物质量")
    assert(d.fullSourceBefore==d.fullSourceAfter, "满水格拒绝时丢掉来源液体")
    assert(d.singleFreeInitialLevel == 99 and d.singleFreeBudgetLiters == 20 and d.singleFreeFinalLevel == 100,
        "出口有二十升预算时没有填满仅余十升容量的末格")
    assert(math.abs(d.singleFreeSourceBefore - d.singleFreeSourceAfter - 10) < .000001,
        "末格只接收十升时出口扣了整批二十升，或未接收余量没有保留在来源")
end, "液体出口正式水层、满格保留来源")

test.runtime("containers", "whole_pour", {}, function(d)
    assert(d.blockedRemoved == 0 and d.blocked.amount == 101 and d.blocked.level == 100,
        "满水格拒收没有回滚容器")
    assert(d.fractionRemoved == 0 and d.fraction.amount == 101 and d.fraction.level == 0,
        "不足十升的倾倒请求改变了容器或地表")
    assert(d.firstRemoved == 40 and d.first.water == 60.5 and d.first.salt == .5 and d.first.level == 1,
        "倒十升没有准确消耗四十饮用份并增加一份地表水")
    assert(d.secondRemoved == 40 and d.second.water == 20.5 and d.second.salt == .5 and d.second.level == 2,
        "多份请求没有按整十升提交或丢失混液尾量")
    assert(d.tailRemoved == 0 and d.tail.amount == 21 and d.tail.level == 2,
        "不足十升尾量被倒入地表或丢弃")
    assert(d.halfSpaceFilled == 0 and d.halfSpace.amount == d.capacity - .5 and d.halfSpace.level == 2,
        "桶内不足十升空位仍抽取了地表")
    assert(d.wholeSpaceFilled == 40 and d.wholeSpace.amount == d.capacity - 20 and d.wholeSpace.level == 1,
        "桶内十五升空位没有只取十升并保留五升空位")
    assert(d.restoredLevel == d.originalLevel and d.restoredId == d.originalId,
        "真实倒液夹具未归还原地表液体")
end, "容器与地表十升整份、满格回滚、混液尾量和真实体积守恒")
