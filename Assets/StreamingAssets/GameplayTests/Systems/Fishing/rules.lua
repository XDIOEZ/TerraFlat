test.system("fishing", "rig", {}, function(data)
    assert(data.hookAccepted and data.foodAccepted, "合格鱼钩或鱼饵被正式钓具库存拒绝")
    assert(not data.foodInHookAccepted and not data.wrongBaitAccepted, "错误物品绕过钩/饵槽资格")
    assert(data.hookCapacity == 1 and data.baitCapacity == 1 and not data.unlimitedSlots and not data.unlimitedStack,
        "钓具没有限制两个单份槽位")
    assert(data.invalidSlots == "InvalidOperationException", "非法钓具槽数没有拒绝")
end, "钓具鱼钩/鱼饵资格与单份槽位")
