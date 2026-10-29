test.runtime("world_streaming", "configured_entity_proxies", {}, function(data)
    assert(data.sameDefinitionId and data.sameResourceBackend and data.sameModuleSlots and data.noFullTreeItem,
        "切换物理模式改变了树的定义、资源数据后端或模块，或创建了完整树Item")
    assert(data.fixedMode == "WorldFixed" and data.movingMode == "IndependentDynamic"
        and data.fixedType == "Static" and data.movingType == "Dynamic",
        "正式生成未按配置领取不同游戏物理代理")
    assert(math.abs(data.mass - data.expectedMass) < 0.001,
        "物理质量没有读取原ItemStack的总重")
    assert(data.fixedDisplacement < 0.001 and data.movingDisplacement > 0.5,
        "原生物理没有推动独立动态树，或固定合批树被推动")
    local pose = data.movingState
    assert(pose.id == "Tree_Pine" and pose.hasHealth and pose.visualError < 0.001
        and pose.snapshotError < 0.001 and pose.targetError < 0.001
        and pose.rotationError < 0.001 and pose.velocityError < 0.001,
        "物理运动未回写原树实体的显示、目标、保存姿态或速度")
    assert(data.allRegionsFollowBody, "受击、阻挡等区域没有跟随同一个原生物理主体或丢失来源")
    assert(data.renderBefore and data.renderAfter and data.renderBefore.exists and data.renderAfter.exists
        and data.renderAfter.rendererEnabled and data.renderBefore.sprite == data.renderAfter.sprite
        and math.abs((data.renderAfter.x - data.renderBefore.x) - (pose.x - data.fixedState.x)) < 0.1,
        "已提交的树行网格部件没有跟随原生运动")
    assert(data.snapshotTaken and data.oldHandleRemoved and data.oldLeaseReleased and data.oldRegionsUnregistered,
        "保存回收没有注销旧实体、代理租约或区域绑定")
    assert(data.pooledBodyReused and data.newHandle and data.newEntityBinding
        and data.restoredPoseError < 0.001 and data.restoredVelocityError < 0.001
        and data.restored.guid == pose.guid and data.restored.id == pose.id,
        "对象池恢复残留旧身份，或丢失原实体的位置、速度和定义")
    assert(data.ordinaryUsesCompleteItemBackend and data.ordinaryMode == "IndependentDynamic" and data.ordinaryDisplacement > 0.1
        and data.ordinaryTransformError < 0.001 and data.ordinarySnapshotError < 0.001
        and data.ordinaryVelocityError < 0.001,
        "普通Item正式准备提交链路未领取动态代理或未回写显示和存档")
    assert(data.legacyRefreshMode == "IndependentDynamic" and data.legacyRefreshBodyPreserved
        and data.legacyRefreshVelocityError < 0.001 and data.legacyRefreshAngularVelocityError < 0.001
        and data.legacyRefreshContinuedSimulation > 0.05,
        "无JSON配置的旧动态Item结构刷新丢失了同一刚体或速度，刷新后无法继续模拟")
    assert(data.ordinaryFixedRestoredMode == "WorldFixed" and data.ordinaryFixedRestoredType == "Static"
        and data.ordinaryFixedRestoredPositionError < 0.001 and data.ordinaryFixedIdentityUnique,
        "普通动态Item保存后改为固定模式没有按原GUID唯一恢复到保存位置")
    assert(data.clientProxyBodyType == "Kinematic" and data.clientProxyParticipation
        and data.clientProxyNativePositionError < 0.001 and data.clientProxyNativeRotationError < 0.001
        and data.clientProxyNativeVelocity < 0.001 and data.clientProxyNativeAngularVelocity < 0.001
        and data.clientProxyAuthorityVelocityPreserved < 0.001
        and data.clientProxyAuthorityAngularVelocityPreserved < 0.001,
        "客户端物理代理预测了快照速度，或收到的权威速度没有保存在快照中")
    assert(data.nativeDynamicContact and data.nativeCollisionMovedTree > 0.1
        and data.nativeStaticContactStoppedPusher and data.nativeCollisionFixedTree < 0.001,
        "正式生成实体的原生碰撞没有推动可动树，或固定树不能阻挡推动者")
    assert(data.nativeRotationDelta > 0.01 and math.abs(data.nativeAngularVelocity) > 0.01
        and data.rotatingState.rotationError < 0.001 and data.savedAngularVelocityError < 0.001
        and data.restoredRotationError < 0.001 and data.restoredAngularVelocityError < 0.001,
        "开放旋转后原生朝向或角速度没有回写原实体并完整保存恢复")
    assert(data.dynamicMachineRejected and data.rejectedMachineAbsent
        and string.find(data.dynamicMachineReason, "约束") and data.fixedMachineMode == "WorldFixed",
        "固定格数据机器的独立可动配置被静默回退，或拒绝后留下节点")
    assert(data.buildingInitiallyOccupied and data.buildingMovedCell and data.buildingOldCellsFreed
        and data.buildingCurrentCellOccupied and data.buildingRotatedFootprintOccupied
        and data.buildingRollbackSucceeded and data.buildingRollbackItemRemoved
        and data.buildingRollbackProxyReleased and data.buildingRollbackOccupancyReleased,
        "完整Item建筑运动、旋转占地或正式回滚清理失败")
end, "同定义树及普通Item配置生成、原生推动、显示回写、保存和物理池恢复")

