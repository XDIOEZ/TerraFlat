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
local function nearWeapon(actual, expected, label)
    assert(type(actual) == "number" and math.abs(actual - expected) < .0001, label .. "错误")
end
for _, weaponCase in ipairs({
    {id = "ChippedTool", name = "打制石器", physical = 6},
    {id = "Obsidian", name = "黑曜石", physical = 9}
}) do
    local case = weaponCase
    test.runtime("combat", "held_weapon_action", {weaponId = case.id}, function(data)
        assert(data.weaponId == case.id, "未使用真实" .. case.name .. "定义")
        assert(data.held.inHand and data.held.ownedByPlayer and data.held.registered and data.held.indexed, case.name .. "没有完成正式手持装配")
        assert(data.animationRegistered and data.animationLoaded, case.name .. "攻击动作模块未注册或未加载")
        assert(data.damageRegistered and data.damageLoaded, case.name .. "伤害模块未注册或未加载")
        assert(data.shellPrefabId == "ChippedTool" and data.hasKnifeTag, case.name .. "没有复用打制石器外壳或缺少小刀类别")
        nearWeapon(data.physicalDamage, case.physical, case.name .. "物理攻击力")
        nearWeapon(data.staminaCostPerAttack, 1.5, case.name .. "每次基础精力消耗")
        assert(data.impactKind == 1, case.name .. "没有使用切割伤害类型")
        assert(data.initialWindowClosed, case.name .. "待机时伤害窗未关闭")
        assert(data.actionRequested and data.sawAttackState, case.name .. "公开动作入口没有启动攻击动画")
        assert(data.sawOpenWindow, case.name .. "攻击动画未打开真实伤害窗")
        assert(data.returnedToIdle and data.finalWindowClosed, case.name .. "单次攻击后未回待机或未关闭伤害窗")
        if case.id == "ChippedTool" then
            referenceWeapon = data
        else
            assert(referenceWeapon ~= nil, "缺少打制石器的真实参考采样")
            assert(data.rendererPath == "Render/Render_1", "黑曜石贴图没有绑定武器的实际渲染节点")
            nearWeapon(data.physicalDamage, referenceWeapon.physicalDamage * 1.5, "黑曜石相对打制石器的伤害倍率")
            nearWeapon(data.staminaCostPerAttack, referenceWeapon.staminaCostPerAttack, "两件武器的基础精力一致性")
            nearWeapon(data.attackSpeedMultiplier, referenceWeapon.attackSpeedMultiplier, "两件武器的攻击速度一致性")
            nearWeapon(data.staminaBeforeAction - data.staminaAfterAction,
                referenceWeapon.staminaBeforeAction - referenceWeapon.staminaAfterAction, "同一玩家两件武器的即时精力扣除一致性")
        end
    end, case.name .. "正式手持装配、数值与动画伤害窗")
end

test.runtime("combat", "world_health_panel_lifecycle", {}, function(data)
    assert(data.initiallyVisible, "正式动物血条未显示")
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
