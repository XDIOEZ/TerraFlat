test.system("ai", "spawn_countdown", {}, function(data)
    local animalCount = 0
    for _, rule in ipairs(data.catalog) do
        if rule.id == "ghost_milestone_growth" then
            assert(rule.mode == "DayMilestoneGrowth", "幽灵逐日增长规则被改动")
        elseif rule.id == "zombie_horde" then
            assert(rule.mode == "TimedWindows", "僵尸固定窗口规则被改动")
        else
            assert(rule.mode == "Countdown", "自然动物仍使用固定日窗口：" .. rule.id)
            animalCount = animalCount + #rule.entries
            for _, entry in ipairs(rule.entries) do
                assert(entry.interval > 0, "动物倒计时间隔无效：" .. entry.species)
            end
        end
    end
    assert(animalCount == 8, "正式八种自然动物没有全部迁移独立倒计时")
    assert(data.initial.pending == 0 and data.initial.remaining == data.interval,
        "首次进入世界没有等待完整间隔")
    assert(data.beforeDue.pending == 0 and math.abs(data.beforeDue.remaining - .25) < .000001,
        "倒计时在到期前提前生成，或连续推进时被重置")
    assert(data.exactDue.pending == data.batchSize and data.exactDue.remaining == 0,
        "恰好到期没有排入指定物种的一轮出生")
    assert(data.blockedRound.pending == data.batchSize and data.blockedRound.totalPending == data.batchSize,
        "地点、数量或预算受阻等待期间丢掉当前机会，或累计了额外多轮")
    assert(data.blockedRound.replacements == 0, "即时补员绕过了倒计时")
    assert(data.savedRemaining.pending == 0 and math.abs(data.savedRemaining.remaining - .25) < .000001,
        "二进制存档丢失未完成的倒计时")
    assert(data.offlineRestored.pending == 0 and math.abs(data.offlineRestored.remaining - .25) < .000001,
        "读档后把离线时段补进倒计时")
    assert(data.savedPending.pending == data.batchSize and data.savedPending.totalPending == data.batchSize,
        "二进制存档丢失了正在等待落点的指定物种机会")
    assert(data.chanceSkipped.pending == data.batchSize - 1 and data.chanceSkipped.lifetime == 0
        and data.chanceSkipped.budget == -1, "独立概率落空没有消费一次机会，或错误累计出生/消耗预算")
    assert(data.completed.pending == 0 and data.completed.remaining == data.interval
        and data.completed.lifetime == data.batchSize - 1,
        "整轮完成后未重新等待完整间隔，或出生累计不正确")
    assert(data.nextBeforeDue.pending == 0 and data.nextExactDue.pending == data.batchSize,
        "等待地点期间的时间被追补为第二轮，或后续倒计时没有继续")
    assert(data.independentChicken.pending == data.mixedBatchSize and data.independentBoar.pending == 0
        and data.independentBoar.remaining > 0, "小鸡和野猪仍共用一个倒计时")
    assert(math.abs(data.weightedIntervalRatio - data.originalWeightRatio) < .00001,
        "小鸡和野猪拆为独立倒计时后丢掉原有相对稀有度")
    assert(data.bothDueTotal == data.mixedBatchSize * 2, "不同物种的当前轮次没有独立保留")
    local order = data.blockedSelectionOrder
    assert(order[1] == "Chicken" and order[2] == "WildBoar" and order[3] == "Chicken" and order[4] == "WildBoar",
        "一种动物找不到位置时挡住另一种动物，或重试时重新随机更换当前轮的物种")
    assert(data.otherSpeciesPendingAfterSkip.pending == data.mixedBatchSize,
        "小鸡消费机会时改动了野猪的独立队列")
    assert(data.roundChanceSkipped.pending == 0 and data.roundChanceSkipped.remaining > 0,
        "整轮概率落空后立即反复重抽到成功")
    assert(data.certainChancePending == data.mixedBatchSize and data.zeroChancePending == 0
        and data.boundaryChanceRollCalls == 0,
        "零/必定概率仍抽随机数，或随机值恰好为一时错过必定出生")
    assert(not data.preciseBeforeDue and data.preciseExactDue,
        "长时间存档的倒计时精度不足以判断到期")
    assert(data.invalidIntervalsRejected == 4, "倒计时接纳了零、负数或非有限间隔")
    local counts = {0, 0, 0}
    for _, priority in ipairs(data.busyWorkPriorities) do
        counts[priority + 1] = counts[priority + 1] + 1
    end
    assert(counts[1] == 3 and counts[2] == 3 and counts[3] == 3,
        "连续跑图时出生、显隐和居民装卸没有轮流获得优先机会")
end, "自然动物持续倒计时、独立物种、存档恢复与调度公平")

-- 自然、蜂巢、孵化、技能和手动请求共用同一个管理器的权威与就绪守卫。
test.system("ai", "creature_request", {}, function(data)
    assert(data.valid, "合法生物出生请求被拒绝")
    assert(not data.client and not data.unready, "非权威或世界未就绪仍允许生物出生")
    assert(not data.blank and not data.nan and not data.infinity, "空物种或非有限出生点没有被拒绝")
end, "统一生物出生入口的权威、就绪与参数边界")
