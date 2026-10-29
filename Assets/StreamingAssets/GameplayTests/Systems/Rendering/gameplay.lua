-- 全目录从未激活机器直接预备表现，覆盖配置获取而非依赖业务先唤醒。
test.runtime("rendering", "cold_machine_content", {}, function(data)
    assert(#data.samples > 0, "未检查任何正式机器定义")
    local coverage = {furnace = false, drying = false, cultivator = false, combustion = false, lighting = false}
    for _, row in ipairs(data.samples) do
        assert(row.initiallyCold and row.contentResolved and row.contentReused,
            row.id .. "冷机器不能直接读取或复用完整配置")
        assert(row.allDeclaredModulesAvailable, row.id .. "冷读取漏掉了正式声明的模块")
        assert(row.preparedLight == (row.hasFuel or row.hasLighting) and row.preparedInactive,
            row.id .. "落地预备遗漏光源或提前启用了表现")
        assert(row.stillCold and row.snapshotUnchanged and row.sharedDefinitionUnchanged,
            row.id .. "读取表现配置意外激活机器或修改共享/权威状态")
        coverage.furnace = coverage.furnace or row.hasFurnace
        coverage.drying = coverage.drying or row.hasDrying
        coverage.cultivator = coverage.cultivator or row.hasCultivator
        coverage.combustion = coverage.combustion or row.hasCombustion
        coverage.lighting = coverage.lighting or row.hasLighting
    end
    for name, present in pairs(coverage) do assert(present, "冷机器回归缺少类别：" .. name) end
    assert(data.missingDefinitionRejected and data.failedCacheEmpty and data.retrySucceeded,
        "配置失败被吞掉、发布了半成品缓存或阻止重试")
    assert(data.serializedCacheEmpty and data.restoredContentAvailable and data.invalidatedContentRebuilt,
        "配置缓存被序列化、反序列化后无法恢复或失效后未重建")
end, "全目录冷机器：直接读取、落地预备、只读状态、失败重试与缓存重建")

-- 正式世界石斧同帧采排序，自有手持石斧跨帧绑定真实玩家 MPB；湿润粒子来自正式 Prefab。
test.runtime("rendering", "player_external_effects", {}, function(data)
    assert(#data.floors == 2, "没有创建两种正式船板物品")
    for _, floor in ipairs(data.floors) do
        assert(floor.loaded and floor.layer == "Default" and floor.order == 0 and
            floor.layerValue < data.playerLayerValue and not floor.projectedIntoDynamicBatch,
            "正式船板在跨帧后被动态排序或合批覆盖：" .. floor.id)
    end
    assert(data.scope == "live-isolated-world" and data.realItemLoaded and data.itemId == "Axe_Stone", "真实物品没有通过正式加载流程")
    assert(data.realPlayerController and data.bodyVisible and data.worldSortingMatches, "真实玩家主体或正式世界排序未建立")
    assert(data.realWorldItemLoaded and data.worldVisible and data.worldItemReleased, "正式世界石斧排序样本无效或自有世界物没有完整回收")
    assert(data.heldInHand and data.ownedByPlayer and data.selfOwnedAttachment, "跨帧石斧没有使用正式手持身份和独立自有挂点")
    assert(data.itemInstanceId == data.currentItemInstanceId and data.rendererInstanceId == data.currentRendererInstanceId and data.itemGeneration == data.currentItemGeneration, "跨帧等待替换或回收了所测石斧/Renderer")
    assert(data.registered and data.registrationCount == 1 and data.sharedPlayerMaterial, "外部 Renderer 没有接入玩家或重复绑定/创建了独立材质")
    assert(math.abs(data.submittedBodyMin - data.expectedBodyMin) < 0.0001 and data.customPropertyPreserved, "真实跨帧 MPB 没有合成 Sprite 元数据或覆盖了其他属性")
    assert(data.bindingReleased and data.materialRestored and data.originalBodyMinRestored == -987 and data.originalMarkerRestored == 41, "解除附属绑定没有完整恢复原材质和 MPB")
    assert(data.effectActive and data.sameNonStackableEffect and data.ownerBound and data.ownRegistrations == 1, "正式特效没有启动或非叠加调用创建了重复实例")
    assert(data.particleMaterialsReady and data.ownerReleased and data.pooledInactive and data.particlesCleared, "正式粒子材质、Owner 解绑或回池清空错误")
    assert(data.poseRestored and data.ownRegistrationReleased, "特效回池没有恢复缩放，或重复回收破坏激活登记")
    assert(data.stackedEffectsActive and data.stackedOwnerReleased and data.allStackedEffectsPooled and data.stackedParticlesCleared,
        "统一注销漏掉同 Owner 同名的叠加特效，或回池粒子没有清空")
    assert(data.newLeaseOwnerBound and data.newLeaseReturned,
        "注销后重新租用特效继承了旧 Owner 或不能正常停止")
    assert(data.unregisteredBeforeDirectFeatureCalls and data.directFeatureCallsActive and data.directFeatureLifecycleCleaned,
        "未显式注册物品调用高亮/特效后，正式回收没有清理请求和效果")
    assert(data.itemReleased and data.ownEffectReleased and data.attachmentReleased, "真实物品、特效或独立自有挂点留下了未清理现场")
end, "真实排序、角色 MPB 跨帧提交/解除、正式湿润粒子与回池恢复")
-- 结果提交前已回收自有物品和粒子，内部 MPB/池状态不拍空地截图。

test.runtime("rendering", "combat_effect_prewarm", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.itemId == "Axe_Stone", "没有采正式目录中的石斧命中特效")
    assert(data.hasDamageText and data.hasSlicing and #data.samples > 0, "没有覆盖正式伤害数字和切割动画")
    assert(data.randomUnchanged and data.nullRejected, "预热改变了游戏随机数或接受空 Prefab")
    for _, sample in ipairs(data.samples) do
        assert(sample.initial >= 3, sample.prefab .. " 在进入世界前没有准备首轮三个命中实例")
        assert(not sample.grew and sample.afterWarmup == sample.initial, sample.prefab .. " 重复预热仍创建实例")
        assert(sample.uniqueRented == 3 and sample.allActive and sample.remaining == sample.initial - 3, sample.prefab .. " 并发取出没有复用三个独立池实例")
        assert(sample.returned == sample.initial and sample.afterDoubleReturn == sample.initial and sample.allInactive, sample.prefab .. " 归还或重复归还破坏池状态")
    end
end, "正式命中特效加载预热、三实例复用、重复预热与回收幂等")
