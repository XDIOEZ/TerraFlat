-- 同档案先关闭缓存做一轮对照，再开启缓存做两轮真实往返。
local control

local function wallRecorded(round, label)
    assert(type(round.enterWallSeconds) == "number" and round.enterWallSeconds > 0 and
        type(round.returnWallSeconds) == "number" and round.returnWallSeconds > 0 and
        type(round.wallSeconds) == "number" and round.wallSeconds > 0,
        label .. "没有记录两次正式迁移的真实墙钟耗时")
    assert(math.abs(round.wallSeconds - round.enterWallSeconds - round.returnWallSeconds) < .0001,
        label .. "往返耗时与入场、回程耗时不一致")
end

test.runtime("space", "presentation_cache_roundtrip", { cacheEnabled = false }, function(data)
    local round = data.rounds[1]
    local first, space, surface = round.before, round.space, round.surface
    wallRecorded(round, "控制轮")
    assert(data.scope == "live-isolated-world" and not data.cacheEnabled and #data.rounds == 1,
        "关闭缓存的控制轮没有完成真实往返")
    assert(first.scene == data.initial.scene and first.footViewId ~= 0 and first.windowReady and
        first.currentModelCount == first.boundViewCount and first.boundViewCount > 0,
        "控制轮的起始地表窗口未绑定当前数据")
    assert(space.spaceView and space.gameplayReady and space.profile == data.profile and
        space.samePlayerData and space.sameSaveData and space.sameSessionState and
        space.sectorFound and space.sectorActive and not space.sectorRetained and
        space.planetBodyId == data.bodyId and space.planetInstanceId ~= 0,
        "控制轮没有进入同档案的正式太空和星区")
    assert(space.chunkRetained == 0 and #space.cachedViews == 0 and
        space.sectorRetainedCount == 0 and space.chunkHits == first.chunkHits and
        space.sectorHits == first.sectorHits and space.sectorCreated > first.sectorCreated,
        "关闭缓存后仍保留区块/星区表现或命中了旧实例")
    assert(surface.scene == first.scene and surface.profile == data.profile and
        surface.samePlayerData and surface.sameSaveData and surface.sameSessionState and
        surface.gameplayReady and surface.worldActive and surface.windowReady and
        surface.worldId == first.worldId and not surface.priorRuntimeReused and
        surface.boundViewCount > 0 and surface.currentModelCount == surface.boundViewCount and
        surface.footViewId ~= 0,
        "控制轮返回地表没有重绑当前世界和同档案玩家")
    local ids, reused = {}, 0
    for _, id in ipairs(first.viewIds) do ids[id] = true end
    for _, id in ipairs(surface.viewIds) do if ids[id] then reused = reused + 1 end end
    assert(surface.chunkRetained == 0 and surface.sectorRetainedCount == 0 and
        not surface.sectorRetained and surface.chunkHits == first.chunkHits and
        surface.sectorHits == first.sectorHits and surface.chunkCreated > first.chunkCreated and
        reused == 0 and surface.planetInstanceId == 0,
        "关闭缓存后回程仍复用了旧区块壳或星体表现")
    assert(first.simulationSeconds < space.simulationSeconds and
        space.simulationSeconds < surface.simulationSeconds,
        "控制轮往返重置或停止了宇宙时钟")
    control = data
    return string.format("关闭缓存控制轮：入场 %.3fs，回地表 %.3fs，合计 %.3fs",
        round.enterWallSeconds, round.returnWallSeconds, round.wallSeconds)
end, "关闭表现缓存的一轮真实迁移对照")

