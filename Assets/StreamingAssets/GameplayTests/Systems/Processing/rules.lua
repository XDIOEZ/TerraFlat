local function near(a, b, message)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= 0.00001, message)
end

test.system("processing", "recipe_selection", {}, function(data)
    near(data.sameProgress, 4, "重复绑定同配方清除了真实加工进度")
    near(data.sameRatio, 0.4, "加工进度比例错误")
    near(data.changedProgress, 0, "更换配方保留旧进度")
    assert(data.invalidWork == "ArgumentOutOfRangeException", "零加工工时被接受")
    assert(data.clearedId == "" and data.clearedRatio == 0, "清配方后残留配方或进度显示")
    assert(not data.zeroAdvance and not data.negativeAdvance, "零或负时间仍提交加工")
end, "同配方进度保持、换配方清零与非法工时")

test.system("processing", "slot_identity", {}, function(data)
    near(data.unchangedElapsed, 5, "未变来源清除了整槽计时")
    assert(data.unchangedPending == 3, "未变来源清除了已抽定产量")
    assert(data.changed.elapsed == 0 and data.changed.pending == -1 and data.changed.pendingItem == "",
        "真实来源数量变化没有清除旧计时和待产物")
    assert(data.emptySignature == "" and data.slots == 2 and data.secondElapsed == 0 and data.secondPending == -1,
        "清空来源或新槽默认状态错误")
end, "整槽来源身份变化与已抽定产量重置")

test.system("processing", "atomic_output", {}, function(data)
    assert(not data.blocked.advanced and data.blocked.commits == 0, "满输出时加工仍提交库存")
    assert(data.blocked.progress == 5 and data.blocked.input == 4 and data.blocked.output == 1,
        "输出无法同时放下全部产物时丢进度、扣原料或部分产出")
    assert(data.resumed.advanced and data.resumed.commits == 1, "清出空间后没有恢复加工事务")
    assert(data.resumed.input == 2 and data.resumed.output == 2 and data.resumed.progress == 1,
        "恢复加工没有按实际工时一次扣料并提交两个产物")
    assert(not data.fullAdvanced and data.finalProgress == 1 and data.finalInput == 2 and data.finalOutput == 2
        and data.finalCommits == 1, "再次满输出后继续推进或重复产出")
end, "真实加工进度、满输出原子拒绝与恢复产出")

test.system("processing", "cold_fuel_candidates", {}, function(data)
    assert(data.sourceCount == 834 and #data.candidateIds == 2, "后台炉体把全世界掉落物送去排序，或漏掉本格候选")
    assert(data.candidateIds[1] == 2 and data.candidateIds[2] == 1, "本格燃料未按近先顺序，或飞行、邻格、重复身份和不可拾取记录混入候选")
    assert(data.originalPresent and data.shiftedPresent, "候选列表下标移动后错误拒绝仍存在的实物")
    assert(not data.removedPresent and not data.replacedArchivePresent, "已删除实物或被替换的世界档案仍允许扣料")
    assert(data.wrappedCount == 1 and data.wrappedGuid == 90 and data.unwrappedCount == 0,
        "后台炉体借用了其它世界的环绕尺寸或格子")
    assert(data.farRecordStillCold, "筛选本格时补热了远处掉落物")
end, "后台炉体只排序本格候选、距离顺序、身份失效与独立世界环绕")

test.system("processing", "cold_fuel_transfer", {}, function(data)
    near(data.blocked, 0, "拒绝存入的燃料槽仍接收了物品")
    near(data.afterBlocked, 5, "燃料槽拒绝后扣除了地面实物")
    near(data.partial, 2, "容量不足时没有按实际接纳量搬运")
    near(data.sourceAfterPartial, 3, "部分接纳后地面剩余数量错误")
    near(data.targetAfterPartial + data.sourceAfterPartial, 5, "燃料转移前后数量不守恒")
    assert(data.splitIdentity, "部分搬运复用了地面剩余物品的身份")
    near(data.full, 0, "满燃料槽仍重复吸料")
    near(data.sourceAfterFull, 3, "满燃料槽吞掉了地面剩余燃料")
    near(data.resumed, 2, "空出燃料槽后没有恢复正式转移")
    near(data.finalSource + data.finalTarget, 3, "恢复后没有按本次真实余量守恒")
end, "后台燃料实际库存接纳、满槽拒绝、分堆身份与恢复守恒")
