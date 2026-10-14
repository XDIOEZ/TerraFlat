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
