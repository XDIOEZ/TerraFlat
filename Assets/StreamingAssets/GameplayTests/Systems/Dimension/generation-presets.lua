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
    assert(data.marsWorld == "火星" and data.marsBodyBefore == "mars" and data.marsBodyAfter == "mars",
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
    assert(data.marsPresetBefore == "core:surface.mars" and data.marsPresetAfter == data.marsPresetBefore,
        "地下复用地球预设污染了火星地表的生成选择")
    assert(state.world == "火星__dimension__cave" and state.chunkDimension == "cave" and state.bodyId == "mars",
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
    assert(state.timeReference == "火星" and state.lightingFromPortals and state.fixedLighting and
        math.abs(state.lightingCap - .2) < .000001 and state.suppressWeather,
        "地下昼夜、光照或天气配置随地形预设改变")
    assert(state.seed ~= state.sourceSeed and state.independentMaps and state.independentEcology and
        state.independentAtmosphere and not journey.cave.originalRuntime and not journey.cave.originalData,
        "共享预设复用了火星地表的种子、区块、环境库存或运行实例")
    assert(journey.enterAccepted and journey.returnAccepted and journey.exitCreated and journey.exitModulePresent and
        journey.anchorFound and journey.anchorSurface == "火星",
        "地表式地下没有正式配对出口，或出口返回了地球")
    assert(journey.surface.address == "火星" and journey.surface.originalRuntime and journey.surface.originalData and
        journey.surface.windowReady and journey.returnNearEntrance <= 4 and journey.restored,
        "正式出口没有复用火星地表完整窗口并回到原入口附近")
    assert(data.undergroundRetained and data.savedWorld == state.world and data.savedBody == "mars" and
        data.savedPreset == "core:surface.earth" and data.savedNumber == .375 and
        data.savedText == "火星地下地球地形" and data.savedFrozenConfiguration and
        data.savedFrozenProfile == "core:surface.earth" and data.savedMapCount > 0 and
        data.savedMapCount == data.undergroundMapCount,
        "火星地下的正式 MemoryPack 保存丢失独立身份、预设或冻结配置")
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
