-- 两个独立 Mirror 连接的可见角色实际移动、挥刀、拾取并丢出物品。
test.check("world_ready", "假人行动世界就绪")
test.check("player_ready", "本地主角就绪")

test.runtime("networking", "bots_actions", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.transport == "mirror-in-memory",
        "假人行动没有走隔离世界的 Mirror 连接")
    assert(data.botA.netId > 0 and data.botB.netId > 0 and data.botA.netId ~= data.botB.netId,
        "没有生成两个独立的联网角色")
    local movement = data.movement
    assert(movement.coreInitialized and #movement.commands == 6,
        "假人没有通过正式移动命令执行完整动作")
    assert(movement.toX - movement.fromX > 1.5,
        "Mirror 服务端没有接受假人的真实位移")
    assert(math.abs(movement.bodyX - movement.toX) < 0.5 and
        math.abs(movement.bodyY - movement.authoritativeY) < 0.5,
        "服务端角色实体没有跟随权威移动位置")
    local attack = data.attack
    assert(attack.weapon == "Knife_Flint" and attack.started and attack.damageWindowInitiallyClosed
        and attack.damageWindowOpened
        and attack.damageWindowClosed, "联网假人没有真正举刀攻击并关闭伤害窗口")
    local pickup = data.pickup
    assert(pickup.worldItemBefore and pickup.responseGranted and pickup.receivedItem == "Ore_Copper"
        and pickup.inventoryReceived and pickup.worldItemRemoved,
        "假人没有完成真实拾取租约、测试背包收货和服务端删除")
    assert(not pickup.realClientInventoryVerified,
        "仅有服务端内存假人，不能冒称独立客户端背包已验收")
    local drop = data.drop
    assert(drop.slotCleared and drop.worldItemExists and drop.itemId == "Ore_Copper" and drop.nearBot,
        "假人未从背包扣除物品并在角色身边生成真实掉落物")
    assert(data.localUnmoved, "假人操作意外推动了本地主角")
end, "联网假人真实动作：移动、挥刀、拾取、丢弃")

test.screenshot("联机假人完成移动攻击及拾取丢弃后的真实现场")

test.runtime("networking", "bots_actions_cleanup", {}, function(data)
    local cleanup = data.cleanup
    assert(not cleanup.serverActive and not cleanup.clientActive and cleanup.connections == 0
        and cleanup.spawned == 0, "假人动作后仍残留 Mirror 服务器或网络对象")
    assert(cleanup.localUnchanged and cleanup.sceneUnchanged and cleanup.transportRestored
        and cleanup.profilesRestored, "假人动作错误修改了真实玩家、场景或存档")
    assert(cleanup.botRuntimeLeft == 0 and cleanup.botPassengersLeft == 0,
        "假人动作结束后残留网络玩家或太空乘员")
end, "真实动作现场结束后清理两假人及所有测试物品")
