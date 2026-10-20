-- 阵营查询沿用正式服务，额外 GC 断言仅在计数器可用时执行。
test.system("combat", "faction_relation_lookup", {}, function(data)
    assert(data.sameFactionFriendly and data.differentFactionHostile and data.neutralFactionNeutral
        and data.emptyFactionHostile, "阵营默认关系或大小写、空值规则变化")
    assert(data.registeredBothDirections and data.ownerConflictRejected and data.neutralOverride
        and data.ownerReplacement and data.ownerCleared and data.candidateIsolated,
        "MOD 阵营关系的双向覆盖、归属冲突、清理或候选隔离失效")
    assert(data.defaultSum == 0 and data.registeredSum == 256,
        "阵营重复查询返回了错误的默认敌对或外部中立关系")
    assert(data.controlSize == 2048, "GC 计数器探针没有实际完成分配对照")
    if data.allocationCounterAvailable then
        assert(data.defaultAllocatedBytes == 0 and data.registeredAllocatedBytes == 0,
            "阵营热查询产生托管分配：默认 " .. data.defaultAllocatedBytes ..
            "B，注册 " .. data.registeredAllocatedBytes .. "B")
    end
end, "阵营无字符串键查询与MOD关系覆盖语义")

-- 正式掉落条目及展开到 GO 的配置必须保持相同分布，不能只改变某一种后端。
test.system("combat", "loot_amount", {}, function(data)
    assert(not data.rejected and data.backendEquivalent, "GO 与原生掉落数量抽样不一致")
    assert(data.minimum == 1 and data.maximum == 20 and data.goMinimum == 1 and data.goMaximum == 20,
        "掉叶子未包含 1 与 20 的端点")
    assert(#data.counts == 20 and data.counts[1] > 0 and data.counts[20] > 0, "端点没有实际概率")
    local total = 0
    for amount, count in ipairs(data.counts) do
        total = total + count
        if amount < 10 then assert(count < data.counts[amount + 1], "低数量侧没有向 10 片递增") end
        if amount > 10 then assert(count < data.counts[amount - 1], "高数量侧没有离开 10 片递减") end
    end
    assert(total == 10000 and math.abs(data.counts[9] - data.counts[11]) <= 2, "抽样丢失数量或峰值偏离 10")
    local peakCurvature = data.counts[9] - 2 * data.counts[10] + data.counts[11]
    for amount = 2, 19 do
        local curvature = data.counts[amount - 1] - 2 * data.counts[amount] + data.counts[amount + 1]
        assert(curvature < 0 and math.abs(curvature - peakCurvature) <= 8, "概率没有沿二次曲线变化")
    end
end, "树叶二次分布：1～20 片且 10 片唯一最高概率")

for _, row in ipairs({
    {minimum = 1, maximum = 4, distribution = "Uniform", peak = 0},
    {minimum = 3, maximum = 3, peak = 3},
    {minimum = 1, maximum = 5, peak = 1},
    {minimum = 1, maximum = 5, peak = 5}
}) do
    test.system("combat", "loot_amount", row, function(data)
        assert(not data.rejected and data.backendEquivalent, "有效数量分布被拒绝或后端不一致")
        assert(data.minimum == row.minimum and data.maximum == row.maximum, "数量分布越界")
        if row.distribution == "Uniform" then
            for _, count in ipairs(data.counts) do assert(count == 2500, "普通掉落不再均匀") end
        elseif row.minimum == row.maximum then
            assert(data.counts[1] == 10000, "固定掉落数量变化")
        else
            for index = 2, #data.counts do
                assert((row.peak == row.minimum and data.counts[index] < data.counts[index - 1]) or
                    (row.peak == row.maximum and data.counts[index] > data.counts[index - 1]), "边界峰值分布方向错误")
            end
        end
    end, "掉落分布：均匀、固定与端点峰值")
end

for _, row in ipairs({{minimum = 0}, {minimum = 20, maximum = 1}, {peak = 0}, {peak = 21}, {distribution = "Unknown"}}) do
    test.system("combat", "loot_amount", row, function(data)
        assert(data.rejected, "无效掉落配置没有在加载时拒绝")
    end, "掉落分布：拒绝非法数量、峰值与分布")
end

test.system("combat", "tree_leaf_configuration", {}, function(data)
    local expected = {AppleTree = true, PeachTree = true, OrangeTree = true, Tree_Coconut = true,
        Tree_Pine = true, Tree_Oak = true, Tree_Willow = true}
    assert(#data.trees >= 7, "树木采样遗漏正式定义")
    for _, tree in ipairs(data.trees) do
        expected[tree.id] = nil
        assert(#tree.leaves == 1, tree.id .. " 缺失或重复掉叶子")
        local leaf = tree.leaves[1]
        assert(leaf.probability == 1 and leaf.minimum == 1 and leaf.maximum == 20 and
            leaf.distribution == "QuadraticPeak" and leaf.peak == 10, tree.id .. " 没有统一树叶掉落配置")
    end
    assert(next(expected) == nil, "已知树种有遗漏")
end, "所有树种都必定掉叶子，包括柳树及继承定义")

local function near(actual, expected, label)
    assert(type(actual) == "number" and math.abs(actual - expected) < .00001, label .. "错误")
end
local damageCases = {
    {name = "只抵扣一次防御", args = {attack = 10, defense = 3}, loss = 7, difficulty = 1},
    {name = "防御完全挡住", args = {attack = 10, defense = 12, received = 2}, loss = 0, difficulty = 1},
    {name = "难度先乘再扣防御", args = {attack = 10, defense = 3, difficulty = 2, received = 2}, loss = 34, difficulty = 1},
    {name = "负攻击无伤害", args = {attack = -1, defense = -3}, loss = 0, difficulty = 1},
    {name = "负倍率钳制", args = {received = -1}, loss = 0, difficulty = 1},
    {name = "怪物攻击玩家", args = {sourcePlayer = false, targetPlayer = true}, loss = 7, difficulty = .5},
    {name = "怪物血量最小归一化", args = {creatureHealth = 0}, loss = 7, difficulty = 20}
}
for _, case in ipairs(damageCases) do
    test.system("combat", "physical", case.args, function(data)
        near(data.physical, case.loss, "物理结算")
        near(data.resolvedTotal, case.loss, "四槽共用结算")
        near(data.difficulty, case.difficulty, "来源难度")
        if case.loss > 0 then near(data.cut, case.loss * .5, "刃伤反馈比例") end
    end, "战斗结算：" .. case.name)
end
local bleedCases = {
    {name = "过量伤害裁剪", args = {}, total = 10, tier = 2},
    {name = "无血目标", args = {blood = false}, total = 10, tier = 0},
    {name = "死亡不新加出血", args = {alive = false}, total = 10, tier = 0},
    {name = "纯钝击", args = {cut = 0, pierce = 0, chop = 0, blunt = 20}, total = 10, tier = 0},
    {name = "随机未命中", args = {random = 1}, total = 10, tier = 0},
    {name = "零生命损失", args = {loss = 0}, total = 0, tier = 0},
    {name = "轻刃伤", args = {cut = 2, pierce = 0, chop = 0, blunt = 0, loss = 2}, total = 2, tier = 1},
    {name = "重刃伤", args = {loss = 100}, total = 20, tier = 3}
}
for _, case in ipairs(bleedCases) do
    test.system("combat", "loss_bleeding", case.args, function(data)
        near(data.actualTotal, case.total, "实际生命损失")
        assert(data.bleeding == case.tier, "出血资格或分级错误")
    end, "实际损失与出血：" .. case.name)
end
for _, change in ipairs({"none", "generation", "world", "dimension", "backend", "value", "invalid"}) do
    test.system("combat", "identity", {change = change}, function(data)
        assert(data.originalValid, "基准身份失效")
        assert(data.sameIdentity == (change == "none"), "完整身份没有区分实例来源")
        assert(data.sameAttack == (change == "none"), "攻击去重身份遗漏字段")
        assert(data.changedValid == (change ~= "invalid"), "零编号身份被接受")
    end, "战斗身份：" .. change)
end
test.system("combat", "identity", {changePulse = true}, function(data)
    assert(data.sameIdentity and not data.sameAttack, "不同脉冲被当成同一攻击")
end, "同一来源不同脉冲独立结算")

test.system("combat", "temporary_invulnerability", {}, function(data)
    assert(not data.initiallyImmune, "普通生命实例不应自带临时无敌")
    assert(data.twoSourcesImmune and data.oneSourceImmune, "释放一个来源误撤销另一个来源的无敌")
    assert(not data.releasedImmune, "最后一个来源释放后仍然无敌")
    assert(not data.unloadedImmune and data.oldDisposeKeptNew and not data.reloadReleasedImmune,
        "卸载后旧租约泄漏或误撤销重新加载的无敌")
    assert(not data.returnedImmune and data.poolOldDisposeKeptNew and not data.takenImmune,
        "对象池生命周期没有隔离无敌来源")
    assert(not data.deadImmune and data.deadAcquireRejected, "死亡后仍保留或取得无敌租约")
    assert(data.deadDisposeKeptRevival and not data.revivalReleasedImmune, "死亡旧租约影响复活后的无敌来源")
end, "临时无敌：多来源独立释放、卸载换代、池化与死亡清理")

test.system("combat", "invulnerability_damage", {}, function(data)
    for _, key in ipairs({"legacyRejected", "targetedLegacyRejected", "contextRejected", "targetedContextRejected",
        "forceRejected", "environmentRejected", "pressureRejected", "legacyZeroImmuneRejected"}) do
        near(data[key], -1, key .. "无敌拒绝")
    end
    near(data.structuralBlocked, 0, "结构损伤拦截")
    near(data.immuneHp, 100, "无敌期间生命")
    near(data.immuneCallbacks, 0, "无敌期间伤害回调")
    near(data.sameTimeZeroHit, 0, "免疫拒绝不消耗普通受击冷却")
    near(data.nextZeroHitCooldown, -1, "有效零伤命中仍消耗普通受击冷却")
    near(data.zeroHitCallbacks, 0, "有效零伤不发布扣血回调")
    near(data.zeroEnvironmentHit, 0, "环境零伤仍可绕过普通受击冷却")
    near(data.forceHpAfterRelease, 95, "无敌解除后的生存伤害")
    near(data.structuralLossAfterRelease, 5, "无敌解除后的结构损伤")
    near(data.releasedHp, 90, "无敌解除后的总生命")
    near(data.releasedCallbacks, 2, "无敌解除后的实际扣血回调")
    near(data.legacyZeroHit, 0, "普通攻击零伤是有效命中")
end, "临时无敌：正式伤害入口、无副作用拒绝与零伤命中区别")
