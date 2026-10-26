-- 在隔离世界检查真实手持磷肥的补给、扣料和拒绝，结束后恢复自有地块。
test.check("player_ready", "磷肥现场玩家就绪")
test.runtime("agriculture", "phosphate_fertilizer_use", {}, function(data)
    assert(data.held and not data.ordinaryAccepted and data.ordinaryAmount == 3,
        "真实磷肥未被玩家持有，或对准普通地面仍消耗材料")
    assert(not data.emptyAccepted and data.emptyAmount == 0 and math.abs(data.emptyFertility - .2) < .00001,
        "空数量磷肥仍补充了真实耕地肥力")
    assert(not data.distantAccepted and data.distantAmount == 3 and math.abs(data.distantFertility - .2) < .00001,
        "超距离磷肥使用改变了真实肥力或扣料")
    assert(not data.notHeldAccepted and data.notHeldAmount == 3, "未手持磷肥仍可提交补给")
    assert(data.accepted and data.amount == 2 and data.water == 4 and math.abs(data.fertility - .55) < .00001,
        "真实磷肥使用未恰好扣除一份、补充0.35肥力或错误增加水分")
    assert(data.nearlyFullAccepted and data.nearlyFullAmount == 1 and data.fullWater == 4
        and data.fullFertility == 1, "真实磷肥没有按肥力上限封顶并只扣一份")
    assert(not data.fullAccepted and data.fullAmount == 1, "满肥耕地仍消耗了真实磷肥")
    assert(data.restored, "磷肥现场没有恢复自有地块与手持物")
end, "真实磷肥手持使用：普通地、空数量、超距、未持有、补肥扣料与满肥拒绝")
