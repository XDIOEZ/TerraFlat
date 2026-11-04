-- 检查音量数据与空间播放参数，不播放声音、不修改用户音量。
local propagationLevels = { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512 }
for level, distance in ipairs(propagationLevels) do
    test.system("audio", "propagation_ranges", {
        propagation = { levels = propagationLevels, cues = { { id = "PROBE.FIRST", level = level } } }
    }, function(data)
        assert(data.firstMaxDistance == distance, "JSON 传播档位未实际应用：" .. level)
        assert(data.secondMaxDistance == 20, "未配置的事件丢失自身距离")
        assert(data.firstResetMaxDistance == 20 and data.secondResetMaxDistance == 20,
            "移除 JSON 绑定并重建目录后残留旧距离")
    end, "JSON 传播档位：" .. level .. " / " .. distance .. " 格")
end

test.system("audio", "propagation_ranges", {
    propagation = { levels = { 1, 2, 4, 8, 16, 24, 64, 128, 256, 500 }, cues = {
        { id = "probe.first", level = 6 }, { id = "probe.second", level = 10 }
    } }
}, function(data)
    assert(data.firstMaxDistance == 24 and data.secondMaxDistance == 500,
        "修改 JSON 档位距离没有同时作用于对应声音")
end, "传播档位距离支持直接修改")

