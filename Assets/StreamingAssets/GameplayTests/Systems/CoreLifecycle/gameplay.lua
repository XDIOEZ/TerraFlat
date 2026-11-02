-- 观察正式入世已完成的本地玩家、资源闸门和可见区块，不模拟 Scene 流程。
test.runtime("core_lifecycle", "live_entry", {}, function(data)
    local hierarchy = data.sceneHierarchy
    assert(hierarchy.playerCategory == "Players" and hierarchy.chunkCategory == "Chunks", "真实玩家或区块没有进入场景分类")
    assert(hierarchy.playerSceneCorrect and hierarchy.chunkSceneCorrect and hierarchy.sameSceneSharedRoot,
        "真实对象分类改变了场景归属，或同场景创建了多个公共根")
    if data.editorSpriteTextures ~= nil then
        assert(#data.editorSpriteTextures.textures > 0, "正式目录没有可核对的编辑器 Sprite 纹理")
        for _, texture in ipairs(data.editorSpriteTextures.textures) do
            assert(texture.found and texture.localId == 2800000,
                "编辑器 Sprite 异步预热的主纹理身份不匹配：" .. texture.path)
        end
    end
    assert(data.scope == "live-isolated-world" and data.resourceState == "Ready" and data.startupReady, "正式资源没有完成加载")
    assert(data.audioReady and data.audioServiceReused and not data.audioPreloadYielded,
        "正式就绪早于音库完成，或重复预热重新创建服务/空等一帧")
    assert(data.inWorld and data.gameplayReady and not data.entryInProgress, "正式入世闸门尚未释放")
    assert(data.localProfile and data.playerInitialized and data.playerActive and data.profile ~= nil, "本地测试角色未完成生命周期")
    assert(data.footViewReady and data.windowReady, "输入已解锁但脚下或可见区块表现未就绪")
    assert(data.hotbarCount == 1 and data.handCount == 1
        and data.currentHotbarRegistered and data.currentHandRegistered,
        "旧世界的快捷栏/手持面板残留，或当前玩家 HUD 未正确注册")
end, "正式入世、玩家生命周期和表现就绪")

test.runtime("core_lifecycle", "persistent_object_hierarchy", {}, function(data)
    assert(data.persistentRootHideFlags == 0 and data.sceneRootHideFlags == 0,
        "运行时分类根使用了会让整棵对象树残留的 HideFlags")
    assert(data.sceneHierarchyDestroyedWithScene, "场景卸载后分类根和子对象仍然残留")
    assert(data.persistentScene and data.idempotent and data.ownedChildPreserved and data.inactiveAndPosePreserved,
        "常驻挂接改变了所有权、激活状态或姿态，或重复创建了分类")
    assert(data.sourceSceneUnloaded and data.transientDestroyed and data.persistentTreeSurvived,
        "源场景卸载后常驻对象丢失，或普通场景对象被错误保留")
    assert(data.activeSceneUnchanged and data.playerWorldNotPromoted,
        "整理常驻层级改变了活动场景或玩家世界归属")
    assert(data.ownerReleasedIndependently, "释放一个业务宿主销毁了其它常驻对象")
    assert(data.ungroupedPersistentRootCollected and data.oneVisiblePersistentRoot,
        "未分类常驻根没有收纳到统一层级")
    assert(data.gameManagerGrouped and data.uiGrouped and data.audioGrouped, "正式管理器未进入对应分类")
end, "常驻对象跨场景保留、第三方收纳与独立释放")

-- 真正驱动由游戏 Update 时钟负责，不假设 Unity FixedUpdate 与 TPS 相同。
test.runtime("core_lifecycle", "fixed_step_clock", {}, function(data)
    local function near(actual, expected, tolerance, message)
        assert(type(actual) == "number" and math.abs(actual - expected) < tolerance, message)
    end
    assert(data.hostCount == 1 and data.managerOwnsActiveHost, "游戏时钟驱动不是 GameManager 的唯一活动宿主")
    assert(data.defaultTicksPerSecond == 30, "默认 TPS 不是 30 Hz")
    near(data.defaultTickDelta, 1 / 30, .000001, "默认游戏 Tick 时长错误")
    assert(data.manualPhysicsMode, "Unity 2D 物理没有交给脚本手动模拟")
    local baseRate = data.effectiveTicksPerSecond
    assert((baseRate == 30 or baseRate == 60 or baseRate == 120) and
        (data.selectedTicksPerSecond == 30 or data.selectedTicksPerSecond == 60 or data.selectedTicksPerSecond == 120),
        "玩家模拟频率无效")
    local maxStepsPerFrame = data.maximumCatchUpTicks
    assert(maxStepsPerFrame == 4, "独立时钟追赶上限不再是4个 Tick")
    assert(data.afterRegister == data.beforeRegister + 1 and data.afterUnregister == data.beforeUnregister - 1,
        "临时观察系统登记或卸载错误")

    local function check_window(sample, speed, minimumWall)
        local wall = sample.endWall - sample.startWall
        local ticks = sample.endTick - sample.startTick
        local frames = sample.endFrame - sample.startFrame
        assert(wall >= minimumWall and frames > 0, "现场没有等待真实时间或渲染帧没有前进")
        assert(sample.timeScale == speed and sample.endTimeScale == speed, "采样期间游戏倍速改变")
        assert(sample.targetFrameRate == 30 and sample.endTargetFrameRate == 30 and
            sample.vSyncCount == 0 and sample.endVSyncCount == 0, "低帧率现场未生效或中途被改变")
        near(sample.tickDelta, 1 / baseRate, .000001, "自有时钟 Tick 时长错误")
        near(sample.endTickDelta, sample.tickDelta, .000001, "采样中 Tick 步长发生变化")
        near(sample.endFixedDelta, sample.fixedDelta, .000001, "游戏 Tick 意外改写 Unity FixedUpdate")
        assert(#sample.steps == ticks, "游戏 Tick 计数与观察系统实际调用次数不一致")
        near(sample.endClock - sample.startClock, ticks * sample.tickDelta, .001,
            "自有游戏时钟与实际 Tick 次数不一致")
        assert(sample.endPhysicsDispatch - sample.startPhysicsDispatch == ticks,
            "世界原生物理分发没有按游戏 Tick 一对一执行")
        assert(sample.endPhysicsSceneSteps - sample.startPhysicsSceneSteps >= ticks or ticks == 0,
            "物理分发没有推进任何有效原生 PhysicsScene2D")
        local sameFrameSteps = false
        local stepsPerFrame = {}
        local previous
        for index, step in ipairs(sample.steps) do
            assert(step.tick == sample.startTick + index, "游戏 Tick 序号不连续")
            near(step.delta, sample.tickDelta, .000001, "系统收到的时间不是游戏 Tick 时长")
            near(step.clock, sample.startClock + index * sample.tickDelta, .001, "公共时钟没有按 Tick 推进")
            assert(step.physicsDispatch == sample.startPhysicsDispatch + index and
                step.manualPhysicsMode, "物理未随同一次游戏 Tick 手动推进")
            stepsPerFrame[step.frame] = (stepsPerFrame[step.frame] or 0) + 1
            assert(stepsPerFrame[step.frame] <= maxStepsPerFrame, "同一渲染帧的游戏 Tick 超过追赶预算")
            if previous then
                assert(step.frame >= previous.frame, "游戏 Tick 的渲染帧序号倒退")
                if step.frame == previous.frame then sameFrameSteps = true end
                near(step.clock - previous.clock, sample.tickDelta, .0001, "相邻游戏 Tick 时长不一致")
            end
            previous = step
        end
        return wall, ticks, frames, sameFrameSteps
    end

    local normalWall, normalTicks, normalFrames, normalMulti = check_window(data.normal, 1, 1.5)
    local doubleWall, doubleTicks, doubleFrames, doubleMulti = check_window(data.double, 2, 1.5)
    assert(normalFrames / normalWall <= 36 and doubleFrames / doubleWall <= 36,
        "现场没有实际降到约30渲染帧，不能验证低帧率独立追赶")
    assert((baseRate <= 30 or normalMulti) and doubleMulti,
        "低帧率下没有观察到同一渲染帧中的多个真实游戏 Tick")
    local function check_unclipped_time_scale(sample, speed)
        local scaled, unscaled = 0, 0
        for _, frame in ipairs(sample.frames) do
            if frame.unscaledDelta > 0 and frame.unscaledDelta * speed < frame.maximumDelta * .9 then
                scaled = scaled + frame.scaledDelta
                unscaled = unscaled + frame.unscaledDelta
            end
        end
        assert(unscaled >= .1, "卡顿过多，未能取得足够的未截断渲染帧验证倍率")
        near(scaled / unscaled, speed, speed * .15, "未截断帧的游戏时间与倍速不一致")
    end
    check_unclipped_time_scale(data.normal, 1)
    check_unclipped_time_scale(data.double, 2)

    -- 受控卡顿按游戏自己的 Tick 预算丢弃超额时间，不能用 Unity 时间截断代替。
    local function check_bounded_hitch(sample, speed)
        local wall = sample.endWall - sample.startWall
        local simulatedTime = sample.endClock - sample.startClock
        local ticks = sample.endTick - sample.startTick
        assert(sample.hitchMilliseconds >= 200 and wall >= .18 and sample.unscaledDelta >= .18,
            "受控卡顿未触发，无法验证追赶上限")
        assert(sample.timeScale == speed and sample.endTimeScale == speed,
            "卡顿采样期间倍速发生变化")
        assert(sample.endFrame == sample.startFrame + 1, "卡顿采样不是相邻的两个渲染帧")
        near(sample.tickDelta, 1 / baseRate, .000001, "卡顿后游戏 Tick 时长发生变化")
        assert(ticks >= 1 and ticks <= maxStepsPerFrame and #sample.steps == ticks,
            "卡顿后单帧游戏 Tick 次数超出 TPS 预算")
        assert(sample.manualPhysicsMode and
            sample.endPhysicsDispatch - sample.startPhysicsDispatch == ticks,
            "卡顿后原生物理未和游戏 Tick 保持一对一")
        assert(simulatedTime <= maxStepsPerFrame * sample.tickDelta + .003 and
            simulatedTime < sample.unscaledDelta * speed,
            "卡顿后独立时钟未丢弃超额模拟时间")
        near(simulatedTime, ticks * sample.tickDelta, .001, "卡顿后游戏时钟与 Tick 不匹配")
        for index, step in ipairs(sample.steps) do
            assert(step.frame == sample.endFrame and step.tick == sample.startTick + index,
                "卡顿后的 Tick 没有落在同一个渲染帧")
        end
    end
    check_bounded_hitch(data.normalHitch, 1)
    check_bounded_hitch(data.doubleHitch, 2)

    local function check_hud_rate(sample, wall, ticks, label)
        assert(sample.hudVisible and type(sample.hudText) == "string", label .. "没有显示正式 FPS/TPS 文本")
        local fps, tps = string.match(sample.hudText, "^FPS%s+(%d+)%s+TPS%s+(%d+)$")
        assert(fps and tps and tonumber(fps) > 0, label .. "FPS/TPS 文本格式错误：" .. sample.hudText)
        local measured = ticks / wall
        assert(math.abs(tonumber(tps) - measured) < math.max(18, measured * .35),
            label .. "TPS 不符合真实固定步采样")
        return tonumber(tps)
    end
    check_hud_rate(data.normal, normalWall, normalTicks, "正常速度")
    check_hud_rate(data.double, doubleWall, doubleTicks, "两倍速度")

    local pausedWall, pausedTicks, pausedFrames = check_window(data.paused, 0, 1.0)
    assert(check_hud_rate(data.paused, pausedWall, pausedTicks, "暂停") == 0, "暂停后 TPS 没有归零")
    assert(pausedFrames > 0 and pausedTicks == 0 and data.paused.endClock == data.paused.startClock,
        "暂停后独立游戏时钟仍推进或渲染停止")
    local _, resumedTicks, resumedFrames = check_window(data.resumed, 1, .35)
    assert(resumedTicks > 0 and resumedFrames > 0, "恢复正常时间后固定步或渲染没有继续")
    for _, key in ipairs({ "timeScale", "fixedDelta", "targetFrameRate", "vSyncCount", "showFps", "showFpsKeyExists", "showFpsStored" }) do
        assert(data.restored[key] == data.original[key], "现场结束后设置没有恢复：" .. key)
    end
end, "真实30帧画面、自有30Hz TPS、原生手动物理、倍速暂停与设置恢复")
