-- 通过正式消息核验服务器真实库存与世界余量，独立 Client 和 Host 仍由用户验收。
test.check("world_ready", "假人拾取测试世界就绪")
test.check("player_ready", "假人拾取测试本地玩家就绪")

test.runtime("networking", "bots_pickup", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.mode == "in-process-server-bots",
        "假人拾取没有通过隔离世界内的正式服务端连接")
    assert(data.inventoryReceipt == "server-authority-verified-client-side-unverified",
        "拾取夹具错误宣称独立客户端或 Host 已完成验收")
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
    local partial = data.partial
    assert(partial.plannedSourceAmount == 3 and partial.accepted and partial.consumed == 3
        and partial.added == 3 and partial.remaining == 7 and partial.inventoryDelta == 3,
        "只够三份的真实容量没有让十份来源只入包三份、保留七份")
    assert(partial.payloadDecoded and partial.payloadGuid == partial.playerGuid,
        "成功回执没有携带当前连接真实角色的可解码库存快照")
    for _, name in ipairs({ "afterPartial", "afterDuplicate" }) do
        local item = data[name]
        assert(item.exists and item.pickable and item.amount == 7,
            name .. "错误销毁、锁住或扣除了地面剩余七份")
    end
    assert(data.duplicateInventoryDelta == 3, "重发同一提交重复向服务器背包添加物品")
    assert(data.pendingAckInventoryDelta == 3,
        "收到 Client 库存应用确认前，过期全量库存覆盖了服务器已经收货的数量")
    assert(#data.invalidAmounts == 5, "零、NaN、Infinity、超额与整件小数回执边界未完整采样")
    for index, invalid in ipairs(data.invalidAmounts) do
        assert(not invalid.accepted and invalid.consumed == 0 and invalid.added == 0
            and invalid.inventoryDelta == 3 and invalid.world.exists and invalid.world.pickable
            and invalid.world.amount == 7, "非法数量案例 " .. index .. " 修改了真实来源或玩家库存")
    end
    local zero = data.zeroCapacity
    assert(not zero.accepted and zero.consumed == 0 and zero.added == 0
        and zero.inventoryDelta == 3 and zero.world.exists and zero.world.pickable and zero.world.amount == 7,
        "所有目标实际拒收时错误扣除了世界来源或增加库存")
    local complete = data.complete
    assert(complete.accepted and complete.consumed == 7 and complete.added == 7
        and complete.remaining == 0 and complete.inventoryDelta == 10 and not data.afterCommit.exists,
        "最后七份没有真实入包十份累计，或来源未在全部收货后回收")
    assert(data.timeout.exists and data.timeout.pickable and not data.timeout.accepted,
        "未提交租约超时后没有原量恢复可拾取状态")
    assert(data.disconnect.exists and data.disconnect.pickable,
        "连接断开后没有释放尚未消费的拾取来源")
    assert(data.itemCleaned, "自有测试物品仍在正式 GUID 索引中")
    local cleanup = data.cleanup
    assert(not cleanup.serverActive and not cleanup.clientActive
        and cleanup.connections == 0 and cleanup.spawned == 0, "拾取夹具遗留服务器、连接或出生对象")
    assert(cleanup.localUnchanged and cleanup.sceneUnchanged and cleanup.transportRestored and cleanup.profilesRestored,
        "拾取夹具没有恢复本地主角、场景、传输或存档档案")
    assert(cleanup.botRuntimeLeft == 0 and cleanup.botPassengersLeft == 0, "拾取夹具遗留假人或太空乘员")
end, "真实拾取事务：三份入包余七、重复幂等、旧库存保护、非法拒收与租约收尾")
