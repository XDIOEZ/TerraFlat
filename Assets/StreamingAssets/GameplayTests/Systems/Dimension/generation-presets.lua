test.check("world_ready", "共享预设现场原世界就绪")
test.check("player_ready", "共享预设现场原玩家就绪")
local function verify_incremental_creature_world(data)
    assert(data.created and data.unpublishedWhileYielding and not data.prematureDelivery,
        "跨星球前后分帧出生失败或提前发布了未加载的候选")
    assert(data.sceneValid and data.sceneLoaded and data.sceneMatchesOwner and
        data.physicsSceneValid and data.physicsSceneMatchesOwner,
        "跨星球前后出生物没有进入有效的所属世界物理场景")
end
-- 跨星球会真实卸载地表场景，回程后再复用同一个候选准备入口。
test.runtime("ai", "incremental_creature_creation", {}, verify_incremental_creature_world,
    "跨星球前建立候选准备根并核对出生归属")
test.runtime("dimension", "generation_preset_roundtrip", {}, function(data)
    local journey, state = data.journey, data.journey.presetAudit
    assert(data.marsTemplateBody == "mars" and data.marsWorld == "sector:0:0/planet:火星" and
        data.expectedMarsBody == "sector:0:0/body:mars" and data.marsBodyBefore == data.expectedMarsBody and
        data.marsBodyAfter == data.expectedMarsBody,
        "现场没有真正落地火星或改变了星球归属")
    assert(#data.exitSaves == 2 and data.exitSaves[1].world == data.homeWorld and
        data.exitSaves[2].world == data.marsWorld and not data.exitSaves[1].selectedManager and
        not data.exitSaves[2].selectedManager,
        "地表跨星球保存后重新选中了已解绑或其他世界的区块管理器")
    for _, destination in ipairs({data.landing, data.home}) do
        assert(destination.address == destination.expected and destination.scene == destination.address and
            destination.savedScene == destination.address and destination.modelWorldKey == destination.address and
            destination.initialized and destination.windowReady and destination.gameplayReady and destination.runtimeActive,
            "跨星球迁移后地址、玩家、区块管理器或完整窗口没有正确就绪")
    end
    assert(data.sameHomePlayerData and data.home.originalRuntime and data.home.originalData,
        "跨星球回程没有恢复原档案、世界模型和存档数据")
    for _, destination in ipairs({data.beforeLanding, data.landing, journey.cave, journey.surface, data.home}) do
        assert(destination.playerWorld == destination.address and destination.observerPresent and
            not destination.observerInSpace and destination.observerWorld == destination.address and
            destination.observerDistance >= 0 and destination.observerDistance < .01,
            "跨星球或矿洞往返后玩家实体和权威观察者的世界、位置不一致")
    end
    assert(journey.entrance.registered and journey.entrance.world == data.marsWorld and
        journey.entrance.scene == data.marsWorld and journey.exit.registered and
        journey.exit.world == journey.cave.address and journey.exit.scene == journey.cave.address,
        "火星入口或地下出口没有登记到各自的真实世界")
    for _, pair in ipairs({{journey.exitApproach, journey.cave.address},
        {journey.terrainReenterApproach, data.marsWorld}, {journey.terrainReturnApproach, journey.cave.address}}) do
        local approach, world = pair[1], pair[2]
        assert(approach and approach.world == world and approach.portalWorld == world and
            approach.observerWorld == world and not approach.inSpace and approach.reach > 0 and
            approach.actorDistance < approach.reach and approach.observerDistance < approach.reach and
            math.abs(approach.actorDistance - approach.observerDistance) < .01,
            "准备入口交互时玩家或权威观察者没有进入正式交互范围")
    end
    for _, pair in ipairs({{data.landingSavedPassenger, data.landing}, {data.homeSavedPassenger, data.home}}) do
        local saved, destination = pair[1], pair[2]
        assert(saved.present and not saved.inSpace and saved.world == destination.address and
            math.abs(saved.x - destination.logicalX) < .01 and math.abs(saved.y - destination.logicalY) < .01,
            "跨星球迁移后的太空乘员存档没有保存当前地表和位置")
    end
    assert(data.marsPresetBefore == "core:surface.mars" and data.marsPresetAfter == data.marsPresetBefore,
        "地下复用地球预设污染了火星地表的生成选择")
    assert(state.world == data.marsWorld .. "__dimension__cave" and state.chunkDimension == "cave" and
        state.bodyId == data.expectedMarsBody,
        "选择地球地形改变了地下世界、区块身份或火星归属")
    assert(state.presetId == "core:surface.earth" and state.profileId == "core:surface.earth" and
        journey.cave.generationMode == "Surface" and state.terrainMode == "Surface" and not state.caveLayerPresent,
        "火星地下仍被所属星球或 cave 名称强制生成为矿洞")
    assert(state.profileTemperature == state.earthTemperature and state.groundTileId == state.earthGroundTileId,
        "正式地形快照没有复用现有地球预设的温度和底材")
    assert(state.overrideNumber == .375 and state.overrideText == "火星地下地球地形" and not state.sourceHasOverride,
        "本世界参数覆盖没有生效或污染了火星地表")
    assert(state.atmosphereProfileId == "core:surface.mars" and
        math.abs(state.pressureKPa - state.sourcePressureKPa) < .000001 and state.pressureKPa < 1,
        "选择地球地形把地下环境大气变成了地球大气")
    assert(state.timeReference == data.marsWorld and state.lightingFromPortals and state.fixedLighting and
        math.abs(state.lightingCap - .2) < .000001 and state.suppressWeather,
        "地下昼夜、光照或天气配置随地形预设改变")
    assert(state.seed ~= state.sourceSeed and state.independentMaps and state.independentEcology and
        state.independentAtmosphere and not journey.cave.originalRuntime and not journey.cave.originalData,
        "共享预设复用了火星地表的种子、区块、环境库存或运行实例")
    assert(journey.enterAccepted and journey.returnAccepted and journey.exitCreated and journey.exitModulePresent and
        journey.anchorFound and journey.anchorSurface == data.marsWorld,
        "地表式地下没有正式配对出口，或出口返回了地球")
    assert(journey.surface.address == data.marsWorld and journey.surface.originalRuntime and journey.surface.originalData and
        journey.surface.windowReady and journey.returnNearEntrance <= 4 and journey.restored,
        "正式出口没有复用火星地表完整窗口并回到原入口附近")
    assert(data.undergroundRetained and data.savedWorld == state.world and data.savedBody == data.expectedMarsBody and
        data.savedPreset == "core:surface.earth" and data.savedNumber == .375 and
        data.savedText == "火星地下地球地形" and data.savedFrozenConfiguration and
        data.savedFrozenProfile == "core:surface.earth",
        "火星地下的正式 MemoryPack 保存丢失独立身份、预设或冻结配置")
    local terrain, revisited, cleaned = journey.terrainSnapshot, journey.terrainAfterReturn, journey.terrainAfterCleanup
    assert(terrain.world == state.world and terrain.bodyId == data.expectedMarsBody and
        terrain.presetId == "core:surface.earth" and terrain.frozenProfile == "core:surface.earth" and
        terrain.playerWorld == state.world and terrain.managerWorld == state.world and
        terrain.modelWorld == state.world and terrain.chunkDimension == "cave" and
        terrain.chunkCount > 0 and terrain.chunkWorld == state.world and terrain.expectedCost > 0 and
        ((terrain.blockId == "Tile_WaterPlatform" and terrain.expectedTile == 13) or
         (terrain.blockId == "Tile_Floor_Wood" and terrain.expectedTile == 23)) and
        terrain.savedTile == terrain.expectedTile and terrain.savedCost == terrain.expectedCost,
        "正式完整存档未恢复火星地下世界和实际铺设的区块地板差量")
    assert(journey.terrainReenterAccepted and journey.terrainReturnAccepted and revisited.world == state.world and
        revisited.chunkDimension == "cave" and revisited.tile == terrain.expectedTile and
        revisited.cost == terrain.expectedCost and revisited.underlyingGround == terrain.underlyingGround and
        cleaned.world == state.world and cleaned.tile == 0 and cleaned.underlyingGround == terrain.underlyingGround,
        "地下地板在出洞、再次进入或清理后没有保留应有的地形状态")
    local override = state.frozenOverrides
    assert(override.frozenAfterFirst and override.firstPreset == "core:surface.earth" and
        override.changedPreset == override.firstPreset and override.firstSeaLevel == .31 and
        override.changedSeaLevel == .43 and override.firstMode == "Surface" and override.changedMode == "Cave",
        "相同预设冻结后实例参数变更被旧快照回盖，或旧快照随字典一起变化")
    assert(override.firstTerrain.terrainMode == "Surface" and not override.firstTerrain.caveLayer and
        override.changedTerrain.terrainMode == "Cave" and override.changedTerrain.caveLayer and override.sharedPresetUnchanged,
        "terrain.mode覆盖只改了标签，实际地形没有切换或污染了共享预设")
    assert(data.restoredHome, "跨星球专项没有恢复原测试世界")
end, "火星地下直接引用地球地表预设、真实出口回火星与独立保存")
test.check("world_ready", "共享预设专项恢复后世界就绪")
test.check("player_ready", "共享预设专项恢复后宿主玩家就绪")
test.runtime("ai", "incremental_creature_creation", {}, verify_incremental_creature_world,
    "跨星球回程后分帧出生仍进入有效物理场景")
