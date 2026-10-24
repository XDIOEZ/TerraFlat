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
