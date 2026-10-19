for _, case in ipairs({
    {label = "高代价格绕行", args = {penalty = 1000}, found = true, costly = false, cost = 28},
    {label = "低代价格直达", args = {penalty = 1}, found = true, costly = true, cost = 20},
    {label = "障碍格绕行且不切角", args = {blocked = true}, found = true, costly = false, cost = 40, center = false},
    {label = "零权重为阻挡而非免费地形", args = {penalty = 0}, found = true, costly = false, cost = 40, center = false},
    {label = "搜索预算不足", args = {budget = 1}, found = false}
}) do
    test.system("navigation", "weighted_path", case.args, function(data)
        assert(data.found == case.found, "路径搜索结果不正确")
        if case.found then
            assert(data.valid and data.endsAtTarget, "路径包含不可走边或没有到达目标")
            assert(data.includesCostlyCell == case.costly and data.totalCost == case.cost, "导航没有按正式代价择路")
            if case.center ~= nil then assert(data.centerWalkable == case.center, "零权重或障碍格被误当可走") end
        else
            assert(data.length == 0, "未到达目标却返回了成功路径")
        end
    end, "导航路径：" .. case.label)
end
test.system("navigation", "blockers", {}, function(data)
    assert(data.initiallyWalkable and not data.bothWalkable and not data.oneWalkable, "多个阻挡来源没有分别保留")
    assert(data.releasedWalkable and data.oldFootprintReleased and data.newFootprintBlocked, "撤销或移动阻挡后格子状态不正确")
end, "导航动态占地登记、移动与撤销")
test.system("navigation", "grid_changes", {}, function(data)
    assert(data.duringRevision == data.initialRevision and data.afterRevision == data.initialRevision + 1, "批量更新没有合并版本通知")
    assert(data.beforeRepeat == data.afterRepeat, "相同格数据重复更新导致无效版本变化")
    assert(data.removed == 2 and data.remaining == 0 and data.clearInvalidatesAll, "区域删除或清理失效通知不正确")
end, "导航网格批量更新与失效通知")
test.system("navigation", "corner", {}, function(data)
    assert(data.open and data.openCost == 14, "无阻挡对角边应允许通行")
    assert(not data.closed and not data.otherClosed, "对角移动穿过阻挡角落")
    assert(not data.farTraversal and not data.sameTraversal, "非法相邻移动被接受")
end, "导航斜角防穿与相邻边保护")

test.system("navigation", "canonical_batch", {}, function(data)
    assert(data.cells and data.notifications and data.revisions,
        "规范坐标批量写入改变了地格、变更通知或路径失效版本")
    assert(data.walkability and data.liquidCost,
        "规范坐标导航丢失建筑阻挡、水体成本或水深")
    assert(data.wrapped and data.unwrapped,
        "批量拓扑归一化改变了环绕接缝或无限世界坐标")
end, "区块导航批量坐标归一化与旧接口严格等价")

test.system("navigation", "liquid_cell_snapshot", {}, function(data)
    assert(data.wet and data.covered and data.dry and data.blockedWet,
        "导航共用单格水位后改变了真实水面、平台遮水或不可走格水深")
    assert(data.uncovered and data.drained,
        "拆除平台或抽干水体后，导航使用了过期水位/地形代价")
    assert(data.iterations == 65536 and data.sameOutputs,
        "导航批量读取与原水位/水深/代价接口结果不一致")
end, "导航格水位单次采样与液体权威结果等价")

test.system("navigation", "chunk_block_pool", {}, function(data)
    assert(data.originalWater and data.emptyAfterUnload and data.reusedClean and data.clearReset,
        "导航块卸载复用后遗留旧的地形、水深、代价或世界格")
    assert(data.initiallyBlocked and data.blockerPreserved and data.blockerReleased,
        "导航块回收影响动态阻挡源的登记与解除")
    assert(data.rounds == 64 and data.createdAtWarmup == 1 and data.createdAfterRounds == 1,
        "预热后64轮不同区块装卸仍构造新的导航块数组")
    assert(data.poolBounded and data.worldResetReleasesPool and data.resetRequiresFreshBlock,
        "导航缓存超过上限，或切换世界后没有释放旧数组")
end, "导航区块复用零分配、完整清理及动态阻挡隔离")

test.system("navigation", "machine_obstacle_dirty", {}, function(data)
    assert(data.unchanged and data.removedOnlyOne and data.movedNotRemoved,
        "机器碰撞增量索引误删未改变的设备，或迁移锚点后仍删除已移动设备")
    assert(data.neighborRemoved and data.allRemoved,
        "单格变化误清邻近机器，或机器拆除后遗留旧碰撞")
end, "机器单格碰撞重建只撤销真正失效的锚点")

test.system("navigation", "avoidance_topology", {}, function(data)
    local function bucket(actual, x, y)
        assert(#actual == 2 and actual[1] == x and actual[2] == y,
            "局部避让桶的规范坐标或负数向下取整错误")
    end
    bucket(data.unwrappedNegative, -2, -1)
    bucket(data.wrappedSeam, 7, 0)
    bucket(data.equivalentImage, 7, 0)
    bucket(data.wrappedCrossing, -8, 0)
    bucket(data.unwrappedCrossing, 8, 0)
    bucket(data.nonSquareWrapped, -6, -3)
    assert(#data.neighbours == 9, "局部避让漏掉九宫格候选桶")
    for index, actual in ipairs(data.neighbours) do
        local x = ({6, 7, -8})[((index - 1) % 3) + 1]
        local y = math.floor((index - 1) / 3) - 1
        bucket(actual, x, y)
    end
    assert(#data.seamDelta == 2 and data.seamDelta[1] == 1 and data.seamDelta[2] == 0,
        "环绕接缝的相邻 AI 距离没有采用最短路径")
end, "局部避让冻结拓扑后保留无限世界、环绕及九宫格桶边界")

test.system("navigation", "topology_bounds_cache", {}, function(data)
    assert(data.initial and data.reused and data.radiusRefresh and data.chunkRefresh,
        "世界边界缓存没有复用稳定配置，或修改半径/区块尺寸后继续使用旧结果")
    assert(data.invalidCached and data.recovered and data.infinite and data.wrapped,
        "非法区块尺寸、无限世界或重新启用环绕时缓存未正确失效")
    assert(data.differentPlanet and data.missingPlanet and data.returned and data.reset,
        "切换星球、清空存档或恢复世界后沿用了错误的拓扑缓存")
end, "活动星球拓扑边界缓存复用与动态配置失效")
