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
