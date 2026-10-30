-- 正式端点解析和网络物品字节协议无需创建 Host/Client。
test.system("networking", "space_snapshot_delta", {}, function(data)
    local shared, motion, changes = data.shared, data.motion, data.changes
    assert(shared.firstFull and shared.secondFull and shared.capturedFirst > 0 and
        shared.capturedFirst == shared.capturedSecond and shared.encodedFirst == shared.encodedSecond,
        "两个观察者没有独立完整首包，或同轮重复采集/编码了共享实体")
    assert(shared.compressedFirst > 0 and shared.firstWorlds == 1 and shared.secondWorlds == 1 and
        shared.firstWorldPayload and shared.secondWorldPayload,
        "大字段未压缩，或第二个观察者缺少独立船内世界首包")
    assert(motion.deltaBytes < shared.firstBytes and motion.encodedFields > 0 and motion.compressedFields == 0,
        "移动差量重发了完整结构或重复压缩未变化的大字段")
    assert(motion.shipX == 120.5 and motion.secondShipX == 120.5 and motion.passengerX == 4.5 and
        motion.pieceHealth == 100 and motion.inventoryIntact and motion.sectorIntact,
        "运动没有同步到两个观察者，或表现状态/未提交解码污染了冻结基线")
    assert(motion.worlds == 0 and motion.secondWorlds == 0, "船内世界载荷在普通差量中被重复回放")
    assert(changes.doorOpen and changes.health == 73 and changes.inventoryChanged and changes.gas == 81 and
        changes.configuration == 42 and changes.configurationNative and changes.height == 35,
        "门、耐久、库存、真实气体、设备配置或飞行状态的原位变化丢失")
    assert(changes.looseSnapshot == "changed-loose" and changes.looseX == 33 and
        changes.added and changes.removed and changes.transferred and not changes.oldDoorOpen and changes.oldHealth == 100,
        "散物、船件增删/转移或新旧表现状态隔离错误")
    local views, visibility = data.differentViews, data.visibility
    assert(views.wideShipA and views.wideShipB and not views.narrowShipA and views.narrowShipB and
        views.narrowFlights == 1 and views.narrowFlightShip == "ship-b" and views.narrowPassengers == 0 and
        views.narrowLooseItems == 0 and views.narrowWorlds == 0, "两个观察者串用了其他玩家的可见集合")
    assert(visibility.left and visibility.returned and visibility.returnedHealth == 73 and
        visibility.returnedInventory and visibility.returnedWorlds == 1 and visibility.returnedWorldPayload,
        "离开视野未删除实体，或重新可见后缺少完整结构/船内资料")
    local corrupt, recovery = data.corrupt, data.recovery
    assert(corrupt.truncatedRejected and corrupt.invalidLengthRejected and corrupt.validAfterRejection,
        "坏包未拒绝，或拒绝后污染了同序号正常包的接收基线")
    assert(recovery.missingRejected and recovery.fullAgain and recovery.recoveredX == 202 and
        recovery.recoveredHealth == 73 and recovery.recoveredWorlds == 1 and recovery.immediateX == 203 and recovery.worlds == 0,
        "缺前序基线后没有全量恢复，或即时发送复用了上一轮旧状态")
    assert(data.gasOnly.encodedFields == 1 and data.gasOnly.geometryIntact and data.gasOnly.gas == 82 and
        data.gasOnly.bytes < shared.firstBytes, "单独舱气变化重发了舱室格子或破坏了容积与气体状态")
end, "太空差量：共享编码、独立视野、动态字段、结构增删、坏包与全量恢复")

