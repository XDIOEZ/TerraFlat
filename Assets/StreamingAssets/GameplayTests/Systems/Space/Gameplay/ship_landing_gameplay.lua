-- 落地专项只走正式 GM、起降和普通管路，不执行导航、惯性或跨星区夹具。
test.space("ship_landing", function(data)
    assert(data.scope == "live-ship-landing" and #data.ships > 0, "没有执行真实飞船起降专项")
    local function ground(snapshot, label)
        assert(snapshot.phase == "GroundBuildings" and not snapshot.spaceView, "当前不是普通地面建筑：" .. label)
        local ids, machines, occupied, fibers = {}, 0, {}, 0
        for _, building in ipairs(snapshot.buildings) do
            assert(building.guid ~= 0 and not ids[building.guid] and building.snapshotGuid == building.guid,
                "普通建筑 Guid 重复、丢失或快照串号：" .. label)
            assert(building.installed and building.scope == snapshot.world,
                "普通建筑没有按正式地表作用域安装：" .. label)
            local key = building.cell.x .. ":" .. building.cell.y .. ":" .. building.placementLayer
            assert(not occupied[key] and building.placementOccupied,
                "普通建筑同层重叠或没有登记正式占地：" .. label .. "/" .. building.itemId)
            occupied[key] = building.guid
            if building.itemId == "ShipFiber" then
                fibers = fibers + 1
                assert(building.placementLayer == 5 and building.sameLayerPlacementRejected,
                    "光纤落地丢失显式层，或允许同层重复铺设：" .. label)
            end
            assert(building.cell.x % 1 == 0 and building.cell.y % 1 == 0 and
                building.quarterTurns % 1 == 0 and building.quarterTurns >= 0 and building.quarterTurns <= 3,
                "落地建筑没有对齐格子和 90 度朝向：" .. label)
            ids[building.guid] = building
            if building.machine then machines = machines + 1 end
        end
        assert(snapshot.pieceCount > 0 and #snapshot.buildings == snapshot.pieceCount,
            "普通地面成员采样不完整：" .. label)
        local stackedFibers = 0
        for _, building in pairs(ids) do
            if building.itemId == "ShipFiber" and
                occupied[building.cell.x .. ":" .. building.cell.y .. ":0"] then
                stackedFibers = stackedFibers + 1
            end
        end
        assert(fibers > 0 and stackedFibers > 0, "落地专项没有覆盖与设备同格的非机器光纤：" .. label)
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
    local function ground_preparation(ship, restored, machineCount, label)
        local preparation = ship.preparation
        local cancelled = preparation.cancellation
        assert(cancelled.before.valid and cancelled.before.found and cancelled.before.instanceId ~= 0 and cancelled.before.guid == 0 and
            not cancelled.before.active and not cancelled.before.registered,
            "普通候选准备时提前拥有正式身份或表现：" .. label)
        assert(not cancelled.validAfter and cancelled.instanceAfter == 0 and cancelled.retired and
            not cancelled.activeAfter and not cancelled.registeredAfter and
            cancelled.sourceGuidBefore == cancelled.sourceGuidAfter,
            "重复取消没有安全回收候选，或修改了来源数据：" .. label)
        local function candidates(snapshot)
            local result = {}
            for _, candidate in ipairs(snapshot.candidates) do
                local guid = tonumber(candidate.pieceId)
                assert(candidate.valid and candidate.instanceId ~= 0 and candidate.guid == 0 and
                    not candidate.active and not candidate.registered and not result[guid],
                    "隐藏候选身份、激活或注册状态错误：" .. label)
                result[guid] = candidate.instanceId
            end
            return result
        end
        for _, snapshot in ipairs({preparation.ready, preparation.moved, preparation.expanded, preparation.finalReady}) do
            assert(snapshot.state == "ready" and snapshot.ready and snapshot.shipPresent and
                snapshot.phase == "SurfaceDescending" and snapshot.height > 0 and
                snapshot.groundMatches == 0 and snapshot.registeredOriginalItems == 0,
                "准备完成前提前移交地面，或丢失真实下降船体：" .. label)
            assert(snapshot.preparedItems > 0 and snapshot.preparedItems == snapshot.candidateItems and
                snapshot.validCandidates == snapshot.candidateItems and snapshot.activeCandidates == 0 and
                snapshot.registeredCandidates == 0 and #snapshot.candidates == snapshot.candidateItems,
                "没有真正备齐停用且未登记的普通建筑：" .. label)
            assert(snapshot.machineCount == machineCount and snapshot.preparedMachines == machineCount and
                snapshot.hiddenMachines == snapshot.preparedMachines and snapshot.consumedMachines == 0,
                "机器表现未提前备齐，或在真实交接前就被激活或消费：" .. label)
            candidates(snapshot)
            assert(#snapshot.machines == machineCount, "准备期间机器采样丢失：" .. label)
            for _, machine in ipairs(snapshot.machines) do
                assert(machine.shipRegistered and not machine.surfaceRegistered and machine.sameMachine and
                    machine.sameProcessor and machine.sameInventories and machine.sameFluidWrapper and
                    machine.scope == "ship:" .. ship.lifting.id,
                    "准备期间克隆或提前移交了运行机器、库存或流体：" .. label)
            end
        end
        local beforeMove, afterMove = candidates(preparation.ready), candidates(preparation.moved)
        local dx = preparation.moved.position.x - preparation.ready.position.x
        local dy = preparation.moved.position.y - preparation.ready.position.y
        assert(dx * dx + dy * dy > 0.00000001 and
            preparation.ready.invalidations == preparation.moved.invalidations,
            "正式下降操纵没有改变落点，或只改变落点就重建准备布局：" .. label)
        for guid, instance in pairs(beforeMove) do
            assert(afterMove[guid] == instance, "移动落点没有复用已准备的建筑：" .. label)
        end
        assert(preparation.expanded.invalidations > preparation.moved.invalidations and
            preparation.finalReady.invalidations > preparation.expanded.invalidations and
            preparation.expanded.preparedItems == preparation.moved.preparedItems + 1 and
            preparation.finalReady.preparedItems == preparation.moved.preparedItems and
            not preparation.addedPieceStillPresent,
            "真实加装或撤销地板没有使准备布局重新对齐当前结构：" .. label)
        for _, snapshot in ipairs({preparation.expanded, preparation.finalReady}) do
            assert(snapshot.lastCleanup.state == "invalidated" and snapshot.lastCleanup.remainingCandidates == 0 and
                snapshot.lastCleanup.remainingMachines == 0,
                "结构失效后残留旧候选或旧机器表现：" .. label)
            assert(#snapshot.previousCandidates > 0, "结构失效没有采到旧候选：" .. label)
            for _, candidate in ipairs(snapshot.previousCandidates) do
                assert(not candidate.active and not candidate.registered and
                    (candidate.retired or candidate.currentCandidate),
                    "结构失效后旧候选既未回收也未合法复用：" .. label)
            end
        end
        local finalCandidates = candidates(preparation.finalReady)
        for guid, instance in pairs(finalCandidates) do
            assert(restored[guid] and not restored[guid].machine and restored[guid].itemInstanceId == instance,
                "正式落地重新实例化了已经准备好的普通建筑：" .. label)
        end
        local committed = preparation.committed
        assert(committed.state == "committed" and committed.ready and committed.error == nil and
            committed.committedItems == preparation.finalReady.preparedItems and
            committed.preparedItems == committed.committedItems and committed.remainingCandidates == 0 and
            committed.remainingMachines == 0 and committed.preparedMachines == machineCount and
            committed.consumedMachines > 0 and committed.consumedMachines <= committed.preparedMachines,
            "落地没有一次消费已准备对象或清理剩余表现：" .. label)
        assert(#preparation.landedMachines == machineCount, "落地机器身份采样丢失：" .. label)
        for _, machine in ipairs(preparation.landedMachines) do
            assert(not machine.shipRegistered and machine.surfaceRegistered and machine.sameMachine and
                machine.scope == ship.landedAgain.world,
                "落地机器没有唯一移交原实例：" .. label)
            if not machine.fluid then
                assert(machine.sameProcessor and machine.sameInventories, "落地更换了原加工器或物品库存：" .. label)
            end
        end
        local health = preparation.healthChange
        assert(health.accepted and health.after > 0 and health.after < health.before and restored[health.guid] and
            restored[health.guid].health > 0 and restored[health.guid].health <= health.after + 0.0001,
            "正式落地沿用了准备时的旧血量：" .. label)
        for _, id in ipairs({"hydrogen", "oxygen"}) do
            assert(ship.landedAgain[id] <= preparation.latestBeforeCommit[id] + 0.0000001 and
                preparation.latestBeforeCommit[id] < ship.cruising[id],
                "准备或移交复制了燃料，或下降未消耗真实燃料：" .. label)
        end
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
            assert(math.abs(flight.angle) < 0.000001 and math.abs(flight.angularVelocity) < 0.000001,
                "起降过程没有保持朝上：" .. label)
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
        local hudBefore, hudAfter = ship.descending.flightHud, ship.landedAgain.flightHud
        assert(hudBefore.found and hudBefore.phase == "SurfaceDescending" and hudAfter.found and
            not hudAfter.driving and hudAfter.shipId == "", "落地清船后正式 HUD 只读接口丢失乘员：" .. label)
        assert(hudAfter.receipt.valid and hudAfter.receipt.shipId == ship.lifting.id and
            hudAfter.receipt.worldKey == ship.landedAgain.world and hudAfter.receipt.eventId ~= "" and
            hudAfter.receipt.eventId ~= hudBefore.receipt.eventId and
            hudAfter.receipt.time >= hudBefore.simulationSeconds,
            "普通建筑移交后没有发布本船本世界的新落地凭证，或复用了历史触地：" .. label)
        assert(ship.landedAgain.world == ship.landed.world and restoredMachines == machineCount and
            ship.landedAgain.pieceCount == ship.landed.pieceCount and
            ship.landedAgain.activeShipCount == ship.beforeBuild.activeShipCount and
            ship.landedAgain.shipRemoved and ship.landedAgain.flightRemoved and not ship.landedAgain.supported,
            "落地仍依赖活动船体、飞行记录、乘员绑定或特殊作用域：" .. label)
        for guid, building in pairs(initial) do
            assert(restored[guid] and restored[guid].itemId == building.itemId and
                restored[guid].machine == building.machine, "落地没有还原同 Guid 的普通建筑：" .. label)
            assert(restored[guid].quarterTurns == building.quarterTurns, "起降改变了建筑安装朝向：" .. label)
        end
        ground_preparation(ship, restored, machineCount, label)
        ground_view(ship.viewCache.surface, label .. "/GM 创建")
        whole_view(ship.viewCache.space, ship.landed.pieceCount, label .. "/太空")
        whole_view(ship.viewCache.descending, ship.landed.pieceCount, label .. "/下降")
        ground_view(ship.viewCache.returned, label .. "/落地")
        local fuel = ship.autoRefuel
        assert(fuel.fixtureSetup == "landed-independent-tanks-no-passive-transfer" and not fuel.targetDirectRefill and
            fuel.world == ship.landedAgain.world and fuel.sourceScope == fuel.world and fuel.pipeScope == fuel.world and
            fuel.outletScope == fuel.world and fuel.targetScope == fuel.world and restored[fuel.targetGuid],
            "落地加油没有使用普通地面管路与原 Guid 燃料罐：" .. label)
        assert(fuel.sourceGuid ~= fuel.targetGuid and fuel.pipeGuid ~= fuel.outletGuid and
            (fuel.pipeMask == 5 or fuel.pipeMask == 10) and fuel.simulationSeconds > 0,
            "独立罐连接管路没有正式连通，或未等待真实世界固定步：" .. label)
        for _, id in ipairs({"hydrogen", "oxygen"}) do
            assert(math.abs(fuel.targetsAfter[id]-fuel.targetsBefore[id])<0.000001 and
                math.abs(fuel.sourceAfter[id]-fuel.sourceBefore[id])<0.000001,
                "普通管路没有把真实 " .. id .. " 从源罐送进落地船罐：" .. label)
            assert(math.abs(fuel.totalAfter[id] - fuel.totalBefore[id]) <= 0.0000001 * math.max(1, fuel.totalBefore[id]),
                "普通加油全链没有守恒真实 " .. id .. "：" .. label)
        end
    end
    return "GM 普通建筑、隐藏准备与结构失效、原对象及最新状态移交、真实起降和管路氢氧独立罐无被动输送：" .. #data.ships .. " 个船型"
end, "普通建筑飞船起降与落地管路独立罐无被动输送")
test.check("world_ready", "飞船落地专项清理后地表世界就绪")
test.check("player_ready", "飞船落地专项清理后当前玩家重新绑定")
