-- 同一主题集中编排，子检查保留独立标签和原始采样。

do
    -- 最小世界测试不创建全物品陈列。
    test.check("world_ready", "正式游戏世界就绪")
    test.check("player_ready", "默认玩家就绪")
    test.check("terrain_ready", "玩家脚下区块就绪")
end

do
    -- 遍历真实目录，检查每个星区、星体及其独立环境。
    test.space("catalog", function(data)
        local function finite(value, label)
            assert(type(value) == "number" and value == value and math.abs(value) < math.huge, label .. "不是有限数")
        end
        local function near(value, expected, label)
            finite(value, label)
            assert(math.abs(value - expected) <= 0.000001 * math.max(1, math.abs(expected)), label .. "不一致")
        end
        assert(type(data.sectors) == "table" and #data.sectors > 0, "宇宙目录没有星区")
        local ids, planetIds, sectorIds = {}, {}, {}
        local count = 0
        for _, sector in ipairs(data.sectors) do
            local sectorId = sector.sector.x .. ":" .. sector.sector.y
            assert(not sectorIds[sectorId], "星区身份重复：" .. sectorId)
            sectorIds[sectorId] = true
            assert(type(sector.templateId) == "string" and #sector.templateId > 0, sectorId .. "缺少模板")
            assert(sector.fixedStepSeconds > 0 and sector.metersPerUnityUnit > 0 and sector.orbitTimeScale > 0,
                sectorId .. "时钟或显示比例无效")
            assert(type(sector.bodies) == "table" and #sector.bodies == #sector.repeatBodies and
                #sector.bodies == #sector.alternateBodies, sectorId .. "重复生成数量不一致")
            local localIds = {}
            for _, body in ipairs(sector.bodies) do localIds[body.id] = body end
            for index, body in ipairs(sector.bodies) do
                local label = body.id
                assert(type(label) == "string" and #label > 0 and not ids[label], "星体身份缺失或重复：" .. tostring(label))
                assert(type(body.planetId) == "string" and #body.planetId > 0 and not planetIds[body.planetId], label .. "星球身份重复")
                ids[label], planetIds[body.planetId] = true, true
                assert(label == "sector:" .. sectorId .. "/body:" .. body.templateId, label .. "实例身份作用域错误")
                assert(body.sector.x == sector.sector.x and body.sector.y == sector.sector.y, label .. "所属星区错误")
                assert(body.definitionId == body.templateId and body.resolvedPlanetId == label, label .. "目录映射错误")
                assert(type(body.prefabId) == "string" and #body.prefabId > 0, label .. "缺少表现资源身份")
                for _, key in ipairs({"radius", "gravity", "axis", "phase", "periodSeconds", "eccentricity",
                    "periapsisRadians", "rotationPeriodSeconds", "rotationInitialRadians"}) do finite(body[key], label .. "/" .. key) end
                assert(body.radius > 0 and body.gravity >= 0 and body.axis >= 0 and body.periodSeconds >= 0 and
                    body.eccentricity >= 0 and body.eccentricity < 1, label .. "物理参数越界")
                if body.parentId and #body.parentId > 0 then
                    assert(localIds[body.parentId] and body.parentId ~= label, label .. "父节点不存在或自引用")
                    local visited, current = {}, body
                    while current and current.parentId and #current.parentId > 0 do
                        assert(not visited[current.id], label .. "父子关系成环")
                        visited[current.id] = true
                        current = localIds[current.parentId]
                    end
                end
                local repeated, alternate = sector.repeatBodies[index], sector.alternateBodies[index]
                assert(repeated.id == label and repeated.planetId == body.planetId and repeated.seed == body.seed and
                    repeated.phase == body.phase and repeated.rotationInitialRadians == body.rotationInitialRadians,
                    label .. "相同种子生成不稳定")
                assert(alternate.id == label and alternate.seed ~= body.seed, label .. "不同世界种子未隔离")
                local surface, planet = body.surface, body.planet
                assert(type(surface) == "table" and type(planet) == "table", label .. "缺少地表或星球采样")
                assert(surface.id == body.surfaceProfileId and planet.surfaceProfileId == surface.id and
                    planet.atmosphereProfileId == surface.id, label .. "地表与大气配置映射错误")
                assert(planet.id == label and planet.name == body.planetId and planet.displayName == body.displayName and
                    planet.prefabId == body.prefabId, label .. "星球元数据没有使用实例身份")
                near(planet.radius, body.radius, label .. "/星球半径")
                near(planet.gravity, body.gravity, label .. "/星球重力")
                near(planet.rotationDegreesPerSecond,
                    math.abs(body.rotationPeriodSeconds) > 0.000000000001 and 360 / body.rotationPeriodSeconds or 0,
                    label .. "/自转元数据")
                finite(surface.temperatureCelsius, label .. "/地表温度")
                finite(surface.pressureKPa, label .. "/地表气压")
                assert(surface.temperatureCelsius > -273.15 and surface.pressureKPa >= 0 and surface.ecologyMultiplier >= 0,
                    label .. "环境参数无效")
                near(planet.temperatureKelvin, surface.temperatureCelsius + 273.15, label .. "/大气温度")
                near(planet.pressureKPa, surface.pressureKPa, label .. "/大气气压")
                assert(planet.mapCount == 0, label .. "新星球继承了旧地图")
                local fraction = 0
                for gasId, amount in pairs(surface.composition) do
                    assert(type(gasId) == "string" and #gasId > 0 and amount >= 0 and amount <= 1, label .. "配置气体组分无效")
                    fraction = fraction + amount
                end
                near(fraction, surface.pressureKPa > 0 and 1 or 0, label .. "/配置组分之和")
                local actualFraction = 0
                for _, gas in ipairs(planet.gases) do
                    assert(surface.composition[gas.id] ~= nil and gas.moles >= 0, label .. "大气存在未知气体")
                    near(gas.fraction, surface.composition[gas.id], label .. "/组分:" .. gas.id)
                    actualFraction = actualFraction + gas.fraction
                end
                near(actualFraction, surface.pressureKPa > 0 and 1 or 0, label .. "/实际组分之和")
                for key, value in pairs(surface.numbers) do finite(value, label .. "/生成参数:" .. key) end
                for key, value in pairs(surface.caveNumbers) do finite(value, label .. "/洞穴参数:" .. key) end
                count = count + 1
            end
        end
        assert(count > 0, "配置星区没有任何星体")
        assert(data.templateBefore == data.templateAfter, "生成过程修改了原始太阳系模板")
        assert(type(data.empty) == "table" and data.empty.templateId == "core:empty-space" and
            #data.empty.bodies == 0, "非配置星区没有保持真实空域")
        return "目录覆盖 " .. #data.sectors .. " 个星区、" .. count .. " 个星体及独立大气"
    end, "全目录身份、轨道参数和独立星球环境")
end

do
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
end

do
    -- 比较真实存档解析和目录重新绑定前后的权威采样。
    test.space("snapshots", function(data)
        local function near(value, expected, label)
            assert(type(value) == "number" and value == value and math.abs(value - expected) <=
                0.000001 * math.max(1, math.abs(expected)), label .. "不一致")
        end
        local function compare(before, after, label)
            assert(type(before) == "table" and type(after) == "table", label .. "快照缺失")
            assert(before.version == 2 and after.version == before.version and after.navigationSequence == before.navigationSequence,
                label .. "版本或导航序号改变")
            near(after.time, before.time, label .. "/全局时钟")
            for _, key in ipairs({"ships", "flights", "devices", "passengers", "navigation", "docking", "looseItems"}) do
                assert(type(after[key]) == "number" and after[key] == before[key], label .. "/集合数量:" .. key)
            end
            assert(type(before.sectors) == "table" and #before.sectors > 0 and #after.sectors == #before.sectors, label .. "星区数量改变")
            local count = 0
            for index, original in ipairs(before.sectors) do
                local restored = after.sectors[index]
                local sectorLabel = label .. "/" .. original.sector.x .. ":" .. original.sector.y
                assert(restored.sector.x == original.sector.x and restored.sector.y == original.sector.y and
                    restored.templateId == original.templateId and restored.seed == original.seed, sectorLabel .. "身份或种子改变")
                for _, key in ipairs({"simulationSeconds", "fixedStepSeconds", "metersPerUnityUnit", "orbitTimeScale"}) do
                    near(restored[key], original[key], sectorLabel .. "/" .. key)
                end
                near(restored.simulationSeconds, after.time, sectorLabel .. "/单一时钟")
                assert(type(restored.bodies) == "table" and #restored.bodies == #original.bodies, sectorLabel .. "星体数量改变")
                for bodyIndex, body in ipairs(original.bodies) do
                    local result = restored.bodies[bodyIndex]
                    local bodyLabel = label .. "/" .. body.id
                    for _, key in ipairs({"id", "templateId", "planetId", "parentId", "surfaceProfileId", "displayName", "prefabId", "seed"}) do
                        assert(result[key] == body[key], bodyLabel .. "/字段改变:" .. key)
                    end
                    for _, key in ipairs({"radius", "gravity", "axis", "phase", "periodSeconds", "eccentricity",
                        "periapsisRadians", "rotationPeriodSeconds", "rotationInitialRadians"}) do
                        near(result[key], body[key], bodyLabel .. "/" .. key)
                    end
                    for _, key in ipairs({"position", "velocity"}) do
                        near(result[key].x, body[key].x, bodyLabel .. "/" .. key .. "/X")
                        near(result[key].y, body[key].y, bodyLabel .. "/" .. key .. "/Y")
                    end
                    count = count + 1
                end
            end
            assert(count > 0, label .. "未覆盖星体快照")
            return count
        end
        local count = compare(data.original, data.restored, "JSON 往返")
        compare(data.original, data.rebound, "重新绑定当前目录")
        assert(data.serializedHasUniverse == false, "运行时 Universe 别名错误进入存档")
        assert(type(data.stale) == "table" and #data.stale.sectors == #data.original.sectors, "缺少旧规则采样")
        local changed = 0
        for index, original in ipairs(data.original.sectors) do
            local stale = data.stale.sectors[index]
            near(stale.simulationSeconds, original.simulationSeconds, "重新绑定前的时钟")
            for bodyIndex, body in ipairs(original.bodies) do
                local previous = stale.bodies[bodyIndex]
                assert(previous.id == body.id and previous.seed == body.seed and previous.phase == body.phase and
                    previous.rotationInitialRadians == body.rotationInitialRadians, body.id .. "准备旧规则时修改了身份或初相位")
                if previous.radius ~= body.radius or previous.periodSeconds ~= body.periodSeconds or previous.displayName ~= body.displayName then
                    changed = changed + 1
                end
            end
        end
        assert(changed > 0, "重新绑定样本没有实际改变规则，无法检验恢复")
        assert(type(data.guards) == "table" and #data.guards >= 4, "非法快照采样不足")
        for _, guard in ipairs(data.guards) do
            assert(type(guard.exception) == "string" and #guard.exception > 0, "非法快照未拒绝:" .. guard.name)
        end
        return "快照覆盖 " .. #data.original.sectors .. " 个星区、" .. count .. " 个星体及目录重新绑定"
    end, "星区快照往返、当前规则重新绑定与非法存档拒绝")
end

do
    -- 在独立冷生成结果中检查全部星体的确定性、环境和地表环绕。
    test.space("surface", function(data)
        local function finite(value, label)
            assert(type(value) == "number" and value == value and math.abs(value) < math.huge, label .. "不是有限数")
        end
        local function near(value, expected, label)
            finite(value, label)
            assert(math.abs(value - expected) <= 0.00001 * math.max(1, math.abs(expected)), label .. "不一致")
        end
        assert(data.source == "isolated-cold-generation", "冷地表采样来源不明确")
        assert(type(data.bodies) == "table" and #data.bodies > 0, "缺少星体冷地表采样")
        local ids, sampleCount = {}, 0
        for _, body in ipairs(data.bodies) do
            local label = body.id
            assert(type(label) == "string" and #label > 0 and not ids[label], "冷地表星体缺失或重复:" .. tostring(label))
            ids[label] = true
            local profile, surface = body.profile, body.surface
            assert(type(profile) == "table" and type(surface) == "table", label .. "缺少冻结配置")
            assert(profile.id == surface.id and profile.texts["space.bodyId"] == label and
                profile.texts["space.surfaceProfileId"] == surface.id, label .. "冻结 Profile 未绑定正确星体")
            assert(profile.width > 0 and profile.height > 0 and type(profile.fingerprint) == "string" and
                #profile.fingerprint > 0, label .. "冻结 Profile 尺寸或身份无效")
            near(profile.numbers["space.surfaceTemperatureCelsius"], surface.temperatureCelsius, label .. "/冻结基温")
            near(profile.numbers["space.surfacePressureKPa"], surface.pressureKPa, label .. "/冻结气压")
            assert(body.planet.id == label and body.planet.name == body.planetId, label .. "冷生成使用了其它星球")
            assert(body.span.x > 0 and body.span.y > 0 and type(body.generationSeed) == "number", label .. "拓扑或种子缺失")
            assert(type(body.samples) == "table" and #body.samples == 3 and type(body.repeatSamples) == "table" and
                #body.repeatSamples == #body.samples, label .. "冷地表样本不完整")
            local uniform = (profile.numbers["space.uniformSurface"] or 0) > 0
            local climateOverride = uniform or (profile.numbers["space.climateOverride"] or 0) > 0
            local variation = profile.numbers["space.temperatureVariationCelsius"] or 0
            if surface.id == "core:surface.moon" then
                -- 月球只保留月壤和高低起伏，坑沿、矿物和自然物不能重新混入石头。
                assert(uniform and surface.tiles["space.uniformTileId"] == "Tile_MoonRegolith", "月球没有统一使用月壤")
                for key, tileId in pairs(surface.tiles) do
                    assert(tileId == "Tile_MoonRegolith", "月球混入其它地表材质:" .. key)
                end
                assert(profile.ecologyRuleCount == 0 and profile.caveResourceRuleCount == 0 and
                    profile.ecologyMultiplier == 0, "月球仍会生成自然物或矿物")
                assert(profile.numbers["cave.portal.enabled"] == 0 and profile.numbers["structure.enabled"] == 0,
                    "月球仍会生成洞口或遗迹")
                assert(profile.numbers["space.crater.enabled"] == 1 and
                    profile.numbers["space.crater.minimumRadius"] <= 3 and
                    profile.numbers["space.crater.maximumRadius"] >= 50, "月球缺少大小差异明显的环形山")
            end
            for index, sample in ipairs(body.samples) do
                local repeated = body.repeatSamples[index]
                local sampleLabel = label .. "/第" .. index .. "个地表样本"
                assert(sample.temperatureFound == true, sampleLabel .. "没有真实温度层")
                finite(sample.temperatureCelsius, sampleLabel .. "/温度")
                finite(sample.liquidDepth, sampleLabel .. "/液体深度")
                assert(sample.temperatureCelsius > -273.15 and sample.liquidDepth >= 0, sampleLabel .. "物理环境越界")
                near(sample.pressureKPa, body.planet.pressureKPa, sampleLabel .. "/实际气压")
                assert(type(sample.terrainHash) == "string" and #sample.terrainHash > 0 and
                    sample.terrainHash == repeated.terrainHash, sampleLabel .. "独立冷生成地形哈希不稳定")
                assert(sample.cell.x == repeated.cell.x and sample.cell.y == repeated.cell.y, sampleLabel .. "重建坐标改变")
                for _, key in ipairs({"groundTileId", "blockingTileId", "supportTileId", "biomeId", "walkable", "liquidId", "temperatureFound"}) do
                    assert(sample[key] ~= nil and repeated[key] == sample[key], sampleLabel .. "/重建字段改变:" .. key)
                end
                near(repeated.liquidDepth, sample.liquidDepth, sampleLabel .. "/重建液体深度")
                near(repeated.temperatureCelsius, sample.temperatureCelsius, sampleLabel .. "/重建温度")
                near(repeated.pressureKPa, sample.pressureKPa, sampleLabel .. "/重建气压")
                if climateOverride then
                    assert(math.abs(sample.temperatureCelsius - surface.temperatureCelsius) <= variation + 0.001,
                        sampleLabel .. "星体气候超出配置基温和变化范围")
                end
                if uniform then
                    near(sample.liquidDepth, 0, sampleLabel .. "/均匀地表液体清除")
                    assert(sample.blockingTileId == 0, sampleLabel .. "均匀地表混入阻挡地块")
                    if profile.numbers["space.uniformTileId"] then
                        assert(sample.groundTileId == profile.numbers["space.uniformTileId"], sampleLabel .. "均匀地表地块错误")
                    end
                end
                sampleCount = sampleCount + 1
            end
            local origin, negative, wrapped = body.samples[1], body.samples[2], body.samples[3]
            assert(origin.input.x == 0 and origin.input.y == 0 and negative.input.x == -1 and negative.input.y == -1,
                label .. "原点或负坐标样本缺失")
            assert(wrapped.input.x == body.span.x and wrapped.input.y == body.span.y and
                wrapped.cell.x == origin.cell.x and wrapped.cell.y == origin.cell.y and
                wrapped.terrainHash == origin.terrainHash, label .. "跨地表拓扑边界没有回到同一地形")
            near(wrapped.temperatureCelsius, origin.temperatureCelsius, label .. "/环绕温度")
            assert(wrapped.groundTileId == origin.groundTileId and wrapped.liquidId == origin.liquidId, label .. "环绕地形或液体不一致")
        end
        return "冷地表覆盖 " .. #data.bodies .. " 个星体、" .. sampleCount .. " 个坐标与独立重生成；不含活跃差量验收"
    end, "全星体冷地表生成、环境确定性和拓扑环绕")
end
