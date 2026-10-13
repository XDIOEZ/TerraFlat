-- 实际进入每个配置星区，检查全部星体的数据登记与场景表现。
test.space("space_visit", function(data)
    assert(data.scope == "live-presentation" and #data.visits > 0, "没有进入配置星区")
    local ids, sectors, count = {}, {}, 0
    for _, visit in ipairs(data.visits) do
        local key = visit.sector.x .. ":" .. visit.sector.y
        assert(not sectors[key], "重复访问星区：" .. key)
        sectors[key] = true
        assert(visit.state.spaceView and not visit.state.dimensionTransitioning, "星区入场未完成：" .. key)
        assert(visit.state.sector.x == visit.sector.x and visit.state.sector.y == visit.sector.y, "玩家归属星区错误：" .. key)
        assert(#visit.bodies > 0, "配置星区没有任何星体：" .. key)
        for _, body in ipairs(visit.bodies) do
            assert(not ids[body.id], "星体实例身份重复：" .. body.id)
            ids[body.id] = true
            assert(body.planetRegistered and body.registeredBodyId == body.id, "星球数据登记缺失：" .. body.id)
            assert(body.viewReady and body.viewBodyId == body.id, "真实星体场景对象缺失：" .. body.id)
            local dx, dy = body.projected.x - body.viewPosition.x, body.projected.y - body.viewPosition.y
            assert(math.sqrt(dx * dx + dy * dy) < 2, "星体表现没有跟随权威位置：" .. body.id)
            count = count + 1
        end
    end
    return "实际访问 " .. #data.visits .. " 个配置星区、" .. count .. " 个真实星体表现"
end, "所有配置星区与星体场景表现")