test.runtime("world_streaming", "resource_proxy_regions", {}, function(data)
    assert(data.regionCount == 6 and data.gapOwnRegions == 0,
        "自然资源多区域被联合包围盒填实空洞或丢失区域")
    assert(data.leftBlocking and data.leftDamage and data.leftInteraction and data.exactResourceIdentity,
        "自然资源区域用途或原始数据实体归属不准确")
    assert(not data.gapCanInteract and data.leftCanInteract,
        "自然资源交互精筛未使用准确区域，空洞可交互或真实区域不可交互")
end, "自然资源多区域原生几何、用途归属和交互空洞精筛")

test.runtime("world_streaming", "proxy_chunk_streaming", {}, function(data)
    assert(data.formalChunkSpawn and data.crossedChunk and data.nativeMovement > 0.5
        and data.sameBodyAfterCrossing and data.birthChunkUnloadedAlive,
        "正式区块可动资源跨块后复制主体，或出生区块解绑错误回收已移动实体")
    assert(data.sourceRebindRecognized and data.sourceDidNotClone and data.currentChunkUnloadedReleased
        and data.restoredUnique and data.restoredPositionError < 0.001 and data.restoredVelocityError < 0.001,
        "区块重绑重复生成原来源，或当前位置区块卸载恢复丢失身份、位置和速度")
    assert(data.independentSaveIdentityPreserved and data.independentSavePositionError < 0.001
        and data.independentSaveVelocityError < 0.001 and data.independentSaveAngularVelocityError < 0.001
        and data.independentSaveContactsCleared and data.changedItemSaveFound
        and data.changedItemSavePositionError < 0.001 and data.changedItemSaveVelocityError < 0.001
        and data.changedItemSaveAngularVelocityError < 0.001 and data.changedItemSaveContactsCleared,
        "正式生态存档MemoryPack往返丢失自然资源出生身份、运动快照，或把瞬时碰撞事实写入存档")
    assert(data.modeChangedSourceRecognized and data.modeChangedSourceNotCloned
        and data.restoredChangedMode == "WorldFixed" and data.restoredChangedType == "Static"
        and data.changedModePositionError < 0.001 and data.repeatBindOneEntity,
        "保存后仅改JSON为固定模式导致出生点克隆或未在当前位置唯一恢复")
    assert(data.fixedTransferFailureKeepsSource and data.fixedTransferFailureHasReason
        and data.fixedTransferSuccess and data.fixedTransferSourceInvalidated
        and data.fixedTransferBusyTargetInitialized and data.fixedTransferBusySourceGuarded and data.fixedTransferBusyTargetGuarded
        and data.fixedTransferTargetMode == "WorldFixed" and data.fixedTransferTargetNativeScene
        and data.fixedTransferTargetCollisionRendererBound and data.fixedTransferTargetGuidPreserved
        and data.fixedTransferTargetSourceWorldPreserved and data.fixedTransferTargetPositionError < 0.001
        and data.fixedTransferTargetVelocityError < 0.001 and data.fixedTransferTargetAngularVelocityError < 0.001
        and data.fixedTransferTargetRotationError < 0.001
        and data.fixedTransferTargetProxyUnique and data.fixedTransferNativeTargetVisible
        and data.fixedTransferOtherWorldNativeShapeAbsent and data.fixedTransferSourceOtherWorldShapeVisible
        and data.fixedTransferSourceMigratedEntityShapeRemoved,
        "固定自然资源跨世界迁移没有保护事务重入、保留目标运动状态、Chunk碰撞归属、生成身份或原生隔离")
    assert(data.dynamicTransferFailureKeepsSource and data.dynamicTransferFailureHasReason
        and data.dynamicTransferSuccess and data.dynamicTransferSourceInvalidated
        and data.dynamicTransferBusyTargetInitialized and data.dynamicTransferBusySourceGuarded and data.dynamicTransferBusyTargetGuarded
        and data.dynamicTransferTargetMode == "IndependentDynamic" and data.dynamicTransferTargetNativeScene
        and data.dynamicTransferTargetCollisionRendererBound and data.dynamicTransferTargetGuidPreserved
        and data.dynamicTransferTargetSourceWorldPreserved and data.dynamicTransferTargetPositionError < 0.001
        and data.dynamicTransferTargetVelocityError < 0.001 and data.dynamicTransferTargetAngularVelocityError < 0.001
        and data.dynamicTransferTargetRotationError < 0.001
        and data.dynamicTransferTargetProxyUnique and data.dynamicTransferNativeTargetVisible
        and data.dynamicTransferOtherWorldNativeShapeAbsent and data.dynamicTransferSourceOtherWorldShapeVisible
        and data.dynamicTransferSourceMigratedEntityShapeRemoved,
        "动态自然资源跨世界迁移没有保护事务重入、保留目标运动状态、Chunk碰撞归属、生成身份或原生隔离")
end, "可动自然资源正式区块生成、跨块、出生块解绑、保存重载与模式变更唯一性")

