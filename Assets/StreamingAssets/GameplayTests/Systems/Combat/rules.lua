-- 投掷费用读取石头正式配置，距离、数量和非法参数均交给生产规则。
test.system("combat", "discard_throw_stamina", {}, function(data)
    assert(data.baseCost == 1 and data.costPerMeter == .5, "石头投掷体力配置错误")
    local costs = data.costs
    assert(costs.atFeet == 1 and costs.near == 2 and costs.far == 6,
        "石头没有按实际投掷距离增加体力消耗")
    assert(costs.stack == costs.far * 3 and costs.empty == 0,
        "整组投石漏算数量，或空组仍扣体力")
    assert(costs.free == 0 and data.otherItemsFree, "未配置投掷费用的物品被额外收费")
    assert(data.invalidRejected, "非法距离、费用或溢出没有拒绝")
end, "投石：距离递增、整组计费与费用边界")

-- 雪球读取正式配置与投掷结果，区分有效命中和实际扣血。
test.system("combat", "snowball_throw", {}, function(data)
    local modules, samples, throwable = data.modules, {}, false
    for _, tag in ipairs(data.tags) do if tag == "Throwable" then throwable = true end end
    assert(data.worldDropBehavior == "interactive" and throwable,
        "雪球没有保留完整投掷模块或缺少可投掷标记")
    assert(modules.discardFlightDamage.enabled and
        modules.discardFlightDamage.prefab == data.stoneFlightPrefab and
        modules.discardFlightDamage.id == "Mod_DiscardFlightDamage", "雪球没有复用石头的丢弃飞行伤害")
    local damage = modules.damage
    assert(damage.enabled and damage.id == "Mod_Damage" and
        damage.parameters.DamageValues.Physical == 1 and damage.parameters.DamageValues.ImpactKind == 4 and
        damage.parameters.MaxAttackTargets == 1 and not damage.parameters.OnlyDealDamageWhenInHand,
        "雪球伤害不是1点钝击，或飞行伤害条件错误")
    assert(modules.snow.parameters.shatterChance == .5 and data.breaksPer10000 == 5000,
        "雪球碎裂概率不是50%")
    for _, sample in ipairs(data.samples) do samples[sample.name] = sample end
    for _, name in ipairs({"miss_shatter", "rejected", "invalid_damage", "certain"}) do
        assert(not samples[name].hit and not samples[name].damage and samples[name].result == "ScatterSnow",
            name .. "：未命中碎裂没有允许铺一层雪")
    end
    for _, name in ipairs({"zero_hit", "hit_then_rejected"}) do
        assert(samples[name].hit and not samples[name].damage and samples[name].result == "Shatter",
            name .. "：零伤有效命中被当成未命中或错误铺雪")
    end
    for _, name in ipairs({"damage_hit", "damage_then_zero"}) do
        assert(samples[name].hit and samples[name].damage and samples[name].result == "Shatter",
            name .. "：实际扣血命中被漏记或错误铺雪")
    end
    for _, name in ipairs({"miss_keep", "hit_keep", "disabled", "invalid_chance", "invalid_roll"}) do
        assert(samples[name].result == "Keep", name .. "：概率边界、关闭或非法参数没有保留雪球")
    end
end, "雪球：石头投掷复用、1点伤害、50%碎裂及零伤命中与未命中铺雪区别")

