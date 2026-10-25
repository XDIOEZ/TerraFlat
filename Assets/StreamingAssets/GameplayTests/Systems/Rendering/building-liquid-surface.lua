-- 建筑水面覆盖独立规则，避免与其他渲染主题合并后超过步骤上限。
test.system("rendering", "building_liquid_surface", {}, function(data)
    local expected = { false, true, false, true, false, true, true }
    assert(#data.samples == #expected, "建筑水面状态采样不完整")
    for index, submerged in ipairs(expected) do
        assert(data.samples[index].submerged == submerged,
            "建筑水面覆盖错误：" .. data.samples[index].stage)
    end
    assert(data.samples[3].depth > 0 and data.samples[3].support ~= 0,
        "平台错误地清空了原始水层")
    assert(data.samples[7].support == 0, "相邻平台错误地支撑当前建筑")
    assert(not data.outsideSubmerged and not data.missingSubmerged, "缺失地形错误地标记为水下")
    assert(data.groundQueue < data.buildingQueue and data.buildingQueue < data.waterQueue,
        "水下建筑未处于海床之后、水面之前")
    local batch = {}
    for _, notification in ipairs(data.notifications) do
        if notification.kind == "batch" then batch[#batch + 1] = notification end
    end
    assert(#batch == 3 and batch[1].index == 0 and not batch[1].submerged,
        "批量排水没有读取已提交的干地状态")
    assert(batch[2].index == 0 and batch[2].submerged and batch[3].index == 1 and batch[3].submerged,
        "批量涨水未覆盖浅水和相邻深水格")
end, "建筑水面：直接入水、平台增删、批量排水与涨水")
