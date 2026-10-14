-- 真实服务端拾取租约不等于客户端背包接收；这里只断言正式收包和权威掉落物状态。
test.check("world_ready", "假人拾取测试世界就绪")
test.check("player_ready", "假人拾取测试本地玩家就绪")

test.runtime("networking", "bots_pickup", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.mode == "in-process-server-bots",
        "假人拾取没有通过隔离世界内的正式服务端连接")
    assert(data.inventoryReceipt == "client-side-unverified", "拾取夹具错误宣称真实客户端库存已入账")
    assert(not data.sameCorePlayer and data.botA.netId > 0 and data.botB.netId > 0
        and data.botA.netId ~= data.botB.netId and data.botA.profileName ~= data.botB.profileName,
        "两个拾取连接没有绑定独立的真实角色")
    assert(data.botA.profileName == data.botA.coreProfileName and data.botB.profileName == data.botB.coreProfileName,
        "拾取连接的网络名称与真实玩家档案不一致")
    local initial = data.initial
    assert(data.itemGuid ~= 0 and initial.exists and initial.loaded and initial.pickable and initial.amount > 0,
        "拾取前提没有建立真实可拾取的自有掉落物")
    local first = data.aGrant
    assert(first.responseReceived and first.granted and first.token > 0,
        "A 没有收到正式服务端授予的拾取租约")
    assert(first.exists and not first.pickable and first.amount == initial.amount,
        "授予租约提前销毁、扣量，或没有锁住实际掉落物")
    assert(first.payloadDecoded and first.payloadGuid == data.itemGuid and first.payloadAmount == initial.amount,
        "授予负载不能通过正式协议解码，或身份数量与真实来源不一致")
    assert(data.bDenied.responseReceived and not data.bDenied.granted, "B 同时获得了 A 独占的掉落物")
    for _, name in ipairs({ "afterWrongOwner", "afterWrongToken" }) do
        local item = data[name]
        assert(item.exists and not item.pickable and item.amount == initial.amount,
            name .. "越权提交释放、销毁或修改了 A 的真实预留物")
    end
    assert(data.afterReject.exists and data.afterReject.pickable and data.afterReject.amount == initial.amount,
        "A 拒收后没有原量恢复实际物品的可拾取状态")
    local again = data.aRegrant
    assert(again.responseReceived and again.granted and again.token > 0 and again.token ~= first.token,
        "重新申请复用了旧令牌，或没有获得新的正式租约")
    assert(again.exists and not again.pickable and again.amount == initial.amount
        and again.payloadDecoded and again.payloadGuid == data.itemGuid and again.payloadAmount == initial.amount,
        "重新授予丢失来源身份、数量、真实锁定或协议负载")
    assert(not data.afterCommit.exists, "合法 owner 和 token 提交后服务端仍保留自有掉落物")
    assert(data.itemCleaned, "自有测试物品仍在正式 GUID 索引中")
    local cleanup = data.cleanup
    assert(not cleanup.serverActive and not cleanup.clientActive
        and cleanup.connections == 0 and cleanup.spawned == 0, "拾取夹具遗留服务器、连接或出生对象")
    assert(cleanup.localUnchanged and cleanup.sceneUnchanged and cleanup.transportRestored and cleanup.profilesRestored,
        "拾取夹具没有恢复本地主角、场景、传输或存档档案")
    assert(cleanup.botRuntimeLeft == 0 and cleanup.botPassengersLeft == 0, "拾取夹具遗留假人或太空乘员")
end, "两假人拾取独占：越权拒绝、拒收恢复、新令牌与合法提交")
