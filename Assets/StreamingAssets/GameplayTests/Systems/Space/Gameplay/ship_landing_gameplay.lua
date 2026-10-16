-- 落地专项只走正式 GM、起降和普通管路，不执行导航、惯性或跨星区夹具。
test.space("ship_landing", function(data)
    assert(data.scope == "live-ship-landing" and #data.ships > 0, "没有执行真实飞船起降专项")
    local function ground(snapshot, label)
        assert(snapshot.phase == "GroundBuildings" and not snapshot.spaceView, "当前不是普通地面建筑：" .. label)
        local ids, machines = {}, 0
        for _, building in ipairs(snapshot.buildings) do
            assert(building.guid ~= 0 and not ids[building.guid] and building.snapshotGuid == building.guid,
                "普通建筑 Guid 重复、丢失或快照串号：" .. label)
            assert(building.installed and building.scope == snapshot.world,
                "普通建筑没有按正式地表作用域安装：" .. label)
            assert(building.cell.x % 1 == 0 and building.cell.y % 1 == 0 and
                building.quarterTurns % 1 == 0 and building.quarterTurns >= 0 and building.quarterTurns <= 3,
                "落地建筑没有对齐格子和 90 度朝向：" .. label)
            ids[building.guid] = building
            if building.machine then machines = machines + 1 end
        end
        assert(snapshot.pieceCount > 0 and #snapshot.buildings == snapshot.pieceCount,
            "普通地面成员采样不完整：" .. label)
        return ids, machines
    end
    local function ground_view(snapshot, label)
        assert(#snapshot.presenters == 0 and #snapshot.pieces == 0 and snapshot.parkedPieces == 0,
            "普通建筑残留 ShipView、船件缓存或 ParkedPieces：" .. label)
    end
    local function whole_view(snapshot, count, label)
        assert(#snapshot.presenters == 1 and #snapshot.pieces == count and snapshot.parkedPieces == 0,
            "飞行或下降没有唯一整船与完整部件：" .. label)
        local body = snapshot.presenters[1]
        assert(body.wholeShipBody and body.activeBodies == 1 and body.activeBlockers == 1 and
            body.active and not body.pooled and body.scene == snapshot.scene,
            "飞行或下降没有使用当前场景的整体碰撞：" .. label)
    end
    for _, ship in ipairs(data.ships) do
        local label = ship.templateId
        local initial, machineCount = ground(ship.landed, label .. "/GM 创建")
        local filled = ground(ship.filled, label .. "/普通地面补给")
        assert(ship.landed.activeShipCount == ship.beforeBuild.activeShipCount and
            ship.landed.totalBuildings >= ship.beforeBuild.totalBuildings + ship.landed.pieceCount,
            "GM 创建时提前装配整船，或没有创建普通建筑：" .. label)
        assert(ship.enteredConsole and ship.filled.hydrogen > 0 and ship.filled.oxygen > 0,
            "没有通过已供电的正式控制台和真实氢氧燃料起飞：" .. label)
        assert(ship.liftingGround.pieceCount == 0 and ship.liftingGround.hydrogen == 0 and ship.liftingGround.oxygen == 0 and
            ship.liftingGround.activeShipCount == ship.beforeBuild.activeShipCount + 1,
            "点火没有唯一移交地面部件，或地面仍在读取船内库存：" .. label)
        assert(ship.lifting.phase == "Lifting" and ship.cruising.phase == "Cruising" and ship.cruising.spaceView and
            ship.landingAccepted and ship.descending.phase == "SurfaceDescending" and not ship.descending.spaceView,
            "没有完整经过点火、太空和真实地表下降：" .. label)
        assert(ship.cruising.hydrogen < ship.filled.hydrogen and ship.cruising.oxygen < ship.filled.oxygen,
            "起飞没有消耗真实氢氧：" .. label)
        for _, flight in ipairs({ship.lifting, ship.cruising, ship.descending}) do
            assert(flight.id == ship.lifting.id and flight.pieceCount == ship.landed.pieceCount and
                #flight.pieces == ship.landed.pieceCount and #flight.machines == machineCount,
                "起降丢失船体身份、部件或普通机器：" .. label)
            local seen = {}
            for _, piece in ipairs(flight.pieces) do
                local guid = tonumber(piece.id)
                assert(initial[guid] and filled[guid] and not seen[guid] and piece.alive and
                    piece.itemId == initial[guid].itemId, "起降没有保留原建筑 Guid 与定义：" .. label)
                seen[guid] = true
            end
            for _, node in ipairs(flight.machines) do
                assert(initial[node.guid] and initial[node.guid].machine and node.itemId == initial[node.guid].itemId and
                    node.scope == "ship:" .. flight.id, "普通机器没有完整移交到本船作用域：" .. label)
            end
        end
        local restored, restoredMachines = ground(ship.landedAgain, label .. "/落地移交")
        assert(ship.landedAgain.world == ship.landed.world and restoredMachines == machineCount and
            ship.landedAgain.pieceCount == ship.landed.pieceCount and
            ship.landedAgain.activeShipCount == ship.beforeBuild.activeShipCount and
            ship.landedAgain.shipRemoved and ship.landedAgain.flightRemoved and not ship.landedAgain.supported,
            "落地仍依赖活动船体、飞行记录、乘员绑定或特殊作用域：" .. label)
        for guid, building in pairs(initial) do
            assert(restored[guid] and restored[guid].itemId == building.itemId and
                restored[guid].machine == building.machine, "落地没有还原同 Guid 的普通建筑：" .. label)
        end
        ground_view(ship.viewCache.surface, label .. "/GM 创建")
        whole_view(ship.viewCache.space, ship.landed.pieceCount, label .. "/太空")
        whole_view(ship.viewCache.descending, ship.landed.pieceCount, label .. "/下降")
        ground_view(ship.viewCache.returned, label .. "/落地")
        local fuel = ship.autoRefuel
        assert(fuel.fixtureSetup == "landed-world-pipe-outlet-source-tank" and not fuel.targetDirectRefill and
            fuel.world == ship.landedAgain.world and fuel.sourceScope == fuel.world and fuel.pipeScope == fuel.world and
            fuel.outletScope == fuel.world and fuel.targetScope == fuel.world and restored[fuel.targetGuid],
            "落地加油没有使用普通地面管路与原 Guid 燃料罐：" .. label)
        assert(fuel.sourceGuid ~= fuel.targetGuid and fuel.pipeGuid ~= fuel.outletGuid and
            (fuel.pipeMask == 5 or fuel.pipeMask == 10) and fuel.simulationSeconds > 0,
            "加油管路没有正式连通，或未等待真实世界固定步：" .. label)
        for _, id in ipairs({"hydrogen", "oxygen"}) do
            assert(fuel.targetsAfter[id] > fuel.targetsBefore[id] + 0.000001 and
                fuel.sourceAfter[id] < fuel.sourceBefore[id] - 0.000001,
                "普通管路没有把真实 " .. id .. " 从源罐送进落地船罐：" .. label)
            assert(math.abs(fuel.totalAfter[id] - fuel.totalBefore[id]) <= 0.0000001 * math.max(1, fuel.totalBefore[id]),
                "普通加油全链没有守恒真实 " .. id .. "：" .. label)
        end
    end
    return "GM 普通建筑、点火装配、真实起降、同 Guid 还原及普通管路氢氧自动加油：" .. #data.ships .. " 个船型"
end, "普通建筑飞船起降与落地管路自动加油")
test.check("world_ready", "飞船落地专项清理后地表世界就绪")
test.check("player_ready", "飞船落地专项清理后当前玩家重新绑定")
