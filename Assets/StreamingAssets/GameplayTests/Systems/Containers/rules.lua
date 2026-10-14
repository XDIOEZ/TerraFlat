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