-- 弓弩读取正式继承配置，推进正式蓄力时钟，不在 Lua 重新实现动作逻辑。
test.system("combat", "archery_cycle", {steps = {
    {action = "press"}, {seconds = .4}, {seconds = 1.1}, {seconds = 3},
    {action = "release"}, {seconds = 20}, {action = "release"},
    {action = "press"}, {seconds = 10}, {action = "release"}, {action = "release"}
}}, function(data)
    local p, reference, s = data.parameters, data.recurveParameters, data.samples
    local launch = data.launchPositionProbe
    local expectedWeaponLaunchX = launch.weapon.x + launch.direction.x * launch.forwardOffset
    local expectedWeaponLaunchY = launch.weapon.y + launch.direction.y * launch.forwardOffset
    local ownerOriginLaunchX = launch.owner.x + launch.direction.x * launch.forwardOffset
    local ownerOriginLaunchY = launch.owner.y + launch.direction.y * launch.forwardOffset
    assert(math.abs(launch.position.x - expectedWeaponLaunchX) < .0001
        and math.abs(launch.position.y - expectedWeaponLaunchY) < .0001
        and math.abs(launch.position.x - ownerOriginLaunchX) > .1
        and math.abs(launch.position.y - ownerOriginLaunchY) > .1,
        "箭矢生成点没有从手持弓的位置前移")
    assert(data.mode == "LoadThenReleaseToFire" and p.AmmoTag == "Arrow", "弩没有使用两次松开发射或普通箭矢")
    for _, key in ipairs({"FullChargeSeconds", "ProjectileDamageMultiplier", "MaxAimDistance",
        "SpawnForwardOffset", "StaminaConsumePerSecond"}) do
        assert(p[key] == reference[key], "弩的数值没有沿用反曲弓：" .. key)
    end
    assert(p.FullChargeSeconds == 1 and p.ProjectileDamageMultiplier == 1.5
        and p.StaminaConsumePerSecond == 2.25 and data.woodBowStamina == 2.25
        and data.blowgunStamina == 1.5, "弓体力未乘1.5或误改吹箭体力")
    assert(s[1].started and s[3].charge == 1 and s[4].shots == 0, "蓄满自动发射或蓄力比例错误")
    assert(s[5].release == "Loaded" and s[5].active and s[5].shots == 0,
        "弩第一次松手没有保留上膛状态")
    assert(s[7].release == "None" and s[7].shots == 0, "没有再次按下也能发射")
    assert(not s[8].started and s[9].charge == 1 and s[9].shots == 0, "再次按下重新蓄力或提前发射")
    assert(s[10].release == "Fire" and s[10].shots == 1 and not s[10].active
        and s[11].shots == 1, "第二次松手没有发射或重复发射")
    for index = 3, #s do
        assert(math.abs(s[index].stamina - 2.25) < .00001, "蓄满后或上膛待发时继续耗体力")
    end
end, "弩：反曲弓数值、满蓄力停止耗体力、两次松开与防重复发射")

test.system("combat", "archery_cycle", {steps = {
    {action = "press"}, {seconds = .4}, {action = "release"},
    {action = "press"}, {seconds = .6}, {action = "release"},
    {action = "press"}, {seconds = 1}, {action = "release"},
    {action = "cancel"}, {action = "press"}, {action = "release"}
}}, function(data)
    local s = data.samples
    assert(s[3].release == "Cancelled" and not s[3].active and s[3].charge == 0,
        "未满蓄力松手没有取消")
    assert(s[6].release == "Cancelled" and data.shots == 0, "未满进度跨按键累积或发射")
    assert(s[9].release == "Loaded" and not s[10].active and s[10].charge == 0,
        "取消操作没有清除已上膛状态")
    assert(s[11].started and s[12].release == "Cancelled", "取消后仍可免上膛发射")
end, "弩：未蓄满取消、进度不保留与清除上膛状态")

test.system("combat", "archery_cycle", {weaponId = "Bow_Recurve", steps = {
    {action = "press"}, {seconds = .25}, {action = "release"},
    {action = "press"}, {seconds = 1}, {seconds = 2}, {action = "release"}
}}, function(data)
    local s = data.samples
    assert(data.mode == "ReleaseToFire" and s[3].release == "Fire" and s[3].shots == 1,
        "普通弓未满蓄力松手不再发射")
    assert(s[6].shots == 1 and s[7].shots == 2 and s[7].release == "Fire",
        "普通弓被改为自动发射或需要第二次松开")
    assert(math.abs(s[6].stamina - 7.3125) < .00001, "普通弓满蓄力持箭不再持续消耗体力")
end, "反曲弓：松手即发射与满蓄力持续耗体力")

