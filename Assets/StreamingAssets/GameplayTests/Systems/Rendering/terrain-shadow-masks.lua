-- 全部256种邻格组合独立编排，保留单份测试256步上限。
local function has(mask, index) return math.floor(mask / (2 ^ (index - 1))) % 2 == 1 end
local adjacent = { { 1, 3 }, { 2, 3 }, { 1, 4 }, { 2, 4 } }
for mask = 0, 255 do
    test.system("rendering", "terrain_shadow_masks", { mask = mask }, function(data)
        assert(data.visible == (mask ~= 0), "仅有斜对角高地时没有提交阴影格，或平地多画阴影")
        for index = 1, 4 do
            assert(data.edges[index] == (has(mask, index) and 1 or 0), "四向高差改变了原有直边")
            local corner = has(mask, index + 4) and not has(mask, adjacent[index][1])
                and not has(mask, adjacent[index][2])
            assert(data.corners[index] == (corner and 1 or 0), "圆角缺失、方向颠倒或重复补已覆盖的边")
        end
    end, "地形阴影八邻域全部组合：" .. mask)
end
