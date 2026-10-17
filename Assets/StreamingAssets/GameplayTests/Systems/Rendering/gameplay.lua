-- 正式世界石斧同帧采排序，自有手持石斧跨帧绑定真实玩家 MPB；湿润粒子来自正式 Prefab。
test.runtime("rendering", "player_external_effects", {}, function(data)
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