for _, row in ipairs({
    { first = "animal.wolf.idle", second = "animal.bird.idle", expectedFirst = 64, expectedSecond = 64 },
    { first = "animal.bee.idle", second = "animal.wildboar.idle", expectedFirst = 16, expectedSecond = 16 },
    { first = "animal.seagull.idle", second = "animal.smallfish.idle", expectedFirst = 64, expectedSecond = 8 },
    { first = "ambient.foliage.rustle.loop", second = "ambient.river.flow.loop", expectedFirst = 32, expectedSecond = 32 }
}) do
    test.system("audio", "propagation_ranges", row, function(data)
        assert(#data.propagationLevels == #propagationLevels, "正式 JSON 必须声明十个传播档位")
        for level, distance in ipairs(propagationLevels) do
            assert(data.propagationLevels[level] == distance, "正式传播档位距离错误：" .. level)
        end
        assert(data.firstMaxDistance == row.expectedFirst and data.secondMaxDistance == row.expectedSecond,
            "正式 JSON 未保留动物与自然声的传播距离")
    end, "正式声音传播配置：" .. row.first .. " / " .. row.second)
end

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

-- 三档硬度按攻击方、受击方的顺序组成九对，每对分别检查挡击和真实受伤。
for _, source in ipairs({ "Low", "Medium", "High" }) do
    for _, target in ipairs({ "Low", "Medium", "High" }) do
        for _, result in ipairs({ { damage = 0, prefix = "blocked" }, { damage = 1, prefix = "impact" } }) do
            local expected = "combat." .. result.prefix .. "." .. string.lower(source) .. "." .. string.lower(target)
            test.system("audio", "combat_impact", {
                sourceHardness = source, targetHardness = target, damage = result.damage
            }, function(data)
                assert(data.cue == expected and not data.silent, "通用命中声没有消费双方硬度和真实伤害结果")
                assert(data.sourceHardness == source and data.targetHardness == target, "攻击方与受击方硬度顺序颠倒")
            end, "三档硬度：" .. source .. " → " .. target .. " / " .. result.prefix)
        end
    end
end

for _, row in ipairs({
    { sourceItemId = "Stick_Wood", itemId = "Mine_Stone", damage = 0, source = "Low", target = "High", expected = "combat.blocked.low.high" },
    { sourceItemId = "Stick_Wood", itemId = "Mine_Stone", damage = 1, source = "Low", target = "High", expected = "combat.impact.low.high" },
    { sourceItemId = "Ore_Stone", itemId = "Mine_Stone", damage = 1, source = "High", target = "High", expected = "combat.impact.high.high" },
    { sourceItemId = "Axe_Stone", itemId = "Stick_Wood", damage = 1, source = "High", target = "Low", expected = "combat.impact.high.low" },
    { sourceItemId = "Stick_Wood", itemId = "Log", damage = 1, source = "Low", target = "Low", expected = "combat.impact.low.low" },
    { sourceItemId = "Dagger_Bone", itemId = "Ore_Copper", damage = 0, source = "Medium", target = "Medium", expected = "combat.blocked.medium.medium" },
    { sourceItemId = "Axe_Iron", itemId = "Mine_Stone", damage = 0, source = "High", target = "High", expected = "combat.blocked.high.high" },
    { sourceItemId = "IronHammer", itemId = "Mine_Stone", damage = 0, source = "High", target = "High", configuredSource = "High", expected = "combat.blocked.high.high" },
    { sourceItemId = "SteelHammer", itemId = "Mine_Stone", damage = 0, source = "High", target = "High", configuredSource = "High", expected = "combat.blocked.high.high" },
    { sourceItemId = "Arrow_Iron", itemId = "Mine_Stone", damage = 0, source = "High", target = "High", expected = "combat.blocked.high.high" },
    { sourceItemId = "Shovel_Iron", itemId = "Mine_Stone", damage = 0, source = "High", target = "High", expected = "combat.blocked.high.high" },
    { sourceItemId = "Arrow_Stone", itemId = "Mine_Stone", damage = 1, source = "High", target = "High", expected = "combat.impact.high.high" },
    { sourceItemId = "Arrow_Wood", itemId = "Mine_Stone", damage = 1, source = "Low", target = "High", expected = "combat.impact.low.high" },
    { sourceItemId = "Axe_Stone", itemId = "Mine_Coal", damage = 1, source = "High", target = "Low", expected = "combat.impact.high.low" },
    { sourceItemId = "Axe_Stone", itemId = "Mine_Obsidian", damage = 1, source = "High", target = "Medium", expected = "combat.impact.high.medium" }
}) do
    test.system("audio", "combat_impact", row, function(data)
        assert(data.cue == row.expected and data.sourceHardness == row.source and data.targetHardness == row.target,
            "真实物品定义、矿物表或弹丸接触硬度未生效：" .. row.sourceItemId .. " → " .. row.itemId)
        if row.configuredSource then
            assert(data.sourceConfiguredHardness == row.configuredSource, "显式接触硬度没有覆盖或继承：" .. row.sourceItemId)
        end
    end, "正式硬度定义：" .. row.sourceItemId .. " → " .. row.itemId)
end

for _, row in ipairs({
    { itemId = "Tree_Pine", damage = 1, targetCue = " combat.impact.tree ", expected = "combat.impact.tree" },
    { itemId = "Tree_Pine", damage = 0, targetCue = "combat.impact.tree", expected = "combat.blocked.medium.low" },
    { itemId = "Tree_Pine", damage = 1, targetCue = "combat.impact.tree", weaponCue = " probe.weapon ", expected = "probe.weapon" },
    { itemId = "Tree_Pine", damage = 0, targetBlockedCue = " probe.blocked.target ", expected = "probe.blocked.target" },
    { itemId = "Tree_Pine", damage = 0, targetBlockedCue = "probe.target", weaponBlockedCue = " probe.blocked.weapon ", expected = "probe.blocked.weapon" },
    { itemId = "Tree_Pine", damage = 1, targetBlockedCue = "probe.blocked", expected = "combat.impact.medium.low" },
    { itemId = "Tree_Pine", damage = -1, targetCue = "combat.impact.tree", expected = "" },
    { itemId = "Tree_Pine", material = "None", targetCue = "combat.impact.tree", expected = "" },
    { itemId = "Tree_Pine", invalidDamage = "NaN", expected = "" },
    { itemId = "Tree_Pine", invalidDamage = "Infinity", expected = "" }
}) do
    test.system("audio", "combat_impact", row, function(data)
        assert(data.cue == row.expected and data.silent == (row.expected == ""),
            "显式专用声音、挡击覆盖、拒绝或静音规则错误")
    end, "特殊命中声音与静音：" .. row.itemId .. " / " .. row.expected)
end

test.system("audio", "tree_impact_configuration", {}, function(data)
    local seen = {}
    assert(#data.trees >= 7, "正式树木生命定义采样缺失")
    for _, tree in ipairs(data.trees) do
        assert(not seen[tree.id], "树木存在重复生命模块：" .. tree.id)
        seen[tree.id] = true
        assert(tree.cue == "combat.impact.tree" and tree.material == "Wood",
            "树木未继承砍木声音或木材分类：" .. tree.id)
        assert(tree.normalCue == tree.cue and tree.axeCue == tree.cue and tree.blockedCue == "combat.blocked.high.low",
            "树木未区分砍入与挡击声音：" .. tree.id)
    end
    for _, id in ipairs({ "AppleTree", "PeachTree", "OrangeTree", "Tree_Coconut", "Tree_Pine", "Tree_Willow", "Tree_Oak" }) do
        assert(seen[id], "遗漏正式树木或继承定义：" .. id)
    end
end, "树木定义继承与普通攻击、斧砍、零伤选音")

test.system("audio", "combat_configuration", {}, function(data)
    assert(data.genericCueCount == 18, "通用命中声必须恰好3×3×2类")
    assert(data.damageDefinitions >= 60 and data.healthDefinitions >= 57, "正式战斗定义覆盖缺失")
    for _, row in ipairs(data.targets) do
        assert(row.material ~= "Auto", "目标没有解析材质：" .. row.id)
        assert(row.damagedExists and row.blockedExists, "目标命中 Cue 未登记：" .. row.id)
    end
    for _, row in ipairs(data.cues) do
        assert(row.exists and row.clipCount >= 3, "命中声音缺少三个独立变体：" .. row.id)
        assert(row.validClips, "命中声音变体未导入、重复或不符合48kHz单声道：" .. row.id)
        assert(row.spatial and row.sfx and not row.loop, "命中声音必须为世界空间短音：" .. row.id)
    end
end, "全部正式硬度定义与十八类通用声音")

test.system("networking", "combat_audio_wire", {}, function(data)
    assert(data.cue == "combat.blocked.stone" and data.world == "space:test" and data.ship == "test-ship",
        "音频消息丢失 Cue、世界或船体身份")
    assert(data.x == 123456.125 and data.y == -98765.5 and data.localX == 2.5 and data.localY == -3.25,
        "音频消息丢失 double 星区坐标或船内坐标")
    assert(data.first and not data.duplicate and not data.staleRealm and not data.wrongRealm and data.resumed,
        "正式音频接收门禁未去重、隔离旧世界或恢复新事件")
end, "联机音频正式序列化、重复事件与世界切换")

-- 每个槽位持有独立正式时钟，Lua 只检查返回的发声机会与剩余时间。
local function animal_idle_sequence(label, steps, validate)
    test.system("audio", "animal_idle_sequence", { steps = steps }, function(data)
        assert(#data.samples == #steps, "动物待机叫声没有返回完整连续采样")
        assert(data.randomUnchanged, "动物叫声消耗了玩法的全局随机状态")
        for i, sample in ipairs(data.samples) do
            local step = steps[i]
            assert(sample.slot == (step.slot or 1), label .. "，第 " .. i .. " 步串用了其他动物的时钟")
            assert(sample.due == (step.expectedDue == true), label .. "，第 " .. i .. " 步发声机会错误")
            assert(sample.initialized == (step.expectedInitialized ~= false),
                label .. "，第 " .. i .. " 步初始化或清理状态错误")
            assert(sample.remaining >= 0, label .. "，第 " .. i .. " 步时钟出现负剩余时间")
            if step.expectedRemaining ~= nil then
                assert(math.abs(sample.remaining - step.expectedRemaining) < 0.0001,
                    label .. "，第 " .. i .. " 步间隔错误：" .. tostring(sample.remaining))
            end
        end
        if validate then validate(data.samples) end
    end, "动物待机叫声：" .. label)
end

animal_idle_sequence("首次延迟、到期抑制、拒播等待和清理重建", {
    { reset = true, seed = 55, expectedRemaining = 5 },
    { deltaTime = 0, expectedRemaining = 5 },
    { deltaTime = 4.5, expectedRemaining = 0.5 },
    { untilDue = true, expectedDue = true, expectedRemaining = 10 },
    { deltaTime = 0, expectedRemaining = 10 },
    { deltaTime = 0.25, expectedRemaining = 9.75 },
    { untilDue = true, alreadyPlaying = true, expectedRemaining = 10 },
    { deltaTime = 0.25, expectedRemaining = 9.75 },
    { untilDue = true, eligible = false, expectedRemaining = 10 },
    { deltaTime = 0.25, eligible = false, expectedRemaining = 9.75 },
    { untilDue = true, expectedDue = true, expectedRemaining = 10 },
    { clear = true, expectedInitialized = false, expectedRemaining = 0 },
    { deltaTime = 100, expectedInitialized = false, expectedRemaining = 0 },
    { reset = true, seed = 99, initialDelayMinSeconds = 2, initialDelayMaxSeconds = 2, expectedRemaining = 2 },
    { deltaTime = 0.25, expectedRemaining = 1.75 },
    { untilDue = true, expectedDue = true, expectedRemaining = 10 }
})

animal_idle_sequence("同种独立错峰、随机间隔和局部随机状态", {
    { slot = 1, reset = true, seed = 123, intervalMinSeconds = 2, intervalMaxSeconds = 6, initialDelayMinSeconds = 1, initialDelayMaxSeconds = 5 },
    { slot = 2, reset = true, seed = 456, intervalMinSeconds = 2, intervalMaxSeconds = 6, initialDelayMinSeconds = 1, initialDelayMaxSeconds = 5 },
    { slot = 3, reset = true, seed = 123, intervalMinSeconds = 2, intervalMaxSeconds = 6, initialDelayMinSeconds = 1, initialDelayMaxSeconds = 5 },
    { slot = 1, untilDue = true, expectedDue = true },
    { slot = 2, deltaTime = 0 },
    { slot = 3, deltaTime = 0 },
    { slot = 3, untilDue = true, expectedDue = true },
    { slot = 1, untilDue = true, expectedDue = true },
    { slot = 3, untilDue = true, expectedDue = true },
    { slot = 2, untilDue = true, expectedDue = true },
    { slot = 1, clear = true, expectedInitialized = false, expectedRemaining = 0 },
    { slot = 2, deltaTime = 0 }
}, function(samples)
    for i = 1, 3 do
        assert(samples[i].remaining >= 1 and samples[i].remaining <= 5, "动物首播延迟越出配置范围")
    end
    assert(samples[1].remaining == samples[3].remaining, "相同种子的独立时钟初始结果不一致")
    assert(math.abs(samples[1].remaining - samples[2].remaining) > 0.0001, "不同动物首播挤在同一时刻")
    assert(samples[2].remaining == samples[5].remaining and samples[3].remaining == samples[6].remaining,
        "推进一只动物时修改了其他动物的时钟")
    assert(samples[4].remaining == samples[7].remaining and samples[8].remaining == samples[9].remaining,
        "独立动物相互消费了随机序列")
    for _, i in ipairs({ 4, 7, 8, 9, 10 }) do
        assert(samples[i].remaining >= 2 and samples[i].remaining <= 6, "动物重复叫声间隔越出配置范围")
    end
    assert(samples[10].remaining == samples[12].remaining, "清理一只动物重置了其他动物的叫声")
end)

animal_idle_sequence("卡帧不追补齐叫、负时间不倒退", {
    { reset = true, initialDelayMinSeconds = 2, initialDelayMaxSeconds = 2, expectedRemaining = 2 },
    { deltaTime = 1000, expectedDue = true, expectedRemaining = 10 },
    { deltaTime = 0, expectedRemaining = 10 },
    { deltaTime = -100, expectedRemaining = 10 },
    { deltaTime = 0.25, expectedRemaining = 9.75 },
    { clear = true, expectedInitialized = false, expectedRemaining = 0 }
})

for _, row in ipairs({ { first = "probe.first", second = "probe.second", errors = 0 }, { first = "probe.first", second = "PROBE.FIRST", errors = 1 } }) do
    test.system("audio", "catalog_lifecycle", row, function(data)
        assert(data.cueCount == 2 and data.errors == row.errors, "独立音频目录没有报告重复稳定事件 ID")
        assert(data.found and data.resolvedFirst and not data.emptyAccepted, "目录索引重建、去首尾空格或不区分大小写查询错误")
    end, "音频目录装配、重复校验和缓存重建")
end

-- 只有主动停用事件允许空录音，不能把丢失录音的正常事件当成静音配置。
for _, row in ipairs({ { firstEnabled = false, errors = 0 }, { firstEnabled = true, errors = 1 } }) do
    test.system("audio", "catalog_lifecycle", row, function(data)
        assert(data.defaultPlaybackEnabled, "新增 Cue 没有默认启用播放")
        assert(data.firstPlaybackEnabled == row.firstEnabled and data.firstClipCount == 0,
            "事件播放开关或空录音采样不正确")
        assert(data.errors == row.errors, "目录混淆了主动停用与正常事件缺失录音")
        assert(data.found and data.resolvedFirst, "停用事件丢失了正式目录身份")
    end, "事件启停与缺失录音校验：" .. tostring(row.firstEnabled))
end

test.system("audio", "weather_audio_configuration", {}, function(data)
    assert(math.abs(data.RainMaximumVolume - 0.9) < 0.00001 and
        math.abs(data.WindMaximumVolume - 0.2) < 0.00001, "正式 JSON 没有保留雨声音量和减半后的风声音量")
    assert(data.WindStartStrength == 0.5 and data.WindVolumeExponent == 1.5 and
        data.WindVolumeResponsePerSecond == 0.75 and math.abs(data.WindCrossFadeSeconds - 0.8) < 0.00001,
        "正式 JSON 的风声起声、曲线或淡化参数没有保留")
    assert(data.ThunderNearRadius == 64 and data.ThunderPropagationSpeed == 64,
        "地表与洞口共用的雷声配置错误")
end, "天气声音 JSON：正式音量、风声曲线与雷声参数")

for _, row in ipairs({
    { field = "WindVolumeExponent", value = 0 },
    { field = "WindCrossFadeSeconds", value = 0 },
    { field = "ThunderPropagationSpeed", value = 0 },
    { field = "WindVolumeResponsePerSecond", value = -0.1 },
    { field = "RainMaximumVolume", value = 1e39 }
}) do
    test.system("audio", "weather_audio_configuration", { configuration = { [row.field] = row.value } }, function(data)
        assert(data.error ~= nil, "天气声音 JSON 未拒绝会产生无效数学结果的参数：" .. row.field)
    end, "天气声音 JSON 数学域：" .. row.field)
end

-- 玩家雨声只跟随有效降水；缺失区块样本继续上一目标，不重建循环。
local function rain_sequence(label, steps, configuration)
    test.system("audio", "rain_sequence", { steps = steps, configuration = configuration }, function(data)
        assert(#data.samples == #steps, "雨声没有返回完整连续采样")
        for i, sample in ipairs(data.samples) do
            local step = steps[i]
            assert(math.abs(sample.intensity - step.intensity) < 0.00001,
                label .. "，第 " .. i .. " 步雨声没有从当前音量连续过渡")
            assert(math.abs(sample.target - step.target) < 0.00001,
                label .. "，第 " .. i .. " 步缺样或停雨目标错误")
            local maximumVolume = configuration and configuration.RainMaximumVolume or 0.9
            assert(math.abs(sample.volume - step.intensity * maximumVolume) < 0.00001,
                label .. "，第 " .. i .. " 步雨声音量倍率错误")
        end
    end, "雨声连续性：" .. label)
end

rain_sequence("完整十二秒起雨，区块缺样与无效数值不误停", {
    { rain = 0.8, intensity = 0, target = 0.8 },
    { rain = 0.8, deltaTime = 3, intensity = 0.125, target = 0.8 },
    { rain = 0.8, deltaTime = 3, intensity = 0.4, target = 0.8 },
    { rain = 0.8, deltaTime = 6, intensity = 0.8, target = 0.8 },
    { available = false, deltaTime = 30, intensity = 0.8, target = 0.8 },
    { invalidRain = true, deltaTime = 30, intensity = 0.8, target = 0.8 }
})

rain_sequence("真实停雨淡出，途中恢复与缺样续接不归零", {
    { rain = 0.8, deltaTime = 12, intensity = 0.8, target = 0.8 },
    { rain = 0, deltaTime = 3, intensity = 0.675, target = 0 },
    { rain = 0, deltaTime = 3, intensity = 0.4, target = 0 },
    { rain = 1, intensity = 0.4, target = 1 },
    { available = false, deltaTime = 6, intensity = 0.7, target = 1 },
    { rain = 1, deltaTime = 6, intensity = 1, target = 1 },
    { rain = 0, deltaTime = 12, intensity = 0, target = 0 }
})

rain_sequence("未就绪不抢播，小雨保留完整过渡，退出重置", {
    { available = false, rain = 1, deltaTime = 100, intensity = 0, target = 0 },
    { invalidRain = true, deltaTime = 100, intensity = 0, target = 0 },
    { rain = 0.1, deltaTime = 6, intensity = 0.05, target = 0.1 },
    { available = false, deltaTime = 6, intensity = 0.1, target = 0.1 },
    { reset = true, intensity = 0, target = 0 },
    { rain = 0.6, deltaTime = 6, intensity = 0.3, target = 0.6 },
    { rain = 0.6, deltaTime = 6, intensity = 0.6, target = 0.6 }
})

rain_sequence("负时间、卡帧、越界雨量和零时长边界", {
    { rain = 2, deltaTime = 1000, intensity = 1, target = 1 },
    { rain = 0.2, deltaTime = -1, intensity = 1, target = 0.2 },
    { rain = 0.2, deltaTime = 6, intensity = 0.6, target = 0.2 },
    { rain = 0.2, deltaTime = 6, intensity = 0.2, target = 0.2 },
    { rain = -1, transitionSeconds = 0, intensity = 0, target = 0 },
    { rain = 1, transitionSeconds = -1, intensity = 1, target = 1 }
})

rain_sequence("JSON 自定义雨声音量与重置后重用配置", {
    { rain = 1, deltaTime = 12, intensity = 1, target = 1 },
    { reset = true, intensity = 0, target = 0 },
    { rain = 0.5, deltaTime = 12, intensity = 0.5, target = 0.5 }
}, { RainMaximumVolume = 0.3 })

rain_sequence("JSON 零音量不回退硬编码默认值", {
    { rain = 1, deltaTime = 12, intensity = 1, target = 1 }
}, { RainMaximumVolume = 0 })

-- 使用玩家当地风强直接驱动正式风声状态，不建世界或播放声音。
local function wind_sequence(label, steps, configuration)
    test.system("audio", "wind_sequence", { steps = steps, configuration = configuration }, function(data)
        assert(#data.samples == #steps, "风声没有返回完整连续采样")
        for i, sample in ipairs(data.samples) do
            local step = steps[i]
            local expectedCue = step.gale and "weather.wind.gale.loop" or "weather.wind.loop"
            assert(sample.gale == (step.gale == true) and sample.cue == expectedCue,
                label .. "，第 " .. i .. " 步八级风选音边界错误")
            local volume = sample.normalVolume + sample.galeVolume
            local maximumVolume = configuration and configuration.WindMaximumVolume or 0.2
            assert(sample.normalVolume >= 0 and sample.galeVolume >= 0 and volume <= maximumVolume + 0.00001,
                label .. "，第 " .. i .. " 步交叉淡化超出风音量上限")
            if step.volume ~= nil then
                assert(math.abs(volume - step.volume) < 0.00001,
                    label .. "，第 " .. i .. " 步风音量曲线或停止错误")
            end
            if step.galeRatio ~= nil then
                assert(volume > 0 and math.abs(sample.galeVolume / volume - step.galeRatio) < 0.00001,
                    label .. "，第 " .. i .. " 步切换未从当前音量平滑续接")
            end
            if step.presented ~= nil then
                assert(math.abs(sample.presentedStrength - step.presented) < 0.00001,
                    label .. "，第 " .. i .. " 步玩家风强平滑或清理错误")
            end
        end
    end, "风声音效：" .. label)
end

for _, row in ipairs({
    { strength = -1, volume = 0 },
    { strength = 0, volume = 0 },
    { strength = 0.5, volume = 0 },
    { strength = 0.7, volume = 0.0505964425, galeRatio = 0 },
    { strength = 0.7999, volume = 0.0929051285, galeRatio = 0 },
    { strength = 0.8, gale = true, volume = 0.0929516005, galeRatio = 1 },
    { strength = 0.9, gale = true, volume = 0.1431083505, galeRatio = 1 },
    { strength = 1, gale = true, volume = 0.2, galeRatio = 1 },
    { strength = 2, gale = true, volume = 0.2, galeRatio = 1 }
}) do
    wind_sequence("弱风静音与七/八/九/十级边界：" .. row.strength, { row })
end

wind_sequence("七级升八级交叉淡化，短暂降级再升回不从头切换", {
    { strength = 0.7, volume = 0.0505964425, galeRatio = 0 },
    { strength = 0.8, deltaTime = 0.4, gale = true, volume = 0.0929516005, galeRatio = 0.5 },
    { strength = 0.8, deltaTime = 0.4, gale = true, volume = 0.0929516005, galeRatio = 1 },
    { strength = 0.7, deltaTime = 0.2, galeRatio = 0.75 },
    { strength = 0.8, deltaTime = 0.2, gale = true, volume = 0.0929516005, galeRatio = 1 },
    { strength = 0.7, deltaTime = 0.8, volume = 0.0505964425, galeRatio = 0 }
})

wind_sequence("初进八级风立即选狂风且保留三秒风强过渡，停风归零", {
    { strength = 0.8, deltaTime = 0, gale = true, volume = 0, presented = 0 },
    { strength = 0.8, deltaTime = 1.5, gale = true, volume = 0, presented = 0.5 },
    { strength = 0.8, deltaTime = 0.9, gale = true, volume = 0.0929516005, galeRatio = 1, presented = 0.8 },
    { strength = 0, volume = 0, presented = 0 }
})

wind_sequence("退出重置清除双路包络，新世界按当前风级重新选音", {
    { strength = 0.9, gale = true, volume = 0.1431083505, galeRatio = 1 },
    { reset = true, volume = 0, presented = 0 },
    { strength = 0.7, volume = 0.0505964425, galeRatio = 0 },
    { reset = true, volume = 0, presented = 0 },
    { strength = 0.8, deltaTime = -1, gale = true, volume = 0, presented = 0 },
    { strength = 1, deltaTime = 100, gale = true, volume = 0.2, galeRatio = 1 },
    { strength = 0, volume = 0, presented = 0 }
})

wind_sequence("JSON 自定义起声、音量曲线和交叉淡化", {
    { strength = 0.5, volume = 0 },
    { strength = 0.7, volume = 0.15, galeRatio = 0 },
    { strength = 0.8, deltaTime = 0.5, gale = true, volume = 0.3, galeRatio = 0.25 },
    { strength = 0.8, deltaTime = 1.5, gale = true, volume = 0.3, galeRatio = 1 },
    { reset = true, volume = 0, presented = 0 },
    { strength = 1, gale = true, volume = 0.6, galeRatio = 1 }
}, {
    WindStartStrength = 0.6, WindMaximumVolume = 0.6, WindVolumeExponent = 1,
    WindVolumeResponsePerSecond = 2, WindCrossFadeSeconds = 2
})

wind_sequence("JSON 音量响应速率控制上升和下降", {
    { strength = 1, deltaTime = 1, transitionSeconds = 0.01, gale = true, volume = 0.05, galeRatio = 1 },
    { strength = 1, deltaTime = 1, transitionSeconds = 0.01, gale = true, volume = 0.1, galeRatio = 1 },
    { strength = 0, deltaTime = 1, transitionSeconds = 0.01, volume = 0.05, galeRatio = 0 },
    { strength = 0, deltaTime = 1, transitionSeconds = 0.01, volume = 0 }
}, {
    WindStartStrength = 0.5, WindMaximumVolume = 0.4, WindVolumeExponent = 1.5,
    WindVolumeResponsePerSecond = 0.05, WindCrossFadeSeconds = 0.8
})

wind_sequence("JSON 零风声音量不回退硬编码默认值", {
    { strength = 1, gale = true, volume = 0 }
}, { WindMaximumVolume = 0 })

-- 河流索引读取权威水量、水文和融冰层；Lua 只检查生产索引返回值与增量版本。
test.system("audio", "nature_ambience_sequence", {}, function(data)
    local initial = data.initial
    assert(data.waterContactProvider == "explicit test fixture IDs; no GameRes lookup",
        "规则探针没有显式提供 WaterContact 液体特征")
    assert(initial.candidateCount == 2 and initial.revision > 0,
        "初始稀疏索引未只保留两格可听河流")
    assert(data.initialLowStrength > 0 and data.initialHighStrength > data.initialLowStrength,
        "河水增深且流量增大时声强没有增强")
    assert(data.lakeStrength == 0 and data.seaStrength == 0 and data.dryStrength == 0 and
        data.nonWaterContactStrength == 0, "湖泊、海水、干格或非 WaterContact 液体误成河流声")
    assert(initial.found and initial.x == 0.5 and initial.y == 0.5 and
        math.abs(initial.strength - data.initialLowStrength) < 0.000001,
        "最近河流没有返回实际格心与对应强度")
    assert(data.radiusBoundary.found and data.radiusBoundary.x == 1.5 and data.radiusBoundary.y == 0.5,
        "半径边界上的最近河流格被漏掉")
    assert(not data.outsideRadius.found and not data.emptyRadius.found,
        "半径外或周围无可听河流时仍返回候选")

    assert(data.singleLiquidStrength > data.initialLowStrength and
        data.singleEnvironmentStrength > data.singleLiquidStrength,
        "单格水量或流量变大后声强没有单调增加")
    assert(data.singleLiquidRevision > initial.revision and
        data.singleEnvironmentRevision > data.singleLiquidRevision,
        "单格液体或河流环境变化没有推进候选版本")
    assert(data.unrelatedLayerRevisionBefore == data.unrelatedLayerRevisionAfter,
        "无关湿度层变化误刷新河流候选")
    assert(data.liquidBatchStrength > data.initialHighStrength and
        data.environmentBatchStrength > data.liquidBatchStrength,
        "液体或河流环境批次没有更新候选声强")
    assert(data.liquidBatchRevision > data.singleEnvironmentRevision and
        data.environmentBatchRevision > data.liquidBatchRevision,
        "液体或河流环境批次没有推进候选版本")
    assert(data.singleLiquidCandidateCount == initial.candidateCount and
        data.singleEnvironmentCandidateCount == initial.candidateCount and
        data.liquidBatchCandidateCount == initial.candidateCount and
        data.environmentBatchCandidateCount == initial.candidateCount,
        "仅声强变化时错误增删河流候选")

    assert(data.frozenStrength == 0 and data.halfThawedStrength > data.frozenStrength and
        data.fullyThawedStrength > data.halfThawedStrength,
        "冰盖融化时河流声没有从静音逐渐增强")
    assert(data.halfThawedCandidateCount == initial.candidateCount + 1 and
        data.halfThawedRevision > data.environmentBatchRevision,
        "融冰环境事件没有增量加入可听河流格")
    assert(data.revisionAfterDispose == data.revisionBeforeDispose and
        data.candidateCountAfterDispose == 0 and not data.queryAfterDispose,
        "Dispose 后候选未清除、查询未静音或地形事件仍修改版本")
end, "河流环境声：WaterContact、水文增量、融冰、最近格和释放")

test.system("audio", "foliage_ambience_volume", { samples = {
    { nearbySourceCount = 0, windStrength = 0.75 },
    { nearbySourceCount = 4, windStrength = 0 },
    { nearbySourceCount = 1, windStrength = 0.2 },
    { nearbySourceCount = 4, windStrength = 0.2 },
    { nearbySourceCount = 4, windStrength = 0.75 },
    { nearbySourceCount = 8, windStrength = 0.75 },
    { nearbySourceCount = 100, windStrength = 100 }
} }, function(data)
    assert(#data.samples == 7, "叶声音量探针没有返回全部输入样本")
    for _, sample in ipairs(data.samples) do
        assert(sample.volume >= 0 and sample.volume <= 1, "叶声音量越出零至一")
    end
    assert(data.samples[1].volume == 0 and data.samples[2].volume == 0,
        "无树或无风时叶声没有静音")
    assert(data.samples[3].volume > 0 and data.samples[4].volume >= data.samples[3].volume,
        "附近树木密度增加时叶声没有增强")
    assert(data.samples[5].volume > data.samples[4].volume and
        data.samples[6].volume >= data.samples[5].volume,
        "风力增加或附近树木增加时叶声没有单调增强")
end, "树叶环境声：风力与附近树木密度")

test.system("audio", "nature_foliage_config", {}, function(data)
    assert(data.appleTreeFoliage and data.coconutTreeFoliage and data.pineTreeFoliage and
        data.willowTreeFoliage and data.treesUseResourceEntities,
        "正式树木定义缺少叶声标签，或没有通过原生 resource 实体运行")
    assert(data.deadTreeHasTreeTag and not data.deadTreeHasFoliage,
        "枯树继承了叶声标签")
end, "树木正式定义：原生资源实体标签与枯树排除")

-- 地表优先级：深水静音 > 浅水 > 积雪 > 人工支撑/石地 > 沙滩 > 草被 > 泥土。
test.system("audio", "motion_audio_spatial", {}, function(data)
    assert(data.wrappedPosition and data.footstepDelta and data.swimmingDelta and data.independentBaseline,
        "脚步和游泳共用物理帧位置时丢失了环绕接缝或误共用位移基线")
    assert(data.infinitePosition and data.infiniteDelta and data.negativeSeam and data.halfPeriodTie and data.stationary,
        "音效空间快照在无限世界、反向接缝、半周期或静止情况下改变位移")
end, "脚步与游泳每物理帧共用一次世界拓扑，独立计步与传送边界保持")

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

-- 十档只采样正式脚步规则，Lua 检查逐级加重和边界，不重算插值算法。
local previous_snow_volume = 0
local previous_snow_pitch = 2
for layer = 1, 10 do
    test.system("audio", "footstep_surface", { snow = layer / 10, grass = 2 }, function(data)
        assert(data.cue == "player.footstep.snow" and not data.silent, "第 " .. layer .. " 层未选雪地脚步")
        assert(data.volume > previous_snow_volume and data.pitch < previous_snow_pitch,
            "积雪加深时响度未逐档增强或压雪声未逐档变厚：" .. layer)
        assert(data.volume >= 0.44999 and data.volume <= 1.60001 and data.pitch >= 0.85999 and data.pitch <= 1.10001,
            "雪地脚步倍率越出浅雪与深雪范围")
        if layer == 1 then
            assert(math.abs(data.volume - 0.45) < 0.00001 and math.abs(data.pitch - 1.10) < 0.00001,
                "一层雪没有使用轻、短的脚步参数")
        elseif layer == 10 then
            assert(math.abs(data.volume - 1.60) < 0.00001 and math.abs(data.pitch - 0.86) < 0.00001,
                "十层雪没有使用更响、更厚的压雪参数")
        end
        previous_snow_volume = data.volume
        previous_snow_pitch = data.pitch
    end, "雪深逐层增强：" .. layer .. " 层")
end

for _, row in ipairs({
    { snow = -1, grass = 2, cue = "player.footstep.grass", volume = 1, pitch = 1 },
    { snow = 0, grass = 2, cue = "player.footstep.grass", volume = 1, pitch = 1 },
    { snow = 0.05, grass = 2, cue = "player.footstep.grass", volume = 1, pitch = 1 },
    { snow = 0.0501, grass = 2, cue = "player.footstep.snow", volume = 0.45, pitch = 1.10 },
    { snow = 0.0999, cue = "player.footstep.snow", volume = 0.45, pitch = 1.10 },
    { snow = 0.1001, cue = "player.footstep.snow", volume = 0.57777778, pitch = 1.07333333 },
    { snow = 0.1999, cue = "player.footstep.snow", volume = 0.57777778, pitch = 1.07333333 },
    { snow = 1.0001, cue = "player.footstep.snow", volume = 1.60, pitch = 0.86 },
    { snow = 2, cue = "player.footstep.snow", volume = 1.60, pitch = 0.86 },
    { snow = 1000, cue = "player.footstep.snow", volume = 1.60, pitch = 0.86 },
    { biome = 2, snow = 0, grass = 2, cue = "player.footstep.sand", volume = 1, pitch = 1 },
    { biome = 3, snow = 0, cue = "player.footstep.sand", volume = 1, pitch = 1 },
    { biome = 7, snow = 0, cue = "player.footstep.stone", volume = 1, pitch = 1 },
    { snow = 0, support = true, grass = 2, cue = "player.footstep.stone", volume = 1, pitch = 1 },
    { snow = 0, cue = "player.footstep.dirt", volume = 1, pitch = 1 },
    { snow = 0.1, support = true, cue = "player.footstep.snow", volume = 0.45, pitch = 1.10 },
    { water = 0.0301, snow = 1, cue = "player.footstep.water", volume = 0.1, pitch = 1 },
    { water = 0.3, snow = 2, cue = "player.footstep.water", volume = 0.72222222, pitch = 1 },
    { water = 0.3001, snow = 2, cue = "", volume = 1, pitch = 1 },
    { water = 0.55, snow = 1, cue = "", volume = 1, pitch = 1 }
}) do
    test.system("audio", "footstep_surface", row, function(data)
        assert(data.cue == row.cue and data.silent == (row.cue == ""), "雪声改变了无雪或地表材质优先级")
        assert(math.abs(data.volume - row.volume) < 0.00001, "雪层量化、声音封顶或原材质音量错误")
        assert(math.abs(data.pitch - row.pitch) < 0.00001, "雪层音高或非雪脚步原音高错误")
    end, "雪声量化与材质隔离：雪=" .. tostring(row.snow) .. "，水=" .. tostring(row.water or 0))
end
-- 人工地表声明自己的音色，水和雪仍覆盖地板材质。
for _, row in ipairs({
    { support = true, surfaceCue = "player.footstep.wood", cue = "player.footstep.wood" },
    { support = true, surfaceCue = "player.footstep.stone", cue = "player.footstep.stone" },
    { support = true, surfaceCue = "player.footstep.metal", cue = "player.footstep.metal" },
    { biome = 4, surfaceCue = "player.footstep.ice", cue = "player.footstep.ice" },
    { biome = 4, grass = 2, surfaceCue = "player.footstep.ice", cue = "player.footstep.ice" },
    { biome = 4, grass = 2, surfaceCue = "player.footstep.stone", cue = "player.footstep.stone" },
    { biome = 4, grass = 2, surfaceCue = "player.footstep.dirt", cue = "player.footstep.grass" },
    { biome = 4, grass = 0, surfaceCue = "player.footstep.dirt", cue = "player.footstep.dirt" },
    { support = true, grass = 2, surfaceCue = "player.footstep.dirt", cue = "player.footstep.dirt" },
    { surfaceCue = "mod:surface.footstep", cue = "mod:surface.footstep" },
    { support = true, surfaceCue = "player.footstep.wood", water = 0.1, cue = "player.footstep.water" },
    { support = true, surfaceCue = "player.footstep.wood", snow = 0.5, cue = "player.footstep.snow" },
    { support = true, surfaceCue = "player.footstep.metal", snow = 0.5, cue = "player.footstep.snow" },
    { support = true, surfaceCue = "player.footstep.metal", water = 0.1, cue = "player.footstep.water" },
    { surfaceCue = "player.footstep.ice", snow = 0.5, cue = "player.footstep.snow" },
    { surfaceCue = "player.footstep.ice", water = 0.1, cue = "player.footstep.water" },
    { support = true, surfaceCue = "player.footstep.wood", water = 0.31, cue = "" }
}) do
    test.system("audio", "footstep_surface", row, function(data)
        assert(data.cue == row.cue, "地板脚步音色或水雪优先级错误")
    end, "地表声明脚步音色：" .. row.cue)
end