-- 投石索弹药必须在正式继承后具备投射物与伤害模块。
test.system("combat", "archery_cycle", {weaponId = "StoneThrowingRope", ammoIds = {
    "SlingAmmo_Base", "Ore_Stone", "Ore_Copper", "Ore_Iron", "Ore_Tin", "Ore_Gold", "Clay", "Ore_Niter"
}}, function(data)
    local p, ammo = data.parameters, {}
    for _, entry in ipairs(data.ammunition) do ammo[entry.id] = entry end
    assert(p.AmmoItemId == "Ore_Stone" and p.AmmoTag == "SlingAmmo" and not p.UseHeldItemAsAmmo,
        "投石索没有从库存选取石头和矿石弹药")
    local reference = ammo.SlingAmmo_Base.modules.projectile.parameters
    for _, id in ipairs({"Ore_Stone", "Ore_Copper", "Ore_Iron", "Ore_Tin", "Ore_Gold"}) do
        local modules = ammo[id].modules
        assert(modules.projectile and modules.projectile.enabled
            and modules.projectile.id == "Mod_Projectile" and modules.projectile.prefab == "Mod_Projectile",
            id .. " 缺少启用的正式投射物模块")
        assert(modules.damage and modules.damage.enabled and modules.damage.id == "Mod_Damage",
            id .. " 缺少投射物依赖的伤害模块")
        for key, value in pairs(reference) do
            assert(modules.projectile.parameters[key] == value, id .. " 没有沿用投石索弹丸参数：" .. key)
        end
    end
    local stone = ammo.Ore_Stone.modules
    assert(stone.damage.parameters.DamageValues.Physical == 12
        and stone.damage.parameters.DamageValues.ImpactKind == 4
        and stone.damage.parameters["$collider2D"].size.x == .4375, "石头伤害或命中范围被继承覆盖")
    assert(stone.discardFlightDamage.enabled and stone.discardFlightDamage.id == "Mod_DiscardFlightDamage"
        and stone.discardFlightDamage.parameters.HardImpactTransformItemId == "ChippedTool"
        and stone.discardFlightDamage.parameters.HardImpactTransformChance == .1,
        "石头丢弃飞行伤害或硬目标变化配置丢失")
    for _, id in ipairs({"Clay", "Ore_Niter"}) do
        assert(id ~= p.AmmoItemId, "投石索错误地指定软质材料为弹药")
        for _, tag in ipairs(ammo[id].tags) do
            assert(tag ~= p.AmmoTag, id .. " 继承后错误地获得投石索弹药资格")
        end
    end
end, "投石索：石头与矿石投射模块、继承参数、原有丢弃能力与软质材料边界")

-- 命中手感采样正式动画时钟与材质合成，不用测试侧算法替代生产实现。
test.system("combat", "hit_feedback_clock", {}, function(data)
    local function near(actual, expected)
        return type(actual) == "number" and math.abs(actual - expected) < .00001
    end
    assert(near(data.defaultStop, .12) and near(data.defaultShake, .16) and near(data.defaultDistance, .035),
        "卡肉或轻抖默认值错误")
    assert(data.zeroRejected and data.negativeRejected and data.invalidRejected and data.disabledRejected
        and data.invalidDurationRejected and data.invalidClockRejected, "无伤害、关闭或非法参数触发了卡肉")
    assert(data.started and data.multiTargetRejected and data.earlyResumeRejected and data.resumed
        and data.sameAttackCannotRestart, "单段挥击重复卡肉或恢复时机错误")
    assert(near(data.frozen, 10) and near(data.resumeTime, 10) and near(data.advancedTime, 10.05),
        "卡肉消耗了连击动画时钟")
    assert(data.nextAttackStarted and data.cancelCleared and data.slowTimeResumed
        and near(data.slowTimePreserved, 30.05) and data.pausedGameResumed, "下一段、取消、慢速或暂停游戏后恢复错误")
    assert(near(data.peak, .035) and near(data.firstKick, -.035) and near(data.fallbackKick, .035)
        and data.settled and data.disabledShake and data.invalidShake and near(data.stopClamped, .25),
        "抖动幅度、方向、停止归零或参数上限错误")
end, "卡肉：正伤害、每段一次、连击时钟与取消；轻抖：方向、幅度及归零")

