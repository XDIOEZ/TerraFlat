test.runtime("space", "ship_interior_world", {}, function(data)
    assert(data.scope == "live-isolated-world", "必须由隔离存档中的正式运行时探针采样")
    assert(data.shipCount == 2, "夹具必须通过正式船体部件事务创建两艘独立船")
    assert(data.unlandedAssembliesRetained, "尚未完成降落的组装船不能被落地恢复提前移交")
    assert(data.localWorldClocksAdvance, "船内普通农业与生物必须能读取持续推进的世界时钟")
    assert(data.localWorldHasNoPlanetaryWeather, "船内局部世界不能创建或借用星球天气")
    assert(data.openShipUsesSurfaceAtmosphere, "露天船舱必须读取真实地表大气并恢复调用方的世界上下文")

    local first, second = data.ships[1], data.ships[2]
    assert(first.shipId ~= second.shipId and first.worldKey ~= second.worldKey,
        "两艘船必须保有独立身份和逻辑世界")
    assert(first.worldRuntimeId == first.worldKey and second.worldRuntimeId == second.worldKey,
        "船内区块 Runtime 必须按本船世界身份注册")
    assert(first.readyChunkCount > 0 and second.readyChunkCount > 0,
        "两艘船都必须有已提交的真实区块数据")
    assert(data.worldsDistinct and data.scenesDistinct and data.physicsScenesDistinct,
        "同坐标船内世界必须隔离 Runtime、场景和原生物理空间")

    assert(data.sameLocalCoordinates and data.queryOnlyOwnItem,
        "同一船内坐标只能查到本船普通 Item")
    assert(first.itemWorldKey == first.worldKey and second.itemWorldKey == second.worldKey and
        first.itemGuid ~= second.itemGuid and first.itemRuntimeRegistered and second.itemRuntimeRegistered,
        "普通 Item 必须以唯一实例加入各自船内 Item 账本")
    assert(data.floorProvidesOrdinarySurface and data.floorVisualNotDuplicated and
        data.ordinaryTileWallMirrored and data.ordinaryTileRemovalUpdatedStructure,
        "船内普通地板、支撑墙和拆除必须走真实地形/建造后端")
    assert(data.ordinaryGroundPlacementInfersFloor,
        "未声明船专用部件的普通 GroundPlacement 定义应自动推断为船内地板")
    assert(data.firstShipCollided and data.secondShipPassed and data.nativeQueryIsolated,
        "两份原生物理场景必须分别阻挡和放行同一坐标的真实刚体")

    assert(data.projectionRoundTrip and data.projectionSeparatesLocalFromDisplay and data.surfacePresentationRemainsVisible,
        "船内本地坐标与画面投影必须可逆且分属各自坐标系")
    assert(data.machineWorldKey == first.worldKey and data.machineInstancePreserved and data.machinePayloadPreserved,
        "普通机器及其实际载荷必须由本船世界和同一实例持有")
    assert(data.fluidPipeWorldKey == first.worldKey and data.electricalWireWorldKey == first.worldKey and
        data.electricalGridContainsRealNodes and data.machineVisualLayerMatchesInteraction,
        "船内管网或机器视觉/交互没有使用普通机器后端")
    assert(data.doorFound and data.doorInteracted and data.doorStatePersisted and data.healthApplied and
        data.ordinaryDamageWindowRejectedDuplicate,
        "船内普通舱门、交互和生命模块必须走真实 Item 路径")
    assert(data.doorVisualSurvivedImmediateReenable and data.doorHasWorldPresentationProjection,
        "船内舱门同帧停用重启后必须保留排序和普通世界显示投影")
    assert(data.zeroGravityInteriorRetainsWorld and data.zeroGravityUsesNativeMoverVelocity and
        data.zeroGravityMovesThroughNativePhysics,
        "无人工重力舱内乘员必须留在本船世界并通过普通刚体积分移动")
    assert(data.terrainTransactionAppliedBeforeDispose and data.terrainRollbackKeptSoilCropAndLiquid and
        data.terrainCommitMovedSoilCropAndLiquid and
        data.terrainReverseReturnedPayload and data.terrainTransferReversedAndCleaned,
        "船内土壤、真实作物 GUID 和三级水必须支持事务回滚、提交、反移与清理")
    assert(data.authoritySurvivedViewUnload and data.simulationAdvancedWithoutView,
        "卸载船内表现后，权威物品与机器模拟仍须继续")
    assert(data.ordinaryChickenSpawned and data.ordinaryChickenHasWorldLedger and
        data.ordinaryChickenUsesShipNativePhysics and data.ordinaryChickenSavedInLocalChunk and
        data.ordinaryChickenTickedWithoutView and data.ordinaryChickenDespawned,
        "普通小鸡必须经通用生物后端在船世界生成、运行、存档并由标准入口清理")
    assert(data.passengerOwnedLocalPhysics and data.passengerChildrenMoved and data.passengerMovedByNativePhysics and
        data.ordinaryMovementDelegated and data.cameraFollowsProjectedPassenger and data.pointerReturnsShipLocalPosition,
        "乘员移动、子物品世界归属、指针或相机没有接入船内本地世界")
    assert(data.identityAnchorPreservedAcrossStructureChanges and data.splitDamageAccepted and data.splitFloorDestroyed and
        data.splitMovedPlayerToLocalWorld and
        data.transferHadNoDuplicate and data.fragmentPayloadPreserved and
        data.destroyReleasedWorlds and data.fixtureDropsRemoved,
        "结构拆分必须移交乘员与真实载荷，销毁和收尾不能复制实体或泄漏世界")
    return "覆盖两艘船的本地坐标、真实区块与物理隔离、Item/机器归属、投影、门与武器伤害、零重力移动、土层事务、无视图模拟和销毁清理。起降接管另由 ship_landing_gameplay 覆盖。"
end, "船内独立世界与普通物品/交互")
