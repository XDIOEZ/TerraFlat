-- 已入世 AudioService 播放正式录音；Cue 只隔离测试 ID，播放、池与用户音量均走正式实现。
test.runtime("audio", "voice_lifecycle", {}, function(data)
    assert(data.startupServiceStable and data.startupCatalogStable and data.startupSettingsStable,
        "重复预热替换了音频服务、目录或覆盖用户设置")
    assert(data.startupUiCueReady, "正式点击音没有完成音频数据预加载")
    assert(#data.feedbackCues == 6, "未读取全部玩法反馈音效")
    for _, cue in ipairs(data.feedbackCues) do
        assert(cue.loaded and cue.variants == 2 and cue.validClips and cue.spatial,
            cue.id .. " 没有正确导入双变体单声道空间 Cue")
    end
    assert(#data.movementCues == 9, "未读取全部脚步与钓鱼反馈 Cue")
    for _, cue in ipairs(data.movementCues) do
        assert(cue.loaded and cue.variants == cue.expectedVariants and cue.validClips and cue.spatial,
            cue.id .. " 未正确导入 LMMS 音频或缺少声源限流")
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
    assert(data.handlesReleased and data.settingsRestored, "测试声音或临时音量、静音设置没有释放还原")
end, "正式录音播放、玩家监听平面、音量/静音与声源池完整往返")
-- 声音现场没有可解释的画面变化，操作已清理，不拍空场截图。
