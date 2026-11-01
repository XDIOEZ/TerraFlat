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
    assert(query.returnedCollectionReused and query.worldIdentityPreserved and query.disposedWorldRentRejected,
        "受击碰撞集合没有从所属世界统一领取/复用，或关闭的世界仍能发放新租约")
    assert(query.defaultSceneChanged and query.fixedSpaceHit and query.separateCandidateHit and query.fixedSpaceSweep,
        "战斗临时投影未固定到租用时的物理空间，或切换默认世界后查询发生漂移")
    assert(query.oldCandidateCleared and query.newCandidateHit and query.disposedSpaceRejected
        and query.disposedProjectionCount == 0,
        "战斗查询复租残留旧候选，或物理空间释放后租约仍可使用/泄漏形状")
    local pool = data.obstaclePoolReuse
    assert(#pool == 16, "缺少落地障碍在两个地形宿主下的反复回收重绑采样")
    local instanceId = pool[1].instanceId
    for index, sample in ipairs(pool) do
        assert(sample.instanceId == instanceId and sample.parentIndex == math.floor((index - 1) / 8)
            and sample.activeMatchesParent and sample.attachedToCurrentBody,
            "活动或停用地形下租出的碰撞体没有复用原实例，或重绑仍引用旧刚体")
        assert(not sample.usedByComposite and sample.queryOnlyObstacle and sample.registered,
            "落地障碍错误并入地形 Composite，或丢失独立几何和世界归属")
        assert(sample.terrainPointCountBefore > 0 and
            sample.terrainPointCountAfter == sample.terrainPointCountBefore,
            "回收重绑改变了地形 Composite 的几何")
        assert(sample.pooledInactive and sample.pooledUnregistered and sample.pooledInScenePhysicsRoot,
            "障碍回池后仍启用碰撞、保留命中登记，或未归入所属场景Physics分类")
    end
end, "世界物理原生隔离、真实接触和多区域归属生命周期")

test.system("world_streaming", "world_physics_definition", {}, function(data)
    assert(data.declaredWithoutNativeComponents and data.returnedComponentsInactive and
        data.rentedComponentsReused and data.worldReleasedRentedComponents,
        "物理声明提前生成了原生组件，或世界领取、回收复用、关闭清理契约失效")
    assert(data.moduleDisabledOnlyOwnRegion and data.moduleReenabledBeforeLoad,
        "关闭模块未禁用自身区域、误关其他区域，或重启时没有复用并恢复自身受击形状")
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

test.system("world_streaming", "detached_item_projection", {}, function(data)
    assert(data.noNativePhysicsOnItem and data.separatePhysicsRoot and data.declaredGeometry,
        "普通物品仍携带原生物理组件，或独立代理丢失声明几何")
    assert(data.hitResolvesLogicalItem and data.nativeQueryFindsProxy,
        "独立物理代理没有保留真实物品身份或无法被原生空间查询")
    assert(data.viewHasNoLogicOrPhysics and data.noLogicPlaceholderNodes and data.realVisualCopied,
        "视觉副本包含逻辑/碰撞组件或多余模块空节点，或没有绘制真实贴图")
    assert(data.viewReleaseKeepsLogicAndPhysics and data.poseAndScaleFollowOwner,
        "撤销绘制销毁了逻辑或物理，或位置代理没有同步姿态和尺寸")
    assert(data.heldItemDisablesPhysics and data.dropReusesSameLease and data.releasedProxyCannotResolve
        and data.reuseKeepsItemFreeOfNativePhysics,
        "手持/落地、卸载或复租破坏了代理生命周期或重新给视觉外壳添加碰撞体")
    assert(data.independentBodyDetached and data.nativeMovementWritesBack and data.reregisterPreservesRepresentation,
        "独立运动仍依赖物品根刚体、原生结果未回写，或重新登记重置了临时运动接管状态")
end, "普通物品逻辑、纯视觉投影和独立物理代理生命周期")

test.runtime("world_streaming", "prefab_creature_physics", {}, function(data)
    assert(data.usesNativeBody and data.noProxy and data.bodyTypePreserved,
        "移动生物被迁入物理代理，或重复登记修改了预制体原有刚体")
    assert(data.hitboxRegistered and data.hitboxChannel and data.rootRegistered and data.collidersReused,
        "生物受击盒未按原预制体配置登记到物理管理器，或重复登记创建了多余碰撞体")
end, "移动生物保留预制体碰撞并由世界物理管理器统一登记")

test.runtime("world_streaming", "item_visual_ownership", {}, function(data)
    assert(data.actorSeparated, "GO 单图生物的 SpriteRenderer 未迁至独立 Rendering 节点")
    assert(data.playerProjected and data.ordinaryProjected and data.sourcesHiddenWhileProjected
        and data.viewHasNoLogicOrPhysics
        and data.viewsUnderRendering, "玩家或普通物品的绘制副本未独立，或混入 Item、刚体、碰撞组件")
    assert(data.repeatReusesView and data.releaseAndRebind,
        "渲染重复登记创建副本，或取消登记后无法正常重绑")
end, "GO 生物、玩家、普通物品统一渲染所有权及复用")
