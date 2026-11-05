-- 在隔离临时角色上走正式丢弃事务，检查真实扣体力、拒绝和失败回退。
test.runtime("combat", "discard_throw_stamina", {}, function(data)
    local s = data.samples
    assert(s.near.accepted and s.near.stamina == 98 and s.near.remaining == 4,
        "近距离投石没有同步扣除体力和一块石头")
    assert(s.far.accepted and s.far.stamina == 92 and s.far.remaining == 3,
        "连续远投没有按每次实际距离扣费")
    assert(s.stack.accepted and s.stack.stamina == 74 and s.stack.remaining == 0,
        "整组投石未按数量扣费")
    assert(not s.exhausted.accepted and s.exhausted.stamina == 1 and s.exhausted.remaining == 5
        and s.exhausted.spawnCalls == 0 and data.rejections == 1,
        "体力不足时仍扣石头、扣体力或生成掉落，或没有发送不足反馈")
    assert(s.exact.accepted and s.exact.stamina == 0 and s.exact.remaining == 4,
        "体力刚好足够时未完成投掷")
    assert(not s.rollback.accepted and s.rollback.stamina == 100
        and s.rollback.remaining == 4 and data.rollbackRemoved,
        "生成后槽位变化未撤销掉落并返还实际消耗的体力")
    assert(not s.paymentConflict.accepted and s.paymentConflict.stamina == 100
        and s.paymentConflict.remaining == 4 and s.paymentConflict.spawnCalls == 0,
        "扣体力回调改变槽位后仍生成掉落，或没有退回本次体力")
    assert(s.free.accepted and s.free.stamina == 0 and s.free.remaining == 4,
        "普通丢弃错误消耗体力或被体力不足阻止")
    assert(not s.invalid.accepted and s.invalid.stamina == 100 and s.invalid.remaining == 5
        and s.invalid.spawnCalls == 0, "无效投掷在预检前扣费或生成掉落")
end, "投石正式事务：连续投掷、整组、体力不足、刚好足够及失败回退")

-- 验证正式玩家生命状态与管理员伤害门禁；本用例不声称模拟了武器命中。
test.check("player_ready", "真实玩家就绪")
test.gm("enable_admin")
test.gm("disable_invincibility")
test.check("gm_damage_normal", "普通生命的伤害倍率门禁恢复")
test.gm("set_health_ratio", "0.5")
test.check("gm_health_partial", "真实生命状态低于满血")
test.gm("set_health_ratio", "1")
test.check("gm_health_full", "真实生命状态恢复满血")
test.gm("enable_invincibility")
test.check("gm_invincibility_enabled", "正式无敌状态开启")
test.check("gm_damage_blocked", "正式管理员伤害倍率归零")
test.gm("disable_invincibility")
test.check("gm_invincibility_disabled", "无敌状态关闭")
test.check("gm_damage_normal", "伤害倍率恢复正常")

-- 走真实定义、手持工厂和公开动作接口，业务断言留在 Lua；不直接打开伤害碰撞体。
local referenceWeapon = nil
local hammerReference = nil
local function nearWeapon(actual, expected, label)
    assert(type(actual) == "number" and math.abs(actual - expected) < .0001, label .. "错误")