test.runtime("world_streaming", "resource_proxy_world_isolation", {}, function(data)
    assert(data.differentNativeScenes and data.sameInitialCoordinates and data.firstMoved > 0.5
        and data.secondStayed < 0.001 and data.noCrossWorldNativeContact,
        "两个正式世界同坐标资源进入了同一原生接触空间，或运动相互影响")
    assert(data.firstOnlyOwnRegions and data.secondOnlyOwnRegions,
        "世界物理查询混入了另一世界同坐标自然资源的区域")
end, "两个正式世界相同树资源同坐标的原生接触和查询隔离")

test.runtime("world_streaming", "resource_proxy_native_triggers", {}, function(data)
    assert(data.firstEnter > 0 and data.firstStay > 0 and data.firstExit > 0
        and data.secondEnter > 0 and data.secondStay > 0 and data.secondExit > 0,
        "资源Trigger仅登记了用途，没有将真实原生进入、持续和离开事件交给具体数据能力")
    assert(data.invalidIdentity == 0 and data.samePooledRegion and data.oldLeaseReleased
        and data.oldReceiverUnchanged and data.newHandle,
        "触发区域复用后残留旧接收者，或丢失准确实体、区域、模块和另一碰撞体归属")
end, "通用资源Trigger的真实原生事件、准确能力接收与对象池旧回调清理")
