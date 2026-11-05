-- 已入世 AudioService 播放正式录音；Cue 只隔离测试 ID，播放、池与用户音量均走正式实现。
test.runtime("audio", "voice_lifecycle", {}, function(data)
    local nature = data.natureAmbience
    assert(nature ~= nil and #nature.cues == 2, "没有完整采样两条正式自然环境循环")
    for _, cue in ipairs(nature.cues) do
        assert(cue.accepted and cue.sourceFound and cue.sourcePlaying and cue.stopped,
            "自然环境循环没有真实播放、采样或立即释放：" .. tostring(cue.id))
        assert(cue.sourceSpatialBlend == cue.spatialBlend and cue.sourceRolloff == cue.rolloff and
            cue.sourceMinDistance == cue.minDistance and cue.sourceMaxDistance == cue.maxDistance,
            cue.id .. " 实际 AudioSource 没有沿用 Cue 的空间参数")
    end

    local foliage = nature.foliageSources
    local leafCount = foliage.leafDefinitionTagged and 1 or 0
    local deadCount = foliage.deadDefinitionTagged and 1 or 0
    assert(foliage.afterLeafCount == foliage.beforeCount + leafCount and
        foliage.afterDeadCount == foliage.afterLeafCount + deadCount and
        foliage.afterRemoveLeafCount == foliage.beforeCount + deadCount and
        foliage.afterCleanupCount == foliage.beforeCount,
        "正式自然实体空间索引未按实际树叶标签增减计数或未清除测试实体")
    assert(foliage.afterLeafRevision > foliage.beforeRevision and
        foliage.afterDeadRevision > foliage.afterLeafRevision and
        foliage.afterRemoveLeafRevision > foliage.afterDeadRevision and
        foliage.afterCleanupRevision > foliage.afterRemoveLeafRevision,
        "原生树木注册、枯树注册或释放没有推进空间索引版本")

    local animalCues = {}
    for _, cue in ipairs(data.animalIdleCues) do
        if cue.loaded then animalCues[cue.cueId] = cue end
    end
    assert(#data.animalPropagationRanges == 16, "没有完整采样四种动物的16个传播边界")
    for _, sample in ipairs(data.animalPropagationRanges) do
        local cue = animalCues[sample.id]
        assert(cue and cue.maxDistance > 0, "传播边界缺少实际 Cue 配置：" .. tostring(sample.id))
        if math.abs(sample.distance) < cue.maxDistance then
            assert(sample.accepted and sample.sourceFound and sample.voicesAfter == sample.voicesBefore + 1,
                sample.id .. " 在传播范围内仍然未播放")
            assert(sample.minDistance == cue.minDistance and sample.maxDistance == cue.maxDistance and
                sample.spatialBlend == cue.spatialBlend and sample.rolloff == cue.rolloff,
                "真实声源未沿用当前 Cue 的空间衰减参数")
        else
            assert(not sample.accepted and not sample.sourceFound and sample.voicesAfter == sample.voicesBefore,
                sample.id .. " 在静音边界或范围外仍然占用声源")
        end
    end
    local burning = data.sharedCombustion
    assert(burning.initiallySilent and burning.started and burning.spatialLoop and burning.reused,
        "共享燃烧循环没有按真实状态起声或重复占用音源")
    assert(burning.ownerReleased and burning.extinguished and burning.reignited and burning.stopped,
        "共享燃烧循环没有在熄灭后淡出释放或无法再次点燃")
    assert(data.startupServiceStable and data.startupCatalogStable and data.startupSettingsStable,
        "重复预热替换了音频服务、目录或覆盖用户设置")
    assert(data.startupUiConfirmReady, "正式确认音没有完成音频数据预加载")
    local click = data.disabledUiClick
    if click.loaded and not click.playbackEnabled then
        assert(not click.directAccepted and not click.loopAccepted, "停用点击事件仍然取得了单次或循环播放句柄")
        assert(click.voicesBefore == click.voicesAfter and click.sourcesBefore == click.sourcesAfter,
            "停用点击事件分配或抢占了声源")
        assert(click.randomUnchanged and #click.messages == 0, "主动静音仍然选片或产生缺失录音警告")
    end
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
    assert(data.farLoopAccepted and math.abs(data.farOffsetX - (data.loopMaxDistance + 2)) < 0.001 and math.abs(data.farOffsetY) < 0.001 and math.abs(data.farOffsetZ) < 0.001, "远处循环没有保留句柄或空间位置错误")
    assert(data.farLoopMuted and not data.returnedLoopMuted and data.returnedLoopPlaying
        and math.abs(data.returnedLoopOffsetX - 1) < .001 and math.abs(data.returnedLoopOffsetY) < .001
        and math.abs(data.returnedLoopOffsetZ) < .001, "范围外循环未静音，或回到范围内后没有恢复原声源与位置")
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
