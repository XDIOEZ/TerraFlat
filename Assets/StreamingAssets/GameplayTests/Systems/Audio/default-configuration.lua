-- 保留正式默认音频资源、装配和原有相关现场契约。
test.runtime("audio", "voice_lifecycle", {}, function(data)
    local nature = data.natureAmbience
    assert(nature ~= nil and #nature.cues == 2, "没有采样两条正式自然环境循环")
    local natureCueIds = {
        ["ambient.foliage.rustle.loop"] = true,
        ["ambient.river.flow.loop"] = true
    }
    for _, cue in ipairs(nature.cues) do
        assert(natureCueIds[cue.id] and cue.accepted and cue.sourceFound and cue.sourcePlaying and cue.stopped,
            "自然环境循环没有真实播放、采样或立即释放：" .. tostring(cue.id))
        natureCueIds[cue.id] = nil
        assert(cue.bus == "Ambient" and cue.loop and cue.minDistance == 2 and cue.maxDistance == 32,
            cue.id .. " Cue 没有使用 Ambient 循环与 2/32 格距离")
        assert(cue.spatialBlend == 1 and cue.rolloff == "Linear" and
            cue.sourceSpatialBlend == 1 and cue.sourceRolloff == "Linear" and
            cue.sourceMinDistance == 2 and cue.sourceMaxDistance == 32,
            cue.id .. " 实际 AudioSource 没有使用空间线性衰减与正式距离")
        assert(cue.clipCount == 1 and cue.clipChannels == 1 and cue.clipFrequency == 48000 and
            cue.clipSamples > 0 and math.abs(cue.clipDuration - 20) < 0.05,
            cue.id .. " 没有单条 20 秒 48kHz 单声道正式录音")
        assert(cue.readable and cue.rms > 0 and cue.minimumWindowRms >= cue.rms * 0.03,
            cue.id .. " 循环录音出现人为静音空档")
    end
    assert(next(natureCueIds) == nil, "自然环境循环 Cue 有重复或缺失")

    local foliage = nature.foliageSources
    assert(foliage.leafDefinitionTagged and not foliage.deadDefinitionTagged,
        "正式 Pine/Dead 定义的 NatureAudio.Foliage 标签错误")
    assert(foliage.afterLeafCount == foliage.beforeCount + 1 and
        foliage.afterDeadCount == foliage.afterLeafCount and
        foliage.afterRemoveLeafCount == foliage.beforeCount and
        foliage.afterCleanupCount == foliage.beforeCount,
        "正式自然实体空间索引未按树叶标签增减计数或未清除测试实体")
    assert(foliage.afterLeafRevision > foliage.beforeRevision and
        foliage.afterDeadRevision > foliage.afterLeafRevision and
        foliage.afterRemoveLeafRevision > foliage.afterDeadRevision and
        foliage.afterCleanupRevision > foliage.afterRemoveLeafRevision,
        "原生树木注册、枯树注册或释放没有推进空间索引版本")

    local propagationDistances = {
        ["animal.wolf.idle"] = 64, ["animal.bird.idle"] = 64,
        ["animal.bee.idle"] = 16, ["animal.wildboar.idle"] = 16
    }
    assert(#data.animalPropagationRanges == 16, "动物传播边界没有完成全部现场采样")
    for _, sample in ipairs(data.animalPropagationRanges) do
        local distance = propagationDistances[sample.id]
        assert(distance ~= nil, "传播边界采样出现未知声音")
        if sample.distance < distance then
            assert(sample.accepted and sample.sourceFound and sample.voicesAfter == sample.voicesBefore + 1,
                sample.id .. " 在传播范围内仍然未播放")
            assert(sample.minDistance == 1.5 and sample.maxDistance == distance
                and sample.spatialBlend == 1 and sample.rolloff == "Linear", "真实声源未使用 JSON 档位衰减")
        else
            assert(not sample.accepted and not sample.sourceFound and sample.voicesAfter == sample.voicesBefore,
                sample.id .. " 在静音边界或范围外仍然占用声源")
        end
    end
    local animalDistances = {
        Chicken = 16, WildBoar = 16, Wolf = 64, Bird = 64, Seagull = 64,
        Bee = 16, Sheep = 32, Rabbit = 16, SnowLeopard = 32, SmallFish = 8
    }
    assert(#data.animalIdleCues == 10, "没有采样全部十种动物的待机声音")
    for _, cue in ipairs(data.animalIdleCues) do
        local distance = animalDistances[cue.actorId]
        assert(distance ~= nil, "动物声音资源重复或出现未知物种：" .. tostring(cue.actorId))
        animalDistances[cue.actorId] = nil
        local expectedId = "animal." .. string.lower(cue.actorId) .. ".idle"
        local config = cue.parameters
        assert(cue.actorLoaded and cue.isActor and cue.moduleCount == 1 and cue.moduleEnabled,
            cue.actorId .. " 没有启用唯一的动物环境音模块")
        assert(cue.stableName == "animalAudio" and cue.implementation == "Mod_AnimalAmbientAudio",
            cue.actorId .. " 动物声音模块没有装配正式通用实现")
        assert(config.cueId == expectedId and cue.loaded and cue.cueId == expectedId,
            cue.actorId .. " 未绑定本物种正式待机 Cue")
        assert(config.intervalMinSeconds >= 0.25 and config.intervalMaxSeconds >= config.intervalMinSeconds,
            cue.actorId .. " 缺少有效的随机叫声间隔")
        assert(config.initialDelayMinSeconds > 0 and config.initialDelayMaxSeconds >= config.initialDelayMinSeconds
            and config.volumeScale > 0, cue.actorId .. " 初次加载会立刻齐叫或音量配置无效")
        assert(cue.bus == "Sfx" and not cue.loop and cue.spatialBlend == 1 and cue.rolloff == "Linear",
            expectedId .. " 没有使用单次世界 SFX 和完整空间线性衰减")
        assert(cue.minDistance == 1.5 and cue.maxDistance == distance and cue.maxInstances == 3
            and math.abs(cue.cooldown - 0.2) < 0.00001, expectedId .. " 距离或群体并发限流配置错误")
        assert(#cue.clips >= 2, expectedId .. " 缺少不同实录片段变体")
        for _, clip in ipairs(cue.clips) do
            assert(clip.loaded and clip.channels == 1 and clip.frequency == 48000 and clip.samples > 0,
                expectedId .. " 没有完整导入 48kHz 单声道动物录音")
        end
    end
    local fire = data.fireCue
    assert(fire.configured == "fire.burning.loop" and fire.loaded and fire.loop and #fire.clips == 1,
        "篝火没有绑定正式燃烧循环或音频未导入")
    assert(fire.bus == "Sfx" and fire.spatialBlend == 1 and fire.rolloff == "Linear" and fire.cooldown == 0,
        "篝火燃烧声未使用无冷却的世界 SFX 线性衰减")
    assert(fire.minDistance == 1.5 and fire.maxDistance == 14, "篝火声音距离范围改变")
    assert(fire.clips[1].channels == 1 and fire.clips[1].frequency == 48000
        and fire.clips[1].samples == 960000, "篝火素材不是 20 秒 48kHz 单声道循环")
    local rain = data.rainCue
    assert(rain.loaded and rain.clipCount == 1 and rain.loop and rain.cooldown == 0 and rain.maxInstances == 1,
        "雨声没有保持单实例、无冷却的连续循环")
    assert(rain.bus == "Ambient" and rain.spatialBlend == 0 and rain.channels == 2 and rain.frequency == 48000,
        "雨声没有使用全局环境音或 48kHz 立体声素材")
    assert(rain.preload and rain.decoded and rain.readable and rain.samples >= rain.frequency * 20,
        "雨声没有预先解码为可连续读取的长循环")
    assert(rain.rms > 0 and rain.minimumWindowRms >= rain.rms * 0.1
        and rain.maximumWindowRms <= rain.minimumWindowRms * 4,
        "雨声仍有 0.1 秒以上的低谷或明显断续")
    local click = data.disabledUiClick
    assert(click.loaded and click.id == "ui.click" and not click.playbackEnabled,
        "正式点击事件没有保留明确停用配置")
    assert(not click.directAccepted and not click.loopAccepted, "停用点击事件仍然取得了单次或循环播放句柄")
    assert(click.voicesBefore == click.voicesAfter and click.sourcesBefore == click.sourcesAfter,
        "停用点击事件分配或抢占了声源")
    assert(click.randomUnchanged and #click.messages == 0, "主动静音仍然选片或产生缺失录音警告")
    assert(#data.feedbackCues == 6, "未读取全部玩法反馈音效")
    for _, cue in ipairs(data.feedbackCues) do
        assert(cue.loaded and cue.variants == 2 and cue.validClips and cue.spatial,
            cue.id .. " 没有正确导入双变体单声道空间 Cue")
    end
    local movementCueKinds = {
        ["player.footstep.dirt"] = "footstep", ["player.footstep.grass"] = "footstep",
        ["player.footstep.sand"] = "footstep", ["player.footstep.stone"] = "footstep",
        ["player.footstep.metal"] = "footstep",
        ["player.footstep.ice"] = "footstep",
        ["player.footstep.wood"] = "footstep", ["player.footstep.snow"] = "footstep",
        ["player.footstep.water"] = "footstep", ["fishing.cast"] = "fishing",
        ["fishing.hook"] = "fishing", ["fishing.reel"] = "fishing"
    }
    assert(#data.movementCues == 12, "未读取九种脚步和三种钓鱼反馈 Cue")
    for _, cue in ipairs(data.movementCues) do
        local kind = movementCueKinds[cue.id]
        assert(kind ~= nil, "脚步或钓鱼 Cue 重复或出现未知 ID：" .. tostring(cue.id))
        movementCueKinds[cue.id] = nil
        assert(cue.loaded and cue.playbackEnabled and cue.bus == "Sfx" and not cue.loop
            and cue.spatialBlend == 1 and cue.rolloff == "Linear"
            and cue.minDistance == 1.5 and cue.maxDistance == 14,
            cue.id .. " 没有使用启用的单次世界音效和正确空间衰减")
        if kind == "footstep" then
            assert(#cue.clips == 8 and cue.cooldown == 0 and cue.maxInstances == 16
                and cue.selection == 0 and cue.noRepeatHistory == 3,
                cue.id .. " 的八变体、独立步距限流或最近三声不重复配置错误")
        else
            assert(#cue.clips == 2 and cue.cooldown > 0 and cue.maxInstances == 3
                and cue.selection == 0 and cue.noRepeatHistory == 1,
                cue.id .. " 的双变体或单次播放限流配置错误")
        end
        local clipIds = {}
        for _, clip in ipairs(cue.clips) do
            assert(clip.loaded and clip.instanceId ~= 0 and not clipIds[clip.instanceId]
                and clip.channels == 1 and clip.frequency == 48000 and clip.samples > 0
                and clip.preload and clip.audioDataLoaded,
                cue.id .. " 缺少不同的已预加载 48kHz 单声道录音")
            clipIds[clip.instanceId] = true
        end
    end
    assert(#data.swimmingCues == 3, "未读取全部入水、划水和出水 Cue")
    local swimmingIds = { ["player.swim.enter"] = true, ["player.swim.stroke"] = true, ["player.swim.exit"] = true }
    for _, cue in ipairs(data.swimmingCues) do
        assert(swimmingIds[cue.id], "游泳 Cue 重复或出现未知 ID：" .. tostring(cue.id))
        swimmingIds[cue.id] = nil
        assert(cue.loaded and #cue.clips == 3 and cue.bus == "Sfx" and cue.spatialBlend == 1,
            cue.id .. " 没有正确导入三变体世界空间音效")
        assert(cue.cooldown >= 0 and cue.maxInstances > 0 and cue.minDistance >= 0 and cue.maxDistance > cue.minDistance,
            cue.id .. " 缺少有效限流或空间范围")
        if cue.id == "player.swim.stroke" then
            assert(cue.loop and cue.cooldown == 0, "连续游泳 Cue 未启用无冷却循环")
        else
            assert(not cue.loop and cue.cooldown > 0, "入出水应保持单次短音")
        end
        for _, clip in ipairs(cue.clips) do
            assert(clip.loaded and clip.channels == 1 and clip.frequency == 48000 and clip.samples > 0,
                cue.id .. " 未完整导入 48kHz 单声道 LMMS 音频")
            if cue.loop then
                assert(clip.samples >= clip.frequency * 5, "连续游泳素材仍然是短划水片段")
            end
        end
    end
    assert(data.inventoryUiCueCount == 75 and data.inventoryUiCuesValid,
        "72 个 LMMS 取放音效及 3 种交换/装备 Cue 未完整导入 Unity UI 总线")
end, "正式音频默认资源、装配与对应现场契约")
