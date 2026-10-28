-- 已入世 AudioService 播放正式录音；Cue 只隔离测试 ID，播放、池与用户音量均走正式实现。
test.runtime("audio", "voice_lifecycle", {}, function(data)
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
    assert(data.startupServiceStable and data.startupCatalogStable and data.startupSettingsStable,
        "重复预热替换了音频服务、目录或覆盖用户设置")
    assert(data.startupUiConfirmReady, "正式确认音没有完成音频数据预加载")
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
        ["player.footstep.wood"] = "footstep", ["player.footstep.snow"] = "footstep",
        ["player.footstep.water"] = "footstep", ["fishing.cast"] = "fishing",
        ["fishing.hook"] = "fishing", ["fishing.reel"] = "fishing"
    }
    assert(#data.movementCues == 10, "未读取七种脚步和三种钓鱼反馈 Cue")
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
    assert(data.scope == "live-isolated-world" and data.formalCatalog and data.clipCount > 0 and data.clipSamples > 0, "正式录音或目录没有真实加载")
    assert(data.playing and data.spatialLinear and data.cueDistancesPreserved and data.sourceUnmuted, "角色附着声音没有按正式空间参数播放")
    assert(data.nearPlayFrame == data.nearSampleFrame and data.farPlayFrame == data.farSampleFrame,
        "正式播放与监听位置采样混入了不同帧")
    assert(math.abs(data.nearOffsetX) < 0.001 and math.abs(data.nearOffsetY) < 0.001 and math.abs(data.nearOffsetZ) < 0.001, "角色声音没有投影到监听器平面，镜头偏移进入声音距离")
    assert(data.initialVolume > 0 and math.abs(data.scaledVolume - data.initialVolume * 0.5) < 0.0001, "句柄音量倍率没有更新真实声源")
    assert(math.abs(data.masterVolume - data.scaledVolume * 0.5) < 0.0001, "主音量没有立即作用于正在播放的音源")
    assert(math.abs(data.busVolume - data.masterVolume / 3) < 0.0001, "总线音量没有立即作用于真实音源")
    assert(data.mutedVolume == 0 and data.retainedWhenMuted and math.abs(data.unmutedVolume - data.busVolume) < 0.0001, "静音销毁句柄或取消静音没有恢复真实音量")
    assert(not data.farShortAccepted and data.beforeFarShort == data.afterFarShort, "范围外短音仍占用了声源池")
    assert(data.farLoopAccepted and math.abs(data.farOffsetX - data.expectedFarOffsetX) < 0.001 and math.abs(data.farOffsetY) < 0.001 and math.abs(data.farOffsetZ) < 0.001, "远处循环没有保留句柄或空间位置错误")
    assert(data.released and data.sourceReused and data.newHandleDistinct and data.replacementSurvivedOldStop, "停止、声源复用或旧句柄隔离错误")
    -- 采样来自真实 AudioSource，Lua 检查暂停续播、界面豁免与原始音高保留。
    local pause = data.gamePause
    local function byBus(phase)
        local values = pause[phase]
        assert(#values == 5, phase .. " 没有采样全部五条音频总线")
        local buses = {}
        for _, value in ipairs(values) do
            assert(not buses[value.bus], phase .. " 出现重复总线：" .. value.bus)
            buses[value.bus] = value
        end
        return buses
    end
    local normal, slow, fast = byBus("normal"), byBus("slow"), byBus("fast")
    local pausedStart, pausedEnd, resumed = byBus("pausedStart"), byBus("pausedEnd"), byBus("resumed")
    for _, bus in ipairs({ "Music", "Sfx", "Ambient", "Voice", "UI" }) do
        local first = normal[bus]
        assert(first and first.retained and first.playing and first.samples > 0,
            bus .. " 初始音源没有真正播放")
        assert(math.abs(first.pitch - 0.83) < 0.00001, bus .. " 初始音高倍率未生效")
        for _, value in ipairs({ slow[bus], fast[bus] }) do
            assert(value.retained and value.playing and value.sourceId == first.sourceId
                and value.clipId == first.clipId and math.abs(value.pitch - first.pitch) < 0.00001,
                bus .. " 被游戏加减速改了音高、换了声音或停止播放")
        end
        local stopped, held = pausedStart[bus], pausedEnd[bus]
        assert(stopped.retained and held.retained and held.sourceId == first.sourceId
            and held.clipId == first.clipId and math.abs(held.pitch - first.pitch) < 0.00001,
            bus .. " 暂停丢失了原句柄、录音或音高")
        if bus == "UI" then
            assert(stopped.playing and held.playing and held.samples > stopped.samples,
                "游戏暂停阻止了 UI 总线播放")
        else
            assert(not stopped.playing and not held.playing and held.samples == stopped.samples,
                bus .. " 游戏暂停后仍然在推进声音采样")
        end
        local restored = resumed[bus]
        if bus == "Voice" then
            assert(not restored.retained, "暂停中主动停止的声音在恢复后又复活")
        else
            assert(restored.retained and restored.playing and restored.sourceId == first.sourceId
                and restored.clipId == first.clipId and restored.samples > held.samples
                and math.abs(restored.pitch - first.pitch) < 0.00001,
                bus .. " 恢复没有从原音源和原播放位置正常续播")
        end
    end
    local created, held = pause.newOneShotImmediate, pause.newOneShotHeld
    assert(created.retained and not created.playing and held.retained and not held.playing
        and created.sourceId == held.sourceId and created.clipId == held.clipId and created.samples == held.samples,
        "暂停中新建的单次音没有立即暂停，或被误判结束并回收")
    local continued = pause.newOneShotResumed
    assert(continued.retained and continued.playing and continued.sourceId == created.sourceId
        and continued.clipId == created.clipId and continued.samples > held.samples,
        "暂停中新建的单次音没有恢复续播")
    assert(pause.fadeInStart.retained and pause.fadeInHeld.retained and not pause.fadeInHeld.playing
        and pause.fadeInStart.volume == 0 and pause.fadeInHeld.volume == 0,
        "游戏暂停期间淡入还在推进")
    assert(pause.fadeInResumed.retained and pause.fadeInResumed.playing and pause.fadeInResumed.volume > 0,
        "恢复游戏后淡入没有继续")
    for _, stopped in ipairs({ pause.immediateStop, pause.fadeOutStopped }) do
        assert(not stopped.retained and not stopped.playing and stopped.clipId == 0,
            "暂停中的即时停止或淡出没有回收音源")
    end
    assert(pause.uiConfirm.retained and pause.uiConfirm.playing and pause.uiReusedStoppedSource,
        "暂停世界音源复用给 UI 确认声时仍带着暂停状态")
    assert(data.handlesReleased and data.settingsRestored and data.timeScaleRestored,
        "测试声音或临时音量、静音、游戏时间倍率没有释放还原")
end, "正式录音播放、音量/静音、暂停续播与声源池完整往返")
-- 声音现场没有可解释的画面变化，操作已清理，不拍空场截图。
