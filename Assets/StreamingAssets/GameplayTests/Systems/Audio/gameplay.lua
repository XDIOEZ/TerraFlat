-- 已入世 AudioService 播放正式录音；Cue 只隔离测试 ID，播放、池与用户音量均走正式实现。
test.runtime("audio", "voice_lifecycle", {}, function(data)
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
