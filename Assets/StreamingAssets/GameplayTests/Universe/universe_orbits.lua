-- 对全部真实星体检查椭圆公转、自转和移动父节点。
test.space("orbits", function(data)
    local function near(value, expected, label, tolerance)
        assert(type(value) == "number" and value == value and math.abs(value) < math.huge, label .. "不是有限数")
        assert(math.abs(value - expected) <= (tolerance or 0.000001) * math.max(1, math.abs(expected)), label .. "不一致")
    end
    local function vectorNear(value, expected, label)
        near(value.x, expected.x, label .. "/X")
        near(value.y, expected.y, label .. "/Y")
    end
    local function magnitude(vector) return math.sqrt(vector.x * vector.x + vector.y * vector.y) end
    assert(type(data.bodies) == "table" and #data.bodies > 0, "没有星体轨道采样")
    local orbitCount, reverseCount = 0, 0
    for _, body in ipairs(data.bodies) do
        local label = body.id
        assert(type(body.initial) == "table" and type(body.full) == "table", label .. "缺少周期采样")
        vectorNear(body.full.relativePosition, body.initial.relativePosition, label .. "/完整公转周期")
        for _, name in ipairs({"initial", "quarter", "half", "full", "rotationQuarter"}) do
            local sample = body[name]
            assert(type(sample) == "table", label .. "缺少采样:" .. name)
            near(sample.position.x, sample.parentPosition.x + sample.relativePosition.x, label .. "/父子X:" .. name)
            near(sample.position.y, sample.parentPosition.y + sample.relativePosition.y, label .. "/父子Y:" .. name)
            near(sample.velocity.x, sample.parentVelocity.x + sample.relativeVelocity.x, label .. "/父子速度X:" .. name)
            near(sample.velocity.y, sample.parentVelocity.y + sample.relativeVelocity.y, label .. "/父子速度Y:" .. name)
            near(sample.rotation, body.rotationInitialRadians +
                (math.abs(body.rotationPeriodSeconds) > 0.000000000001 and sample.time * math.pi * 2 / body.rotationPeriodSeconds or 0),
                label .. "/自转:" .. name)
            if body.periodSeconds > 0 then
                local c, s = math.cos(body.periapsisRadians), math.sin(body.periapsisRadians)
                local x = sample.relativePosition.x * c + sample.relativePosition.y * s
                local y = -sample.relativePosition.x * s + sample.relativePosition.y * c
                local a, e = body.axis, body.eccentricity
                assert(a > 0, label .. "公转半长轴必须大于零")
                near(((x + a * e) / a)^2 + (y / (a * math.sqrt(1 - e * e)))^2,
                    1, label .. "/椭圆方程:" .. name)
                local anomaly = math.atan(y / (a * math.sqrt(1 - e * e)), x / a + e)
                local actualMean = anomaly - e * math.sin(anomaly)
                local expectedMean = body.phase + sample.time * math.pi * 2 * body.orbitTimeScale / body.periodSeconds
                near((actualMean - expectedMean + math.pi) % (math.pi * 2) - math.pi,
                    0, label .. "/轨道相位:" .. name, 0.00001)
            else
                vectorNear(sample.relativePosition, body.initial.relativePosition, label .. "/静止轨道:" .. name)
                near(magnitude(sample.relativeVelocity), 0, label .. "/静止轨道速度:" .. name)
            end
        end
        if body.periodSeconds > 0 then
            assert(type(body.periapsis) == "table" and type(body.apoapsis) == "table", label .. "缺少近远日点")
            near(magnitude(body.periapsis.relativePosition), body.axis * (1 - body.eccentricity), label .. "/近日距离")
            near(magnitude(body.apoapsis.relativePosition), body.axis * (1 + body.eccentricity), label .. "/远日距离")
            local slowSpeed = magnitude(body.apoapsis.relativeVelocity)
            assert(slowSpeed > 0, label .. "远日速度没有保持公转")
            near(magnitude(body.periapsis.relativeVelocity) / slowSpeed,
                (1 + body.eccentricity) / (1 - body.eccentricity), label .. "/近远日速度比")
            near(body.full.time, body.periodSeconds / body.orbitTimeScale, label .. "/公转时钟倍率")
            orbitCount = orbitCount + 1
        end
        if body.rotationPeriodSeconds < 0 then
            near(body.rotationQuarter.rotation - body.initial.rotation, -math.pi / 2, label .. "/逆向自转")
            reverseCount = reverseCount + 1
        elseif body.rotationPeriodSeconds > 0 then
            near(body.rotationQuarter.rotation - body.initial.rotation, math.pi / 2, label .. "/正向自转")
        end
        local derivative = body.derivative
        assert(type(derivative) == "table" and derivative.dt > 0, label .. "缺少速度微分采样")
        near((derivative.after.position.x - derivative.before.position.x) / (2 * derivative.dt),
            derivative.center.velocity.x, label .. "/解析速度X", 0.00001)
        near((derivative.after.position.y - derivative.before.position.y) / (2 * derivative.dt),
            derivative.center.velocity.y, label .. "/解析速度Y", 0.00001)
        local entry = body.spaceEntry
        assert(type(entry) == "table", label .. "缺少发射入口采样")
        near(entry.position.x, body.initial.position.x + math.cos(entry.angle) * (body.radius + entry.height), label .. "/发射位置X")
        near(entry.position.y, body.initial.position.y + math.sin(entry.angle) * (body.radius + entry.height), label .. "/发射位置Y")
        near(entry.velocity.x, body.initial.velocity.x + math.cos(entry.angle) * entry.outwardSpeed, label .. "/继承速度X")
        near(entry.velocity.y, body.initial.velocity.y + math.sin(entry.angle) * entry.outwardSpeed, label .. "/继承速度Y")
    end
    local function find(state, id)
        for _, body in ipairs(state.bodies) do if body.id == id then return body end end
        error("高偏心率样本缺少星体:" .. id)
    end
    local fixture = data.fixture
    assert(type(fixture) == "table", "缺少乱序父子和高偏心率采样")
    local earth = find(fixture.before, "fixture-earth")
    local moon = find(fixture.before, "fixture-moon")
    local half, full = find(fixture.half, "fixture-earth"), find(fixture.full, "fixture-earth")
    near(earth.position.x, 1.5, "高偏心率近日点")
    near(half.position.x, -18.5, "高偏心率远日点")
    vectorNear(full.position, earth.position, "高偏心率周期")
    near(magnitude(earth.velocity) / magnitude(half.velocity), (1 + .85) / (1 - .85), "高偏心率速度比")
    near(magnitude({x = moon.position.x - earth.position.x, y = moon.position.y - earth.position.y}), 2, "乱序父子初始距离")
    local halfMoon = find(fixture.half, "fixture-moon")
    near(magnitude({x = halfMoon.position.x - half.position.x, y = halfMoon.position.y - half.position.y}), 2, "移动父节点的子轨道")
    near(fixture.zeroRotation, 0, "零周期自转")
    assert(type(data.guards) == "table" and #data.guards >= 6, "轨道非法输入采样缺失")
    for _, guard in ipairs(data.guards) do
        assert(type(guard.exception) == "string" and #guard.exception > 0, "非法轨道未拒绝:" .. guard.name)
    end
    return "轨道覆盖 " .. #data.bodies .. " 个星体、" .. orbitCount .. " 条公转和 " .. reverseCount .. " 个逆转星体"
end, "全星体椭圆公转、父子运动、自转与发射入口")
