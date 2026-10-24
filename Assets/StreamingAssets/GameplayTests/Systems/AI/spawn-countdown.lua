-- 所有自然生物共享世界倒计时；规则和物种只在到期且存在合格落点后参与抽取。
test.system("ai", "spawn_monitor", {}, function(data)
    assert(data.initialized and data.remaining == 7.5 and data.interval == 80 and data.sameClock,
        "监控没有读取世界共享进度，或按物种权重生成了不同计时")
    assert(data.pausedRemaining == 7.5 and data.pausedInterval == 80 and data.unchanged and data.lastObserved == 500,
        "暂停刷新或查看监控修改了真实进度")
    assert(data.pending == 3 and data.otherPending == 0 and data.pendingRemaining == 0 and data.batchUnchanged,
        "未抽中的物种出现待出生队列，或监控消费了共享批次")
    assert(not data.missingInitialized and data.missingRemaining == -1 and data.missingInterval == 50,
        "未初始化的世界时钟被伪装成已经到期")
    assert(data.disabled, "关闭自然生成的规则仍被显示为候选")
end, "世界单一倒计时监控只读，待出生数只属于本轮所选物种")

test.system("ai", "spawn_countdown", {}, function(data)
    assert(data.ruleCount == 9 and data.speciesCount == 10 and data.zombieDisabled,
        "共享生物库遗漏正式规则或错误开启自然尸潮")
    assert(data.initial.pending == 0 and data.initial.remaining == data.interval and data.earlySelectionRejected,
        "首次进入世界未等待完整间隔，或到期前已经绑定物种")
    assert(data.beforeDue.pending == 0 and math.abs(data.beforeDue.remaining - .25) < .000001,
        "世界倒计时提前触发或逐次被重置")
    assert(data.exactDue and data.notChosenAtExpiry,
        "共享计时没有在边界到期，或尚未筛选加载区域就预定了物种")
    assert(data.blockedRound.pending == data.batchSize and data.blockedRound.totalPending == data.batchSize
        and data.blockedRound.replacements == 0 and data.secondSelectionRejected,
        "已有本轮批次时叠加第二物种、离线积压或即时补员")
    assert(data.savedRemaining.pending == 0 and math.abs(data.savedRemaining.remaining - .25) < .000001
        and math.abs(data.offlineRestored.remaining - .25) < .000001,
        "世界存档丢失共享剩余时间或读档补算离线时间")
    assert(data.savedPending.pending == data.batchSize and data.savedSelection,
        "存档丢失本轮唯一的规则、物种或剩余出生数量")
    assert(data.chanceSkipped.pending == data.batchSize - 1 and data.chanceSkipped.lifetime == 0
        and data.chanceSkipped.budget == -1, "独立概率落空未消费机会，或误扣预算和累计出生")
    assert(data.completed.pending == 0 and data.completed.remaining == data.interval
        and data.completed.lifetime == data.batchSize - 1 and data.selectionCleared,
        "完成批次后没有清除所选物种并重启同一个世界时钟")
    assert(not data.nextBeforeDue and data.nextExactDue,
        "批次等待时间被补算为第二轮，或共享倒计时不再推进")
    for index, count in ipairs(data.distribution) do
        assert(math.abs(count / 10000 - data.expectedWeights[index]) <= .00011,
            "跨配置候选没有统一按物种权重抽取：" .. index)
    end
    assert(data.emptySelection == -1 and data.filteredSelection == "SmallFish" and data.weightDoesNotChangeClock,
        "空候选强行指定物种、环境筛选后仍抽中其它生物，或权重影响时钟")
    assert(data.roundChanceSkipped and data.certainChance and not data.zeroChance and data.boundaryChanceRollCalls == 0,
        "概率落空立即重抽到成功，或零/必定概率错误消费随机数")
    assert(data.pausePreservesRemaining and data.frequencyScalesRemaining and data.frequencyScalesInterval,
        "频率关闭后积累时间，或调整频率不保持统一时钟的进度比例")
    assert(not data.preciseBeforeDue and data.preciseExactDue and data.invalidIntervalsRejected == 4,
        "长存档到期精度不足或接纳非法间隔")
    assert(data.loadedAccepted and data.generatingRejected and data.otherWorldRejected
        and data.missingChunkRejected and data.unloadedRejected,
        "生成中、未加载、其它维度或已经卸载的区块加入了自然候选池")
    local counts = {0, 0, 0}
    for _, priority in ipairs(data.busyWorkPriorities) do counts[priority + 1] = counts[priority + 1] + 1 end
    assert(counts[1] == 3 and counts[2] == 3 and counts[3] == 3,
        "出生、显隐和居民装卸失去轮转机会")
end, "世界统一自然刷新：到期后环境候选抽取、存档、暂停、概率与批次不积压")

