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

-- 十份水共用倒抛物线，零水量静音，其他材质脚步不受影响。
for _, row in ipairs({
    { depth = -1, expected = 0 }, { depth = 0, expected = 0 },
    { depth = 0.01, expected = 0.1 },
    { depth = 0.1, expected = 0.1 }, { depth = 0.2, expected = 0.45555556 },
    { depth = 0.3, expected = 0.72222222 }, { depth = 0.4, expected = 0.9 },
    { depth = 0.5, expected = 0.98888889 }, { depth = 0.55, expected = 1 },
    { depth = 0.6, expected = 0.98888889 }, { depth = 0.7, expected = 0.9 },
    { depth = 0.8, expected = 0.72222222 }, { depth = 0.9, expected = 0.45555556 },
    { depth = 1, expected = 0.1 }, { depth = 2, expected = 0.1 }
}) do
    test.system("audio", "water_depth_volume", { depth = row.depth }, function(data)
        assert(math.abs(data.volume - row.expected) < 0.00001, "水量音量曲线错误：" .. row.depth)
        assert(math.abs(data.wetFootstepVolume - row.expected) < 0.00001, "涉水脚步未共用水量曲线")
        assert(data.dryFootstepVolume == 1, "水量曲线误改了非水材质脚步")
    end, "水量与水声音量：" .. row.depth)
end

