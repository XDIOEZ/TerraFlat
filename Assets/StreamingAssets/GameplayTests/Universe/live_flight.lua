-- 每个正式船型走真实生成、供油供电、起飞、惯性导航和降落入口。
test.space("ship_flight", function(data)
    assert(data.scope == "live-ship-flight" and #data.ships > 0, "没有完成任何真实船型流程")
    local function magnitude(v) return math.sqrt(v.x * v.x + v.y * v.y) end
    local function difference(a, b) return magnitude({x = a.x - b.x, y = a.y - b.y}) end
    for _, ship in ipairs(data.ships) do
        local label = ship.templateId
        assert(ship.enteredConsole, "进入控制台失败：" .. label)
        assert(ship.landed.phase == "Landed", "生成后不是地面停靠状态：" .. label)
        assert(ship.filled.hydrogen > 0 and ship.filled.oxygen > 0, "真实燃料罐为空：" .. label)
        assert(ship.lifting.phase == "Lifting", "点火没有进入升空：" .. label)
        assert(ship.cruising.phase == "Cruising" and ship.cruising.spaceView, "起飞未进入真实太空：" .. label)
        assert(ship.cruising.id == ship.landed.id and ship.cruising.pieceCount == ship.landed.pieceCount, "起飞丢失船体或部件：" .. label)
        assert(ship.thrust.engineOutput > 0, "真实推进没有引擎出力：" .. label)
        assert(ship.thrust.hydrogen < ship.filled.hydrogen and ship.thrust.oxygen < ship.filled.oxygen, "飞行未消耗真实氢氧：" .. label)
        assert(difference(ship.thrust.velocity, ship.cruising.velocity) > 0.01, "推进没有改变速度：" .. label)
        assert(difference(ship.coast.velocity, ship.thrust.velocity) < 0.05, "松开控制后惯性速度丢失：" .. label)
        assert(math.abs(ship.turned.angle - ship.coast.angle) > 0.01, "正式旋转输入未转动船体：" .. label)
        assert(ship.navigationAccepted and ship.navigationDistance <= ship.arrivalDistance, "正式导航未到点：" .. label)
        assert(magnitude(ship.navigated.velocity) <= ship.arrivalSpeed + 0.001, "到点后没有制动：" .. label)
        local crossing = ship.shipTransition
        local function same(a, b) return a.x == b.x and a.y == b.y end
        local function contains(values, expected)
            for _, value in ipairs(values) do if same(value, expected) then return true end end
            return false
        end
        assert(crossing.fixtureSetup == "owned-ship-preposition", "船体过渡缺少明确的测试夹具来源：" .. label)
        assert(crossing.shipId == ship.cruising.id and crossing.phase == "Cruising", "过渡期间船体身份或飞行状态丢失：" .. label)
        assert(same(crossing.crossed.sector, crossing.destination) and same(crossing.shipSector, crossing.destination), "乘员或船体没有进入目标星区：" .. label)
        assert(same(crossing.flightSector, crossing.shipSector), "飞行记录与船体星区不一致：" .. label)
        assert(contains(crossing.crossed.required, crossing.source) and contains(crossing.crossed.required, crossing.destination), "船体跨界没有保留双侧租约：" .. label)
        assert(not contains(crossing.settled.required, crossing.source), "船体越过过渡带后没有清退源星区：" .. label)
        assert(crossing.before.playerInstance == crossing.crossed.playerInstance and not crossing.crossed.dimensionTransitioning, "船体跨区重建玩家或触发维度迁移：" .. label)
        assert(ship.landingAccepted and ship.landedAgain.phase == "Landed", "没有完成受控陆地降落：" .. label)
        assert(not ship.landedAgain.spaceView and ship.landedAgain.height <= 0.001, "降落后仍在太空或半空：" .. label)
        assert(ship.landedAgain.id == ship.landed.id, "降落重建了错误的飞船身份：" .. label)
    end
    return "真实起飞、氢氧消耗、惯性、旋转、导航、船体跨区与降落：" .. #data.ships .. " 个船型"
end, "所有船型的真实起飞航行降落")
