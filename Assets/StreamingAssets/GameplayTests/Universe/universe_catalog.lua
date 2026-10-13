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
