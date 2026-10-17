-- 检查音量数据与空间播放参数，不播放声音、不修改用户音量。
for _, row in ipairs({ { volume = -1, master = -1, expected = 0 }, { volume = 0.25, master = 0.25, expected = 0.25 }, { volume = 3, master = 3, expected = 1 } }) do
    test.system("audio", "bus_volume", row, function(data)
        assert(data.master == row.expected and #data.buses == 5, "总音量或总线数量错误")
        local seen = {}
        for _, bus in ipairs(data.buses) do
            assert(not seen[bus.bus] and bus.volume == row.expected, "音频总线钳制或读写错误")
            seen[bus.bus] = true
        end
        assert(not data.globalHasPosition and data.positionedHasPosition, "全局和空间声音定位混淆")
        assert(data.x == 3 and data.y == 4 and math.abs(data.volume - 0.6) < 0.000001 and math.abs(data.pitch - 1.2) < 0.000001, "空间播放覆盖参数丢失")
    end, "音频总线范围与空间参数：" .. row.expected)
end

for _, row in ipairs({ { first = "probe.first", second = "probe.second", errors = 0 }, { first = "probe.first", second = "PROBE.FIRST", errors = 1 } }) do
    test.system("audio", "catalog_lifecycle", row, function(data)
        assert(data.cueCount == 2 and data.errors == row.errors, "独立音频目录没有报告重复稳定事件 ID")
        assert(data.found and data.resolvedFirst and not data.emptyAccepted, "目录索引重建、去首尾空格或不区分大小写查询错误")
    end, "音频目录装配、重复校验和缓存重建")
end

-- 地表优先级：深水静音 > 浅水 > 积雪 > 人工支撑/石地 > 沙滩 > 草被 > 泥土。
for _, row in ipairs({
    { biome = 4, grass = 2, expected = "player.footstep.grass" },
    { biome = 4, expected = "player.footstep.dirt" },
    { biome = 2, grass = 2, expected = "player.footstep.sand" },
    { biome = 3, expected = "player.footstep.sand" },
    { biome = 7, expected = "player.footstep.stone" },
    { biome = 4, support = true, grass = 2, expected = "player.footstep.stone" },
    { biome = 4, snow = 0.1, grass = 2, expected = "player.footstep.snow" },
    { biome = 4, water = 0.25, snow = 0.8, expected = "player.footstep.water" },
    { biome = 4, water = 0.03, grass = 2, expected = "player.footstep.grass" },
    { biome = 4, water = 0.0301, grass = 2, expected = "player.footstep.water" },
    { biome = 4, water = 0.3, grass = 2, expected = "player.footstep.water" },
    { biome = 4, water = 0.3001, grass = 2, expected = "" },
    { biome = 4, water = 0.55, grass = 2, expected = "" },
    { biome = 4, water = 0.8, snow = 1, expected = "" }
}) do
    test.system("audio", "footstep_surface", row, function(data)
        assert(data.cue == row.expected and data.silent == (row.expected == ""),
            "地表脚步优先级/浅水深水边界错误：" .. tostring(row.expected))
    end, "脚步材质：" .. (row.expected == "" and "深水静音" or row.expected))
end

-- 连续采样正式状态机，覆盖静默建基线、真实进出水和划水节奏，不创建世界或播放声音。
local function swimming_sequence(label, steps)
    test.system("audio", "swimming_sequence", { steps = steps }, function(data)
        assert(#data.samples == #steps, "游泳音效没有返回完整连续采样")
        for i, sample in ipairs(data.samples) do
            local step = steps[i]
            assert(sample.cue == step.expected, label .. "，第 " .. i .. " 步选音错误：" .. tostring(sample.cue))
            assert(sample.hasBaseline == (not step.reset) and sample.inWater == step.wet,
                label .. "，第 " .. i .. " 步水态或基线错误")
            assert(sample.strokeDistance >= 0 and sample.strokeElapsed >= 0, "划水距离或计时出现负数")
            if step.cleared then
                assert(sample.strokeDistance == 0 and sample.strokeElapsed == 0,
                    label .. "，第 " .. i .. " 步没有清除旧划水累计")
            end
            if step.maxDistance then
                assert(sample.strokeDistance <= step.maxDistance and sample.strokeElapsed == 0,
                    "卡帧后的划水余量或计时没有限制")
            end
        end
    end, "游泳音效：" .. label)
end

swimming_sequence("入水边界、浅水静默和同水体连续移动", {
    { depth = 0, expected = "", wet = false, cleared = true },
    { depth = 0.03, moving = true, distance = 0.2, expected = "", wet = false, cleared = true },
    { depth = 0.0301, moving = true, distance = 0.2, expected = "player.swim.enter", wet = true, cleared = true },
    { depth = 0.3, moving = true, distance = 1.5, deltaTime = 1, expected = "", wet = true, cleared = true },
    { depth = 0.3001, moving = true, distance = 0.7, deltaTime = 0.25, expected = "", wet = true },
    { depth = 0.8, moving = true, distance = 0.7, deltaTime = 0.25, expected = "player.swim.stroke", wet = true },
    { depth = 0.0301, moving = true, distance = 0.2, expected = "", wet = true, cleared = true },
    { depth = 0.03, moving = true, distance = 0.2, expected = "player.swim.exit", wet = false, cleared = true }
})

swimming_sequence("深水加载静默且划水必须同时达到距离与时间", {
    { depth = 0.8, moving = true, distance = 1.5, deltaTime = 1, expected = "", wet = true, cleared = true },
    { depth = 0.8, moving = true, distance = 1.5, deltaTime = 0.25, expected = "", wet = true },
    { depth = 0.8, moving = true, distance = 0.01, deltaTime = 0.25, expected = "player.swim.stroke", wet = true },
    { depth = 0.8, expected = "", wet = true, cleared = true },
    { depth = 0.8, moving = true, distance = 0.1, deltaTime = 1, expected = "", wet = true },
    { depth = 0.8, moving = true, distance = 1.31, deltaTime = 0.1, expected = "player.swim.stroke", wet = true }
})

swimming_sequence("入水当帧不叠划水，之后重新累计", {
    { depth = 0, expected = "", wet = false },
    { depth = 0.8, moving = true, distance = 1.5, deltaTime = 1, expected = "player.swim.enter", wet = true, cleared = true },
    { depth = 0.8, moving = true, distance = 0.7, deltaTime = 0.25, expected = "", wet = true },
    { depth = 0.8, moving = true, distance = 0.7, deltaTime = 0.25, expected = "player.swim.stroke", wet = true }
})

swimming_sequence("停步、零位移和被动水流不划水也不补旧累计", {
    { depth = 0.8, expected = "", wet = true },
    { depth = 0.8, moving = true, distance = 0.7, deltaTime = 0.25, expected = "", wet = true },
    { depth = 0.8, distance = 1.5, deltaTime = 1, expected = "", wet = true, cleared = true },
    { depth = 0.8, moving = true, distance = 0.7, deltaTime = 0.25, expected = "", wet = true },
    { depth = 0.8, moving = true, distance = 0, deltaTime = 1, expected = "", wet = true, cleared = true },
    { depth = 0, distance = 0.4, expected = "", wet = false, cleared = true },
    { depth = 0.8, distance = 0.4, expected = "", wet = true, cleared = true },
    { depth = 0.8, moving = true, distance = 0.7, deltaTime = 0.25, expected = "", wet = true },
    { depth = 0.8, moving = true, distance = 0.7, deltaTime = 0.25, expected = "player.swim.stroke", wet = true }
})

swimming_sequence("完全下沉停止划水，重新浮起后重新累计", {
    { depth = 0.8, immersion = 0.5, expected = "", wet = true },
    { depth = 0.8, immersion = 0.9499, moving = true, distance = 0.7, deltaTime = 0.25, expected = "", wet = true },
    { depth = 0.8, immersion = 0.95, moving = true, distance = 1.5, deltaTime = 1, expected = "", wet = true, cleared = true },
    { depth = 0.8, immersion = 1, moving = true, distance = 1.5, deltaTime = 1, expected = "", wet = true, cleared = true },
    { depth = 0.8, immersion = 0.9499, moving = true, distance = 0.7, deltaTime = 0.25, expected = "", wet = true },
    { depth = 0.8, immersion = 0.9499, moving = true, distance = 0.7, deltaTime = 0.25, expected = "player.swim.stroke", wet = true }
})

swimming_sequence("非 WaterContact 液体不冒充水体", {
    { depth = 0, waterContact = false, expected = "", wet = false },
    { depth = 0.8, waterContact = false, moving = true, distance = 1.5, deltaTime = 1, expected = "", wet = false, cleared = true },
    { depth = 1, waterContact = false, moving = true, distance = 1.5, deltaTime = 1, expected = "", wet = false, cleared = true }
})

swimming_sequence("重置后不把迁移或重建补播成入出水", {
    { depth = 0.8, expected = "", wet = true },
    { depth = 0.8, moving = true, distance = 0.7, deltaTime = 0.25, expected = "", wet = true },
    { reset = true, expected = "", wet = false, cleared = true },
    { depth = 0.8, moving = true, distance = 1.5, deltaTime = 1, expected = "", wet = true, cleared = true },
    { depth = 0.8, moving = true, distance = 1.4, deltaTime = 0.5, expected = "player.swim.stroke", wet = true },
    { reset = true, expected = "", wet = false, cleared = true },
    { depth = 0, moving = true, distance = 1.5, deltaTime = 1, expected = "", wet = false, cleared = true }
})

swimming_sequence("单次卡帧最多一划且不连续补播", {
    { depth = 0.8, expected = "", wet = true },
    { depth = 0.8, moving = true, distance = 5, deltaTime = 5, expected = "player.swim.stroke", wet = true, maxDistance = 1.4 },
    { depth = 0.8, moving = true, distance = 0.01, deltaTime = 0.01, expected = "", wet = true }
})

-- 按本次转移数量独立计算千克和升，素材类别允许用稳定 AudioMaterial Tag 显式覆写。
for _, row in ipairs({
    { action = "Pickup", tags = { "Wood" }, kg = .3, liters = 1, amount = 1, expected = "ui.inventory.pickup.wood.light.compact" },
    { action = "Backpack", tags = { "Metal" }, kg = .9, liters = 3.9, amount = 1, expected = "ui.inventory.backpack.metal.light.compact" },
    { action = "Backpack", tags = { "Metal" }, kg = .9, liters = 3.9, amount = 2, expected = "ui.inventory.backpack.metal.medium.bulky" },
    { action = "Chest", tags = { "Stone" }, kg = 2, liters = 1, amount = 4, expected = "ui.inventory.chest.stone.heavy.bulky" },
    { action = "Chest", tags = { "Wood" }, kg = 7.99, liters = 3.99, amount = 1, expected = "ui.inventory.chest.wood.medium.compact" },
    { action = "Chest", tags = { "Wood" }, kg = 8, liters = 4, amount = 1, expected = "ui.inventory.chest.wood.heavy.bulky" },
    { action = "Pickup", itemId = "CopperPlate", kg = 1, liters = 1, expected = "ui.inventory.pickup.metal.medium.compact" },
    { action = "Pickup", tags = { "AudioMaterial.Soft", "Metal" }, kg = 5, liters = 6, expected = "ui.inventory.pickup.soft.medium.bulky" },
    { action = "Swap", kg = 0, liters = 0, amount = 1, expected = "ui.inventory.swap" },
    { action = "Backpack", kg = 1, liters = 1, amount = 0, expected = "" },
    { action = "Backpack", kg = -1, liters = 1, amount = 1, expected = "" }
}) do
    test.system("audio", "inventory_cue", row, function(data)
        assert(data.cue == row.expected and data.valid == (row.expected ~= ""),
            "物品材质/重量/体积/数量音效映射错误：" .. tostring(row.expected))
    end, "物品取放音效：" .. (row.expected == "" and "无效参数" or row.expected))
end
