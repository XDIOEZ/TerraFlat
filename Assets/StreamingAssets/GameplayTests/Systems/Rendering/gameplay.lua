-- 正式石斧经过 ItemMgr 加载后绑定真实玩家 MPB，等待正式 LateUpdate；湿润粒子来自正式 Prefab。
test.runtime("rendering", "player_external_effects", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.realItemLoaded and data.itemId == "Axe_Stone", "真实物品没有通过正式加载流程")
    assert(data.realPlayerController and data.bodyVisible and data.worldSortingMatches, "真实玩家主体或正式世界排序未建立")
    assert(data.registered and data.registrationCount == 1 and data.sharedPlayerMaterial, "外部 Renderer 没有接入玩家或重复绑定/创建了独立材质")
    assert(math.abs(data.submittedBodyMin - data.expectedBodyMin) < 0.0001 and data.customPropertyPreserved, "真实跨帧 MPB 没有合成 Sprite 元数据或覆盖了其他属性")
    assert(data.bindingReleased and data.materialRestored and data.originalBodyMinRestored == -987 and data.originalMarkerRestored == 41, "解除附属绑定没有完整恢复原材质和 MPB")
    assert(data.effectActive and data.sameNonStackableEffect and data.ownerBound and data.ownRegistrations == 1, "正式特效没有启动或非叠加调用创建了重复实例")
    assert(data.particleMaterialsReady and data.ownerReleased and data.pooledInactive and data.particlesCleared, "正式粒子材质、Owner 解绑或回池清空错误")
    assert(data.poseRestored and data.ownRegistrationReleased, "特效回池没有恢复缩放，或重复回收破坏激活登记")
    assert(data.itemReleased and data.ownEffectReleased, "真实物品或特效夹具留下了未清理现场")
end, "真实排序、角色 MPB 跨帧提交/解除、正式湿润粒子与回池恢复")
-- 结果提交前已回收自有物品和粒子，内部 MPB/池状态不拍空地截图。
