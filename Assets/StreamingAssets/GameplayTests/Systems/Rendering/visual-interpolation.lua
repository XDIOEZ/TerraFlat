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
    for _, config in ipairs({ {data.tps30Fps60, 30, 60}, {data.tps30Fps120, 30, 120},
                              {data.tps120Fps240, 120, 240} }) do
        local frames, tps, fps = config[1], config[2], config[3]
        assert(#frames == fps / tps + 1, "TPS档位的显示帧采样数量错误：" .. tps .. "/" .. fps)
        for i, value in ipairs(frames) do
            near(value, (i - 1) * tps / fps, "模拟" .. tps .. "TPS/画面" .. fps .. "FPS，第" .. i .. "帧")
        end
    end
    near(data.nextTickMid, 1.5, "下一逻辑步未从上一目标连续过渡")
    assert(data.pausedUnchanged, "暂停时显示位置继续推进")
    near(data.teleported, 20, "瞬移后残留旧位置")
    near(data.reused, -3, "对象池复用后继承旧实体轨迹")
    near(data.idleResumeMid, 0.5, "静止多逻辑步后首次移动不应延长补间时间")
    near(data.seamMid, 99.5, "跨环绕接缝绕远路插值")
    near(data.pixelAligned, 0.125, "插值偏移未对齐像素网格")
    near(data.poseMidPosition, .5, "位置未按逻辑快照补间")
    near(data.poseMidRotation, 45, "旋转未按逻辑快照补间")
    near(data.poseMidScale, 1.5, "缩放未按逻辑快照补间")
    near(data.catchUpPosition, 2.5, "同帧多 Tick 追赶丢失倒数第二份快照")
    assert(data.pureReads, "多次读取显示位置改变了模拟快照或表现结果")
    assert(data.externalPosePreserved and data.snapPosePreserved, "逐帧外部姿态或显式瞬移被旧轨迹覆盖")
    assert(data.rateChangeKeepsRemainder and data.rateChangeAdvances, "切换 TPS 丢失时间余量或让显示时间倒退")
    assert(data.heldPositionFollowsParent and data.heldRotationStillInterpolated and data.heldSourceUnchanged,
        "载具移动时手持挂点位置被固定步插值拉回，或改动了逻辑旋转")
    assert(data.emptyAttachmentOmitted and data.lateHeldItemProjected,
        "首次显示后装入空挂点的手持物没有接入持有者投影，仍按物理位置直接绘制")
    assert(data.replacedHeldItemProjected and data.removedHeldItemRestored,
        "切换或卸下手持物没有重建持有者投影并恢复旧源 Renderer")
    assert(data.childPoseInterpolated and data.childSourceUnchanged and data.rootRotationApplied,
        "局部动作/根旋转没有只在绘制副本中插值，或反向修改了逻辑姿态")
    assert(data.sourceUnchanged and data.projectionTranslated and data.projectionRestored,
        "显示投影改变了权威Transform，或下一帧残留平移")
end, "视觉插值：20/30/120TPS与更高FPS、暂停、环绕、瞬移、复用与源位置隔离")
