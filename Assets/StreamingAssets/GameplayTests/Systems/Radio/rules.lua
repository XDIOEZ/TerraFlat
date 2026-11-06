test.system("radio", "propagation", {}, function(data)
    assert(data.validZero, "有效零信号与失联没有区分")
    assert(data.merged and data.removedHigh and data.rising, "多源、撤源或上升沿错误")
    assert(not data.oldFrameOverwrote and not data.wrongWorldOverwrote, "旧帧或其他世界覆盖了新值")
    assert(data.textCount == 1, "文字范围过滤或发送次数错误")
    assert(not data.expiredHasSource and not data.wrongChannelHasSource, "失联未过期或频道串扰")
    assert(data.wrapped, "环绕距离无线接收错误")
end, "无线有效零、OR、序号、范围、失效与世界隔离")

test.system("radio", "battery_inventory", {}, function(data)
    assert(data.full == 3600 and data.inserted and data.halfConsumed, "新电池或实际插槽装填失败")
    assert(data.halfAfterRestore == 1800 and data.removed and data.halfAfterRemoval == 1800,
        "半电电池保存或拆出后电量改变")
    assert(data.reinserted and data.restConsumed and data.overdrawRejected, "重装、耗尽或超量扣能错误")
    assert(data.emptyRetained and data.emptyAfterRestore == 0 and data.wrongRejected, "空电池或插槽接收规则错误")
end, "真实电池槽、半电/空电保存与拒绝")

test.system("radio", "scope_recreation", {}, function(data)
    assert(data.epochsDifferent and not data.oldAccepted, "重建世界后接受了旧作用域的信号")
    assert(data.hasSource and not data.value, "旧作用域信号改变了新设备或心跳")
end, "同世界重建后的无线作用域代际")

test.system("radio", "broadcast_definition", {}, function(data)
    assert(data.positive and data.negative and not data.zero and not data.unknown,
        "电台广播拒绝了有效 GUID 或接受了无效定义")
end, "电台广播定义与真实 GUID")

test.system("radio", "network_ownership", {}, function(data)
    assert(data.bagChannel == 3 and data.hotbarChannel == 3 and data.equippedChannel == 3,
        "客户端快照覆盖了服务器频道")
    assert(data.bagEnergy == 1800 and data.hotbarEnergy == 1800 and data.equippedEnergy == 1800,
        "客户端快照覆盖了服务器电量")
    assert(data.clientChannel == 3 and data.clientEnergy == 1800, "服务端电量没有回传到客户端")
    assert(data.otherChannel == 8, "影响了不同身份的物品")
    assert(data.first and not data.duplicate and not data.stale and data.next, "无线请求去重或顺序错误")
    assert(data.equippedRuntime and data.removedRuntime, "服务端装备状态未绑定或卸载对讲机")
end, "服务端电池状态合并和无线请求去重")
