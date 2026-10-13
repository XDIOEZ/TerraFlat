-- 检查星区边界、极大编号精度和真实空域接触。
test.space("coordinates", function(data)
    local function near(value, expected, label)
        assert(type(value) == "number" and value == value and math.abs(value - expected) <= 0.000001, label .. "不一致")
    end
    near(data.size, 256000, "星区边长")
    near(data.halfSize, 128000, "星区半边长")
    local expected = {
        inside = {"0", "0", .125, -.25}, left = {"-1", "0", 127999.75, 0},
        right = {"1", "0", -128000, 0}, down = {"0", "-1", 0, 127999.75},
        up = {"0", "1", 0, -128000}, ["negative-edge"] = {"0", "0", -128000, -128000},
        ["multi-corner"] = {"8", "-10", -127999.875, 127999.75},
        ["huge-neighbor"] = {"9223372036854775807", "-9223372036854775808", -127999.875, 127999.75}
    }
    assert(type(data.normalizations) == "table" and #data.normalizations == 8, "归一化样本不完整")
    local seen = {}
    for _, sample in ipairs(data.normalizations) do
        local value = expected[sample.name]
        assert(value and not seen[sample.name], "未知或重复边界样本:" .. sample.name)
        seen[sample.name] = true
        assert(sample.output.sector.x == value[1] and sample.output.sector.y == value[2], sample.name .. "星区归属错误")
        near(sample.output["local"].x, value[3], sample.name .. "/局部X")
        near(sample.output["local"].y, value[4], sample.name .. "/局部Y")
        assert(sample.output["local"].x >= -data.halfSize and sample.output["local"].x < data.halfSize and
            sample.output["local"].y >= -data.halfSize and sample.output["local"].y < data.halfSize, sample.name .. "局部位置未归一化")
    end
    near(data.relative.x, 256000.125, "long 极值附近相对X")
    near(data.relative.y, -256000.25, "long 极值附近相对Y")
    assert(type(data.sceneKeys) == "table" and #data.sceneKeys >= 4, "场景键样本不足")
    for _, sample in ipairs(data.sceneKeys) do
        assert(sample.parsed == true and sample.restored.x == sample.sector.x and sample.restored.y == sample.sector.y,
            "场景键往返丢失 long 精度:" .. sample.key)
    end
    assert(type(data.identities) == "table" and #data.identities >= 8, "星体身份样本不足")
    for _, sample in ipairs(data.identities) do
        assert(sample.parsed == true and sample.restored.x == sample.sector.x and sample.restored.y == sample.sector.y and
            type(sample["local"]) == "string" and #sample["local"] > 0, "全局身份往返失败:" .. sample.identity)
    end
    assert(type(data.invalid) == "table" and #data.invalid >= 5, "非法身份样本不足")
    for _, sample in ipairs(data.invalid) do
        assert(sample.sceneParsed == false and sample.identityParsed == false, "非法身份被接受:" .. sample.input)
    end
    local before, after = data.roundTrip.before, data.roundTrip.after
    assert(before.sector.x == after.sector.x and before.sector.y == after.sector.y, "坐标快照星区改变")
    near(after["local"].x, before["local"].x, "坐标快照X")
    near(after["local"].y, before["local"].y, "坐标快照Y")
    assert(type(data.guards) == "table" and #data.guards >= 4, "坐标保护样本不足")
    for _, guard in ipairs(data.guards) do
        assert(type(guard.exception) == "string" and #guard.exception > 0, "无效坐标未拒绝:" .. guard.name)
    end
    local contacts = {}
    assert(type(data.contacts) == "table", "缺少接触采样")
    for _, sample in ipairs(data.contacts) do contacts[sample.name] = sample end
    for _, name in ipairs({"neighbor", "far", "empty", "overlap", "tangent", "miss"}) do
        assert(type(contacts[name]) == "table", "接触样本缺失:" .. name)
    end
    assert(contacts.neighbor.hit == true, "相邻星区实际星体没有被扫掠命中")
    near(contacts.neighbor.fraction, .1, "跨星区接触时刻")
    near(contacts.neighbor.contactPosition.x, 128000, "接触点的星体参考区")
    near(contacts.neighbor.motionPosition.x, -128000, "运动体的原参考区")
    assert(contacts.far.hit == false, "遥远星区相同局部坐标出现假接触")
    assert(contacts.empty.hit == false, "空域出现假星体")
    near(contacts.empty.position.x, 24.125, "空域加速位置X")
    near(contacts.empty.position.y, -4.25, "空域加速位置Y")
    near(contacts.empty.velocity.x, 14, "空域速度X")
    near(contacts.empty.velocity.y, -1, "空域速度Y")
    assert(contacts.overlap.hit == true and contacts.tangent.hit == true and contacts.miss.hit == false, "圆形扫掠边界错误")
    near(contacts.overlap.fraction, 0, "初始重叠接触")
    near(contacts.tangent.fraction, .5, "切线接触")
    return "覆盖 8 种归一化、long 极值往返、6 种接触与空域惯性"
end, "极大星区、跨界局部精度、空域与星体接触")