end
for _, weaponCase in ipairs({
    {id = "ChippedTool", name = "打制石器", physical = 6, stamina = 4.5, impact = 1, shell = "ChippedTool", attack = "Attack_1", timeScale = 1},
    {id = "Obsidian", name = "黑曜石", physical = 9, stamina = 6.5, impact = 1, shell = "ChippedTool", attack = "Attack_1"},
    {id = "WoodHammer", name = "木锤", physical = 6, stamina = 5, impact = 4, shell = "Stick", attack = "Attack_2", timeScale = 2},
    {id = "StoneHammer", name = "石锤", physical = 10, stamina = 7, impact = 4, shell = "Stick", attack = "Attack_2"}
}) do
    local case = weaponCase
    test.runtime("combat", "held_weapon_action", {weaponId = case.id, attackStateName = case.attack,
        verifyFixedStep = case.timeScale ~= nil, timeScale = case.timeScale}, function(data)
        assert(data.weaponId == case.id, "未使用真实" .. case.name .. "定义")
        assert(data.held.inHand and data.held.ownedByPlayer and data.held.registered and data.held.indexed, case.name .. "没有完成正式手持装配")
        assert(data.hotbarRestored, case.name .. "测试后没有恢复原快捷栏槽和手持物")
        assert(#data.restoredInventories > 0, case.name .. "没有采样拾取目标库存的恢复状态")
        for _, inventory in ipairs(data.restoredInventories) do
            assert(inventory.beforeHash == inventory.afterHash and inventory.beforeSlots == inventory.afterSlots
                and inventory.beforeOccupied == inventory.afterOccupied and inventory.beforeAmount == inventory.afterAmount,
                case.name .. "没有恢复拾取目标库存：" .. tostring(inventory.name))
        end
        assert(data.drawCompleted and data.weaponHeldAfterDraw, case.name .. "正式拔出未完成或武器不再属于原槽位")
        assert(data.animationRegistered and data.animationLoaded, case.name .. "攻击动作模块未注册或未加载")
        assert(data.damageRegistered and data.damageLoaded, case.name .. "伤害模块未注册或未加载")
        assert(data.shellPrefabId == case.shell, case.name .. "没有复用对应武器外壳")
        if case.shell == "ChippedTool" then
            assert(data.hasKnifeTag, case.name .. "缺少小刀类别")
        else
            assert(data.rendererPath == "Render/GameObject", case.name .. "没有绑定挥锤的实际渲染节点")
        end
        nearWeapon(data.physicalDamage, case.physical, case.name .. "物理攻击力")
        nearWeapon(data.staminaCostPerAttack, case.stamina, case.name .. "每次基础精力消耗")
        assert(data.impactKind == case.impact, case.name .. "伤害类型错误")
        assert(data.initialWindowClosed, case.name .. "待机时伤害窗未关闭")
        assert(data.idleDamageTickDisabled, case.name .. "待机时伤害模块仍被逐帧调度")
        assert(data.animatorUpdateMode == "Normal" and data.simulationOwnedAnimation,
            case.name .. "武器时间轴没有交给游戏 Tick 独占，或仍依赖 Unity FixedUpdate")
        assert(data.actionRequested and data.sawAttackState, case.name .. "公开动作入口没有启动攻击动画")
        local first = data.firstAction
        assert(first.administratorBefore == true and first.administratorDuring == false and first.administratorAfter == true,
            case.name .. "攻击采样未隔离或恢复管理员身份")
        local payments = first.payments
        assert(#payments == 1 and payments[1].source == "flatworld.combat.weapon_attack" and payments[1].consumed > 0,
            case.name .. "攻击未产生一次真实体力扣费")
        nearWeapon(payments[1].requested, data.staminaCostPerAttack, case.name .. "攻击请求的基础体力")
        nearWeapon(payments[1].consumed, first.staminaBefore - first.staminaAfter, case.name .. "攻击实际体力消耗")
        nearWeapon(payments[1].remaining, first.staminaAfter, case.name .. "攻击扣费后的剩余体力")
        assert(data.weaponRegisteredAtEnd and data.despawnNotifications == 0 and data.weaponWorld == data.playerWorld,
            case.name .. "攻击期间实例提前回收或失去世界归属")
        local fixed = data.fixedStep
        assert(#fixed.windows > 0, case.name .. "攻击动画未在固定步打开真实伤害窗")
        for _, sample in ipairs(fixed.windows) do
            assert(sample.inFixedStep and sample.damageTickEnabled, case.name .. "伤害窗没有在固定步中唤醒")
        end
        if case.timeScale then
            assert(type(fixed.configuredTicksPerSecond) == "number" and fixed.configuredTicksPerSecond > 0,
                case.name .. "未采到正式模拟频率")
            assert(fixed.timeScale == case.timeScale and
                fixed.targetFrameRate == math.max(1, math.floor(fixed.configuredTicksPerSecond / 2)) and fixed.vSyncCount == 0,
                case.name .. "固定步采样没有处于指定倍率和低帧率")
            assert(fixed.hostileTarget and #fixed.windows > 0 and #fixed.contacts > 0,
                case.name .. "真实固定步伤害窗未采样，或没有命中/尝试命中隔离目标")
            local sameFrameSteps = false
            for index, sample in ipairs(fixed.windows) do
                nearWeapon(sample.delta, 1 / fixed.configuredTicksPerSecond, case.name .. "固定步时间")
                if index > 1 and sample.frame == fixed.windows[index - 1].frame then sameFrameSteps = true end
            end
            assert(sameFrameSteps, case.name .. "低帧率画面下没有观察到同一帧的多个开窗固定步")
            for _, contact in ipairs(fixed.contacts) do
                assert(contact.inFixedStep and contact.windowOpen, case.name .. "真实命中判定不在固定步伤害窗中")
            end
            for _, key in ipairs({ "timeScale", "targetFrameRate", "vSyncCount" }) do
                assert(fixed.restoredSettings[key] == fixed.originalSettings[key], case.name .. "现场设置未恢复：" .. key)
            end
        end
        assert(data.returnedToIdle and data.finalWindowClosed, case.name .. "单次攻击后未回待机或未关闭伤害窗")
        assert(data.beforeDisableActionRequested and data.inactiveActionRejected, case.name .. "停用前未挥击或失活期间仍能攻击")
        assert(data.disabledWindowClosed and data.disabledHitStopped == false, case.name .. "停用未清理伤害窗或命中停顿")
        assert(data.disabledSimulationOwned == true, case.name .. "停用时失去模拟动画时钟所有权")
        nearWeapon(data.disabledAnimatorSpeed, 0, case.name .. "停用时模拟时钟独占的动画速度")
        assert(data.weaponHeldAfterReactivation, case.name .. "武器在停用或重新启用期间失去原持有实例")
        assert(data.reactivatedIdle and data.reactivatedWindowClosed, case.name .. "再次启用未恢复待机或关闭伤害窗")
        local invalid = data.missingControllerAction
        assert(invalid and invalid.requested == false and invalid.administratorBefore == true
            and invalid.administratorDuring == false and invalid.administratorAfter == true
            and invalid.staminaBefore == invalid.staminaAfter and #invalid.payments == 0 and data.missingControllerWindowClosed,
            case.name .. "动画资源失效时仍能攻击、扣体力或留下伤害窗")
        assert(data.reactivatedActionRequested and data.reactivatedAttackState, case.name .. "再次启用后无法正常攻击")
        assert(data.inactiveAnimatorWarning == false, case.name .. "停用时仍对失活 Animator 播放动画")
        if case.id == "ChippedTool" then
            local tags = {}
            for _, tag in ipairs(data.heldTags) do tags[tag] = true end
            assert(tags.Knife and tags.Stone and tags.StoneCraftingMaterial and tags.Modern
                and tags["InventoryCategory.Tool"], "打制石器实例丢失刀具、石料或工具分类标签")
            assert(data.heldPrimaryCategory == "tool", "打制石器分类标签未优先于刀具语义")
            assert(data.heldMatchesKnifeIngredient and not data.heldMatchesSpearIngredient,
                "打制石器应保留刀具标签，但不能冒充长矛工具本体")
            referenceWeapon = data
        elseif case.id == "Obsidian" then
            assert(referenceWeapon ~= nil, "缺少打制石器的真实参考采样")
            assert(data.rendererPath == "Render/Render_1", "黑曜石贴图没有绑定武器的实际渲染节点")
            nearWeapon(data.physicalDamage, referenceWeapon.physicalDamage * 1.5, "黑曜石相对打制石器的伤害倍率")
            assert(data.staminaCostPerAttack > referenceWeapon.staminaCostPerAttack, "黑曜石伤害提高后体力消耗未同步提高")
            nearWeapon(data.attackSpeedMultiplier, referenceWeapon.attackSpeedMultiplier, "两件武器的攻击速度一致性")
            assert(first.staminaBefore - first.staminaAfter >
                referenceWeapon.firstAction.staminaBefore - referenceWeapon.firstAction.staminaAfter,
                "高伤害武器没有消耗更多的实际体力")
        elseif case.id == "WoodHammer" then
            hammerReference = data
        elseif case.id == "StoneHammer" then
            assert(hammerReference ~= nil and data.physicalDamage > hammerReference.physicalDamage
                and data.staminaCostPerAttack > hammerReference.staminaCostPerAttack,
                "升级石锤后伤害提高，基础体力消耗却没有提高")
            assert(first.staminaBefore - first.staminaAfter >
                hammerReference.firstAction.staminaBefore - hammerReference.firstAction.staminaAfter,
                "升级石锤后实际体力消耗没有提高")
        end
    end, case.name .. "正式手持装配、数值与动画伤害窗")
end

test.runtime("combat", "projectile_tick_lifecycle", {}, function(data)
    assert(data.shotLaunched and data.weaponAfterShot == data.weaponBeforeShot - 1
        and data.weaponAfterProjectileHit == data.weaponAfterShot, "远程发射未扣一次耐久或投射物命中重复扣除")
    assert(data.wearSkipSampleLaunched and data.ammunitionWearSkipped, "自用弹药选项没有跳过武器耐久扣除")
    assert(data.idleProjectile and data.idleDamage, "石头待机时投射或伤害模块仍被逐帧调度")
    assert(data.projectileSourceRetained and data.discardSourceRetained and data.damageReloadRetainedSource,
        "单独停用或重载模块抹掉了另一个来源的休眠请求")
    assert(data.lastSourceReleased, "所有休眠来源撤销后伤害模块没有恢复调度")
    assert(data.heldDamageActive and data.openDamageActive and data.closedDamageIdle,
        "手持、伤害窗开启或关闭时的调度状态错误")
    assert(data.flightStarted and data.landedAndIdle, "真实调度没有推进投射物飞行、落地并恢复休眠")
    assert(data.reloadedIdle, "重新生成石头后投射或伤害模块没有恢复待机")
end, "石头投射与丢弃伤害的休眠来源和飞行调度")

test.runtime("combat", "world_health_panel_lifecycle", {}, function(data)
    assert(data.initiallyVisible, "正式动物血条未显示")
    assert(data.initialFillMatchesHealth and data.fillTracksDamageRatio, "生物血条进度没有匹配当前生命比例")
    assert(data.hiddenInactive and data.reusedAcrossTwentyCycles, "反复显隐重新创建血条或隐藏后仍然显示")
    assert(data.ownerDisableHidPanel and data.ownerReactivationReusedInstance, "实体停用后血条未隐藏或重启后未复用")
    assert(data.damageApplied and data.didNotHideBeforeLastDamageDelay and data.automaticallyHidden, "真实生存伤害或末次受伤后自动隐藏失效")
    assert(data.automaticHideRetainedInstance and data.damageReusedInstance, "自动隐藏后下一次伤害没有复用血条")
    assert(data.unloadReleasedReferences and data.unloadImmediatelyDisabledPanel, "模块卸载残留旧血条")
    assert(data.unloadImmediatelyRestoredHierarchy, "模块卸载后延迟销毁的血条仍占用原实体层级")
    assert(data.reloadStartsWithoutPanel and data.reloadCreatedNewInstance, "新一轮模块生命周期复用了旧血条")
    assert(data.despawnReleasedReferences and data.despawnImmediatelyDisabledPanel, "正式回收实体后残留血条或监听引用")
    assert(data.despawnImmediatelyRestoredHierarchy, "正式回收后隐藏血条仍破坏对象池原始层级")
end, "世界血条复用、伤害续时与卸载回收")
