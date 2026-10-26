-- 电子材料三条热反应的温度、扣料与拒绝路径独立回归。
for _, material in ipairs({
    {ore = "Boron", reaction = "core:electronic_boron_sand_glass", minimum = 1000, output = "Glass", amount = 2,
        missing = {{amounts = {Boron = 0}}, {amounts = {Sand = 1}}, {temperatures = {Sand = 999.99}}}},
    {ore = "Quartz", reaction = "core:electronic_quartz_glass", minimum = 1700, output = "Glass", amount = 1,
        missing = {{amounts = {Quartz = 0}}}},
    {ore = "Quartz", reaction = "core:electronic_quartz_reduction_silicon", minimum = 1700, output = "SiliconPlate", amount = 1,
        missing = {{amounts = {Quartz = 0}}, {amounts = {Ore_Coal = 1}}, {temperatures = {Ore_Coal = 1699.99}}}}
}) do
    local cases = {
        {temperature = material.minimum - .01},
        {temperature = material.minimum, committed = true},
        {temperature = material.minimum + 10, committed = true},
        {temperature = 10000},
        {temperature = material.minimum, full = true, matched = true}
    }
    for _, sample in ipairs(material.missing) do
        sample.temperature = material.minimum
        cases[#cases + 1] = sample
    end
    for _, sample in ipairs(cases) do
        sample.ore, sample.reaction = material.ore, material.reaction
        test.system("processing", "mineral_reaction", sample, function(data)
            local committed = sample.committed == true
            assert(data.minimum == material.minimum and data.maximum == 9999
                and data.matched == (sample.matched == true or committed) and data.committed == committed,
                "电子材料反应的温度、每份原料、缺料或满输出判断错误：" .. material.reaction)
            if committed then
                for _, slot in ipairs(data.input) do assert(slot.amount == 0, "电子材料反应没有精确扣料") end
                assert(data.output[1].id == material.output and data.output[1].amount == material.amount,
                    "电子材料反应生成错误产物或数量")
            else
                assert(data.unchanged, "电子材料反应拒绝仍扣料或覆盖输出")
            end
        end, string.format("%s：%g℃缺料/冷原料/满输出%s", material.reaction, sample.temperature, tostring(sample.full == true)))
    end
end
