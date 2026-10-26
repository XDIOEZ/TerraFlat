-- 冷料正式供热、满输出恢复与云母切割独立于整场机械工厂。
test.check("player_ready", "电子材料加工现场玩家就绪")
local function total(slots, id)
    local amount = 0
    for _, item in ipairs(slots) do
        if item.id == id then amount = amount + item.amount end
    end
    return amount
end

for _, row in ipairs({
    {name = "硼与沙子", materials = {{id = "Boron", amount = 1, temperature = 20},
        {id = "Sand", amount = 2, temperature = 20}}, output = "Glass", amount = 2},
    {name = "石英单独制玻璃", materials = {{id = "Quartz", amount = 1, temperature = 20}}, output = "Glass", amount = 1},
    {name = "石英与煤优先还原硅", materials = {{id = "Quartz", amount = 1, temperature = 20},
        {id = "Ore_Coal", amount = 2, temperature = 20}}, output = "SiliconPlate", amount = 1}
}) do
    for _, segments in ipairs({1, 400}) do
        test.runtime("processing", "material_heating_window", {materials = row.materials, segments = segments}, function(data)
            assert(#data.input == 0 and total(data.output, row.output) == row.amount,
                "电子材料从20℃实际升温后没有完成指定反应：" .. row.name)
            if row.output == "SiliconPlate" then
                assert(total(data.output, "Glass") == 0, "石英在煤参与还原前被单料玻璃配方抢先消耗")
            end
        end, string.format("电子材料冷料真实供热%d段：%s", segments, row.name))
    end
    test.runtime("processing", "material_heating_window", {materials = row.materials, full = true}, function(data)
        for _, material in ipairs(row.materials) do
            assert(total(data.input, material.id) == material.amount,
                "电子材料热产物输出已满时仍扣料：" .. row.name)
            assert(total(data.resumedInput, material.id) == 0,
                "电子材料释放输出并保存恢复后无法继续反应：" .. row.name)
        end
        assert(total(data.output, "StoneSlab") == 1
            and total(data.resumedOutput, row.output) == row.amount,
            "电子材料满槽阻塞物被覆盖，或恢复后产物错误：" .. row.name)
    end, "电子材料满输出等待与快照恢复：" .. row.name)
end

for _, sample in ipairs({
    {name = "切割一矿两片", args = {}, resolved = true, success = true},
    {name = "低级切割能力", args = {level = 1}, resolved = true, success = true},
    {name = "研磨不能代替切割", args = {capability = "grind"}, resolved = false, success = false},
    {name = "空原料拒绝", args = {amount = 0}, resolved = false, success = false},
    {name = "满输出不扣矿石", args = {full = true}, resolved = true, success = false}
}) do
    test.runtime("processing", "material_cutting", sample.args, function(data)
        assert(data.resolved == sample.resolved and data.preview == sample.success and data.success == sample.success,
            "云母正式切割能力解析或事务资格错误")
        if sample.success then
            assert(data.work == 4 and data.inputAmount == 1 and #data.input == 0
                and total(data.output, "MicaSheet") == 2, "云母切割没有按一矿两片完成正式事务")
        else
            assert(data.unchanged, "云母拒绝切割仍改变原料或输出")
        end
    end, "云母正式物品加工：" .. sample.name)
end