-- 连续驱动正式状态机，断言循环目标与音量包络，不播放声音。
local function swimming_sequence(label, steps)
    test.system("audio", "swimming_sequence", { steps = steps }, function(data)
        assert(#data.samples == #steps, "游泳音效没有返回完整连续采样")
        for i, sample in ipairs(data.samples) do
            local step = steps[i]
            assert(sample.cue == (step.expected or ""), label .. "，第 " .. i .. " 步入出水选音错误")
            assert(sample.hasBaseline == (not step.reset) and sample.inWater == step.wet,
                label .. "，第 " .. i .. " 步水态或基线错误")
            assert(sample.shouldPlayLoop == (step.loop == true), label .. "，第 " .. i .. " 步循环目标错误")
            assert(math.abs(sample.loopVolume - step.volume) < 0.00001,
                label .. "，第 " .. i .. " 步淡入淡出音量错误：" .. tostring(sample.loopVolume))
            if step.waterVolume ~= nil then
                assert(math.abs(sample.waterVolume - step.waterVolume) < 0.00001, "水量音量平滑或离水保留错误")
            end
            assert(math.abs(sample.transitionVolume - (step.transitionVolume or 0)) < 0.00001,
                "入出水声音未按对应水格的水量取音量")
        end
    end, "游泳音效：" .. label)
end

swimming_sequence("真实入出水只响一次，深浅水格切换不重复入水", {
    { depth = 0, wet = false, volume = 0 },
    { depth = 0.03, moving = true, distance = 0.2, wet = false, volume = 0 },
    { depth = 0.0301, moving = true, distance = 0.2, expected = "player.swim.enter", wet = true, volume = 0, transitionVolume = 0.1 },
    { depth = 0.3, moving = true, distance = 1, wet = true, volume = 0 },
    { depth = 0.3001, moving = true, distance = 0.001, deltaTime = 0.25, wet = true, loop = true, volume = 1 },
    { depth = 0.8, moving = true, distance = 3, deltaTime = 1, wet = true, loop = true, volume = 1 },
    { depth = 0.03, moving = true, distance = 0.2, deltaTime = 0.175, expected = "player.swim.exit", wet = false, volume = 0.5, transitionVolume = 0.72222222 },
    { depth = 0, deltaTime = 0.175, wet = false, volume = 0 }
})

swimming_sequence("深水加载静默，低速游动无需走满一格，持续游动不重触发短音", {
    { depth = 0.8, moving = true, distance = 1, wet = true, volume = 0 },
    { depth = 0.8, moving = true, distance = 0.0001, deltaTime = 0.1, wet = true, loop = true, volume = 0.4 },
    { depth = 0.8, moving = true, distance = 0.0001, deltaTime = 0.15, wet = true, loop = true, volume = 1 },
    { depth = 0.8, moving = true, distance = 5, deltaTime = 5, wet = true, loop = true, volume = 1 },
    { depth = 0.9, moving = true, distance = 0.01, wet = true, loop = true, volume = 1 }
})

swimming_sequence("入水后淡入，停下平滑淡出，短暂停走从当前音量恢复", {
    { depth = 0, wet = false, volume = 0 },
    { depth = 0.8, moving = true, distance = 1, expected = "player.swim.enter", wet = true, volume = 0, transitionVolume = 0.72222222 },
    { depth = 0.8, moving = true, distance = 0.1, deltaTime = 0.25, wet = true, loop = true, volume = 1 },
    { depth = 0.8, deltaTime = 0.175, wet = true, volume = 0.5 },
    { depth = 0.8, moving = true, distance = 0.1, deltaTime = 0.05, wet = true, loop = true, volume = 0.7 },
    { depth = 0.8, deltaTime = 0.35, wet = true, volume = 0 },
    { depth = 0.8, moving = true, distance = 0.1, deltaTime = 0.1, wet = true, loop = true, volume = 0.4 }
})

swimming_sequence("零位移、被动水流和浅水不维持游泳循环", {
    { depth = 0.8, wet = true, volume = 0 },
    { depth = 0.8, moving = true, distance = 0.7, deltaTime = 0.25, wet = true, loop = true, volume = 1 },
    { depth = 0.8, moving = true, distance = 0, deltaTime = 0.35, wet = true, volume = 0 },
    { depth = 0.8, distance = 1.5, deltaTime = 1, wet = true, volume = 0 },
    { depth = 0.3, moving = true, distance = 2, wet = true, volume = 0 }
})

swimming_sequence("完全下沉淡出，重新浮起淡入", {
    { depth = 0.8, wet = true, volume = 0 },
    { depth = 0.8, immersion = 0.9499, moving = true, distance = 0.1, deltaTime = 0.25, wet = true, loop = true, volume = 1 },
    { depth = 0.8, immersion = 0.95, moving = true, distance = 0.1, deltaTime = 0.175, wet = true, volume = 0.5 },
    { depth = 0.8, immersion = 1, moving = true, distance = 0.1, deltaTime = 0.175, wet = true, volume = 0 },
    { depth = 0.8, immersion = 0.9, moving = true, distance = 0.1, deltaTime = 0.25, wet = true, loop = true, volume = 1 }
})

swimming_sequence("非 WaterContact 液体不冒充水体", {
    { depth = 0, wet = false, volume = 0 },
    { depth = 0.8, waterContact = false, moving = true, distance = 2, deltaTime = 1, wet = false, volume = 0 },
    { depth = 0.8, waterContact = false, wet = false, volume = 0 }
})

swimming_sequence("传送或重建清除循环音量并静默建立新基线", {
    { depth = 0.8, wet = true, volume = 0 },
    { depth = 0.8, moving = true, distance = 0.1, deltaTime = 0.25, wet = true, loop = true, volume = 1 },
    { reset = true, wet = false, volume = 0 },
    { depth = 0.8, moving = true, distance = 5, wet = true, volume = 0 },
    { depth = 0.8, moving = true, distance = 0.1, deltaTime = 0.25, wet = true, loop = true, volume = 1 },
    { reset = true, wet = false, volume = 0 },
    { depth = 0, wet = false, volume = 0 }
})

swimming_sequence("入出水取真实水格音量，深浅水音量平滑且离水保留尾声", {
    { depth = 0, wet = false, volume = 0, waterVolume = 0 },
    { depth = 0.55, moving = true, distance = 0.1, deltaTime = 0.02, expected = "player.swim.enter", wet = true, volume = 0, waterVolume = 0.1, transitionVolume = 1 },
    { depth = 0.55, moving = true, distance = 0.1, deltaTime = 0.25, wet = true, loop = true, volume = 1, waterVolume = 1 },
    { depth = 1, moving = true, distance = 0.1, deltaTime = 0.02, wet = true, loop = true, volume = 1, waterVolume = 0.9 },
    { depth = 1, moving = true, distance = 0.1, deltaTime = 0.2, wet = true, loop = true, volume = 1, waterVolume = 0.1 },
    { depth = 0, moving = true, distance = 0.1, deltaTime = 0.175, expected = "player.swim.exit", wet = false, volume = 0.5, waterVolume = 0.1, transitionVolume = 0.1 },
    { depth = 0, deltaTime = 0.175, wet = false, volume = 0, waterVolume = 0.1 },
    { reset = true, wet = false, volume = 0, waterVolume = 0 }
})

swimming_sequence("负时间不倒退包络，卡帧音量限制在零至一", {
    { depth = 0.8, wet = true, volume = 0 },
    { depth = 0.8, moving = true, distance = 0.1, deltaTime = -1, wet = true, loop = true, volume = 0 },
    { depth = 0.8, moving = true, distance = 5, deltaTime = 5, wet = true, loop = true, volume = 1 },
    { depth = 0.8, deltaTime = -1, wet = true, volume = 1 },
    { depth = 0.8, deltaTime = 5, wet = true, volume = 0 }
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
