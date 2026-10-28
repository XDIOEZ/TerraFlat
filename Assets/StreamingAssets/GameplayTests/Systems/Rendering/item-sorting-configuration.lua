-- 采样正式装配与最终排序键，画面仍需现场复核。
test.system("rendering", "item_sorting_configuration", {}, function(data)
    assert(data.definitionBound and data.relativeOrderPreserved, "物品未持有当前视觉配置，或修改了分组内部排序")
    assert(data.heldRestored and data.removedConfigurationCleared and data.pooledConfigurationCleared and data.aquaticDespawnReleased,
        "手持、删除配置或对象池复用残留旧排序")
    assert(data.rejectsUnknownLayer and data.rejectsEmptyLayer and data.rejectsOrderUnderflow and data.rejectsOrderOverflow,
        "无效图层或越界排序值未在定义编译时拒绝")
    assert(data.droppedLayer == "Default" and data.droppedOrder == 0, "轻量掉落物忽略了物品排序配置")
    assert(data.groundCoverDefault and data.groundCoverFallback and data.groundCoverWrongLayerRejected and data.groundCoverWrongOrderRejected,
        "地表植被未按实际固定图层和顺序校验")
    assert(data.naturalCustom and data.naturalFallback, "自然资源实体没有消费当前定义的排序或类别回退")
    local ecs = data.aiecs
    assert(ecs.batches == 3 and ecs.uploadedBytes == 928 and ecs.sharedYBatch and ecs.otherYBatch and ecs.customBatch,
        "ECS 生物没有按物种配置及脚点 Y 提交原生排序")
    assert(ecs.updated and ecs.reused and ecs.culledBatches == 2 and ecs.activeAfterCull == 2 and ecs.hidden,
        "ECS 生物排序更新、复用、裁剪或隐藏错误")
    assert(ecs.projectedDomainBatchCount == 3 and ecs.domainParented and ecs.domainCategoryApplied and
        ecs.domainRawOrdersSubmitted and ecs.domainFootpointsProjected and ecs.domainInnerKeysPreserved and
        ecs.domainRankOrdersDiffer and ecs.domainYRankOrderPreserved and ecs.domainBatchKeyApplied and
        ecs.domainDifferentSourceYMerged and ecs.domainSameSourceYSeparated and
        ecs.domainWaterLineUsesSourceCoordinates and ecs.domainProjectionHidden,
        "AIECS 原生主体批次没有进入世界域、按投影脚点写入不同 rank，或水线源坐标错误")
    assert(ecs.domainHiddenRanksRestored and ecs.domainProjectionReentered and ecs.domainReentryRanked,
        "AIECS 隐藏、域内重入没有恢复 raw 键并重新登记 rank")
    local rows = data.depthRows
    assert(rows.sameRoot and rows.separateKeys and rows.outerKey and rows.innerOrders,
        "行合批没有按图层和顺序区分外层排序，或覆盖了组内的部件顺序")
    assert(rows.initialRows == 3 and rows.remainingRows == 2 and rows.rekeyed,
        "合批部件更新排序后没有迁移并清理旧行")
    assert(rows.snapshots and rows.snapshotOrdering, "世界快照丢失了外层排序、脚点或同组内部顺序")
    assert(rows.sameSourceRow and rows.rowsBeforeEntitySplit == 1 and rows.entityRowsSplit and
        rows.rowsAfterEntitySplit == 2 and rows.projectedEntityGroupsInDomain and rows.rotatedEntityFootpointsDiffer and
        rows.rotatedEntityRanksFollowFootpoints and rows.splitEntityPartOrdersPreserved,
        "同源地表行内的两个实体旋转投影后没有拆成独立脚点组并按实际 Y rank 排序")
    assert(rows.domainExitRestoresRawEntityKeys and rows.domainExitRestoresSourceRenderers and
        rows.entityModeExitMergesRow and rows.rowsAfterEntityModeExit == 1 and rows.mergedEntityPartOrdersPreserved,
        "退出实体排序域后未恢复组内 raw 键、源 Renderer、原行合批或部件顺序")
    local nested = data.nestedSorting
    assert(nested.nestedGroups and nested.sameGroup and nested.groupsStayTogether and nested.rootRead and nested.rootApply,
        "嵌套排序组没有按子组优先，或 sortAtRoot 没有隔离外层排序")
    local samples = {}
    for _, sample in ipairs(data.samples) do samples[sample.step] = sample end
    for _, step in ipairs({"floor_north", "floor_south", "grouped_floor", "reused_as_floor"}) do
        local sample = samples[step]
        assert(sample.layer == "Default" and sample.order == 0 and sample.layerValue < data.playerLayerValue,
            "地板被自动类别、分组或池复用改回玩家图层：" .. step)
        assert(sample.depthBatch and sample.groundBatch, "地板错误进入动态建筑合批：" .. step)
    end
    assert(samples.floor_north.y > 0 and samples.floor_south.y < 0, "没有覆盖玩家两侧的地板排序")
    for _, step in ipairs({"custom_order", "returned_from_hand", "reenabled"}) do
        local sample = samples[step]
        assert(sample.layer == "Player" and sample.order == 147 and sample.order > data.playerOrder,
            "自定义排序被热刷新、手持切换或重新显示覆盖：" .. step)
        assert(sample.depthBatch and not sample.groundBatch, "自定义排序没有接入按实际图层分组的合批：" .. step)
    end
    for _, step in ipairs({"aquatic_submerged", "aquatic_refreshed", "aquatic_returned"}) do
        local sample = samples[step]
        assert(sample.layer == "Default" and sample.order == 0, "水下环境排序被静态配置覆盖：" .. step)
    end
    for _, step in ipairs({"aquatic_carried", "aquatic_resurfaced"}) do
        local sample = samples[step]
        assert(sample.layer == "Player" and sample.order == 147, "离水后没有恢复生物的静态排序：" .. step)
    end
    assert(samples.order_only.layer == "Player" and samples.order_only.order == 99,
        "只配置排序值时没有保留默认一级图层")
    assert(samples.layer_only.layer == "Default" and samples.layer_only.order == 100 and samples.layer_only.depthBatch,
        "只配置图层时没有保留默认二级顺序或接入合批")
    assert(samples.defaults_restored.layer == "Player" and samples.defaults_restored.order == 100 and
        samples.defaults_restored.depthBatch and not samples.defaults_restored.groundBatch,
        "清除配置没有回到正式动态排序")
    assert(samples.minimum_order.order == -32768 and samples.maximum_order.order == 32767,
        "合法排序边界被截断")
    local catalog = {}
    for _, item in ipairs(data.catalog) do catalog[item.id] = item end
    for _, id in ipairs({"ShipFloor_Iron", "ShipFloor_Refractory", "RailTrack"}) do
        assert(catalog[id].layer == "Default" and catalog[id].order == 0 and
            catalog[id].material == "Assets/9_Shaders/Material/Ground-Facility-Lit.mat", "贴地物品配置错误：" .. id)
    end
    for _, id in ipairs({"ShipFloor_Iron_Summoner", "Stick_Wood"}) do
        assert(catalog[id].layer == "Player" and catalog[id].order == 100, "普通物品未继承动态默认图层：" .. id)
    end
    assert(#data.actors >= 13, "没有读取完整生物目录")
    for _, actor in ipairs(data.actors) do
        assert(actor.layer == "Player" and actor.order == 100, "生物未继承正式动态排序：" .. actor.id)
    end
end, "物品配置排序：一级图层、二级顺序、地板、分组、热更新、手持、池复用与合批")

test.system("rendering", "aiecs_sorting_bridge", {}, function(data)
    assert(data.firstLayer == "Default" and data.firstOrder == 37 and data.secondLayer == "Player" and data.secondOrder == 147,
        "ECS 没有按物种定义解析视觉排序或独立字段回退")
    assert(data.refreshedLayer == "Player" and data.refreshedOrder == 222, "ECS 没有解析已发布的新生物定义")
end, "ECS 生物排序桥接：物种配置与更新")

test.system("rendering", "presentation_sorting_domain", {}, function(data)
    assert(data.category == "projected-world" and data.keyMatchesCategory and data.backgroundBehind,
        "独立世界排序域没有读取正式类别键，或没有排在星体背景上方")
    assert(data.distinctDomains and data.allRenderersInside and data.nestedRootKeyApplied,
        "不同世界混用了排序域，或 SortingGroup.sortAtRoot 绕过了外层域")
    assert(data.localKeysPreserved and data.rendererTypesProjected and data.yAndPositionProjectedOnce,
        "域投影没有保留 Sprite/Mesh/粒子的局部键、Y 顺序或单次位置变换")
    assert(data.copiedRendererCount >= 5 and data.hiddenSourcesSuppressed and data.sourceRestored,
        "投影缺少原生 Renderer，或隐藏/释放没有抑制与恢复源 Renderer")
    assert(data.domainDisposeRestoresSources and data.domainDisposeHidesCopies and
        data.sharedSourceHeldUntilLastProjection and data.sharedSourceRestoredAfterLastProjection,
        "直接释放排序域未清除投影，或共享源在最后一个投影释放前提前恢复")
    assert(data.registryReleased and data.domainsDisposed,
        "投影排序域释放后仍残留注册或根对象")
    assert(data.sameRawYRankedAndSwapped and data.domainsRankIndependently,
        "相同原始层序的组没有按显示脚底 Y 交换真实排序序号，或两个域的 rank 互相影响")
    assert(data.fixedFloorBelowActor and data.sourceGroupRemainsAtomic,
        "贴地地板没有保持在角色下方，或源顶层组的内部部件顺序被 rank 覆盖")
    assert(data.hideKeepsRank and data.moveRestoresRawRank and data.destinationAndReentryRerank and
        data.removeRestoresRawRank and data.releaseRestoresRawRank and data.domainDisposeRestoresRawRank,
        "隐藏/跨域移动/重入/移除/释放没有按约定保留临时键或恢复源 raw Order")
end, "独立世界投影排序域：外层键、局部顺序、Renderer 扩展与清理")
