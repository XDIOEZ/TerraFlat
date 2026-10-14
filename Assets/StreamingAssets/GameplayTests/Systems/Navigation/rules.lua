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