test.system("networking", "building_request_ledger", {}, function(data)
    assert(data.placeToken ~= 0 and data.dismantleToken ~= 0 and data.placeToken ~= data.dismantleToken,
        "建造与拆除回执没有独立非零令牌")
    assert(not data.wrongKindAccepted and data.matched and data.acceptedGuid == 101 and not data.duplicateAccepted,
        "不同类型或重复回执消费了错误事务")
    assert(data.onlyDismantle and data.beforeDeadline == 0 and data.waitingAllocatedBytes == 0,
        "拆除误阻塞材料事务、提前超时或等待帧仍分配键数组")
    assert(data.firstExpiryCount == 1 and data.remainingAfterFirst == 1 and data.callbackNewRequestCount == 1,
        "到期处理丢失未到期事务或吞掉回调创建的新事务")
    assert(data.remaining == 0 and #data.rejected == 4, "断线清理残留事务或重复派发拒绝")
    local expected = { [102] = {"Dismantle", "服务端响应超时"}, [103] = {"Place", "服务器响应超时"},
        [104] = {"Place", "测试断线"}, [105] = {"Dismantle", "测试断线"} }
    for _, actual in ipairs(data.rejected) do
        local row = expected[actual.guid]
        assert(row and actual.kind == row[1] and actual.reason == row[2],
            "到期与断线回执的物品、操作或原因错误")
        expected[actual.guid] = nil
    end
end, "建造拆除回执：统一令牌、精准到期、等待零分配、重入与断线清理")

test.system("networking", "player_capture_cache", {}, function(data)
    assert(#data.fields == 24, "玩家缓存没有逐项覆盖全部专属状态")
    for _, row in ipairs(data.fields) do
        assert(row.reused and row.invalidated and row.sameFrameChanged and row.oldPayloadUnchanged,
            "玩家快照缓存命中、字段失效、同帧更新或旧缓冲隔离失败：" .. row.field)
    end
    assert(data.publicPrivateInventoryIgnored and data.privateInventoryChanged,
        "玩家公开与私有库存的缓存范围不符合正式快照")
    assert(data.nullInventoryNormalized and data.unsupportedFallsBack,
        "玩家空库存规范化或未知类型完整捕获回退错误")
end, "玩家快照缓存：逐字段变化、公开私有边界、同帧变化与冻结缓冲")

test.system("networking", "public_network_save_route", {}, function(data)
    assert(data.publicOnly and data.fallbackSaved and data.payloadReused,
        "玩家公开快照未使用模块的轻量保存接口，或普通模块的正式保存被跳过")
    assert(data.fullSaveUnchanged and data.newValueCaptured and data.oldPayloadUnchanged,
        "完整存档保存被错误跳过、状态变化未进入快照或历史载荷被改写")
end, "玩家公开网络保存：轻量模块、普通模块回退、完整存档及数据隔离")

test.system("networking", "hotbar_save_cache", {}, function(data)
    assert(data.firstWrites == 1 and data.repeatedWrites == 0 and data.payloadReused and data.heldSavedEveryTime,
        "热栏没有复用未变化载荷，或错误跳过了正式手持保存回调")
    assert(data.heldChangeWrites == 1 and data.heldChangeCaptured and data.selectionWrites == 1 and data.selectionCaptured,
        "手持实际保存的变化或选中槽位没有进入热栏快照")
    assert(#data.inventoryFields == 11, "热栏库存字段覆盖不完整")
    for _, row in ipairs(data.inventoryFields) do
        assert(row.writes == 1, "热栏库存真实变化未重新捕获：" .. row.field)
    end
    assert(data.layoutNotPersistedWrites == 0, "热栏为不属于快照的接收布局重复编码")
    for _, key in ipairs({ "externalRewriteWrites", "moduleRebindWrites", "inventoryRebindWrites", "generationWrites",
        "codecRevisionWrites", "fallbackFirstWrites", "fallbackRepeatWrites" }) do
        assert(data[key] == 1, "热栏缓存失效或未知状态回退错误：" .. key)
    end
    assert(data.externalRewriteRepaired and data.unloadInvalidates,
        "热栏外部原位改写没有恢复，或卸载后仍沿用旧保存缓存")
end, "热栏保存缓存：实际OnSave计数、手持进度、字段变化、原位改写和代际失效")

test.system("networking", "terrain_replication", {}, function(data)
    assert(data.partialAccepted and not data.partialFull, "缺片的地形快照被标成完整")
    assert(data.support == 91 and data.effectiveGround == 91 and data.walkable,
        "平台没有进入正式支撑面和通行查询")
    assert(data.baseGround == 17 and data.baseCost == 25 and data.liquid == 8 and data.weather == 45,
        "平台同步破坏了底层地形、液体或天气")
    assert(data.duplicateAccepted and data.duplicateWrites == 0, "重复地形消息再次写入权威副本")
    assert(data.removeAccepted and data.oldSnapshotSupport == 0 and data.fullAfterReorder,
        "旧完整快照覆盖了更新的拆除，或乱序后无法完成补拉")
    assert(not data.removedWalkable and data.removedLiquid == 8, "拆平台没有恢复水域原通行")
    assert(data.wallAccepted and data.wall == 74 and data.damage == 9 and data.oldDeltaWall == 74,
        "墙体/损伤不同步或被旧差量回退")
    assert(not data.invalidAccepted and data.atomicSupport == 0, "非法地形批次留下半批修改")
    for _, key in ipairs({"nanAccepted", "wrongSizeAccepted", "wrongTokenAccepted", "zeroRevisionAccepted",
        "oversizeAccepted", "invalidatedFull", "duplicateCompleted", "reloadedFull", "oldChunkTokenAccepted"}) do
        assert(not data[key], "地形复制边界校验失败：" .. key)
    end
    assert(data.refreshedFull and data.reloadedComplete and data.reloadedSupport == 0 and data.reloadedWall == 0,
        "重载或重新补拉后残留旧平台/墙体")
end, "格子建筑：平台/墙体、底液保留、乱序、坏包、区块重载")

test.system("networking", "terrain_wire", {}, function(data)
    assert(data.protocol >= 24 and data.remainingBytes == 0 and data.cellCount == 2,
        "格子建筑 Mirror 协议未完整往返")
    assert(data.world == "probe-world" and data.dimension == "surface" and data.realm == 7 and data.token == 3,
        "地形消息丢失世界身份或补拉令牌")
    assert(data.chunkX == -16 and data.chunkY == 32 and data.revision == 1234567890123,
        "负区块坐标或地形版本被截断")
    assert(data.support == 91 and data.baseGround == 17, "地形协议混淆支撑层和原地面")
end, "格子建筑：正式 Mirror 序列化和世界身份")

test.system("networking", "world_request_gate", {}, function(data)
    assert(data.valid and data.unversioned, "合法世界请求被门禁拒绝")
    for _, key in ipairs({ "notReady", "transferring", "missingObserver", "missingWorld", "staleRevision" }) do
        assert(type(data[key]) == "string" and #data[key] > 0, "世界门禁拒绝缺少即时原因：" .. key)
    end
end, "建造与拆除：世界门禁提供真实拒绝原因")

test.system("networking", "building_orientation_request", {}, function(data)
    assert(#data.doors == 2, "缺少木门或石门的非机械放置采样")
    for _, door in ipairs(data.doors) do
        assert(not door.hasMechanicalNode and #door.samples == 5, "门放置采样误走机械节点或缺少一整圈转向")
        for index, sample in ipairs(door.samples) do
            local turns = (index - 1) % 4
            assert(sample.canRotate and sample.placementTurns == turns,
                door.id .. "没有通过正式手持转向入口保持四向角度")
            assert(sample.requestTurns == turns and sample.wireTurns == turns,
                door.id .. "正式网络组包或 Mirror 往返丢失放置角度")
            assert(not sample.mirror and not sample.wireMirror,
                door.id .. "四向旋转被错误当成水平镜像")
            assert(sample.requestToken == 16 + index and sample.sourceGuid == 91901
                and sample.itemId == door.id and sample.payloadPreserved and sample.remainingBytes == 0,
                door.id .. "门建造请求往返丢失材料身份、令牌或快照")
        end
        assert(not door.canRotateOutsideHand and door.afterLeavingHand == 0,
            door.id .. "离开手持后仍可修改放置角度")
    end
end, "木门石门：正式四向转向、非机械建造请求与 Mirror 朝向往返")

test.system("networking", "local_observer_window", {}, function(data)
    assert(data.localX == 16.5 and data.remoteX == 15.5, "本机预测跨块时仍使用旧服务器窗口，或远端误用预测")
    assert(data.invalidX == 15.5 and data.wrappedX == -31.5, "非法预测没有回退，或环绕后窗口没有跟随本机")
end, "联机流送：本机预测跨块、远端权威与接缝窗口")

for _, row in ipairs({
    { address = "localhost", fallbackPort = 7777, host = "localhost", port = 7777 },
    { address = " kcp://example.com:9000 ", fallbackPort = 7777, host = "example.com", port = 9000 },
    { address = "udp://127.0.0.1:65535", fallbackPort = 7777, host = "127.0.0.1", port = 65535 },
    { address = "[::1]:8000", fallbackPort = 0, port = 8000 },
    { address = "localhost", fallbackPort = 0, host = "localhost", port = 7777 },
    { address = "kcp://localhost", fallbackPort = 9001, host = "localhost", port = 9001 },
    { address = "udp://localhost:7777", fallbackPort = 9001, host = "localhost", port = 7777 },
    { address = "localhost:1", fallbackPort = 9001, host = "localhost", port = 1 },
    { address = "kcp://localhost:00001", fallbackPort = 9001, host = "localhost", port = 1 },
    { address = "[::1]", fallbackPort = 9001, port = 9001 },
    { address = "udp://[::1]:65535", fallbackPort = 7777, port = 65535 }
}) do
    test.system("networking", "endpoint", row, function(data)
        assert(data.accepted and data.port == row.port and data.error == "", "合法联机端点解析失败")
        if row.host then assert(data.host == row.host, "主机地址解析错误") end
        assert(data.display ~= nil and #data.display > 0, "端点无法显示")
    end, "联机端点：" .. row.address)
end

for _, address in ipairs({ "", "http://localhost:7777", "kcp://user@localhost:7777", "kcp://localhost:7777/path", "kcp://localhost:7777?x=1",
    "kcp://localhost:0", "localhost:0", "udp://localhost:00000", "kcp://[::1]:0", "[::1]:0",
    "kcp://localhost:65536", "localhost:-1", "kcp://localhost:abc", "kcp://localhost:", "udp://[::1]:65536" }) do
    test.system("networking", "endpoint", { address = address }, function(data)
        assert(not data.accepted and data.error ~= nil and #data.error > 0, "非法联机端点被接受")
    end, "拒绝非法联机端点：" .. address)
end

test.system("networking", "item_payload", { guid = 143, amount = 3 }, function(data)
    assert(data.serialized and data.valid and data.read and data.bytes > 4, "正式网络实例负载无法读取")
    assert(data.guid == 143 and data.itemId == "probe:item", "网络快照身份错误")
    assert(not data.corruptedAccepted and not data.emptyAccepted, "网络协议接受损坏或空负载")
    assert(data.sourceAmount == 3, "网络捕获修改了源物品")
end, "网络实例身份与协议头拒绝")

test.system("networking", "initial_sync_gate", {}, function(data)
    assert(data.valid, "世界和网络玩家就绪后仍无法发起首次同步")
    assert(not data.beforeReady and not data.beforeSpawn and not data.duringTransfer,
        "网络未就绪、未出生或迁移中提前发送了首次同步")
    assert(not data.otherWorld and not data.missingWorld,
        "跨世界或没有世界身份时提前消耗了首次同步机会")
end, "联机首次物品与天气状态：世界身份就绪门禁")

test.system("networking", "roll_snapshot", {}, function(data)
    assert(data.protocol >= 19 and data.roundtrip and data.remainingBytes == 0, "翻滚协议版本或动作快照往返错误")
    assert(math.abs(data.delayedElapsed - 0.25) < 0.00001, "远端没有按服务器 double 时标补齐动作进度")
    assert(math.abs(data.pastEndElapsed - 0.4) < 0.00001, "过期动作快照延长了翻滚")
    assert(math.abs(data.futureSampleElapsed - 0.1) < 0.00001 and math.abs(data.pausedElapsed - 0.1) < 0.00001,
        "未来时标或暂停状态错误推进了翻滚")
    assert(data.invalidElapsed == 0, "非法动作进度进入表现")
end, "联机翻滚：正式协议、延迟补偿、到期与暂停")

test.system("networking", "roll_stamina_handoff", {}, function(data)
    assert(data.started and data.protected and data.protectedCurrent == 90, "旧客户端快照覆盖了服务器翻滚扣费")
    assert(data.missingRejected and data.duplicateBeginRejected, "体力交接接受缺失模块或重入事务")
    assert(data.wrongRequestRejected and data.wrongAmountRejected and data.pendingAfterBadAck,
        "错误体力回执提前撤销扣费保护")
    assert(data.acknowledged and not data.pendingAfterAck and data.duplicateAckRejected,
        "合法体力回执未完成交接或被重复接受")
    assert(data.reducedCapacityAcknowledged and not data.reducedCapacityPending,
        "接收端体力上限降低后的合法钳制回执卡死了翻滚")
end, "联机翻滚：扣费快照保护与合法回执")
