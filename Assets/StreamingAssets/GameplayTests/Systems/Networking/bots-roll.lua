-- 请求经过正式 Mirror Command，验证服务器实际扣费、无敌、拒绝和生命周期收尾。
test.check("world_ready", "联机翻滚世界就绪")
test.check("player_ready", "联机翻滚本地主角就绪")

test.runtime("networking", "bots_roll", {}, function(data)
    assert(data.transport == "mirror-in-memory" and data.scope == "live-isolated-world", "翻滚没有经过隔离世界正式网络入口")
    assert(data.oldRealmRejected, "过期世界翻滚请求在服务器扣费或取得无敌，或吞掉同序号新请求")
    assert(not data.moduleInitiallyLoaded and data.started and data.invulnerable,
        "未完整加载的服务器远端玩家不能合法翻滚，或没有取得无敌")
    assert(data.paidStamina < data.beforeStamina and data.cost == 10, "服务器没有按正式体力入口支付翻滚费用")
    assert(data.hurtDuringRoll < 0 and data.hpDuringRoll == data.hpBefore, "服务器翻滚无敌期间仍受到伤害")
    assert(data.staminaAfterStaleSnapshot == data.paidStamina, "扣费前的客户端旧整包补回了翻滚体力")
    assert(data.sequenceAfterDuplicate == data.firstSequence and data.directionAfterDuplicateX > 0,
        "同一请求被重复发动或改变了已确定的翻滚方向")
    assert(not data.invulnerableAfterEnd and data.cooldownAfterEnd == 0, "翻滚结束仍留有无敌或非零冷却")
    assert(data.physicalOwnershipUnchanged, "服务器翻滚改变远端移动锁或物理模拟归属")
    assert(data.nonFiniteRejected and data.emptyStaminaRejected, "非法方向或体力不足时仍发动翻滚")
    assert(data.secondStarted and data.secondPaidStamina < data.paidStamina, "上一轮结束后无法立即翻滚或未再次扣费")
    assert(data.rebindProtectedStamina == data.secondPaidStamina and data.rebindInvulnerabilityCleared,
        "动作组件解绑丢失扣费保护，或遗留临时无敌")
    assert(data.disconnectedRuntimeRemoved, "翻滚中断线遗留远端玩家运行实例")
    local c = data.cleanup
    assert(not c.serverActive and not c.clientActive and c.connections == 0 and c.spawned == 0,
        "翻滚夹具遗留服务器、连接或出生对象")
    assert(c.localUnchanged and c.sceneUnchanged and c.transportRestored and c.profilesRestored
        and c.botRuntimeLeft == 0 and c.botPassengersLeft == 0, "翻滚夹具没有完整恢复隔离现场")
end, "联机翻滚：真实服务器许可、扣费、防旧包、零冷却接续与无敌收尾")
