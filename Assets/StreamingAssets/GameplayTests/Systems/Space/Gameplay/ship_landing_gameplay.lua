-- 落地专项只走正式 GM、起降和普通管路，不执行导航、惯性或跨星区夹具。
local function verify_infinite_propellant_sources(snapshots, label)
    local expectedEngineCount = snapshots[1].unlimitedFuelEngineCount or 0
    local expectedSourceCount = snapshots[1].infiniteGasSourceCount or 0
    local previous = nil
    for _, snapshot in ipairs(snapshots) do
        assert((snapshot.unlimitedFuelEngineCount or 0) == expectedEngineCount and
            (snapshot.infiniteGasSourceCount or 0) == expectedSourceCount,
            "无限燃料能力在起降阶段改变：" .. label)
        local engineCount, sourceCount = 0, 0
        local machineRows = snapshot.machines or snapshot.buildings or {}
        for _, row in ipairs(machineRows) do if row.unlimitedFuel then engineCount = engineCount + 1 end end
        local sourceRows = snapshot.machines or snapshot.fluidInventories or {}
        for _, row in ipairs(sourceRows) do if row.infiniteGasSource then sourceCount = sourceCount + 1 end end
        assert(engineCount == expectedEngineCount and sourceCount == expectedSourceCount,
            "无限推进或无限气源计数没有对应到真实机器能力：" .. label)
        local sources = snapshot.infiniteGasSources or {}
        assert(#sources == expectedSourceCount, "无限气源资格采样数量不一致：" .. label)
        local current = {}
        for _, source in ipairs(sources) do
            assert(source.infiniteGasSource and source.guid ~= 0 and not current[source.guid] and
                source.totalMoles > 0 and source.hydrogen > 0 and source.oxygen > 0,
                "无限气源没有真实有限配比样本：" .. label)
            local composition = source.composition
            local expectedHydrogen, expectedOxygen = composition["core:hydrogen"], composition["core:oxygen"]
            assert(expectedHydrogen and expectedHydrogen > 0 and expectedOxygen and expectedOxygen > 0 and
                math.abs(source.hydrogen / source.oxygen - expectedHydrogen / expectedOxygen) < 0.00001,
                "无限气源有限样本与配置配比不符：" .. label)
            current[source.guid] = source
        end
        if previous then
            for guid, before in pairs(previous) do
                local after = current[guid]
                assert(after and math.abs(after.hydrogen - before.hydrogen) < 0.000001 and
                    math.abs(after.oxygen - before.oxygen) < 0.000001 and
                    math.abs(after.totalMoles - before.totalMoles) < 0.000001,
                    "飞行或落地消耗了无限气源的有限展示样本：" .. label)
                for fluidId, ratio in pairs(before.composition) do
                    assert(math.abs((after.composition[fluidId] or -1) - ratio) < 0.000001,
                        "起降改变了无限气源配比配置：" .. label)
                end
                for fluidId in pairs(after.composition) do
                    assert(before.composition[fluidId] ~= nil, "起降改变了无限气源组分：" .. label)
                end
            end
        end
        previous = current
    end
    return expectedEngineCount > 0 or expectedSourceCount > 0
end

local function verify_visible_height_hud_layout(snapshot)
    local layout = snapshot.layout
    if not layout.visible then return false end
    assert(layout.statusVisible, "高度尺布局现场缺少真实生存状态条")
    local h, s = layout.hud, layout.status
    assert(h.right - h.left > 40 and h.top - h.bottom > 100,
        "高度尺为避让状态条缩成了不可读尺寸")
    assert(h.right <= s.left or h.left >= s.right or h.top <= s.bottom or h.bottom >= s.top,
        "真实飞行高度尺或落地提示仍然遮挡生存状态条")
    return true
end

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
            if building.itemId == "Door_Stone" or building.itemId == "Door_Wood" then
                assert(building.door and building.door.rendererBound and building.door.colliderBound and
                    building.door.blockingMatchesState and building.door.interactable,
                    "普通或隐藏候选恢复的门没有绑定贴图、碰撞和交互：" .. label)
            end
            local key = string.format("%d:%d:%d", building.cell.x, building.cell.y, building.placementLayer)
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
                occupied[string.format("%d:%d:%d", building.cell.x, building.cell.y, 0)] then
                stackedFibers = stackedFibers + 1
            end
        end
        assert(fibers > 0 and stackedFibers > 0, "落地专项没有覆盖与设备同格的非机器光纤：" .. label)
        return ids, machines
    end
    local function ground_view(snapshot, label)
        assert(#snapshot.presenters == 0 and #snapshot.itemProjections == 0 and #snapshot.chunkProjections == 0,
            "普通建筑残留整船、物品或区块投影：" .. label)
    end
    local function whole_view(snapshot, count, label)
        assert(#snapshot.presenters == 1 and #snapshot.itemProjections > 0 and #snapshot.chunkProjections > 0,
            "飞行或下降没有唯一整船及真实普通物品/区块投影：" .. label)
        local body = snapshot.presenters[1]
        assert(body.wholeShipBody and body.activeBodies == 1 and body.activeBlockers == 1 and
            body.active and not body.pooled and body.scene == snapshot.scene,
            "飞行或下降没有使用当前场景的整体碰撞：" .. label)
    end
    local function ground_preparation(ship, restored, machineCount, label)
        local preparation = ship.preparation
        local function ordinary_items(snapshot)
            local state = snapshot.ordinaryItems
            assert(state.expectedItemCount > 0 and #state.items == state.expectedItemCount and
                state.sameInstanceCount == state.expectedItemCount and state.sameHealthCount == state.expectedItemCount,
                "下降期间普通 Item 身份或生命状态发生变化：" .. label)
            local result = {}
            for _, item in ipairs(state.items) do
                assert(item.guid ~= 0 and item.instanceId ~= 0 and item.runtimeRegistered and
                    item.sameRuntimeInstance and item.itemWorldKey == state.worldKey and not result[item.guid],
                    "下降期间重建或错放普通 Item：" .. label)
                if item.health ~= nil then assert(item.sameHealth, "下降期间普通 Item 生命值改变：" .. label) end
                result[item.guid] = item.instanceId
            end
            return result
        end
        for _, snapshot in ipairs({preparation.ready, preparation.moved, preparation.expanded, preparation.finalReady}) do
            assert(snapshot.worldRegistered and snapshot.presentationWorld == snapshot.world and
                snapshot.targetSceneCurrent and snapshot.targetSceneHandle ~= 0,
                "地表迁移完成后没有重新绑定降落准备所需的世界运行时：" .. label)
            assert(snapshot.state == "ready" and snapshot.ready and snapshot.shipPresent and
                snapshot.phase == "SurfaceDescending" and snapshot.height > 0 and
                snapshot.groundMatches == 0,
                "准备完成前提前移交地面，或丢失真实下降船体：" .. label)
            assert(snapshot.machineCount == machineCount and snapshot.preparedMachines == machineCount and
                snapshot.hiddenMachines == snapshot.preparedMachines and snapshot.consumedMachines == 0,
                "机器显示准备状态错误，或在真实交接前就被消费：" .. label)
            ordinary_items(snapshot)
            assert(#snapshot.machines == machineCount, "准备期间机器采样丢失：" .. label)
            for _, machine in ipairs(snapshot.machines) do
                assert(machine.shipRegistered and not machine.surfaceRegistered and machine.sameMachine and
                    machine.sameProcessor and machine.sameInventories and machine.sameFluidWrapper and
                    machine.scope == "ship:" .. ship.lifting.id,
                    "准备期间克隆或提前移交了运行机器、库存或流体：" .. label)
            end
        end
        local beforeMove, afterMove = ordinary_items(preparation.ready), ordinary_items(preparation.moved)
        local dx = preparation.moved.position.x - preparation.ready.position.x
        local dy = preparation.moved.position.y - preparation.ready.position.y
        assert(dx * dx + dy * dy > 0.00000001 and
            preparation.ready.invalidations == preparation.moved.invalidations,
            "正式下降操纵没有改变落点，或只改变落点就重建结构准备：" .. label)
        for guid, instance in pairs(beforeMove) do
            assert(afterMove[guid] == instance, "移动落点时普通 Item 实例发生变化：" .. label)
        end
        assert(preparation.expanded.invalidations > preparation.moved.invalidations and
            preparation.finalReady.invalidations > preparation.expanded.invalidations and
            not preparation.addedPieceStillPresent,
            "真实加装或撤销地板没有使结构准备重新对齐：" .. label)
        for _, snapshot in ipairs({preparation.expanded, preparation.finalReady}) do
            assert(snapshot.lastCleanup.state == "invalidated" and snapshot.lastCleanup.remainingMachines == 0,
                "结构失效后残留旧机器表现：" .. label)
        end
        local finalItems = ordinary_items(preparation.finalReady)
        for guid, instance in pairs(finalItems) do
            assert(restored[guid] and not restored[guid].machine and restored[guid].itemInstanceId == instance,
                "正式落地没有保留原普通 Item 实例：" .. label)
        end
        local committed = preparation.committed
        assert(committed.state == "committed" and committed.ready and committed.error == nil and
            committed.remainingMachines == 0 and committed.preparedMachines == machineCount and
            committed.consumedMachines > 0 and committed.consumedMachines <= committed.preparedMachines,
            "落地没有完成机器表现移交：" .. label)
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
        local hasInfinitePropellant = (ship.cruising.unlimitedFuelEngineCount or 0) > 0 or
            (ship.cruising.infiniteGasSourceCount or 0) > 0
        for _, id in ipairs({"hydrogen", "oxygen"}) do
            assert(ship.landedAgain[id] <= preparation.latestBeforeCommit[id] + 0.0000001,
                "准备或移交复制了燃料：" .. label)
            if not hasInfinitePropellant then
                assert(preparation.latestBeforeCommit[id] < ship.cruising[id],
                    "下降未消耗真实有限燃料：" .. label)
            end
        end
    end

    local function verify_ordinary_item_migration(stages, landed, label)
        assert(#stages >= 4, "没有采到普通 Item 起降迁移全阶段：" .. label)
        local initial, last = nil, nil
        local sawShip, sawFlight, sawLandedHealth = false, false, false
        for _, stage in ipairs(stages) do
            assert(stage.expectedItemCount > 0 and #stage.items == stage.expectedItemCount and
                stage.sameInstanceCount == stage.expectedItemCount and stage.sameHealthCount == stage.expectedItemCount,
                "普通 Item 未以同一实例和生命状态经过阶段 " .. tostring(stage.stage) .. ": " .. label)
            local map = {}
            for _, item in ipairs(stage.items) do
                assert(item.guid ~= 0 and item.instanceId ~= 0 and item.runtimeRegistered and
                    item.sameRuntimeInstance and item.itemWorldKey == stage.worldKey and not map[item.guid],
                    "普通 Item 账本身份或世界归属错误：" .. label)
                if item.health ~= nil then sawLandedHealth = true end
                map[item.guid] = item.instanceId
            end
            if stage.stage == "surface-before-launch" then initial = map end
            if stage.stage == "ship-after-launch" then sawShip = true end
            if stage.stage == "space-flight" then sawFlight = true end
            if stage.stage == "surface-after-landing" then last = map end
        end
        assert(initial and last and sawShip and sawFlight and sawLandedHealth,
            "普通 Item 起飞、飞行、落地或生命采样缺失：" .. label)
        for guid, instance in pairs(initial) do
            assert(last[guid] == instance and landed[guid] and landed[guid].itemInstanceId == instance,
                "普通 Item 落地时更换实例或 Guid：" .. label)
        end
    end
    for _, ship in ipairs(data.ships) do
        local label = ship.templateId
        local hasInfinitePropellant = verify_infinite_propellant_sources({ship.filled, ship.lifting, ship.cruising,
            ship.descending, ship.preparation.latestBeforeCommit, ship.landedAgain}, label)
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
        if not hasInfinitePropellant then
            assert(ship.cruising.hydrogen < ship.filled.hydrogen and ship.cruising.oxygen < ship.filled.oxygen,
                "有限燃料起飞没有消耗真实氢氧：" .. label)
        end
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
        local beforeVisible, afterVisible = verify_visible_height_hud_layout(hudBefore), verify_visible_height_hud_layout(hudAfter)
        assert(beforeVisible or afterVisible,
            "真实起降没有采到高度尺与状态条布局")
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
        verify_ordinary_item_migration(ship.ordinaryItemStages, restored, label)
        ground_view(ship.viewCache.surface, label .. "/GM 创建")
        whole_view(ship.viewCache.space, ship.landed.pieceCount, label .. "/太空")
        whole_view(ship.viewCache.descending, ship.landed.pieceCount, label .. "/下降")
        ground_view(ship.viewCache.returned, label .. "/落地")
        local fuel = ship.autoRefuel
        local function contains_inventory(snapshot, guid)
            for _, inventory in ipairs(snapshot.inventories) do
                if inventory.guid == guid then return inventory end
            end
            return nil
        end
        assert(fuel.fixtureSetup == "landed-independent-finite-tanks-no-passive-transfer" and not fuel.targetDirectRefill and
            fuel.world == ship.landedAgain.world and fuel.sourceScope == fuel.world and fuel.pipeScope == fuel.world and
            fuel.outletScope == fuel.world and fuel.targetScope == fuel.world and
            contains_inventory(fuel.targetsBefore, fuel.targetGuid) and contains_inventory(fuel.targetsAfter, fuel.targetGuid) and
            (fuel.targetIsFixture or restored[fuel.targetGuid]),
            "落地加油没有使用独立有限接收罐、普通地面管路或原有限罐：" .. label)
        assert(fuel.sourceGuid ~= fuel.targetGuid and fuel.pipeGuid ~= fuel.outletGuid and
            (fuel.pipeMask == 5 or fuel.pipeMask == 10) and fuel.simulationSeconds > 0,
            "独立罐连接管路没有正式连通，或未等待真实世界固定步：" .. label)
        assert(fuel.targetIsGasTank and fuel.targetIsFiniteWritableTank and
            contains_inventory(fuel.targetsBefore, fuel.targetGuid).infiniteGasSource == false and
            fuel.sourceBefore.hydrogen > 0 and fuel.sourceBefore.oxygen > 0,
            "独立罐边界检查缺少有限可写接收罐或正量氢氧来源：" .. label)
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

test.space("ore_asteroid_landing", function(data)
    assert(data.scope == "live-ore-asteroid-landing-mining" and #data.ships == 1,
        "大型矿石小行星专项没有复用正式起飞测试船")
    local ship = data.ships[1]
    assert(not ship.generation.showroomOverride and ship.generation.ecologyMultiplier > 0 and ship.generation.ironMineRule,
        "小行星地表被陈列厅无生态覆盖污染，或没有读取正式矿山生成规则")
    assert(ship.lifting.phase == "Lifting" and ship.cruising.phase == "Cruising" and ship.cruising.spaceView,
        "小行星测试船没有经过正式普通建筑点火和太空迁移")
    assert(ship.descending.phase == "SurfaceDescending" and not ship.descending.spaceView and
        ship.world == ship.expectedWorld and ship.landed.world == ship.expectedWorld and
        ship.shipRemoved and ship.flightRemoved and ship.landed.pieceCount > 0,
        "大型矿石小行星降落目标错误，或船体没有移交普通地表建筑")
    local beforeHud, landedHud = ship.descending.flightHud, ship.landed.flightHud
    local beforeVisible, afterVisible = verify_visible_height_hud_layout(beforeHud), verify_visible_height_hud_layout(landedHud)
    assert(beforeVisible or afterVisible,
        "小行星起降没有采到高度尺与状态条布局")
    assert(beforeHud.found and beforeHud.phase == "SurfaceDescending" and landedHud.found and
        not landedHud.driving and landedHud.shipId == "", "小行星落地移交后仍依赖活动船记录")
    assert(landedHud.receipt.valid and landedHud.receipt.worldKey == ship.expectedWorld and
        landedHud.receipt.shipId == ship.lifting.id and not landedHud.receipt.water and
        landedHud.receipt.eventId ~= "" and landedHud.receipt.eventId ~= beforeHud.receipt.eventId and
        landedHud.receipt.time >= beforeHud.simulationSeconds, "小行星没有留下本船本世界的新岩石地面落地凭证")
    assert(ship.mineFound and ship.mineId == "Mine_Iron" and ship.mineHealth > 0 and ship.miningApplied > 0 and
        ship.mineRemoved and ship.oreInventoryGain > 0,
        "小行星地表没有可开采的真实矿山，或正式矿石没有掉落入包")
    assert(ship.generatedMine.guid == ship.mineGuid and ship.generatedMine.scannedChunks > 0 and
        (ship.minedDropId ~= 0 or ship.automaticOrePickup),
        "采矿目标没有来自正式生成记录，或实际实体与生成身份不一致")
    return "普通测试船起降到大型小行星、普通建筑移交、正式铁矿受击及实际矿石入包"
end, "大型矿石小行星真实驾驶降落与矿山开采")

test.check("world_ready", "大型矿石小行星专项清理后地表恢复")
test.check("player_ready", "大型矿石小行星专项清理后玩家重绑")
test.runtime("dimension", "restored_control", {}, function(data)
    assert(data.runtimeActive and data.gameplayReady and not data.transitioning,
        "落地建筑所在世界退出再恢复后，仍复用了正在销毁的区块管理器")
end, "落地建筑随世界卸载后恢复有效区块管理器")
