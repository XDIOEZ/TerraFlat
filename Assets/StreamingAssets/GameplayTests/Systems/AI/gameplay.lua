-- 生物实际出生、推进命令、异步寻路、沿路移动与收尾属于同一条现场链。
test.check("world_ready", "AI 与导航共用真实隔离世界")
test.check("terrain_ready", "自有生物所在区块已加载")
test.runtime("ai", "aquatic_habitat", {}, function(data)
    assert(data.frogDrySafe and data.fishDryDamage, "两栖上岸仍搁浅或旧鱼失去干涸伤害")
    assert(data.waterKnown and data.waterDepth >= .05 and data.shoreline and data.fishWater,
        "真实水体或两栖水陆连通未被识别")
    assert(data.fishCannotCrossLand and data.lavaKnown and data.lavaDepth == 0 and data.lavaRejected,
        "纯水生越过陆地，或岩浆被当成水域/两栖地面")
end, "真实两栖、水生和非水液体栖息规则")
test.runtime("ai", "aquatic_combat", {}, function(data)
    assert(data.shoreBlocked and data.connected, "鲨鱼没有按真实隔岸和连通水路筛选目标")
    assert(data.declaredPreyGeometry and data.preyRootCollisionDisabled and
        data.sharkWorld ~= 0 and data.sharkWorld == data.preyWorld and data.visibleOnDetection,
        "猎物的感知几何、禁用物理碰撞、世界归属或真实视线错误")
    assert(data.detectorApplied and data.detectorSeesPrey, "真实感知未完成或未发现水中猎物")
    assert(data.moveRequested and data.afterDistance < data.beforeDistance and data.attackStarted and data.lockStoppedMovement,
        "水路接通后鲨鱼未实际接近、打开攻击窗口，或攻击锁定时仍主动前进")
    assert(data.attackStoppedAtRange, "猎物离开攻击距离后旧窗口仍然保持")
    assert(data.waterTargetAccepted and data.shoreTargetRejected and data.externalRejected,
        "攻击窗口没有筛选每个真实接收者，或错误占用岸上/未知目标的名额")
    assert(data.restrictionActive and data.restrictionCleared,
        "水生攻击目标限制未随动作进入和中断正确启停")
end, "水中追击、隔岸拒绝与攻击窗口")
test.runtime("ai", "flight_land_predation", {}, function(data)
    assert(data.frogDetected and data.preyDetected, "鹰没有经真实感知发现陆生猎物")
    assert(data.acquired and data.firstRunning and data.wounded and data.killCompleted,
        "飞行捕食者没有锁定陆生猎物、持续俯冲并通过正式伤害击杀")
    assert(data.ordinaryThreatDetected and data.preyDoesNotScare,
        "普通威胁控制或捕食者不被猎物吓跑的配置未生效")
    assert(data.cancelled, "取消捕猎后仍保留俯冲目标")
    assert(data.sharkWeight > 5 and data.heavyRejected and data.lightAccepted,
        "飞行捕食没有按实际猎物重量拒绝超重目标并接受轻型目标")
end, "陆生猎物俯冲、取消与捕食体重限制")
test.runtime("ai", "flight_light_avoidance", {}, function(data)
    assert(data.initiallySafe and data.bright and data.selected and data.seekStarted and data.advanced and
        data.presentationAdvanced and data.escaped and data.sourceStayedBright,
        "飞行生物未识别强光、选择可达暗点或实际离开光区")
end, "真实光照下的飞行避光")
test.runtime("ai", "advance_navigation", {}, function(data)
    assert(data.scope == "live-isolated-world", "没有采样真实隔离世界")
    assert(data.actorGuid ~= 0 and data.actorInitialized and data.aiLoaded and data.moverLoaded,
        "正式生成没有完成生物、AI 或移动模块加载")
    assert(data.limitCallbackReceived and not data.limitAccepted and data.limitRejectedByCost,
        "实际导航请求没有按路径代价拒绝")
    assert(data.playerThreatExcluded, "自有测试生物仍会优先避让近处玩家，无法独立检查推进")
    assert(data.commandAccepted and data.commandStored, "正式 AI 推进命令未写入真实生物")
    assert(data.pathAccepted and data.gridRevision >= 0, "真实导航代理没有获得可执行路径")
    assert(data.maximumDisplacement > 1 and data.maximumSpeed > .01,
        "生物仅接受命令却没有经过正式 Tick 实际移动")
    assert(data.stayedWalkable, "移动中的生物进入真实不可走格")
    assert(data.reached and data.distanceToGoal <= .65 and data.commandClearedAtArrival,
        "真实生物没有到达目标或到达后仍保留推进命令")
    assert(data.cancelAccepted and not data.hasDestinationAfterCancel and
        not data.hasPathAfterCancel and not data.pendingAfterCancel, "取消命令后旧路径仍然运行")
    assert(data.cleanupRegistered, "自有生物没有登记失败时的收尾")
end, "正式生物推进、导航代价拒绝、移动到达和取消")
test.runtime("ai", "cleanup", {}, function(data)
    assert(data.hadFixture and data.released and data.actorRemoved, "自有测试生物未清理")
end, "删除自有生物并撤销正式导航代理")
