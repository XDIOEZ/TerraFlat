-- 检查音量数据与空间播放参数，不播放声音、不修改用户音量。
for _, row in ipairs({ { volume = -1, master = -1, expected = 0 }, { volume = 0.25, master = 0.25, expected = 0.25 }, { volume = 3, master = 3, expected = 1 } }) do
    test.system("audio", "bus_volume", row, function(data)
        assert(data.master == row.expected and #data.buses == 5, "总音量或总线数量错误")
        local seen = {}
        for _, bus in ipairs(data.buses) do
            assert(not seen[bus.bus] and bus.volume == row.expected, "音频总线钳制或读写错误")
            seen[bus.bus] = true
        end
        assert(not data.globalHasPosition and data.positionedHasPosition, "全局和空间声音定位混淆")
        assert(data.x == 3 and data.y == 4 and math.abs(data.volume - 0.6) < 0.000001 and math.abs(data.pitch - 1.2) < 0.000001, "空间播放覆盖参数丢失")
    end, "音频总线范围与空间参数：" .. row.expected)
end

for _, row in ipairs({ { first = "probe.first", second = "probe.second", errors = 0 }, { first = "probe.first", second = "PROBE.FIRST", errors = 1 } }) do
    test.system("audio", "catalog_lifecycle", row, function(data)
        assert(data.cueCount == 2 and data.errors == row.errors, "独立音频目录没有报告重复稳定事件 ID")
        assert(data.found and data.resolvedFirst and not data.emptyAccepted, "目录索引重建、去首尾空格或不区分大小写查询错误")
    end, "音频目录装配、重复校验和缓存重建")
end
