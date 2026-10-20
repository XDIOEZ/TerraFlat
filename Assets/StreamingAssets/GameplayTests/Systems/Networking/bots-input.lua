-- 通过两条真实服务端连接检查输入与意图序号，采样实际乘员状态而非收包成功标记。
test.check("world_ready", "假人输入测试世界就绪")
test.check("player_ready", "假人输入测试本地玩家就绪")

local function near(a, b)
    return a == a and b == b and math.abs(a - b) < 0.000001
end

local function assert_input(sample, x, y, label)
    assert(sample.present and near(sample.x, x) and near(sample.y, y)
        and sample.turn == 0 and sample.thrust == 0, label .. "实际权威输入错误")
end

test.runtime("networking", "bots_input", {}, function(data)
    assert(data.scope == "live-isolated-world", "假人输入没有使用本轮隔离世界")
    assert_input(data.firstA, 0.6, 0.8, "A 首个合法输入")
    assert(not data.beforeB.present, "A 输入写进了尚未发送输入的 B 档案")
    for _, name in ipairs({ "duplicateA", "staleA", "zeroA", "nanA", "oversizedA" }) do
        assert_input(data[name], data.firstA.x, data.firstA.y, "拒绝 " .. name)
    end
    assert_input(data.updatedA, -0.5, 0, "拒绝非法消息后的合法新序号")
    assert_input(data.notReadyA, -0.5, 0, "未就绪连接不能修改输入")
    assert_input(data.readyA, 0.25, 0, "恢复就绪后同一序号仍可提交")
    assert_input(data.independentB, -0.75, 0, "B 独立连接从序号 1 开始")
    assert_input(data.aAfterB, 0.25, 0, "B 输入不能覆盖 A")
    for _, name in ipairs({ "intentA", "intentB" }) do
        local replies = data[name]
        assert(#replies == 1, name .. "重复意图产生重复回复，或另一连接的独立序号被吞掉")
        local reply = replies[1]
        assert(reply.sequence == 1 and not reply.accepted and type(reply.reason) == "string" and #reply.reason > 0,
            name .. "没有用真实回复拒绝非法或不存在的操作目标")
    end
    assert_input(data.timeoutA, 0, 0, "输入失联后正式 Update 清零")
    assert(not data.disconnectedA.present and not data.oldSpawnPresent and not data.oldRuntimePresent,
        "A 断线后旧输入、网络出生对象或实际角色仍存在")
    assert_input(data.bAfterDisconnect, -0.5, 0, "A 断线不清除 B 的当前输入")
    assert(data.savedProfileRetained, "A 正常断线错误删除了可重连档案")
    local cleanup = data.cleanup
    assert(not cleanup.serverActive and not cleanup.clientActive
        and cleanup.connections == 0 and cleanup.spawned == 0, "输入夹具遗留服务器、连接或出生对象")
    assert(cleanup.localUnchanged and cleanup.sceneUnchanged and cleanup.transportRestored and cleanup.profilesRestored,
        "输入夹具没有恢复本地主角、场景、传输或存档档案")
    assert(cleanup.botRuntimeLeft == 0 and cleanup.botPassengersLeft == 0, "输入夹具遗留假人或太空乘员")
end, "两假人正式输入：乱序、非法值、就绪、独立意图、超时和断线")

test.runtime("networking", "bots_teleport", {}, function(data)
    local function near(a, b)
        return a == a and b == b and math.abs(a - b) < 0.001
    end
    local function assert_position(sample, x, y, label)
        assert(near(sample.x, x) and near(sample.y, y), label .. "服务器位置错误")
        assert(near(sample.bodyX, x) and near(sample.bodyY, y), label .. "角色本体没有同步落点")
        assert(near(sample.savedX, x) and near(sample.savedY, y), label .. "角色数据仍是旧位置")
        assert(near(sample.observerX, x) and near(sample.observerY, y), label .. "区块观察者仍是旧位置")
    end
    assert(data.scope == "live-isolated-world" and data.transport == "mirror-in-memory", "传送未经过正式联机夹具")
    assert(data.ordinaryStep < 2, "普通移动错误绕过了服务器单步位移限制")
    assert_position(data.teleported, data.targetX, data.targetY, "GM 传送")
    assert(data.teleported.revision > data.start.revision and data.teleported.velocity == 0,
        "传送没有刷新地表归属或清零速度")
    assert_position(data.afterRejected, data.targetX, data.targetY, "重复、非法或其他连接的传送请求")
    assert(data.afterRejected.revision == data.teleported.revision, "拒绝的请求重复执行了传送")
    assert(near(data.witnessBefore.x, data.witnessAfter.x) and near(data.witnessBefore.y, data.witnessAfter.y),
        "传送影响了另一名玩家")
    assert_position(data.afterMove, data.nextX, data.nextY, "传送后的普通移动")
    local cleanup = data.cleanup
    assert(not cleanup.serverActive and not cleanup.clientActive and cleanup.connections == 0 and cleanup.spawned == 0,
        "传送夹具遗留联机对象")
    assert(cleanup.localUnchanged and cleanup.sceneUnchanged and cleanup.transportRestored and cleanup.profilesRestored,
        "传送夹具没有恢复原现场")
end, "GM 地表传送：正式请求、拒绝、独立玩家和新落点续走")
