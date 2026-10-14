test.system("building", "support_layer", {}, function(data)
    assert(data.surfaceGround == 90 and data.surfaceCost == 2 and data.surfaceWalkable,
        "覆盖支撑没有提供新的有效地面和通行成本")
    assert(data.baseGround == 17 and not data.baseWalkable, "覆盖支撑改写了真实底层地形")
    assert(math.abs(data.wetDepth - 0.6) < 0.00001, "铺支撑覆盖时消除了底下的真实液体")
    assert(data.coveredSurfaceDepth == 0, "有效支撑没有遮断表面浸水")
    assert(data.clearedGround == 17 and data.clearedCost == 25 and not data.clearedWalkable and data.supportId == 0,
        "撤支撑后没有恢复底层有效表面")
    assert(math.abs(data.clearedDepth - data.wetDepth) < 0.00001, "撤支撑破坏了独立液体层")
end, "地表支撑覆盖、底层保留与拆除恢复")

test.system("building", "shared_state", {}, function(data)
    assert(data.copiedFirst == 1 and data.sourceFirstAfterCopyMutation == 1, "目标模块修改污染了载体源状态")
    assert(data.distinctModule and data.distinctPayload, "共享建筑模块或载荷没有深拷贝")
    assert(data.copiedName == "placed-state" and data.copiedId == "system-probe:shared-module" and not data.copiedEnabled,
        "载体状态覆盖了建筑本体的模块元信息")
    assert(data.sourceName == "carrier-state" and data.sourceEnabled, "建筑状态转移修改了原始载体")
    assert(data.duplicate == "InvalidOperationException" and data.missing == "InvalidOperationException"
        and data.stacked == "InvalidOperationException", "重复/缺失共享模块或堆叠载体未拒绝")
end, "载体到建筑模块状态转移与深拷贝隔离")
