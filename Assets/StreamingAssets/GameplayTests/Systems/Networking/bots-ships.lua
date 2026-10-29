test.check("world_ready", "飞船联机拒绝测试世界就绪")
test.check("player_ready", "飞船联机拒绝测试玩家就绪")
test.runtime("networking", "ship_streaming_boundary_prepare", {}, function(data)
    assert(data.prepared and data.boundaryX == 176, "没有通过正式传送准备跨块现场")
end, "准备区块边界窗口")
test.move_to(174.5, 48.5, 15, .2)
test.runtime("networking", "ship_streaming_boundary_sample", {stage = "before"}, function(data)
    assert(data.crossed and data.targetMatchesPlayerChunk and data.presentedMatchesTarget and data.playerInActive,
        "联机测试前真实跨块后，流送目标或云雾窗口没有跟随玩家")
end, "联机测试前跨块与流送窗口")
test.runtime("networking", "bots_ship_requests", {}, function(data)
    assert(data.transport == "mirror-in-memory", "没有经过正式Mirror传输与服务端handler")
    local required = {stale_realm=true, missing_template=true, refill_without_ship=true}
    assert(#data.samples == 3, "缺少生成或补给服务端拒绝采样")
    for _, reply in ipairs(data.samples) do
        assert(required[reply.kind], "出现重复或未知飞船拒绝采样")
        required[reply.kind] = nil
        assert(not reply.accepted and type(reply.reason) == "string" and #reply.reason > 0,
            reply.kind .. "未收到正式拒绝原因")
    end
    assert(next(required) == nil, "飞船拒绝种类不完整")
    assert(not data.launchAccepted and #data.launchReason > 0, "不存在的地面控制台仍能点火")
    assert(data.actorIdentityPreserved and data.independentActors and data.witnessReplies == 0,
        "飞船请求改写角色身份，或回执发送到另一连接")
    assert(data.shipCountUnchanged, "拒绝的飞船请求仍改写真实船体集合")
    assert(data.buildAccepted and data.createdBuildings > 0, "正式服务端没有生成真实地面船部件")
    assert(data.refillAccepted, "正式服务端没有完成模板船补给")
    assert(data.positiveLaunchAccepted and data.authoritativeShipCreated and data.flightIgnited,
        "正式点火没有生成权威船体和起飞状态")
    assert(data.groundOwnershipTransferred and data.identityAfterLaunch,
        "点火后仍残留地面重复部件，或替换了玩家身份")
    assert(not data.cleanup.serverActive and data.cleanup.connections == 0
        and data.cleanup.botRuntimeLeft == 0 and data.cleanup.profilesRestored,
        "飞船假人现场没有清理")
end, "固定船生成、补给、地面点火：服务端拒绝与连接身份隔离")
test.move_to(178, 48.5, 15, .2)
test.runtime("networking", "ship_streaming_boundary_sample", {stage = "after"}, function(data)
    assert(data.crossed and data.targetMatchesPlayerChunk and data.presentedMatchesTarget and data.playerInActive,
        "联机测试清理后真实跨块，流送目标或云雾窗口没有跟随玩家")
    assert(data.restored, "跨块测试没有通过正式地表传送恢复玩家原位")
end, "联机清理后跨块与流送窗口")
