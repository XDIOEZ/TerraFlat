-- 同一主题集中编排，子检查保留独立标签和原始采样。

do
    -- 检查真实会话时钟和正式快照入口，不手动推进当前宇宙。
    test.space("session", function(data)
        assert(data.scope == "live-session", "没有采样真实太空会话")
        assert(data.before.version == 3 and data.after.version == 3, "会话版本不正确")
        assert(data.after.seconds > data.before.seconds, "真实固定步没有推进宇宙时钟")
        assert(data.after.playerProfile == data.before.playerProfile, "观察期间测试玩家改变")
        assert(data.savedVersion == data.after.version, "正式捕获的快照版本不一致")
        assert(data.savedTime >= data.before.seconds and data.savedTime <= data.after.seconds, "快照时钟不属于本次采样")
        assert(data.savedSectorCount == data.after.sectorCount and data.savedSectorCount > 0, "快照遗漏星区")
        local camera = data.cameraOverride
        local function near(actual, expected, label)
            assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) < .0001, label)
        end
        local function brain_restored(sample, label)
            assert(sample.updateMethod == "SmartUpdate" and sample.blendUpdateMethod == "FixedUpdate",
                label .. "没有恢复借用前的 Brain 更新方式")
        end
        local function player_settings(sample, label)
            assert(sample.followsExpected, label .. "跟随目标错误")
            brain_restored(sample, label)
            near(sample.xDamping, .12, label .. "没有恢复基础 X 阻尼")
            near(sample.yDamping, .24, label .. "没有恢复基础 Y 阻尼")
            near(sample.lookahead, .9, label .. "没有应用驾驶中更新的前探与缩放偏好")
            near(sample.smoothing, 3.4, label .. "没有恢复驾驶中更新的平滑偏好")
        end
        assert(type(camera) == "table" and camera.scope == "isolated-camera-components", "缺少独立正式镜头租约采样")
        assert(not camera.baseline.hasOverride and camera.baseline.followsExpected, "镜头夹具基线已有外部租约")
        brain_restored(camera.baseline, "基线")
        near(camera.baseline.xDamping, .52, "基线负前探没有增加 X 惯性")
        near(camera.baseline.yDamping, .64, "基线负前探没有增加 Y 惯性")
        near(camera.baseline.lookahead, 0, "负前探仍启用预测")
        near(camera.baseline.smoothing, 2, "基线偏好未应用")
        for _, key in ipairs({"locked", "repeated", "changedSettings", "zoomed", "wrongRelease", "relocked", "staleRelease"}) do
            local sample = camera[key]
            assert(sample.hasOverride and sample.followsExpected, key .. "丢失当前驾驶目标")
            assert(sample.updateMethod == "LateUpdate" and sample.blendUpdateMethod == "LateUpdate",
                key .. "没有使用 LateUpdate 更新镜头")
            near(sample.xDamping, 0, key .. "重新叠加了 X 阻尼")
            near(sample.yDamping, 0, key .. "重新叠加了 Y 阻尼")
            near(sample.lookahead, 0, key .. "重新叠加了镜头前探")
            near(sample.smoothing, 0, key .. "重新叠加了预测平滑")
            near(sample.maximumSize, 120000, key .. "丢失驾驶视野上限")
            assert(sample.proportionalZoom, key .. "丢失驾驶比例缩放")
        end
        near(camera.locked.size, 30, "进入驾驶时正交尺寸错误")
        near(camera.changedSettings.userLookahead, .6, "驾驶模式改写了玩家前探偏好")
        near(camera.changedSettings.userSmoothing, 3.4, "驾驶模式改写了玩家平滑偏好")
        near(camera.changedSettings.userZoomInfluence, .5, "驾驶模式改写了玩家缩放偏好")
        near(camera.zoomed.size, 153.6, "锁定驾驶镜头未保留正式比例缩放")
        assert(camera.playerMode.hasOverride, "同目标切回玩家设置不应释放跟随目标")
        player_settings(camera.playerMode, "同目标切回玩家设置")
        for _, key in ipairs({"released", "repeatedRelease"}) do
            local sample = camera[key]
            assert(not sample.hasOverride and not sample.proportionalZoom, key .. "退出后残留驾驶租约")
            player_settings(sample, key)
            near(sample.size, 40, key .. "退出后没有恢复普通视野范围")
            near(sample.maximumSize, 40, key .. "退出后残留太空视野上限")
        end
        assert(type(camera.limitations) == "string" and #camera.limitations > 0, "镜头采样必须说明覆盖边界")
        local passenger = data.passengerPose
        assert(passenger.scope == "production-components" and #passenger.samples == 10, "缺少正式乘员整体位姿采样")
        local expected = {ground = 0, lift = 4, ["repeat"] = 4, descending = 1, landed = 0,
            ["lift-again"] = 4, clear = 0, ["repeat-clear"] = 0, ["late-collider"] = 4, disabled = 0}
        local seen = {}
        for _, sample in ipairs(passenger.samples) do
            local rise = expected[sample.name]
            assert(rise ~= nil and not seen[sample.name], "未知或重复乘员位姿：" .. sample.name)
            seen[sample.name] = true
            near(sample.rootX, 10, "升高不能改变玩家 X")
            near(sample.rootY, 20 + rise, "玩家根节点没有整体升高或重复累加")
            near(sample.bodyY, sample.rootY, "刚体没有跟随玩家根节点")
            near(sample.groundY, 20, "高度混入了地面权威坐标")
            near(sample.handY, 22 + rise, "手持物没有整体跟随")
            near(sample.handLocalY, 2, "升高改坏了手持物局部位置")
            assert(sample.solidTrigger == (rise > 0) and sample.childTrigger == (rise > 0),
                "实体阻挡没有在离地时取消、落地或释放后恢复")
            assert(sample.sensorTrigger and sample.solidEnabled, "原有 Trigger 或碰撞查询被关闭")
            assert(not sample.independentTrigger, "错误改动了独立刚体的碰撞")
            assert(sample.lateTrigger == (sample.name == "late-collider"), "动态新增碰撞体没有接管或恢复")
        end
        assert(type(passenger.limitations) == "string" and #passenger.limitations > 0, "乘员采样缺少覆盖边界")
        return "真实固定步、会话快照、镜头租约，以及乘员整体抬升、重复同步、下降和碰撞恢复已检查"
    end, "真实宇宙会话与固定步时钟")
end

do
    -- 遍历全部正式船型及展开部件，不用固定船型数量判断通过。
    test.space("ship_templates", function(data)
        assert(data.scope == "live-content" and #data.templates > 0, "没有正式飞船模板")
        local ids, count = {}, 0
        for _, ship in ipairs(data.templates) do
            assert(not ids[ship.id], "重复船型：" .. ship.id)
            ids[ship.id] = true
            assert(ship.width > 0 and ship.height > 0 and #ship.members > 0, "空船型：" .. ship.id)
            local members, floor, console, engine = {}, 0, 0, 0
            for _, part in ipairs(ship.members) do
                local label = ship.id .. "/" .. part.id
                assert(not members[part.id], "重复部件：" .. label)
                members[part.id] = true
                assert(part.registered and part.kind ~= nil, "模板引用未注册船体物品：" .. label)
                assert(part.x >= 0 and part.x < ship.width and part.y >= 0 and part.y < ship.height, "模板坐标越界：" .. label)
                assert(part.quarterTurns >= 0 and part.quarterTurns <= 3, "模板朝向错误：" .. label)
                if part.kind == "Floor" then floor = floor + 1 end
                if part.kind == "Console" then console = console + 1 end
                if part.kind == "Engine" then engine = engine + 1 end
                count = count + 1
            end
            assert(floor > 0 and console > 0 and engine > 0, "测试船型缺少地板、控制台或引擎：" .. ship.id)
        end
        return "遍历 " .. #data.templates .. " 个正式船型、" .. count .. " 个部件"
    end, "全部飞船模板与资源引用")
end

do
    -- 实际进入每个配置星区，检查全部星体的数据登记与场景表现。
    test.space("space_visit", function(data)
        assert(data.scope == "live-presentation" and #data.visits > 0, "没有进入配置星区")
        local ids, sectors, count, uiRoot = {}, {}, 0, nil
        for _, visit in ipairs(data.visits) do
            local key = visit.sector.x .. ":" .. visit.sector.y
            assert(not sectors[key], "重复访问星区：" .. key)
            sectors[key] = true
            assert(visit.state.spaceView and not visit.state.dimensionTransitioning, "星区入场未完成：" .. key)
            assert(visit.state.sector.x == visit.sector.x and visit.state.sector.y == visit.sector.y, "玩家归属星区错误：" .. key)
            assert(visit.interaction.senderReady and visit.interaction.missingTargetRejected and
                visit.state.playerWorld == visit.state.playerScene and
                visit.interaction.worldBefore == visit.interaction.worldAfter,
                "太空交互没有使用玩家所属星区或调用后没有恢复世界上下文：" .. key)
            assert(visit.state.uiRootReady and visit.state.uiRootPersistent, "UI 根没有归属持久管理器：" .. key)
            assert(uiRoot == nil or uiRoot == visit.state.uiRootInstance, "切换配置星区销毁了 UI 根：" .. key)
            uiRoot = visit.state.uiRootInstance
            assert(visit.state.timeReady and visit.state.timeSource == "SpaceScene" and visit.state.lightingRateReady,
                "星区缺少统一时间或采光登记：" .. key)
            assert(math.abs(visit.state.lighting - visit.state.clockLighting) < 0.001,
                "星区光照没有继承太空时钟：" .. key)
            assert(visit.state.globalLighting ~= nil and math.abs(visit.state.globalLighting - visit.state.lighting) < 0.001,
                "实际全局光没有跟随星区采光：" .. key)
            assert(#visit.bodies > 0, "配置星区没有任何星体：" .. key)
            for _, body in ipairs(visit.bodies) do
                assert(not ids[body.id], "星体实例身份重复：" .. body.id)
                ids[body.id] = true
                assert(body.planetRegistered and body.registeredBodyId == body.id, "星球数据登记缺失：" .. body.id)
                assert(body.viewReady and body.viewBodyId == body.id, "真实星体场景对象缺失：" .. body.id)
                local dx, dy = body.projected.x - body.viewPosition.x, body.projected.y - body.viewPosition.y
                assert(math.sqrt(dx * dx + dy * dy) < 2, "星体表现没有跟随权威位置：" .. body.id)
                count = count + 1
            end
        end
        return "实际访问 " .. #data.visits .. " 个配置星区、" .. count .. " 个真实星体表现"
    end, "所有配置星区与星体场景表现")
end

do
    -- 每个正式船型走真实生成、供油供电、起飞、惯性导航和降落入口。
    test.space("ship_flight", function(data)
        assert(data.scope == "live-ship-flight" and #data.ships > 0, "没有完成任何真实船型流程")
        local function magnitude(v) return math.sqrt(v.x * v.x + v.y * v.y) end
        local function difference(a, b) return magnitude({x = a.x - b.x, y = a.y - b.y}) end
        local function ground_members(snapshot, label)
            assert(snapshot.phase == "GroundBuildings" and not snapshot.spaceView, "没有恢复普通地面建筑：" .. label)
            local ids, count = {}, 0
            for _, building in ipairs(snapshot.buildings) do
                assert(not ids[building.id] and building.guid == tonumber(building.id) and
                    building.snapshotGuid == building.guid, "普通建筑 Guid 重复或快照串号：" .. label)
                assert(building.installed and building.scope == snapshot.world, "落地建筑没有接入普通世界作用域：" .. label)
                assert(building.quarterTurns >= 0 and building.quarterTurns <= 3 and
                    building.cell.x % 1 == 0 and building.cell.y % 1 == 0, "普通建筑没有对齐格子或朝向：" .. label)
                ids[building.id], count = building, count + 1
            end
            assert(count == snapshot.pieceCount and count > 0, "普通地面成员采样不完整：" .. label)
            return ids
        end
        local function same_ground_members(before, after, label)
            local expected, actual = ground_members(before, label .. "/之前"), ground_members(after, label .. "/之后")
            assert(before.pieceCount == after.pieceCount, "起降或补给丢失普通建筑：" .. label)
            for id, building in pairs(expected) do
                assert(actual[id] and actual[id].itemId == building.itemId and actual[id].machine == building.machine,
                    "起降没有保留部件身份或普通建筑类型：" .. label .. "/" .. id)
            end
            return expected
        end
        for _, ship in ipairs(data.ships) do
            local label = ship.templateId
            assert(ship.enteredConsole, "进入控制台失败：" .. label)
            local groundIds = same_ground_members(ship.landed, ship.filled, label .. "/地面补给")
            assert(ship.landed.activeShipCount == ship.beforeBuild.activeShipCount and
                ship.landed.totalBuildings >= ship.beforeBuild.totalBuildings + ship.landed.pieceCount,
                "GM 生成提前创建了整船，或没有逐件创建普通建筑：" .. label)
            assert(ship.filled.hydrogen > 0 and ship.filled.oxygen > 0, "真实燃料罐为空：" .. label)
            assert(ship.lifting.phase == "Lifting", "点火没有进入升空：" .. label)
            assert(ship.liftingGround.pieceCount == 0 and
                ship.liftingGround.activeShipCount == ship.beforeBuild.activeShipCount + 1,
                "点火装配没有唯一移交普通建筑到活动飞船：" .. label)
            assert(ship.cruising.phase == "Cruising" and ship.cruising.spaceView, "起飞未进入真实太空：" .. label)
            assert(ship.cruising.id == ship.lifting.id and ship.cruising.pieceCount == ship.landed.pieceCount,
                "起飞丢失船体或部件：" .. label)
            local expectedMachines = 0
            for _, building in pairs(groundIds) do
                if building.machine then expectedMachines = expectedMachines + 1 end
            end
            for _, flight in ipairs({ship.lifting, ship.cruising}) do
                assert(#flight.pieces == ship.landed.pieceCount, "装配或迁移丢失部件：" .. label)
                local seenPieces, seenMachines = {}, {}
                for _, piece in ipairs(flight.pieces) do
                    assert(not seenPieces[piece.id] and groundIds[piece.id] and groundIds[piece.id].itemId == piece.itemId and piece.alive,
                        "装配没有沿用原普通建筑 Guid 或定义：" .. label)
                    seenPieces[piece.id] = true
                end
                assert(#flight.machines == expectedMachines, "装配或迁移丢失普通机器节点：" .. label)
                for _, node in ipairs(flight.machines) do
                    local id = string.format("%.0f", node.guid)
                    assert(not seenMachines[node.guid] and groundIds[id] and groundIds[id].machine and
                        node.itemId == groundIds[id].itemId and
                        node.scope == "ship:" .. flight.id, "机器没有移交至本船作用域：" .. label)
                    seenMachines[node.guid] = true
                end
            end
            assert(ship.thrust.engineOutput > 0, "真实推进没有引擎出力：" .. label)
            assert(ship.thrust.hydrogen < ship.filled.hydrogen and ship.thrust.oxygen < ship.filled.oxygen, "飞行未消耗真实氢氧：" .. label)
            assert(difference(ship.thrust.velocity, ship.cruising.velocity) > 0.01, "推进没有改变速度：" .. label)
            local coast = ship.coast
            assert(coast.fixtureSetup == "owned-ship-empty-sector", "惯性检查没有隔离星体引力与大气阻力：" .. label)
            assert(coast.before.id == ship.thrust.id and coast.after.id == ship.thrust.id and
                coast.before.phase == "Cruising" and coast.after.phase == "Cruising", "惯性夹具改变了船体身份或飞行状态：" .. label)
            assert(coast.before.sector.x == coast.sector.x and coast.before.sector.y == coast.sector.y and
                coast.after.sector.x == coast.sector.x and coast.after.sector.y == coast.sector.y, "惯性采样离开空星区：" .. label)
            assert(coast.simulationSeconds >= 0.5 - 0.000001 and magnitude(coast.before.velocity) > 0.01,
                "惯性检查没有带速度推进足够的真实固定步：" .. label)
            assert(difference(coast.before.velocity, ship.thrust.velocity) < 0.05 and
                difference(coast.after.velocity, coast.before.velocity) < 0.05, "空域松开控制后惯性速度丢失：" .. label)
            local expected = {x = coast.before.position.x + coast.before.velocity.x * coast.simulationSeconds,
                y = coast.before.position.y + coast.before.velocity.y * coast.simulationSeconds}
            assert(difference(coast.after.position, expected) < 0.05, "空域松键后船体没有按惯性移动：" .. label)
            assert(coast.before.engineOutput == 0 and coast.after.engineOutput == 0, "松键后引擎仍在出力：" .. label)
            assert(math.abs(coast.after.hydrogen - coast.before.hydrogen) < 0.000001 and
                math.abs(coast.after.oxygen - coast.before.oxygen) < 0.000001, "松键后推进仍在耗油：" .. label)
            for _, snapshot in ipairs({ship.lifting, ship.cruising, ship.turnBefore, ship.turned, ship.reverseTurned,
                ship.navigated, ship.beforeLanding, ship.descending}) do
                assert(math.abs(snapshot.angle) < 0.000001 and math.abs(snapshot.angularVelocity) < 0.000001,
                    "起降、正反转向或导航改变了船体朝上姿态：" .. label)
            end
            for _, snapshot in ipairs({ship.turned, ship.reverseTurned}) do
                assert(snapshot.engineOutput == 0 and math.abs(snapshot.hydrogen - ship.turnBefore.hydrogen) < 0.000001 and
                    math.abs(snapshot.oxygen - ship.turnBefore.oxygen) < 0.000001, "纯转向输入仍触发出力或耗油：" .. label)
            end
            assert(ship.turnPreservedNavigation, "无效转向输入取消了正式导航：" .. label)
            assert(ship.navigationAccepted and ship.navigationDistance <= ship.arrivalDistance, "正式导航未到点：" .. label)
            assert(magnitude(ship.navigated.velocity) <= ship.arrivalSpeed + 0.001, "到点后没有制动：" .. label)
            local crossing = ship.shipTransition
            local function same(a, b) return a.x == b.x and a.y == b.y end
            local function contains(values, expected)
                for _, value in ipairs(values) do if same(value, expected) then return true end end
                return false
            end
            assert(crossing.fixtureSetup == "owned-ship-preposition", "船体过渡缺少明确的测试夹具来源：" .. label)
            assert(crossing.shipId == ship.cruising.id and crossing.phase == "Cruising", "过渡期间船体身份或飞行状态丢失：" .. label)
            assert(crossing.before.supported and crossing.crossed.supported and crossing.settled.supported and
                crossing.before.shipId == crossing.shipId and crossing.crossed.shipId == crossing.shipId and crossing.settled.shipId == crossing.shipId,
                "船体跨区期间乘员失去本船支撑：" .. label)
            for _, state in ipairs({crossing.before, crossing.crossed, crossing.settled}) do
                assert(state.playerWorld == "ship:" .. crossing.shipId and state.playerScene == state.playerWorld,
                    "船体跨区时乘员离开了船内世界：" .. label)
            end
            assert(same(crossing.crossed.sector, crossing.destination) and same(crossing.shipSector, crossing.destination), "乘员或船体没有进入目标星区：" .. label)
            assert(same(crossing.flightSector, crossing.shipSector), "飞行记录与船体星区不一致：" .. label)
            assert(contains(crossing.crossed.required, crossing.source) and contains(crossing.crossed.required, crossing.destination), "船体跨界没有保留双侧租约：" .. label)
            assert(not contains(crossing.settled.required, crossing.source), "船体越过过渡带后没有清退源星区：" .. label)
            assert(crossing.before.playerInstance == crossing.crossed.playerInstance and not crossing.crossed.dimensionTransitioning, "船体跨区重建玩家或触发维度迁移：" .. label)
            assert(crossing.before.cameraReady and crossing.crossed.cameraReady and crossing.settled.cameraReady and
                crossing.before.cameraInstance == crossing.settled.cameraInstance, "船体跨区丢失或重建玩家相机：" .. label)
            assert(ship.beforeLanding.pieceCount == ship.landed.pieceCount,
                "空域导航和跨区提前丢失了船体部件：" .. label)
            assert(ship.landedAgain.pieceCount == ship.beforeLanding.pieceCount and
                ship.landedAgain.landingResolved and ship.landedAgain.height <= 0.001 and
                ship.landedAgain.impactSpeed <= 0.001,
                "受控降落发生真实撞击或丢失部件，需检查载荷、动力与跨测试库存污染：" .. label)
            assert(ship.landingAccepted and ship.landedAgain.phase == "GroundBuildings", "没有完成受控降落及普通建筑移交：" .. label)
            assert(ship.landedAgain.shipRemoved and ship.landedAgain.flightRemoved and not ship.landedAgain.supported and
                not ship.landedAgain.spaceView, "降落后仍有特殊船体、飞行记录或乘员绑定：" .. label)
            assert(ship.landedAgain.world == ship.landed.world and
                ship.landedAgain.activeShipCount == ship.beforeBuild.activeShipCount,
                "降落建筑没有回到原普通世界或残留活动船体：" .. label)
            same_ground_members(ship.landed, ship.landedAgain, label .. "/落地")
            local cache = ship.viewCache
            local function ground_views(snapshot, stage)
                assert(#snapshot.presenters == 0 and #snapshot.itemProjections == 0 and #snapshot.chunkProjections == 0,
                    "普通地面建筑残留整船、物品或区块投影：" .. label .. "/" .. stage)
            end
            ground_views(cache.surface, "GM 生成后")
            ground_views(cache.returned, "落地移交后")
            do
                local snapshot = cache.space
                assert(#snapshot.presenters == 1 and #snapshot.itemProjections > 0 and #snapshot.chunkProjections > 0,
                    "空中船体缺少唯一整体或真实物品/区块投影：" .. label)
                local view = snapshot.presenters[1]
                assert(view.wholeShipBody and view.activeBodies == 1 and view.activeBlockers == 1 and
                    view.active and not view.pooled and view.scene == snapshot.scene,
                    "空中没有使用唯一启用的整船刚体与阻挡：" .. label)
            end
            for _, snapshot in ipairs({cache.surface, cache.space, cache.returned}) do
                assert(snapshot.presenterPoolCount <= snapshot.presenterLimit,
                    "整船碰撞表现缓存超过容量：" .. label)
                for _, projection in ipairs(snapshot.itemProjections) do
                    assert(projection.active and projection.worldKey == "ship:" .. ship.cruising.id and
                        projection.scene == snapshot.scene,
                        "普通 Item 投影脱离船内账本或显示场景：" .. label)
                end
                for _, projection in ipairs(snapshot.chunkProjections) do
                    assert(projection.active and projection.id:sub(1, #("ship:" .. ship.cruising.id) + 1) ==
                        "ship:" .. ship.cruising.id .. ":" and projection.scene == snapshot.scene,
                        "普通区块投影脱离船内世界或显示场景：" .. label)
                end
            end
            local refuel = ship.autoRefuel
            assert(refuel.fixtureSetup == "landed-independent-tanks-no-passive-transfer" and not refuel.targetDirectRefill,
                "独立罐无被动输送没有使用正式普通管道、定向出气口和独立源罐：" .. label)
            assert(refuel.world == ship.landedAgain.world and refuel.sourceScope == refuel.world and
                refuel.pipeScope == refuel.world and refuel.outletScope == refuel.world and refuel.targetScope == refuel.world,
                "落地加油仍依赖特殊船体作用域：" .. label)
            assert(refuel.pipeMask == 5 or refuel.pipeMask == 10, "普通加油管道没有两端连通：" .. label)
            assert(refuel.simulationSeconds > 0, "独立罐无被动输送没有等待真实固定步：" .. label)
            for _, fluid in ipairs({"hydrogen", "oxygen"}) do
                assert(math.abs(refuel.targetsAfter[fluid]-refuel.targetsBefore[fluid])<0.000001 and
                    math.abs(refuel.sourceAfter[fluid]-refuel.sourceBefore[fluid])<0.000001,
                    "落地燃料罐没有从普通源罐接收真实" .. fluid .. "：" .. label)
                local before, after = refuel.totalBefore[fluid], refuel.totalAfter[fluid]
                assert(math.abs(after - before) <= 0.0000001 * math.max(1, before),
                    "普通地面独立罐无被动输送没有守恒真实" .. fluid .. "：" .. label)
            end
        end
        return "普通建筑生成、点火装配、真实航行、落地还原及普通管道独立罐无被动输送：" .. #data.ships .. " 个船型"
    end, "所有船型的真实起飞航行降落")
end

for _, value in ipairs({"right"}) do
    local direction = value
    -- 以正式太空入场准备速度，随后由真实固定步越界、释放租约并折返。
    test.space("sector_transition", function(data)
        local function same(a, b) return a.x == b.x and a.y == b.y end
        local function contains(values, expected)
            for _, value in ipairs(values) do if same(value, expected) then return true end end
            return false
        end
        assert(data.scope == "live-sector-transition" and data.direction == direction, "没有采样指定方向的真实过渡")
        assert(same(data.before.sector, data.source), "初始玩家不在源星区")
        assert(contains(data.before.required, data.source) and contains(data.before.required, data.destination), "行进方向星区没有提前加载")
        assert(same(data.crossed.sector, data.destination), "真实移动未越过目标边界")
        assert(contains(data.crossed.required, data.source) and contains(data.crossed.required, data.destination), "过渡带没有保留两侧租约")
        assert(contains(data.crossed.loadedScenes, data.source) and contains(data.crossed.loadedScenes, data.destination), "过渡两侧实际场景缺失")
        assert(same(data.settled.sector, data.destination) and not contains(data.settled.required, data.source), "离开过渡带后源星区没有释放")
        assert(same(data.returned.sector, data.source), "玩家无法自然折返")
        assert(contains(data.returned.required, data.destination), "折返时没有保留目标侧过渡场景")
        assert(data.before.playerInstance == data.crossed.playerInstance and data.crossed.playerInstance == data.returned.playerInstance, "普通星区越界重建了玩家")
        assert(data.before.uiRootReady and data.crossed.uiRootReady and data.settled.uiRootReady and data.returned.uiRootReady and
            data.before.uiRootPersistent and data.crossed.uiRootPersistent and data.settled.uiRootPersistent and data.returned.uiRootPersistent and
            data.before.uiRootInstance == data.crossed.uiRootInstance and data.crossed.uiRootInstance == data.settled.uiRootInstance and
            data.settled.uiRootInstance == data.returned.uiRootInstance, "普通星区越界销毁或重建了 UI 根")
        assert(data.before.cameraReady and data.crossed.cameraReady and data.settled.cameraReady and data.returned.cameraReady and
            data.before.cameraInstance == data.crossed.cameraInstance and data.crossed.cameraInstance == data.returned.cameraInstance,
            "普通星区越界丢失或重建玩家相机")
        assert(data.before.playerProfile == data.returned.playerProfile, "过渡后玩家身份丢失")
        for _, state in ipairs({data.before, data.crossed, data.settled, data.returned}) do
            assert(state.timeReady and state.timeSource == "SpaceScene" and state.lightingRateReady,
                "跨区丢失统一时间或采光登记：" .. direction)
            assert(math.abs(state.lighting - state.clockLighting) < 0.001, "跨区光照改变了时钟来源：" .. direction)
        end
        for _, state in ipairs({data.before, data.crossed, data.settled, data.returned}) do
            assert(state.observerReady and state.observerInSpace and state.observerWorldKey == state.scene and
                state.observerRealmRevision == state.realmRevision, "跨区网络观察者没有同步当前星区和版本")
            assert(state.playerWorld == state.scene and state.playerScene == state.playerWorld,
                "自由玩家跨区后物品或物理归属没有跟随星区")
        end
        local nextRevision = function(value) return (value + 1) % 4294967296 end
        assert(data.crossed.realmRevision == nextRevision(data.before.realmRevision) and
            data.settled.realmRevision == data.crossed.realmRevision and
            data.returned.realmRevision == nextRevision(data.crossed.realmRevision),
            "星区跨界、保持和折返没有正确更新世界归属版本")
        assert(not data.crossed.dimensionTransitioning and not data.returned.dimensionTransitioning, "普通星区跨界触发了维度加载")
        assert(data.returned.seconds > data.before.seconds, "过渡期间宇宙时钟没有继续推进")
        return direction .. "：真实越界、双侧场景、租约清退、玩家连续性与折返"
    end, "真实星区 " .. direction .. " 过渡", direction)
end

do
    -- 用正式地板事务构造自有夹具，等待真实固定步计算跨星区船体接触。
    test.space("ship_collision", function(data)
        assert(data.scope == "live-fixed-step-contacts" and data.fixtureSetup == "production-floor-transactions", "没有真实固定步接触结果")
        assert(data.afterTime > data.beforeTime, "真实宇宙会话没有推进")
        assert(data.afterTime - data.beforeTime >= 3 - 0.000001, "船体接触采样前没有完成三秒真实模拟")
        local left, right = data.afterLeft, data.afterRight
        for _, ship in ipairs({left, right, data.afterRemote}) do
            assert(math.abs(ship.angle) < 0.000001 and math.abs(ship.angularVelocity) < 0.000001,
                "原生碰撞后船体没有保持朝上")
        end
        assert(left.id == data.beforeLeft.id and right.id == data.beforeRight.id, "接触改变了船体身份")
        assert(left.mass > 0 and right.mass > 0, "接触夹具没有真实质量")
        local beforeMomentum = data.beforeLeft.mass * data.beforeLeft.velocity.x + data.beforeRight.mass * data.beforeRight.velocity.x
        local afterMomentum = left.mass * left.velocity.x + right.mass * right.velocity.x
        assert(math.abs(afterMomentum - beforeMomentum) <= math.max(0.001, (left.mass + right.mass) * 0.0001), "跨区船体接触没有保持总动量")
        assert(math.abs(left.velocity.x - data.beforeLeft.velocity.x) > 0.1 and math.abs(right.velocity.x - data.beforeRight.velocity.x) > 0.1, "相向跨区船体互相穿过，未结算冲量")
        assert(math.abs(data.afterRemote.velocity.x - data.beforeRemote.velocity.x) < 0.000001 and math.abs(data.afterRemote.velocity.y) < 0.000001, "遥远星区的夹具受到错误接触")
        assert(data.afterRemote.id == data.beforeRemote.id and data.afterRemote.mass == data.beforeRemote.mass, "远区船体身份或质量被改变")
        return "真实固定步：跨星区船体相撞、冲量守恒、远区排除与自有夹具回收"
    end, "真实船体跨区接触与远区排除")
end

test.space("ore_asteroid_drops", function(data)
    assert(data.scope == "live-authoritative-asteroid-drops-and-player-pickup", "小行星现场采样来源错误")
    assert(data.partialApplied > 0 and data.afterPartial > 0 and data.afterPartial < data.initialHealth and
        data.beforeDropCount == data.afterPartialDrops, "非致命攻击没有只扣血，或提前掉矿")
    assert(data.lethalApplied == data.afterPartial and data.health == 0 and data.depleted,
        "正式小行星击碎没有提交耗尽状态")
    local amounts, ids, dropsById, dropItems = {}, {}, {}, {}
    for _, drop in ipairs(data.drops) do
        assert(not ids[drop.guid] and drop.guid ~= 0 and drop.amount > 0 and drop.pickupAllowed and
            drop.inSpace and not drop.earlyPickup, "真实冷载荷缺失独立身份、数量或拾取冷却")
        assert(not dropsById[drop.id] and not dropItems[drop.itemId], "小行星掉落身份或种类重复，无法核对各堆库存")
        ids[drop.guid] = true
        dropsById[drop.id], dropItems[drop.itemId] = drop, true
        amounts[drop.itemId] = (amounts[drop.itemId] or 0) + drop.amount
    end
    assert(amounts[data.compositionItem] == data.oreAmount and (amounts.Ore_Stone or 0) == data.stoneAmount,
        "冷载荷中真实矿石或石头数量与小行星库存不一致")
    assert(data.repeatedApplied == 0 and data.beforeRepeatedDrops == data.afterRepeatedDrops and data.savedDepleted and
        data.savedHealth == 0 and data.savedSector and data.generatedCount == data.afterReentry,
        "重复攻击、保存恢复或星区重入重新产矿")
    assert(#data.pickupResults == #data.drops, "没有逐堆记录真实拾取结果")
    local pickupIds = {}
    local function sum_inventory_slots(slots)
        local amount = 0
        for _, slot in ipairs(slots) do
            assert(slot.inventoryIndex >= 0 and slot.slotIndex >= 0 and slot.amount > 0,
                "拾取目标库存或槽位采样无效")
            amount = amount + slot.amount
        end
        return amount
    end
    for _, pickup in ipairs(data.pickupResults) do
        local drop = dropsById[pickup.id]
        assert(drop and not pickupIds[pickup.id] and pickup.itemId == drop.itemId and pickup.removedFromSpace,
            "拾取结果没有对应真实掉落，或掉落仍留在太空")
        pickupIds[pickup.id] = true
        assert(pickup.alreadyPicked ~= pickup.approached, "逐堆接近与提前拾取的记录冲突")
        if pickup.approached then
            assert(pickup.distanceBeforeApproach >= 0 and pickup.distanceAfterApproach >= 0,
                "缺少接近前后真实距离")
            if pickup.presentAtArrival then
                assert(pickup.distanceAfterApproach <= 1, "接近后玩家没有到真实掉落附近")
            end
        end
        assert(pickup.inventoryBeforeDamage == sum_inventory_slots(pickup.inventorySlotsBeforeDamage) and
            pickup.inventoryAfterPickup == sum_inventory_slots(pickup.inventorySlotsAfterPickup) and
            pickup.inventoryGain == drop.amount and
            pickup.inventoryAfterPickup - pickup.inventoryBeforeDamage == drop.amount and
            pickup.inventoryGainDuringApproach == pickup.inventoryAfterPickup - pickup.inventoryBeforeApproach,
            "真实掉落的数量没有完整进入玩家拾取器的具体库存槽位")
    end
    assert(data.allDropsRemoved and data.oreInventoryGain == data.oreAmount and data.stoneInventoryGain == data.stoneAmount,
        "真实掉矿没有完整经玩家拾取器进入实际库存")
    return "正式小行星扣血、击碎、冷载荷解码和真实玩家拾取；武器几何未覆盖"
end, "小型矿石小行星击碎、耗尽保存与实际拾取")

test.check("world_ready", "小行星掉矿专项清理后地表恢复")
test.check("player_ready", "小行星掉矿专项清理后玩家重绑")
