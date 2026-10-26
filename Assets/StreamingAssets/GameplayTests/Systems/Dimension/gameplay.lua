-- 真实迁移保留同一个主角和网络身份，等待完整活动窗口与正式呈现完成。
test.check("world_ready", "地表隔离世界就绪")
test.check("player_ready", "地表主角就绪")
test.runtime("dimension", "cave_roundtrip", {}, function(data)
    assert(data.beforeEnter.observerDistance >= 0 and data.beforeEnter.observerDistance < .01,
        "普通地表角色的权威入口距离仍使用过期乘员位置")
    assert(data.sameDimensionRejected and data.concurrentRejected, "相同维度或并发迁移没有拒绝")
    assert(data.enterAccepted and data.returnAccepted, "正式维度入口或出口拒绝迁移")
    for _, residents in ipairs({data.ecsResidentsBefore, data.ecsResidentsCave, data.ecsResidentsSurface}) do
        assert(residents.active and residents.sameBridge and residents.sameIdentities and residents.sameSpawned,
            "进出山洞销毁重建了开发模拟，或把同一批 ECS 居民重新生到目标维度")
        assert(residents.residentCount > 0 and residents.residentCount == residents.initialCount and residents.world == residents.originalWorld,
            "居民离开原世界、身份丢失或回程数量重复")
    end
    assert(not data.ecsResidentsCave.activeForLocal and data.ecsResidentsCave.externalPlayers == 0 and
        data.ecsResidentsCave.visible == 0 and data.ecsResidentsCave.batches == 0 and not data.ecsResidentsCave.localSuspended,
        "原世界居民仍在洞穴绘制、追踪玩家，或开发暂停错误地停止了洞穴实体世界")
    assert(data.ecsResidentsSurface.activeForLocal and data.ecsResidentsSurface.externalPlayers > 0,
        "返回地表后没有恢复原居民的玩家代理")
    for _, state in ipairs({data.enterPending, data.returnPending}) do
        assert(state.loadingVisible and state.inputLocked and state.inputOwners > 0,
            "入口请求发出时没有立即显示过渡页并取得独立输入锁")
        assert(state.bodyPresent and not state.simulated and state.velocitySqr == 0,
            "等待服务端准备区块时主角仍保留惯性或物理移动")
    end
    assert(data.enterPending.loadingTarget == "cave" and data.returnPending.loadingTarget == "surface",
        "入口请求期间加载页没有显示本次目标维度")
    for _, protection in ipairs({data.enterProtection, data.returnProtection}) do
        assert(protection.frames > 0 and protection.unlockedFrames == 0 and protection.missingOwnerFrames == 0,
            "维度准备、绘制或加载幕退场期间提前释放了迁移输入租约")
    end
    assert(not data.afterEnter.loadingVisible and not data.afterReturn.loadingVisible,
        "正式维度事务结束后加载幕仍未退场")
    assert(data.afterEnter.simulated == data.beforeEnter.simulated and data.afterReturn.simulated == data.beforeEnter.simulated,
        "复用请求阶段保护时错误地将已经冻结的刚体状态当成原始状态恢复")
    assert(data.afterEnter.running == data.beforeEnter.running and data.afterReturn.running == data.beforeEnter.running,
        "进出山洞改变了原有的奔跑偏好")
    assert(data.enterMigrationExpected and data.returnMigrationExpected, "正式洞穴往返没有登记宿主预期迁移租约")
    assert(data.enterMigrationReleased and data.returnMigrationReleased and data.migrationReleased, "维度事务完成后仍遗留迁移租约")
    assert(data.cave.address == data.cave.expected and data.cave.scene == data.cave.address and data.cave.savedScene == data.cave.address, "洞穴运行地址、Scene 与玩家存档不一致")
    assert(data.cave.profile == data.profile and data.cave.guid == data.originalGuid and data.sameDataInCave, "洞穴主角没有保持同档案、GUID 与数据归属")
    assert(data.cave.instance == data.originalInstance and data.surface.instance == data.originalInstance,
        "维度迁移销毁或重建了真实玩家实例")
    assert(data.cave.networkIdentity == data.originalNetworkIdentity and data.surface.networkIdentity == data.originalNetworkIdentity,
        "维度迁移替换了本机网络身份")
    assert(data.cave.sourceWorldRetained and data.cave.targetRuntimePresent and data.surface.targetRuntimePresent,
        "迁移卸载了其他玩家仍可使用的源世界，或目标缺少正式世界运行时")
    assert(data.cave.initialized and data.cave.windowReady and not data.cave.transitioning, "洞穴只创建了场景却没有完成主角和区块窗口准备")
    assert(data.cave.controllerPresent and data.cave.gameplayReady and not data.cave.inputExplicitlyLocked and not data.cave.loadingGateLocked, "洞穴迁移没有解除显式加载锁或恢复可玩状态：" .. tostring(data.cave.inputLockState))
    assert(data.cave.suppressWeather, "洞穴没有应用维度天气覆盖")
    assert(data.cave.externalControlPresent and data.cave.testControlOwned, "洞穴角色没有保留测试控制隔离")
    assert(data.exitCreated and data.exitModulePresent and data.anchorFound, "正式迁移没有产生可使用的配对矿洞出口")
    assert(data.anchorSurface == data.originalSurface, "矿洞出口配到了另一个地表")
    assert(data.surface.address == data.originalSurface and data.surface.scene == data.originalSurface and data.surface.savedScene == data.originalSurface, "返回地表地址或玩家存档错误")
    assert(data.surface.profile == data.profile and data.surface.guid == data.originalGuid and data.sameDataAfterReturn, "回程主角数据归属发生变化")
    assert(data.surface.initialized and data.surface.windowReady and not data.surface.transitioning and data.surface.gameplayReady, "地表回程未完成完整窗口与可玩态恢复")
    assert(not data.surface.inputExplicitlyLocked and not data.surface.loadingGateLocked, "地表回程仍遗留加载输入门禁：" .. tostring(data.surface.inputLockState))
    assert(data.surface.externalControlPresent and data.surface.testControlOwned, "地表角色没有保留测试控制隔离")
    for _, state in ipairs({data.cave, data.surface}) do
        assert(state.inputLocked == (state.inputExplicitlyLocked or state.inputLockOwnerCount > 0 or not state.gameplayReady), "总输入锁与原始锁来源不一致")
    end
    assert(data.returnNearEntrance <= 4, "配对回程没有回到入口附近")
    assert(data.restored, "维度测试没有恢复原地表和主角位置")
end, "真实矿洞往返与完整恢复")
test.check("world_ready", "矿洞往返恢复后世界仍就绪")
test.check("player_ready", "维度往返后宿主保留同一主角")
test.runtime("dimension", "restored_control", {}, function(data)
    assert(data.runtimeActive, "维度回程复用了正在销毁的区块管理器")
    assert(data.controllerPresent and data.gameplayReady and not data.inputExplicitlyLocked and not data.loadingGateLocked, "回程后真实加载门禁没有解除：" .. tostring(data.inputLockState))
    assert(data.externalControlPresent and data.testControlOwned and data.externalControlOwner == "GameplayTestLuaRunner", "回程后控制没有交回本轮 Lua 租约")
    assert(not data.transitioning, "回程控制采样仍处于迁移事务")
end, "回程加载门禁解除并交回 Lua 控制")