test.runtime("space", "presentation_cache_roundtrip", { cacheEnabled = true }, function(data)
    local function overlap(left, right)
        local ids, count = {}, 0
        for _, id in ipairs(left) do ids[id] = true end
        for _, id in ipairs(right) do if ids[id] then count = count + 1 end end
        return count
    end

    local function surfaceReady(surface, earlier, label)
        assert(surface.scene == data.initial.scene, label .. "没有回到起始地表场景")
        assert(surface.profile == data.profile and surface.samePlayerData and surface.sameSaveData,
            label .. "没有绑定本轮同档案玩家与数据")
        assert(surface.sameSessionState and surface.gameplayReady and surface.worldActive and surface.windowReady,
            label .. "地表世界或游戏流程未恢复")
        assert(surface.worldId == data.initial.worldId and not surface.priorRuntimeReused,
            label .. "没有为当前地表世界建立新 WorldRuntime")
        assert(surface.boundViewCount > 0 and surface.currentModelCount == surface.boundViewCount and
            surface.footViewId ~= 0, label .. "区块壳未绑定当前 WorldRuntime 数据")
        assert(surface.chunkHits > earlier.chunkHits and overlap(surface.viewIds, earlier.viewIds) > 0,
            label .. "区块壳没有真正复用旧实例")
        assert(surface.sectorFound and surface.sectorRetained and not surface.sectorActive and
            surface.sectorRetainedCount <= data.sectorCapacity and surface.sectorActiveColliders == 0,
            label .. "星区缓存未停用或容量越界")
        assert(surface.planetInstanceId ~= 0, label .. "缓存星体实例丢失")
    end

    local function spaceReady(space, label)
        assert(space.spaceView and space.gameplayReady and space.samePlayerData and
            space.sameSaveData and space.sameSessionState and space.profile == data.profile,
            label .. "未进入同档案的就绪太空")
        assert(space.sectorFound and not space.sectorRetained and space.sectorActive and
            space.sectorRootId ~= 0 and space.planetBodyId == data.bodyId and space.planetInstanceId ~= 0,
            label .. "正式星区或星体表现未激活")
        assert(space.chunkRetained > 0 and space.chunkRetained <= data.chunkCapacity and
            #space.cachedViews == space.chunkRetained, label .. "地表区块壳缓存为空或超过上限")
        assert(space.sectorRetainedCount <= data.sectorCapacity,
            label .. "保留星区超过容量")
        for _, view in ipairs(space.cachedViews) do
            assert(view.id ~= 0 and not view.bound and not view.modelPresent and
                not view.active and view.activeColliders == 0 and not view.terrainRegistered and
                view.terrainVisuals == 0,
                label .. "区块缓存壳仍持有旧数据、租约或有效碰撞")
        end
    end

    assert(control ~= nil and data.scope == "live-isolated-world" and
        data.cacheEnabled and #data.rounds == 2,
        "没有在控制轮后完成两轮开启缓存的真实往返")
    assert(data.chunkCapacity == 9 and data.sectorCapacity == 2,
        "缓存容量与本场景的上限契约不一致")
    assert(data.initial.gameplayReady and data.initial.worldActive and
        data.initial.windowReady and data.initial.footViewId ~= 0 and
        data.initial.currentModelCount == data.initial.boundViewCount and
        data.initial.boundViewCount > 0, "初始地表窗口未就绪")

    local first, second = data.rounds[1], data.rounds[2]
    wallRecorded(first, "开启缓存第一轮")
    wallRecorded(second, "开启缓存第二轮")
    assert(data.profile == control.profile and data.initial.sameSessionState and
        data.initial.simulationSeconds >= control.rounds[1].surface.simulationSeconds,
        "控制轮与缓存轮没有继续使用同一档案和宇宙时钟")
    assert(first.before.chunkCreated == data.initial.chunkCreated and
        first.before.sectorCreated == data.initial.sectorCreated and
        second.before.chunkCreated == first.surface.chunkCreated and
        second.before.sectorCreated == first.surface.sectorCreated,
        "迁移前创建计数没有对应本轮真实地表采样")
    spaceReady(first.space, "第一轮太空")
    assert(first.space.sectorRootId ~= control.rounds[1].space.sectorRootId and
        first.space.planetInstanceId ~= control.rounds[1].space.planetInstanceId,
        "开启缓存前复用了控制轮已经销毁的星区或星体")
    surfaceReady(first.surface, data.initial, "第一轮地表")
    assert(first.surface.sectorRootId == first.space.sectorRootId and
        first.surface.planetInstanceId == first.space.planetInstanceId,
        "第一轮返回地表时星区/星体对象未完整保留")

    spaceReady(second.space, "第二轮太空")
    assert(second.space.sectorHits > first.space.sectorHits and
        second.space.sectorRootId == first.space.sectorRootId and
        second.space.planetInstanceId == first.space.planetInstanceId,
        "重访同星区没有取回相同的星区根与星体实例")
    surfaceReady(second.surface, first.surface, "第二轮地表")
    assert(second.surface.sectorRootId == second.space.sectorRootId and
        second.surface.planetInstanceId == second.space.planetInstanceId,
        "第二轮返回地表后星区缓存丢失")
    assert(data.initial.simulationSeconds < first.space.simulationSeconds and
        first.space.simulationSeconds < first.surface.simulationSeconds and
        first.surface.simulationSeconds < second.space.simulationSeconds and
        second.space.simulationSeconds < second.surface.simulationSeconds,
        "地表和太空往返重置或停止了同一宇宙时钟")
    return string.format("缓存关闭 %.3fs；缓存开启首轮 %.3fs、再次往返 %.3fs；本次再次往返差值 %.3fs",
        control.rounds[1].wallSeconds, first.wallSeconds, second.wallSeconds,
        control.rounds[1].wallSeconds - second.wallSeconds)
end, "开启表现缓存的两轮真实复用与耗时对照")
