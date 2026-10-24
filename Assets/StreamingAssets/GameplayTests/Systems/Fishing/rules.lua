test.system("fishing", "rig", {}, function(data)
    assert(data.hookAccepted and data.foodAccepted, "合格鱼钩或鱼饵被正式钓具库存拒绝")
    assert(not data.foodInHookAccepted and not data.wrongBaitAccepted, "错误物品绕过钩/饵槽资格")
    assert(data.hookCapacity == 1 and data.baitCapacity == 1 and not data.unlimitedSlots and not data.unlimitedStack,
        "钓具没有限制两个单份槽位")
    assert(data.invalidSlots == "InvalidOperationException", "非法钓具槽数没有拒绝")
end, "钓具鱼钩/鱼饵资格与单份槽位")

for _, uses in ipairs({10, 100, 1000}) do
    test.system("fishing", "cast_supplies", {maximum = uses, attempts = uses + 1}, function(data)
        assert(data.accepted == uses and data.broken == 1, "鱼钩使用次数不符或重复损坏")
        assert(data.firstRemaining == uses - 1 and data.beforeLastRemaining == 1,
            "抛竿没有每次只扣1点耐久")
        assert(data.remaining == 0 and data.hookAmount == 0 and data.hookEmpty,
            "鱼钩耗尽后没有损坏清槽")
        assert(data.baitAmount == 1 and not data.baitEmpty, "损坏后再次抛竿错误消耗鱼饵")
        assert(data.stillMaterial, "鱼钩错误改成了工具分类")
    end, "鱼钩完整寿命与耗尽拒绝：" .. uses .. "次")
end

local rejected = {
    {args = {missingHook = true}, label = "未安装鱼钩"},
    {args = {durability = 0}, label = "鱼钩耐久已耗尽"},
    {args = {wrongHook = true}, label = "错误鱼钩类型"},
    {args = {hookAmount = 2}, label = "非法超量鱼钩"},
    {args = {missingBait = true}, label = "没有鱼饵"},
    {args = {baitAmount = 2}, label = "非法超量鱼饵"},
    {args = {wrongBait = true}, label = "没有食物数据的假鱼饵"},
    {args = {staleHook = true}, label = "同GUID旧鱼钩引用"},
    {args = {staleBait = true}, label = "同GUID旧鱼饵引用"},
}
for _, sample in ipairs(rejected) do
    local expectedDurability = sample.args.durability or 10
    local expectedAmount = sample.args.baitAmount or 1
    test.system("fishing", "cast_supplies", sample.args, function(data)
        assert(data.accepted == 0 and data.broken == 0, "非法抛竿仍然提交耗材")
        assert(data.remaining == expectedDurability and data.baitAmount == expectedAmount,
            "失败抛竿仍消耗鱼钩耐久或鱼饵")
    end, "抛竿预检拒绝且不扣耗材：" .. sample.label)
end

test.system("fishing", "cast_supplies", {replaceHookDuringCommit = true}, function(data)
    assert(data.accepted == 0 and data.broken == 0 and data.remaining == 10,
        "提交期间更换鱼钩仍错误扣除旧鱼钩耐久")
    assert(data.baitAmount == 1 and data.baitRestored and data.replacementPreserved,
        "鱼钩提交失败未原样退饵或覆盖了新鱼钩")
end, "抛竿耗材提交失败回滚")
