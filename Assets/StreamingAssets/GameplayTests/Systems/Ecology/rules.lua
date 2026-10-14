-- 独立四乘四草稿调用正式生态生成器。
for _, seed in ipairs({17413, 8321}) do
    local value = seed
    test.system("ecology", "generation", {seed = value}, function(data)
        assert(#data.warm == 16 and #data.repeated == 16, "百分之百概率的合格格子没有生成自然物")
        local identities, cells = {}, {}
        for index, item in ipairs(data.warm) do
            local repeated = data.repeated[index]
            assert(item.guid ~= 0 and not identities[item.guid], "自然物身份为空或重复")
            identities[item.guid] = true
            assert(item.guid == repeated.guid and item.x == repeated.x and item.y == repeated.y,
                "重复生成改变了稳定身份或位置")
            assert(item.x >= 0 and item.x < 4 and item.y >= 0 and item.y < 4, "生态生成越出小区块")
            local cell = item.x .. ":" .. item.y
            assert(not cells[cell], "单产物规则在同格重复生成")
            cells[cell] = true
            assert(item.itemId == "probe-plant" and item.ruleId == "system-probe:plant", "生成丢失规则归属")
        end
        assert(#data.cold == 0 and #data.blocked == 0 and #data.disabled == 0, "低温、阻挡或零倍率没有阻止生成")
        assert(data.lowerBoundary and data.upperBoundary and not data.wrongBiome, "生态气候边界或群系掩码错误")
    end, "生态稳定身份、气候筛选与阻挡：种子" .. value)
end
