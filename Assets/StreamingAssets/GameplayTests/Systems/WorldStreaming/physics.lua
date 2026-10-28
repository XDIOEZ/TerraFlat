test.system("world_streaming", "world_physics_ownership", {}, function(data)
    assert(data.separateNativeScenes and data.firstOwnMarker and data.secondOwnMarker
        and data.firstQueryCount == 1 and data.secondQueryCount == 1,
        "不同世界的相同坐标没有在原生物理场景中隔离查询")
    assert(data.sharedSpaceRejected and data.wrongSceneRejected and data.wrongSceneOwnerPreserved,
        "创建时允许不同世界共用原生空间，或错误空间登记破坏了原归属")
    assert(data.passedOtherWorldWallX > 1.5 and not data.otherWorldContact,
        "其他世界的阻挡已进入本世界的原生接触计算")
    assert(data.stoppedAtOwnWallX < -0.4 and data.stoppedAtOwnWallX > -0.8 and data.ownWorldContact,
        "本世界正常接触被隔离规则误禁用，或动态体穿过本世界阻挡")
    local owner = data.ownership
    assert(owner.registered and owner.originalItem and owner.exactReceiver and owner.originalEntity
        and owner.regionId == "damage:second" and owner.damageOnly,
        "物理区域丢失原始物品/实体/用途，或命中错误转发给首个受击模块")
    assert(owner.nestedSource and owner.nestedOwnership and owner.unregistered and owner.rebound and owner.oldWorldImageRejected,
        "镜像没有还原准确源区域，或卸载/跨世界复用留下旧世界归属")
    assert(owner.clearedImageReusable and owner.reboundImageSource and owner.sourceCycleRejected
        and owner.sourceCyclePrefixRejected,
        "镜像回池仍残留旧来源、重绑失效，或循环来源被错误当成可命中实体")
    assert(owner.destroyedSourceRejected and data.disposedIndexCleared and data.borrowedSpacePreserved
        and data.otherWorldSurvived,
        "源碰撞销毁或世界释放后仍可命中旧目标，或服务释放错误关闭了借用场景")
    local query = data.combatQueries
    assert(query.defaultSceneChanged and query.fixedSpaceHit and query.separateCandidateHit and query.fixedSpaceSweep,
        "战斗临时投影未固定到租用时的物理空间，或切换默认世界后查询发生漂移")
    assert(query.oldCandidateCleared and query.newCandidateHit and query.disposedSpaceRejected
        and query.disposedProjectionCount == 0,
        "战斗查询复租残留旧候选，或物理空间释放后租约仍可使用/泄漏形状")
end, "世界物理原生隔离、真实接触和多区域归属生命周期")

test.system("world_streaming", "world_physics_definition", {}, function(data)
    assert(data.regionCount == 5 and data.stableRegions and data.shapeReset and data.baselineStable,
        "JSON区域重复配置新建节点、遗漏重置几何，或破坏物品对象池的层级基线")
    assert(data.oldShapesDisabled and data.interactionIsCircle,
        "JSON区域没有替代旧形状，模块重新写参数后启用重复碰撞，或丢失具体形状")
    assert(data.twoExactReceivers and data.exactInteractionReceiver and data.blockingHasNoDamageReceiver and data.purposePrecedesReceiver,
        "多区域命中错误转给首个生命模块，或阻挡/交互区域越权成为受击区域")
    assert(data.gapSolids == 0 and data.leftGeometryPreserved,
        "多个阻挡区域被宽相包围盒填实空洞，或具体形状没有进入正式物理空间")
end, "普通Item JSON多区域、精确接收与重复配置回池基线")

test.system("world_streaming", "world_physics_proxy_modes", {}, function(data)
    assert(data.fixedMode == "WorldFixed" and data.dynamicMode == "IndependentDynamic"
        and data.massSource == "ItemStack" and data.rotationCanMove and data.unityModeRejected,
        "物理配置没有表达游戏代理模式、唯一质量来源或旋转约束，或直接接受Unity刚体类型")
    assert(data.defaultFixed == "WorldFixed" and data.fixedGridDynamicRejected,
        "固定格数据机器将独立可动配置静默回退为固定模式")
    assert(data.blockingCanBatch and data.damageCannotBatch and data.triggerCannotBatch
        and data.receiverCannotBatch and data.dynamicCannotBatch,
        "物理合批忽略用途、事件接收者或运动模式边界")
    assert(data.immutableFixedBoxAccepted and data.immutableDynamicBoxRejected and data.immutableModuleBoxRejected,
        "共享区块体接纳了动态或可变形状，或拒绝了固定根级不可变阻挡Box")
    local interaction = data.defaultInteraction
    assert(interaction and interaction.compiled and interaction.regionCount == 2
        and interaction.blockingRegionPreserved and interaction.hasInteractionPurpose and interaction.isTrigger,
        "自然资源默认交互区域冷编失败，或丢失阻挡/交互职责")
    assert(interaction.localPositionError < 0.0001 and interaction.localRotationError < 0.001
        and interaction.localScaleError < 0.0001
        and interaction.boundsOffsetError < 0.0001 and interaction.boundsSizeError < 0.0001,
        "自然资源默认交互区域没有保留非零渲染TRS或准确Sprite边界几何")
end, "JSON游戏代理模式、固定格约束与共享合批资格")
