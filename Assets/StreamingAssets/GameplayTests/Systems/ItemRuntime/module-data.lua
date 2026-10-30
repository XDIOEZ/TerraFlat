-- 寿命与燃烧数据计算使用正式规则，快照和深拷贝检查不依赖场景。
for _, input in ipairs({
    {steps = {{now = 480}, {now = 1919.9}, {now = 1920}}, elapsed = {0, 1439.9, 1440}, expired = {false, false, true}},
    {steps = {{now = 0}, {now = 700}, {now = 700}, {now = 700, restore = true}, {now = 1440}},
        elapsed = {0, 700, 700, 700, 1440}, expired = {false, false, false, false, true}},
    {steps = {{now = 0}, {now = 600}, {now = 300}, {now = 1140}},
        elapsed = {0, 600, 600, 1440}, expired = {false, false, false, true}},
    {steps = {{now = 0}, {now = 600}, {now = 50000, world = "moon"}, {now = 50840, world = "moon"}},
        elapsed = {0, 600, 600, 1440}, expired = {false, false, false, true}},
    {steps = {{now = 0}, {now = 5000}}, elapsed = {0, 5000}, expired = {false, true}},
    {steps = {{now = 0}, {now = 1440, enabled = false}}, elapsed = {0, 0}, expired = {false, false}},
    {lifetime = 0, steps = {{now = 0}, {now = 1440}}, elapsed = {0, 0}, expired = {false, false}},
}) do
    test.system("item_runtime", "timed_despawn", input, function(data)
        for index, sample in ipairs(data.samples) do
            assert(sample.expired == input.expired[index], "火种寿命边界、暂停或跨世界时钟错误")
            assert(math.abs(sample.elapsed - input.elapsed[index]) < .001,
                "火种快照恢复、跳时或时钟倒退丢失/重复累计寿命")
        end
        assert(data.staleRejected and data.removed and data.empty and data.amount == 0 and data.noProducts,
            "自然消失误删替换物、残留库存或产生了灰烬")
        assert(data.hotPathAllocatedBytes == 0, "寿命连续推进仍在分配持久化载荷")
        assert(data.sourceElapsed == 257 and data.restoredElapsed == 260,
            "强类型寿命快照没有捕获最新进度，或恢复状态与来源共享")
        assert(data.replacedElapsed == 9, "替换正式载荷后仍读取了旧寿命状态")
        assert(data.clonedBefore == 257 and data.clonedAfter == 260 and data.sourceAfterClone == 257,
            "未编码运行寿命深拷贝丢失进度或与源对象共享状态")
    end, "火种游戏时钟、快照与无副产物清除")
end

test.system("item_runtime", "combustion_fuel_batch", {}, function(data)
    local function nearFuel(actual, expected, label)
        assert(type(actual) == "number" and math.abs(actual - expected) < .0001, label .. "错误")
    end
    local expected = {{105, 5, 10}, {100, 0, 0}, {90, -10, 0},
                      {95, 5, 0}, {30, 10, 0}, {12000, 11980, 5990}}
    assert(#data.samples == #expected and data.matchesSingle and data.sliceUntouched,
        "燃烧连续数组与单次计算不一致，或越界修改相邻样本")
    for index, row in ipairs(expected) do
        nearFuel(data.samples[index].last, row[1], "燃烧时间基线")
        nearFuel(data.samples[index].elapsed, row[2], "燃烧流逝时间")
        nearFuel(data.samples[index].demand, row[3], "燃料需求")
    end
    local remaining = math.max(0, 1000 - 10 * 1.5 * data.fuelRuleMultiplier)
    assert(data.fuelConsumed and data.hasFuel == (remaining > 0), "燃料接口提交状态错误")
    nearFuel(data.remainingFuel, remaining, "燃烧速度与游戏规则倍率")
    nearFuel(data.fuelCapacity, 10, "超过显示容量的燃料储备被截断")
end, "燃烧连续数组：暂停、倒退、跳时、倍率与真实燃料扣减")
