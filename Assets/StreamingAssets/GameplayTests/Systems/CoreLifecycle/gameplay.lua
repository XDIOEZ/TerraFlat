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

-- 低帧率、倍速和暂停都读取 Unity 实际推进，不在测试中补算或调用固定步。
test.runtime("core_lifecycle", "fixed_step_clock", {}, function(data)
    local function near(actual, expected, tolerance, message)
        assert(type(actual) == "number" and math.abs(actual - expected) < tolerance, message)
    end
    assert(data.hostCount == 1 and data.managerOwnsActiveHost, "固定步驱动不是 GameManager 上的唯一活动宿主")
    assert(data.defaultTicksPerSecond == 60, "默认固定步频率不是 60 Hz")
    near(data.defaultFixedDelta, 1 / 60, .000001, "默认固定步时长错误")
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
        near(sample.fixedDelta, 1 / 60, .000001, "Unity 固定步不是 60 Hz")
        near(sample.endFixedDelta, sample.fixedDelta, .000001, "采样期间 Unity 固定步长被改变")
        assert(#sample.steps == ticks, "固定步计数与观察系统实际调用次数不一致")
        near(sample.endFixedTime - sample.startFixedTime, ticks * sample.fixedDelta, .001,
            "Unity 固定时间与正式固定步数不一致")
        near(sample.endFixedTime - sample.startFixedTime, sample.endGameTime - sample.startGameTime,
            sample.fixedDelta * 2 + .005, "固定时间没有跟随引擎游戏时间")
        local sameFrameSteps = false
        local previous
        for index, step in ipairs(sample.steps) do
            assert(step.tick == sample.startTick + index, "固定步序号不连续")
            near(step.delta, sample.fixedDelta, .000001, "系统收到的时间不是 Unity 固定步长")
            near(step.clock, step.fixedTime, .000001, "公共时钟没有使用 Unity 固定时间")
            if previous then
                assert(step.frame >= previous.frame, "固定步的渲染帧序号倒退")
                if step.frame == previous.frame then sameFrameSteps = true end
                near(step.fixedTime - previous.fixedTime, sample.fixedDelta, .0001, "连续固定时间不对应一次引擎固定步")
            end
            previous = step
        end
        return wall, ticks, frames, sameFrameSteps
    end

    local normalWall, normalTicks, normalFrames, normalMulti = check_window(data.normal, 1, 1.5)
    local doubleWall, doubleTicks, doubleFrames, doubleMulti = check_window(data.double, 2, 1.5)
    assert(normalFrames / normalWall <= 36 and doubleFrames / doubleWall <= 36,
        "现场没有实际降到约 30 渲染帧，不能验证低帧率补步")
    assert(normalMulti and doubleMulti, "低帧率下没有观察到同一渲染帧中的多个真实固定步")
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
    assert(pausedFrames > 0 and pausedTicks == 0 and data.paused.endFixedTime == data.paused.startFixedTime and
        data.paused.endGameTime == data.paused.startGameTime,
        "暂停后固定步仍推进或渲染停止")
    local _, resumedTicks, resumedFrames = check_window(data.resumed, 1, .35)
    assert(resumedTicks > 0 and resumedFrames > 0, "恢复正常时间后固定步或渲染没有继续")
    for _, key in ipairs({ "timeScale", "fixedDelta", "targetFrameRate", "vSyncCount", "showFps", "showFpsKeyExists", "showFpsStored" }) do
        assert(data.restored[key] == data.original[key], "现场结束后设置没有恢复：" .. key)
    end
end, "真实 30 帧画面、60 Hz 固定步、倍速、暂停与设置恢复")
