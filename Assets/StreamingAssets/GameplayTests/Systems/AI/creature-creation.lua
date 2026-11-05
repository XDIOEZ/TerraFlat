-- 真实隔离世界只采正式管理器，不修改当前玩家世界或模拟出生算法。
test.check("world_ready", "统一生物出生使用真实隔离世界")
test.check("terrain_ready", "出生现场区块已经加载")
test.runtime("ai", "incremental_creature_creation", {}, function(data)
    assert(data.created and data.steps > 6 and data.frames > 1 and not data.prematureDelivery,
        "分帧创建没有跨帧执行，或模块完成前提前交付了生物")
    assert(data.unpublishedWhileYielding,
        "分帧候选在准备阶段已进入正式 Item 注册表")
    assert(data.sceneValid and data.sceneLoaded and data.sceneMatchesOwner and
        data.physicsSceneValid and data.physicsSceneMatchesOwner,
        "分帧出生交付的 GameObject 没有归属有效的世界 Scene/PhysicsScene")
    assert(data.cancelDidNotDeliver and data.cancelRollbackCount == 1,
        "取消分帧创建后仍交付生物，或未执行一次回滚")
    assert(data.invalidCommitRejected and data.invalidCommitNotPublished and data.cleanupComplete,
        "失效落点仍发布了生物，或失败候选未清理")
    local replacement = data.replacedWorld
    assert(replacement.sameKeyDifferentScene and replacement.stoppedAfterReplacement and
        replacement.candidateReleased and replacement.candidateDestroyed and replacement.noDeliveryOrPublish and
        replacement.rolledBackOnce and replacement.originalWorldRestored and replacement.originalPhysicsRetained,
        "同键世界换代后旧生物候选仍被交付，或未回滚并恢复原世界")
    assert(data.fishColdShellWithoutBody and data.fishBornWithMotionBody and data.fishCleaned,
        "小鱼无预置刚体的外壳未能分帧完成物理租借、加载或出生回收")
    assert(data.hasLightController and data.noUnusedLight and data.radianceAdded and data.lightCreatedOnDemand,
        "普通动物预建了闲置光源，或获得光耀后未按需创建光源")
    assert(data.lightDisabled and data.lightReused, "光耀移除未关灯，或再次获得时重复创建光源")
end, "分帧交付、取消回收、失效拒绝和光耀光源复用")

test.runtime("ai", "creature_creation", {}, function(data)
    assert(data.created and data.initialized and data.birthDeltaAfterFirst == 1,
        "转发入口没有交付真实已加载 AI，或新出生被重复记账")
    assert(data.duplicateRejected, "同一个 GUID 被重复出生占用")
    assert(data.callbackRejected and data.beforeLoadUninitialized and data.callbackLoaded,
        "模块配置与来源绑定没有按照加载前后顺序执行")
    assert(data.rollbackRemoved and data.rollbackDelta == 1, "来源绑定失败后没有统一回收")
    assert(data.destroyedCallbackRejected and data.replacementSurvived,
        "回调销毁后仍返回成功，或回滚误删了池复用的新生物")
    assert(data.restored and data.sameIdentity and data.birthsUnaffectedByRestore,
        "恢复没有保留原身份，或被算作新出生")
    assert(data.parked and data.parkedIdentityReserved and data.woke and data.wakeSameIdentity,
        "休眠身份未预留，或唤醒没有恢复同一个居民")
    assert(data.restoreDelta == 2 and data.finalBirthDelta == 2 and data.cleanupComplete,
        "新生/恢复统计混算，或自有测试居民没有清理")
end, "统一出生、重复拒绝、失败回滚、池复用保护和同身份恢复")

-- 对照真实实体与同一模拟步的表现快照，不依赖实体在查询中的顺序。
test.system("ai", "ecs_resident_ownership", {wrapped = false}, function(data)
    local sources, snapshots = data.sourceRecords, data.displayRecords
    assert(#sources == 3 and #snapshots == 3, "ECS 多实体表现快照数量错误")
    local function identity(record)
        return table.concat({record.backend, record.value, record.generation, record.world, record.dimension}, ":")
    end
    local remaining, definitions, externalCount = {}, {}, 0
    for _, source in ipairs(sources) do
        local key = identity(source)
        assert(remaining[key] == nil, "ECS 权威实体身份重复")
        remaining[key], definitions[source.definition] = source, true
        if source.external == 1 then externalCount = externalCount + 1 end
    end
    assert(definitions[0] and definitions[1] and externalCount == 1 and
        math.abs(sources[1].x - sources[2].x) > .01 and math.abs(sources[2].x - sources[3].x) > .01,
        "ECS 快照样本没有覆盖不同定义、位置与外部实体")
    for _, snapshot in ipairs(snapshots) do
        local key = identity(snapshot)
        local source = remaining[key]
        assert(source and snapshot.definition == source.definition and snapshot.external == source.external and
            math.abs(snapshot.x - source.x) < .0001 and math.abs(snapshot.y - source.y) < .0001,
            "ECS 表现快照的身份、定义或位置与实际实体不一致")
        remaining[key] = nil
    end
    assert(next(remaining) == nil, "ECS 表现快照丢失实际实体")
end, "ECS 多实体真实身份与表现快照一致")
