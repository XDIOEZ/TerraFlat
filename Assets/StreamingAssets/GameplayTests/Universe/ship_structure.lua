-- 独立结构样本验证四邻支撑、真实伤害后的分裂和旋转占地，不代表已执行场景放置。
test.space("ship_structure", function(data)
    assert(data.operation == "ship_structure" and data.scope == "production-domain", "缺少正式结构领域结果")
    assert(type(data.inputPieces) == "number" and #data.connected == 1, "连续地板必须属于同一艘船")
    assert(#data.detached == 1 and data.unsupportedDestroyed == true, "无地板支撑设备未被回收")
    assert(data.detached[1].reason == "lost-floor-support", "脱离设备缺少真实支撑原因")
    assert(data.connected[1].pieces == data.inputPieces - #data.detached, "重建后的结构数量错误")
    assert(data.removeAccepted == true and data.bridgeDestroyed == true, "桥梁实际伤害没有移除结构")
    assert(#data.split == 2, "断开桥梁后必须形成两艘独立船体")
    local identities, retained, total = {}, 0, 0
    for _, ship in ipairs(data.split) do
        assert(type(ship.id) == "string" and not identities[ship.id], "分裂船体身份缺失或重复")
        identities[ship.id] = true
        if ship.id == data.originalId then retained = retained + 1 end
        assert(ship.wreck == false and ship.pieces == 2, "两侧控制台与地板未保持各自归属")
        total = total + ship.pieces
    end
    assert(retained == 1 and total == data.connected[1].pieces - 1, "原船身份或移除后的部件守恒失败")
    assert(#data.rotatedFootprint == 6, "旋转矩形地板占地数量错误")
    local cells = {}
    for _, cell in ipairs(data.rotatedFootprint) do
        assert(cell.x >= -2 and cell.x <= 0 and cell.y >= -3 and cell.y <= -2, "旋转后的占地边界错误")
        local key = tostring(cell.x) .. ":" .. tostring(cell.y)
        assert(not cells[key], "旋转占地出现重复格")
        cells[key] = true
    end
    assert(data.noFloorGroups == 0 and data.noFloorWreck == true and data.noFloorOrphanDestroyed == true,
        "失去全部地板后应回收孤立设备并结束船体归属")
    return "覆盖领域四邻支撑、无支撑设备、毁桥分裂、身份保留、全部失支撑与旋转占地。"
end, "船体结构支撑与断裂")
