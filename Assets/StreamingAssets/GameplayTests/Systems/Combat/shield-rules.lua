-- 直接驱动正式 ShieldActionClock，不在 Lua 重写盾牌动作算法。
local cases = {
    {name="按下不是弹反",args={},state="Pressing",defense=0,speed=1},
    {name="短按盾击",args={beforeRelease=.1,release=true},state="Bash",defense=5,speed=1,bash=true},
    {name="盾击结束",args={beforeRelease=.1,release=true,afterRelease=.3},state="Idle",defense=0,speed=1,bash=true},
    {name="长按举盾",args={beforeRelease=.2},state="Guard",defense=10,speed=.5},
    {name="长按持续",args={beforeRelease=12},state="Guard",defense=10,speed=.5},
    {name="松开举盾不盾击",args={beforeRelease=.2,release=true},state="Idle",defense=0,speed=1},
    {name="取消清理",args={beforeRelease=.2,cancel=true},state="Idle",defense=0,speed=1},
    {name="副手弹反短窗",args={action="parry"},state="Parry",defense=10,speed=1},
    {name="弹反过期",args={action="parry",afterRelease=.2},state="Idle",defense=0,speed=1},
    {name="取消不能重置冷却",args={action="parry",cancel=true},state="Idle",defense=0,speed=1},
    {name="冷却恢复",args={action="parry",afterRelease=.601},state="Idle",defense=0,speed=1,retry=true}
}
for _, case in ipairs(cases) do
    test.system("combat","shield_actions",case.args,function(data)
        assert(data.started and data.state == case.state, "盾牌状态错误")
        assert(math.abs(data.defense-case.defense)<.00001, "盾牌防御错误")
        assert(math.abs(data.speed-case.speed)<.00001, "盾牌减速错误")
        assert(data.bashOnRelease == (case.bash == true), "长按松开不应追加盾击")
        assert(data.offhandIndex==7 and data.oldFeetIndex==6, "新副手不能移动旧装备索引")
        assert(data.offhandTag=="EquipmentSlot.OffHand", "副手标签错误")
        if case.args.action=="parry" then
            assert(data.canParryAgain==(case.retry==true), "弹反冷却或互斥错误")
        end
    end,"木盾："..case.name)
end

test.system("combat", "shield_hit_stamina", {steps={
    {action="guard"}, {action="hit",damage=1,time=100}, {action="hit",damage=1,time=100},
    {action="hit",damage=30,time=161}, {action="immune"}, {action="hit",time=222},
    {action="release_immune"}, {action="environment"}, {action="force"}
}}, function(data)
    local s = data.samples
    assert(s[1].accepted and s[1].state=="Guard" and s[1].transactions==0, "举盾起手不应扣体力")
    assert(s[2].damage==0 and s[2].hp==100 and s[2].stamina==90 and s[2].transactions==1,
        "完全挡住的零伤命中仍应消耗一次翻滚体力")
    assert(s[2].requestedCost==10 and s[2].source=="flatworld.combat.shield_guard_hit", "举盾受击费用或来源错误")
    assert(s[2].shieldDurability==99 and s[3].shieldDurability==99 and s[4].shieldDurability==98,
        "盾牌没有按有效受击扣耐久，或冷却拒绝重复扣除")
    assert(s[3].damage<0 and s[3].stamina==90 and s[3].hitEvents==1, "伤害冷却拒绝仍扣体力")
    assert(s[4].damage>0 and s[4].stamina==80 and s[4].transactions==2 and s[4].hitEvents==2,
        "破防命中没有独立扣一次体力")
    assert(s[6].damage<0 and s[6].stamina==80 and s[6].hitEvents==2, "无敌拒绝仍扣体力")
    assert(s[8].transactions==2 and s[9].transactions==2 and s[9].stamina==80,
        "环境或直接生命扣除误触发举盾受击费用")
    assert(s[6].shieldDurability==98 and s[8].shieldDurability==98 and s[9].shieldDurability==98,
        "无敌、环境或直接扣血错误消耗盾牌耐久")
end, "举盾受击：正伤、全挡、冷却拒绝、无敌与环境来源")

for _, action in ipairs({"press", "bash", "cancel", "disable", "unload"}) do
    local steps = {}
    -- 各分支均走正式状态入口，不直接调用扣费回调。
    if action~="press" and action~="bash" then steps[1] = {action="guard"} end
    steps[#steps+1] = {action=action}
    steps[#steps+1] = {action="hit"}
    test.system("combat", "shield_hit_stamina", {steps=steps}, function(data)
        local last = data.samples[#data.samples]
        assert(last.damage>=0 and last.stamina==100 and last.transactions==0,
            "非举盾状态或已卸载模块仍扣受击体力")
    end, "举盾受击拒绝："..action)
end

for _, case in ipairs({
    {name="跟随翻滚配置",args={rollCost=17},remaining=83,cost=17,transactions=1},
    {name="不足扣到零",args={stamina=5},remaining=0,cost=10,transactions=1},
    {name="零费用",args={rollCost=0},remaining=100,cost=0,transactions=0},
    {name="缺省翻滚",args={missingRoll=true,rollCost=17},remaining=90,cost=10,transactions=1}
}) do
    case.args.steps = {{action="guard"},{action="hit"}}
    test.system("combat", "shield_hit_stamina", case.args, function(data)
        local hit = data.samples[2]
        assert(hit.state=="Guard" and hit.damage==0 and hit.stamina==case.remaining and hit.transactions==case.transactions,
            "举盾受击没有复用翻滚配置、钳制剩余体力或允许零费用")
        assert(hit.requestedCost==case.cost, "举盾受击基础费用不符")
    end, "举盾受击费用："..case.name)
end

test.system("combat", "shield_hit_stamina", {steps={
    {action="guard"}, {action="reload"}, {action="guard"}, {action="legacy_hit"}
}}, function(data)
    local hit = data.samples[4]
    assert(data.samples[1].accepted and data.samples[3].accepted and hit.damage==0
        and hit.stamina==90 and hit.transactions==1 and hit.hitEvents==1,
        "模块重载残留受击订阅，或旧攻击入口没有扣体力")
end, "举盾受击生命周期：重载不重复订阅，旧伤害入口共用扣费")
