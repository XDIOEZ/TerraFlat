local cases = {
    {name = "扣除后整堆移交", args = {}, source = 0, target = 3, transferred = true},
    {name = "部分堆叠转移", args = {transfer = 1}, source = 2, target = 1, transferred = true},
    {name = "关闭入库门禁", args = {blocked = true}, source = 3, target = 0, transferred = false},
    {name = "目标槽容量限量", args = {slotCapacity = 1}, source = 2, target = 1, transferred = true},
    {name = "目标无容量", args = {slotCapacity = 0}, source = 3, target = 0, transferred = false}
}
for _, case in ipairs(cases) do
    test.system("inventory", "transactions", case.args, function(data)
        assert(data.consumed and data.afterConsume == 3, "没有通过真实库存扣数量")
        assert(not data.excessiveAccepted and not data.foreignAccepted, "超额或外来槽扣料被接受")
        assert(data.transferred == case.transferred and data.sourceAmount == case.source and data.targetAmount == case.target, "转移结果错误")
        assert(data.sourceAmount + data.targetAmount == 3 and data.totalWeight == 3, "跨库存数量或质量不守恒")
    end, "库存事务：" .. case.name)
end
local flicks = {
    {name = "快速甩动", points = {{0, 0, 0}, {0.04, 48, 0}, {0.08, 96, 0}}, accepted = true},
    {name = "距离不足", points = {{0, 0, 0}, {0.02, 24, 0}, {0.04, 48, 0}}, accepted = false},
    {name = "距离刚过阈值", points = {{0, 0, 0}, {0.04, 65, 0}}, accepted = true},
    {name = "速度不足", points = {{0, 0, 0}, {0.08, 48, 0}, {0.16, 96, 0}}, accepted = false},
    {name = "长距离慢拖", points = {{0, 0, 0}, {0.5, 100, 0}, {1, 200, 0}}, accepted = false},
    {name = "快速移动后停住", points = {{0, 0, 0}, {0.04, 96, 0}, {0.12, 96, 0}}, accepted = false},
    {name = "手指轻微移动不冒充继续甩动", points = {{0, 0, 0}, {0.04, 96, 0}, {0.13, 97, 0}}, accepted = false},
    {name = "原地抖动", points = {{0, 0, 0}, {0.02, 50, 0}, {0.04, -50, 0}, {0.06, 10, 0}}, accepted = false},
    {name = "来回划动", points = {{0, 0, 0}, {0.02, 150, 0}, {0.04, 10, 0}, {0.06, 100, 0}}, accepted = false},
    {name = "负向甩动", points = {{0, 0, 0}, {0.04, -48, 0}, {0.08, -96, 0}}, accepted = true},
    {name = "斜向甩动", points = {{0, 0, 0}, {0.04, 36, 36}, {0.08, 72, 72}}, accepted = true},
    {name = "按住后再甩", points = {{0, 0, 0}, {0.5, 0, 0}, {0.54, 48, 0}, {0.58, 96, 0}}, accepted = true},
    {name = "缩放两倍仍识别相同动作", scale = 2, points = {{0, 0, 0}, {0.04, 96, 0}, {0.08, 192, 0}}, accepted = true},
    {name = "放大界面不放宽距离", scale = 2, points = {{0, 0, 0}, {0.02, 48, 0}, {0.04, 96, 0}}, accepted = false},
    {name = "同帧跳变", points = {{0, 0, 0}, {0, 96, 0}}, accepted = false},
    {name = "取消后不残留", cancel = true, points = {{0, 0, 0}, {0.04, 48, 0}, {0.08, 96, 0}}, accepted = false}
}
for _, case in ipairs(flicks) do
    test.system("inventory", "flick_gesture", case, function(data)
        assert(data.accepted == case.accepted, "甩动距离、末段速度或取消规则错误")
    end, "触屏甩动：" .. case.name)
end
local capacities = {
    {name = "容器由体积限制", args = {}, allowed = 3, weight = 10, same = true, stack = true},
    {name = "整数物品不能塞半件", args = {currentVolume = 9}, allowed = 0, weight = 10, same = true, stack = true},
    {name = "重量先达到上限", args = {currentWeight = 8}, allowed = 1, weight = 10, same = true, stack = true},
    {name = "玩家承载宽限", args = {playerBag = true, currentWeight = 10}, allowed = 2, weight = 15, same = true, stack = true},
    {name = "创造容量绕过", args = {unlimited = true}, allowed = 10, weight = 10, same = true, stack = true},
    {name = "制作品质不同不合堆", args = {otherQuality = 2}, allowed = 3, weight = 10, same = false, stack = false},
    {name = "特殊载荷不同不合堆", args = {otherSpecial = "payload"}, allowed = 3, weight = 10, same = false, stack = false},
    {name = "不可堆叠属性独立", args = {otherStackable = false}, allowed = 3, weight = 10, same = true, stack = false}
}
for _, case in ipairs(capacities) do
    test.system("inventory", "capacity_identity", case.args, function(data)
        assert(data.allowed == case.allowed and data.weightLimit == case.weight, "重量体积容量策略错误")
        assert(data.sameIdentity == case.same and data.canStack == case.stack, "堆叠身份错误")
        assert(data.wholeLarge == 2147483647 and data.wholeNegative == 0, "数量转整数边界错误")
        if case.args.playerBag then assert(data.slots == 20, "生存背包基础格数错误") end
    end, "库存容量与身份：" .. case.name)
end