test.system("combat", "hit_shake_render", {}, function(data)
    assert(math.abs(data.shaderOffset - .035) < .00001 and data.boundsExpanded, "材质位移未提交或裁剪边界未扩展")
    assert(data.rootUnchanged and data.visualTransformUnchanged and data.colliderUnchanged,
        "视觉轻抖改变了逻辑根、动画节点或碰撞体")
    assert(math.abs(data.otherChannelPreserved - .37) < .00001
        and math.abs(data.disabledOtherChannel - .37) < .00001, "轻抖清理覆盖了其它材质通道")
    assert(math.abs(data.repeatedAmplitude - .035) < .00001, "重复命中叠加抖动幅度")
    assert(data.finishedOffset == 0 and data.disabledOffset == 0 and data.pooledOffset == 0
        and data.boundsRestored and data.pooledBoundsRestored, "结束、停用或回池残留位移/裁剪边界")
end, "目标轻抖：正式材质合成、碰撞隔离、重复命中及停用回池清理")

-- 阵营查询沿用正式服务，额外 GC 断言仅在计数器可用时执行。
test.system("combat", "equipment_durability", {}, function(data)
    assert(data.wood == 30 and data.stone == 300 and data.bronze == 1000 and data.iron > data.bronze
        and data.obsidian == 50, "装备材质耐久档位错误")
    assert(data.obsidianNonWeapon == 777 and data.unclassified == 60,
        "黑曜石武器特例误改非武器装备，或未知材质覆盖原定义耐久")
    assert(data.coldWeaponMaximum == 50 and data.coldWeaponTagged and data.obsidianToolMaximum == 777,
        "旧冷兵器漏接统一磨损，或黑曜石武器特例影响非武器工具")
    assert(data.positiveHitWear == 49 and data.zeroDamageHitWear == 49 and
        data.damageRejectedAfterHitWear == 49 and data.missWear == 50 and data.lastPoint == 0,
        "命中后的耐久磨损时机错误，或末点磨损没有归零")
    assert(data.knifeCropMultiplier == 1.5 and data.knifeWrongTarget == 1 and data.postDefense == 9
        and data.blockedAffinity == 0, "小刀作物专精不是扣防后50%加成，或扩大到其他目标")
end, "材质耐久、命中磨损与小刀作物弱点")

test.system("combat", "bite_attack", {}, function(data)
    assert(data.equalHalfUsesA and data.lighterUsesB and data.heavierUsesA, "撕咬身体质量分界错误")
    assert(data.bodyWeight == 40 and data.rangeUsesAttackRange, "撕咬没有使用身体重量或普通攻击范围")
    assert(data.duration == 9 and data.interval == 3 and data.cooldown == 180,
        "撕咬时长、周期或冷却不符合配置")
    assert(data.holdDamage == 5 and data.carryDamage == 6 and data.targetSlow == .25 and data.casterSlow == .5,
        "撕咬伤害或减速配置错误")
    assert(math.abs(data.oneSlow - 3) < .001 and math.abs(data.twoSlows - 2.25) < .001 and
        math.abs(data.oneRemaining - 3) < .001 and math.abs(data.restored - 4) < .001,
        "多来源减速叠加、单来源释放或全部释放错误")
    assert(data.blockedStart and data.retainedBlock and data.releasedBlock, "普通攻击限制没有阻止前摇或独立释放")
    assert(data.persistedCooldown == 123, "剩余冷却快照没有保留")
    assert(data.invalidTimingRejected and data.invalidSlowRejected, "无效周期或100%减速配置未被拒绝")
end, "撕咬身体质量、配置与多来源动作租约")

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

local function near(actual, expected, label)
    assert(type(actual) == "number" and math.abs(actual - expected) < .00001, label .. "错误")
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
