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
