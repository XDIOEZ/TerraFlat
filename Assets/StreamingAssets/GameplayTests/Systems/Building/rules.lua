test.system("building", "placement_feedback", {}, function(data)
    assert(#data.details == 2 and data.details[1] == "召唤器数量不足" and data.details[2] == "地块支撑能力不足",
        "建造拒绝原因被统一位置错误覆盖")
    assert(data.semanticCount == 2 and data.actorPreserved, "详细原因通道破坏既有音效语义或玩家归属")
end, "建造数量和支撑拒绝原因原样发布且保留语义事件")

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
test.system("building", "placement_distance", {}, function(data)
    assert(data.survival == 7.5 and data.fallback == 3.25, "生存放置半径未减半，或忽略玩家实际交互范围")
    assert(data.creativeUnlimited, "减半半径错误限制了创造放置")
end, "建筑距离：生存跟随实际交互半径、无玩家回退与创造无限")
