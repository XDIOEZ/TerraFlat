-- 地形与生态共用真实空格，检查地表覆盖差量和真实资源实体的占地生命周期。
test.check("world_ready", "地形生态共用正式隔离世界")
test.check("terrain_ready", "自有地格已有真实数据与表现")
test.runtime("terrain", "cover_resource_lifecycle", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.firstCoverAccepted and data.secondCoverAccepted,
        "正式地表覆盖事务没有成功")
    assert(data.originalCoverCount == 0 and data.oneCoverCount == 1 and data.twoCoverCount == 2,
        "真实地表覆盖没有逐层压栈")
    assert(data.bottomGround == data.expectedBottomGround and
        data.topUnderlyingGround == data.expectedTopUnderlyingGround and
        data.currentGround == data.expectedCurrentGround and data.savedCoverCount == 2,
        "实际地表、下层快照或隔离存档差量与覆盖顺序不一致")
    assert(data.liquidAfter == data.liquidBefore, "覆盖 Ground 错误修改独立 Liquid 层")
    assert(data.entityRegistered and data.entityCaptured and data.entityGuid == data.expectedGuid and
        data.entityId == "Tree_Pine", "正式资源 ECS 入口没有创建和采样真实松树")
    assert(data.visualsWithTree > data.visualsBefore and data.visualsAfterRemoval == data.visualsBefore
        and data.visualsAfterRestore > data.visualsAfterRemoval,
        "松树未实际进入区块行网格，或移除、恢复时表现槽位未同步更新")
    assert(data.walkableBefore and data.blockedCellRead and not data.walkableWithTree,
        "自有松树未登记到实际世界导航占地")
    assert(data.entityRemoved and not data.entityRemainsAfterRemoval and data.walkableAfterRemoval and
        data.removedGuid == data.expectedGuid, "移除真实资源实体没有保留快照或释放实际占地")
    assert(data.entityRestored and data.entityRecaptured and data.restoredGuid == data.expectedGuid and
        data.restoredId == "Tree_Pine", "真实资源快照无法恢复原实体身份")
    local growth = data.growthSnapshots
    for _, key in ipairs({"captured", "removed", "restored"}) do
        local state = growth[key]
        assert(not state.cultivated and state.soilX == 0 and state.soilY == 0,
            "野生松树在" .. key .. "阶段被错误写成耕地植株或污染了种植坐标")
        assert(state.waterInitialized and state.lastWaterWorldTime == growth.captured.lastWaterWorldTime,
            "松树在" .. key .. "阶段丢失或改写了独立耗水时钟")
    end
    assert(data.habitatRegistered == data.habitatBefore + 1 and
        data.habitatRemoved == data.habitatBefore and data.habitatRestored == data.habitatBefore + 1,
        "ECS树木栖息地索引未随创建、移除和恢复同步")
    local indexes = data.inputIndexes
    assert(indexes.registered.environment == indexes.before.environment + 1 and
        indexes.registered.navigation == indexes.before.navigation + 1 and
        indexes.registered.soil == indexes.before.soil, "野生松树没有按能力登记天气、导航与土壤输入")
    for _, field in ipairs({"environment", "navigation", "soil"}) do
        assert(indexes.removed[field] == indexes.before[field] and
            indexes.restored[field] == indexes.registered[field],
            "自然资源" .. field .. "输入索引移除或恢复后不一致")
    end
    assert(data.cleanupRegistered, "地格与资源未登记失败时的收尾")
end, "真实地表覆盖栈、保存差量、松树占地和实体快照恢复")
test.runtime("terrain", "cleanup", {}, function(data)
    assert(data.hadFixture and data.released and data.terrainRestored and data.layersRestored and
        data.grassRestored and data.resourceRemoved and data.habitatCleaned and data.inputIndexesCleaned,
        "自有地表、草、覆盖栈、资源实体或树木栖息地索引未清理")
end, "恢复自有空地与地层差量并删除松树")
