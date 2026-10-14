local cases = {
    {name = "逐层过期", args = {}, initial = 3, stacks = 2, remaining = 1.5, expired = false, ticks = 5},
    {name = "跨多层结束", args = {elapsed = 6}, initial = 3, stacks = 1, remaining = 0, expired = true, ticks = 12},
    {name = "层数上限", args = {stacks = 20, elapsed = .5}, initial = 4, stacks = 4, remaining = 1.5, expired = false, ticks = 1},
    {name = "层数下限", args = {stacks = 0, elapsed = .5}, initial = 1, stacks = 1, remaining = 1.5, expired = false, ticks = 1},
    {name = "持续暴露不脱层", args = {exposed = true}, initial = 3, stacks = 3, remaining = 2, expired = false, ticks = 5},
    {name = "永久状态不按时间到期", args = {permanent = true, elapsed = 3.5}, initial = 3, stacks = 3, remaining = 0, expired = false, ticks = 7},
    {name = "普通到期一次结束", args = {decay = false, elapsed = 3}, initial = 3, stacks = 3, remaining = 0, expired = true, ticks = 4},
    {name = "续时不重播开始", args = {extend = 1}, initial = 3, stacks = 2, remaining = 1.5, expired = false, ticks = 5},
    {name = "零时间不推进", args = {elapsed = 0}, initial = 3, stacks = 3, remaining = 2, expired = false, ticks = 0}
}
for _, case in ipairs(cases) do
    test.system("buff", "lifecycle", case.args, function(data)
        assert(data.initialized and data.initialStacks == case.initial and data.stacks == case.stacks, "状态层数错误")
        assert(math.abs(data.initialScale - (1 + (case.initial - 1) * .25)) < .00001, "叠层视觉倍率错误")
        assert(math.abs(data.remaining - case.remaining) < .00001 and data.expired == case.expired, "持续时间错误")
        assert(data.startCount == 1 and data.stopCount == 1, "Start或Stop重复执行")
        assert(data.tickCount == case.ticks, "周期效果未按实际存活时长执行")
        assert(not data.invalidDurationAccepted, "接受负剩余持续时间")
        assert(data.extended == (case.args.extend == 1), "续时结果错误")
    end, "Buff生命周期：" .. case.name)
end
