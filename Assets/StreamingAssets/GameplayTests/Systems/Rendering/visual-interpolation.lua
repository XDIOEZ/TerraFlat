-- 20TPS与高帧率下只插入显示帧，传送、环绕、归池、停顿不得拖动真实位置。
test.system("rendering", "visual_motion_interpolation", {}, function(data)
    local function near(actual, expected, context)
        assert(math.abs(actual - expected) < 0.0002, context .. "：" .. tostring(actual) .. " != " .. tostring(expected))
    end
    for _, config in ipairs({ {data.fps60, 60}, {data.fps120, 120}, {data.fps240, 240} }) do
        local frames, fps = config[1], config[2]
        assert(#frames == fps / 20 + 1, "逻辑20TPS的画面采样数量错误：" .. fps)
        for i, value in ipairs(frames) do
            near(value, (i - 1) * 20 / fps, "20TPS插值对应画面" .. fps .. "FPS，第" .. i .. "帧")
        end
    end
    near(data.nextTickMid, 1.5, "下一逻辑步未从上一目标连续过渡")
    assert(data.pausedUnchanged, "暂停时显示位置继续推进")
    near(data.teleported, 20, "瞬移后残留旧位置")
    near(data.reused, -3, "对象池复用后继承旧实体轨迹")
    near(data.seamMid, 99.5, "跨环绕接缝绕远路插值")
    near(data.pixelAligned, 0.125, "插值偏移未对齐像素网格")
    assert(data.sourceUnchanged and data.projectionTranslated and data.projectionRestored,
        "显示投影改变了权威Transform，或下一帧残留平移")
end, "视觉插值：20TPS×60/120/240FPS、暂停、环绕、瞬移、复用与源位置隔离")
