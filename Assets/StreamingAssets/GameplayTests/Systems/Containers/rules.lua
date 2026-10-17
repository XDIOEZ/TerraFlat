local cases = {
    {name = "半份端口消费", args = {}, consumed = true, after = 4.5, pending = .5, quantized = 2},
    {name = "整份端口消费", args = {consume = 1}, consumed = true, after = 4, pending = 0, quantized = 2},
    {name = "消费不能超过预约", args = {consume = 2}, consumed = false, after = 5, pending = 1, quantized = 2},
    {name = "零消费无变化", args = {consume = 0, quantize = -.5}, consumed = false, after = 5, pending = 1, quantized = 0},
    {name = "负消费无变化", args = {consume = -1, quantize = .5}, consumed = false, after = 5, pending = 1, quantized = 0}
}
for _, case in ipairs(cases) do
    test.system("containers", "composition", case.args, function(data)
        assert(data.initial == 5 and data.reserved and data.repeatReserved, "混液总量或端口预约失败")
        assert(data.sameBatch and data.reserveRandomStable, "反复预约改变批次或消耗随机状态")
        assert(not data.wrongPortAccepted and not data.exceededAccepted, "错误端口或超量提交被接受")
        assert(data.consumed == case.consumed and math.abs(data.afterConsume - case.after) < .00001, "真实组分消费错误")
        assert(math.abs(data.pendingAfter - case.pending) < .00001 and data.afterRejected == data.afterConsume, "预约剩余或拒绝事务改变总量")
        assert(data.restored == 5 and data.restoredPending == 1, "快照恢复遗漏组分或预约")
        assert(data.cloneIndependent and data.restoredWater == 3 and data.restoredOil == 2 and data.restoredBatchServings == 1, "消费修改了快照中的真实组分或批次")
        assert(data.quantized == case.quantized, "整份量化错误")
    end, "容器组分事务：" .. case.name)
end
test.system("containers", "composition", {water = 0, oil = 0}, function(data)
    assert(data.initial == 0 and not data.reserved and not data.repeatReserved, "空容器产生虚假预约")
    assert(not data.consumed and data.afterConsume == 0 and data.restoredPending == 0, "空容器产生虚假消费或恢复预约")
end, "空容器无组分不生成输出批次")
test.system("containers", "composition", {water = .5, oil = 0}, function(data)
    assert(data.reserved and data.consumed and data.initial == .5 and data.afterConsume == 0, "小数尾量无法真实消费")
    assert(data.pendingAfter == 0 and data.restored == .5 and data.restoredBatchServings == .5, "小数尾量或预约快照丢失")
end, "小数尾量能够预约消费并完整恢复")

local whole_cases = {
    {name = "每次只倒完整一份", args = {amount = 8, requested = 1, retainedFraction = .75}, transfer = 1, spill = 2, fill = 0},
    {name = "不足一份的倾斜不扣液", args = {amount = 8, retainedFraction = .95}, transfer = 1, spill = 0, fill = 0},
    {name = "分数请求不落地", args = {amount = 8, requested = .5}, transfer = 0, spill = 8, fill = 0},
    {name = "分数尾量留桶", args = {amount = .5}, transfer = 0, spill = 0, fill = 7},
    {name = "混液空位只装整份", args = {amount = 6.5, requested = 20}, transfer = 6, spill = 6, fill = 1},
    {name = "桶内半份空位不抽地表", args = {amount = 7.5}, transfer = 1, spill = 7, fill = 0},
    {name = "倒置可以倒尽完整份", args = {amount = 8, retainedFraction = -1}, transfer = 1, spill = 8, fill = 0}
}
for _, case in ipairs(whole_cases) do
    test.system("containers", "whole_world_transfer", case.args, function(data)
        assert(data.transfer == case.transfer and data.spill == case.spill and data.fill == case.fill,
            "地表倒液、倾角或装液产生了分数份，或丢失可移动的完整份")
    end, "世界容器整份边界：" .. case.name)
end
for _, reserved in ipairs({false, true}) do
    test.system("containers", "whole_batch", {reservedElsewhere = reserved}, function(data)
        local count = reserved and 1 or 2
        for index, sample in ipairs(data.samples) do
            assert(sample.reserved == (index <= count) and sample.consumed == (index <= count),
                "整份出口被其他组分尾量卡住，或超用了别的出口预约")
            if index <= count then
                assert(sample.liquidId == "test:water" and sample.servings == 1,
                    "整份出口把不足一份的组分倒进了世界")
            end
        end
        assert(data.water == (reserved and 1.5 or .5) and data.oil == .5
            and data.remaining == (reserved and 2 or 1) and data.otherReserved == (reserved and 1 or 0),
            "整份预约或消费丢弃了小数尾量，或修改了其他出口的真实预约")
    end, reserved and "整份倾倒尊重其它端口预约" or "半份尾量不阻塞完整组分并保留所有尾量")
end
