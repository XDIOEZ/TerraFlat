-- 真实隔离世界只采正式管理器，不修改当前玩家世界或模拟出生算法。
test.check("world_ready", "统一生物出生使用真实隔离世界")
test.check("terrain_ready", "出生现场区块已经加载")
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

test.runtime("ai", "incremental_creature_creation", {}, function(data)
    assert(data.created and data.steps > 6 and data.frames > 1 and not data.prematureDelivery,
        "分帧创建没有跨帧执行，或模块完成前提前交付了生物")
    assert(data.cancelDidNotDeliver and data.cancelRollbackCount == 1,
        "取消分帧创建后仍交付生物，或未执行一次回滚")
    assert(data.invalidCommitRejected and data.invalidCommitNotPublished and data.cleanupComplete,
        "失效落点仍发布了生物，或失败候选未清理")
    assert(data.hasLightController and data.noUnusedLight and data.radianceAdded and data.lightCreatedOnDemand,
        "普通动物预建了闲置光源，或获得光耀后未按需创建光源")
    assert(data.lightDisabled and data.lightReused, "光耀移除未关灯，或再次获得时重复创建光源")
end, "分帧交付、取消回收、失效拒绝和光耀光源复用")
