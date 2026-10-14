-- 同进程假人走正式服务端收包、入世快照和玩家生命周期；协议现场不拍无意义画面。
test.check("world_ready", "联机假人测试世界就绪")
test.check("player_ready", "联机假人测试本地玩家就绪")

local function assert_cleanup(data)
    assert(data.scope == "live-isolated-world", "联机假人没有使用本轮隔离世界")
    local cleanup = data.cleanup
    assert(not cleanup.serverActive and not cleanup.clientActive
        and cleanup.connections == 0 and cleanup.spawned == 0, "测试服务器、连接或网络角色没有清理")
    assert(cleanup.localUnchanged and cleanup.sceneUnchanged, "假人测试替换了本地主角或切换了正式场景")
    assert(cleanup.transportRestored and cleanup.profilesRestored, "测试传输或玩家档案没有恢复")
    assert(cleanup.botRuntimeLeft == 0 and cleanup.botPassengersLeft == 0, "测试假人运行时或乘员残留")
end

test.runtime("networking", "bots_session", { count = 4 }, function(data)
    assert(data.transport == "mirror-in-memory" and data.requestedCount == 4 and #data.players == 4,
        "正式 Mirror 内存传输没有创建四名远端假人")
    local profiles, net_ids, guids = {}, {}, {}
    for _, player in ipairs(data.players) do
        assert(type(player.profile) == "string" and #player.profile > 0 and not profiles[player.profile],
            "同名加入请求没有分配独立档案")
        -- 正式 GUID 是有符号 int，合法身份要求非零且独立。
        assert(player.netId > 0 and not net_ids[player.netId] and player.guid ~= 0 and not guids[player.guid],
            "假人重复使用了网络身份或真实物品 GUID")
        profiles[player.profile], net_ids[player.netId], guids[player.guid] = true, true, true
        assert(player.profileBound and player.indexed and not player.isLocalProfile and not player.sameAsLocal,
            "远端假人的正式档案、角色索引或本地主角归属错误")
        assert(player.ready and player.authenticated, "未经正式就绪或认证的假人进入了游戏")
        assert(player.sameAfterReady, "重复 WorldReady 又生成了一份网络角色")
        assert(not player.spawnedBeforeReady, "完成 Join 后仍能用 Mirror AddPlayer 越过 WorldReady 提前出生")
        local snapshot = player.snapshot
        assert(snapshot.begins == 1 and snapshot.declaredChunks > 0
            and snapshot.receivedChunks == snapshot.declaredChunks and snapshot.uniqueIndices == snapshot.declaredChunks,
            "正式入世快照缺分片、重复分片或重复启动传输")
        assert(snapshot.declaredBytes > 0 and snapshot.receivedBytes == snapshot.declaredBytes
            and snapshot.actualHash == snapshot.declaredHash and snapshot.gzip,
            "正式收包后的压缩快照字节数、校验和或格式错误")
        assert(snapshot.seed == snapshot.expectedSeed
            and snapshot.generationProtocol == snapshot.expectedGenerationProtocol,
            "入世快照的真实种子或生成协议与服务器不一致")
    end
    assert(data.independentData and data.independentInventories, "多个假人共享了同一份实际玩家数据或库存容器")
    local collision = data.localNameCollision
    assert(collision.profileBound and collision.indexed and collision.ready and collision.authenticated
        and not collision.sameAsLocal and not collision.isLocalProfile,
        "请求本地档案名称的远端连接抢占了本地主角")
    assert(not profiles[collision.profile] and not net_ids[collision.netId] and not guids[collision.guid],
        "本地名称冲突连接复用了已有假人的档案或身份")
    local occupied = data.occupiedNameCollision
    assert(not data.occupiedCoreReused and occupied.profile ~= data.occupiedProfile
        and occupied.profileBound and occupied.indexed and not occupied.isLocalProfile,
        "远端连接复用了已占用短名档案的实际角色")
    assert(data.liveConnections == data.requestedCount + 2, "同名与档案冲突连接的实际数量错误")
    assert_cleanup(data)
end, "四假人正式加入：独立档案、快照分片、重复就绪与本地名冲突")

test.runtime("networking", "bots_rejection", {}, function(data)
    local required = { no_join = true, version = true, mod_api = true, mod_set = true, hello_only = true }
    local seen = {}
    assert(#data.samples == 5, "协议拒绝流程没有覆盖五种正式入口")
    for _, sample in ipairs(data.samples) do
        assert(required[sample.kind] and not seen[sample.kind], "协议拒绝样本重复或出现未知前提")
        seen[sample.kind] = true
        assert(not sample.spawned and sample.snapshots == 0, sample.kind .. "越过入世检查生成角色或发送了存档快照")
        if sample.kind == "version" or sample.kind == "mod_api" or sample.kind == "mod_set" then
            assert(not sample.connected and sample.rejections == 1
                and type(sample.reason) == "string" and #sample.reason > 0,
                sample.kind .. "没有通过正式回复说明拒绝原因并断开连接")
        end
    end
    for kind in pairs(required) do assert(seen[kind], "缺少协议拒绝样本：" .. kind) end
    assert_cleanup(data)
end, "未经加入、仅握手、脚本版本及 MOD 环境不符均不能入世")

test.runtime("networking", "bots_reconnect", {}, function(data)
    assert(#data.rounds == 3, "重连测试没有完成三轮真实连接")
    local profile, net_ids = nil, {}
    for _, round in ipairs(data.rounds) do
        assert(type(round.profile) == "string" and #round.profile > 0, "重连角色没有正式档案")
        if profile == nil then profile = round.profile end
        assert(round.profile == profile and round.sameSavedData, "同一档案重连丢失或复制了真实玩家数据")
        assert(round.netId > 0 and not net_ids[round.netId] and not round.oldSpawnPresent,
            "重连复用了旧网络角色或保留了旧出生对象")
        net_ids[round.netId] = true
        assert(round.connections == 1, "复用连接 ID 后服务端出现重复连接")
        assert(round.freshSequence.present and math.abs(round.freshSequence.x - 0.5) < 0.000001
            and round.freshSequence.y == 0, "新连接仍被上一轮输入序号挡住")
        assert(not round.runtimePresentAfterDisconnect and not round.inputAfterDisconnect.present,
            "断线后实际假人或旧输入没有移除")
        assert(round.savedAfterDisconnect, "正常断线删除了可供重连的玩家档案")
    end
    assert_cleanup(data)
end, "同档案三轮重连：连接 ID 复用、输入序号重置与断线清理")