-- 自然、蜂巢、孵化、技能和手动请求共用同一个管理器的权威与就绪守卫。
test.system("ai", "creature_request", {}, function(data)
    assert(data.valid, "合法生物出生请求被拒绝")
    assert(not data.client and not data.unready, "非权威或世界未就绪仍允许生物出生")
    assert(not data.blank and not data.nan and not data.infinity, "空物种或非有限出生点没有被拒绝")
end, "统一生物出生入口的权威、就绪与参数边界")

test.system("ai", "spawn_batches", {}, function(data)
    local sizes = data.distribution
    assert(data.samples == 200 and sizes[1] == 10 and sizes[2] == 10 and sizes[3] == 90 and sizes[4] == 90,
        "狼批次数量没有保持1/2只各5%、3/4只各45%的权重")
    assert(data.zeroSample == 1 and data.oneSample == 4 and data.radius == 3,
        "批次数量端点或狼同批3格半径配置不正确")
    for _, count in ipairs(data.multiplierCounts) do
        assert(count == 4, "难度频率倍率改变了明确配置的实际批次数量")
    end
    assert(data.initialBatchCount == 4 and data.initialRequiredRoom == 4 and data.continuingRequiredRoom == 1,
        "成批出生没有先预留整批数量门槛，再逐只复核")
    assert(data.blockedPending == 3 and data.chanceRollsAfterWait == 2,
        "批次等待期间重抽了数量、概率，或额外累计多轮")
    assert(data.saved.hasAnchor and data.saved.x == 10.5 and data.saved.y == 20.5 and data.saved.scene == "surface"
        and data.saved.cells == 1 and data.saved.pending == 3, "存档丢失同批落点、已用格或剩余数量")
    assert(not data.duplicateCell and data.insideRadius and data.exactRadius and not data.outsideRadius,
        "同批候选允许叠在已用出生格、跑出半径，或拒绝半径边界")
    assert(data.anchorExactRange and not data.anchorOutOfRange and not data.anchorOtherScene and not data.anchorNoPlayers,
        "玩家离开或切换场景后继续使用旧批次锚点，或距离边界不正确")
    assert(not data.nanAnchor and not data.infiniteAnchor and data.reanchorRequiredRoom == 3,
        "非法锚点没有拒绝，或重选落点时没有按剩余批次预检人数和预算")
    assert(data.wrappedAnchor and data.wrappedCandidate, "环绕世界接缝拆散了同批落点")
    assert(not data.completed.hasAnchor and data.completed.cells == 0 and data.completed.pending == 0
        and data.completed.remaining == data.completed.interval and data.nextBatchCleared,
        "完成或开始下一批后沿用旧锚点和出生格")
    assert(#data.invalidConfigurationsRejected == 11, "批次配置拒绝覆盖缺失")
    for _, rejected in ipairs(data.invalidConfigurationsRejected) do
        assert(rejected, "批次数量、权重、半径或无效全局间隔未拒绝")
    end
    assert(data.fixedCount == data.fixedExpectedCount and data.fixedCountRolls == 0,
        "没有配置数量权重的原规则行为或随机数消耗被改动")
end, "狼成批数量概率、难度倍率、同批落点和保存恢复")
