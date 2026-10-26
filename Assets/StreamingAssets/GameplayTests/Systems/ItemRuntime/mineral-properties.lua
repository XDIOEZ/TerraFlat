local expected = {
    coal = {2, 1, "Ore_Coal", "Mine_Coal", "Mine_Coal_Large"},
    copper_ore = {4, 2, "Ore_Copper", "Mine_Copper", "Mine_Copper_Large"},
    flint = {7, 3, "Ore_Flint"},
    iron_ore = {6, 3, "Ore_Iron", "Mine_Iron", "Mine_Iron_Large"},
    tin_ore = {7, 3, "Ore_Tin", "Mine_Tin", "Mine_Tin_Large"},
    stone = {6, 4, "Ore_Stone", "Mine_Stone", "Mine_Stone_Large"},
    niter = {2, 1, "Ore_Niter", "Mine_Niter", "Mine_Niter_Large"},
    diamond = {10, 4, "Ore_Diamond", "Mine_Diamond", "Mine_Diamond_Large"},
    gold_ore = {7, 3, "Ore_Gold", "Mine_Gold", "Mine_Gold_Large"},
    obsidian = {5, 2, "Obsidian", "Mine_Obsidian", "Mine_Obsidian_Large"},
    sulfur = {2, 1, "Ore_Sulfur", "Mine_Sulfur", "Mine_Sulfur_Large"},
    tungsten_ore = {5, 2, "Ore_Tungsten", "Mine_Tungsten", "Mine_Tungsten_Large"},
    mica = {3, 1, "Ore_Mica", "MicaSheet"},
    quartz = {7, 2, "Quartz"}
}

test.system("item_runtime", "mineral_properties", {}, function(data)
    assert(data.accepted and #data.minerals == 14, "正式矿物参考目录未完整加载")
    local found = {}
    for _, mineral in ipairs(data.minerals) do
        local row = expected[mineral.Id]
        assert(row and not found[mineral.Id], "矿物参考项缺失、重复或身份错误")
        found[mineral.Id] = true
        assert(mineral.Hardness == row[1] and mineral.Toughness == row[2], "矿物初始参考值发生变化")
        assert(#mineral.ItemIds == #row - 2, "矿脉与掉落物的映射数量不正确")
        for index, itemId in ipairs(mineral.ItemIds) do
            assert(itemId == row[index + 2], "矿脉与掉落物没有共用正确的矿物参考项")
        end
        assert(mineral.ReferenceMaterial ~= "", "矿物缺少现实参照材料说明")
    end
    assert(data.sharedReferences and data.caseInsensitive, "参考项没有共享，或物品ID查询不支持大小写一致性")
    assert(not data.unknownFound and not data.nullFound, "未知或空物品错误获得了默认矿物值")
end, "14类矿物初值、云母矿片与石英映射、大小写与未知ID")

local function equal(a, b)
    if type(a) ~= type(b) then return false end
    if type(a) ~= "table" then return a == b end
    for key, value in pairs(a) do
        if not equal(value, b[key]) then return false end
    end
    for key in pairs(b) do
        if a[key] == nil then return false end
    end
    return true
end

local function near(a, b) return math.abs(a - b) < 0.00001 end
local giant_types = {
    Stone = true, Coal = true, Copper = true, Iron = true, Tin = true, Niter = true,
    Diamond = true, Gold = true, Obsidian = true, Sulfur = true, Tungsten = true
}

test.system("item_runtime", "giant_mines", {}, function(data)
    assert(#data.mines == 11 and near(data.bonusChance, 0.05), "巨矿矿种或攻击掉落机会不正确")
    assert(data.emptyRootOccupied and not data.emptyOtherOccupied, "普通资源缺少占地时不再按根格兜底")
    local seen = {}
    for _, pair in ipairs(data.mines) do
        local giant, small = pair.giant, pair.small
        local mineral = giant.id:match("^Mine_(.+)_Large$")
        assert(giant_types[mineral] and not seen[mineral], "巨型矿山矿种缺失、重复或混入无小矿山的矿种")
        seen[mineral] = true
        assert(small.id == "Mine_" .. mineral and giant.sprite == small.sprite and pair.sharedMineral,
            "巨矿没有沿用对应小矿山贴图和矿物硬度/韧性参考")
        assert(giant.health.Data.Hp == 200000 and giant.health.Data.MaxHp == 200000,
            "巨矿当前生命或生命上限不是二十万")
        assert(equal(giant.health.Data.DefenseValues, small.health.Data.DefenseValues) and
            giant.health.weakTool == small.health.weakTool and equal(giant.harvest, small.harvest),
            "巨矿丢失对应小矿山的防御、工具弱点或开采特性")
        local body = giant.body
        assert(near(body.width, 13) and near(body.height, 5 / 3) and near(body.x, 0) and
            near(body.y - body.height / 2, -2.5) and not body.isTrigger,
            "巨矿碰撞体没有固定底边并只保留底部三分之一")
        assert(near(giant.sortingAnchor.x, body.x) and near(giant.sortingAnchor.y, body.y) and
            giant.sortingAnchor.y < body.y + body.height / 2 and
            giant.sortingAnchor.y > body.y - body.height / 2,
            "巨矿排序锚点没有落在实际阻挡体中心，背后贴近时会提前显示玩家")
        assert(near(small.sortingAnchor.x, 0) and near(small.sortingAnchor.y, 0),
            "修正巨矿排序时改变了普通矿点的根部锚点")
        assert(#giant.cells == 26 and not giant.rootOccupied and giant.bottomOccupied and not giant.topOccupied,
            "巨矿离散占地仍阻挡已削去的上部或漏掉实际底部")
        local cells = {}
        for _, cell in ipairs(giant.cells) do
            local key = cell.x .. ":" .. cell.y
            assert(cell.x >= -6 and cell.x <= 6 and (cell.y == -2 or cell.y == -1) and not cells[key],
                "巨矿离散占地与底部碰撞投影不符或重复")
            cells[key] = true
        end
        local hit = giant.health["$collider2D"]
        assert(near(hit.size.x, 13.8) and near(hit.size.y, 9.6) and near(hit.offset.y, 2),
            "削减阻挡时误缩了完整受击范围")
        assert(near(giant.visual.scaleX, 14.02) and near(giant.visual.scaleY, 14.02) and near(giant.visual.y, 1.72),
            "调整碰撞时改变了巨矿贴图大小或位置")
        local products = {}
        for _, entry in ipairs(small.loot) do products[entry.LootPrefabName] = true end
        assert(#giant.loot == #small.loot and products[giant.bonusFirst] and products[giant.bonusLast],
            "巨矿每次攻击奖励没有从对应小矿山的产物中选择")
        for _, entry in ipairs(giant.loot) do
            assert(products[entry.LootPrefabName] and entry.DropChance > 0 and entry.MaxAmount > 0,
                "巨矿最终掉落物种类与对应小矿山不同")
        end
        if mineral == "Stone" then
            assert(giant.lootTableId == "Mine_Stone_Large_Default" and
                giant.loot[1].MinAmount == 60 and giant.loot[1].MaxAmount == 90 and
                giant.loot[2].MinAmount == 6 and giant.loot[2].MaxAmount == 12,
                "原巨型石矿的最终产量被意外削减")
        else
            assert(giant.lootTableId == small.lootTableId and equal(giant.loot, small.loot),
                "新增巨矿没有继承对应小矿山的完整掉落表")
        end
    end
end, "十一种巨矿阻挡中心排序、二十万生命、底部碰撞和继承开采/掉落")

local function entry(hardness, toughness)
    return '{"id":"probe","referenceMaterial":"测试矿物","hardness":' .. hardness ..
        ',"toughness":' .. toughness .. ',"itemIds":["Probe_Ore"]}'
end

for _, levels in ipairs({{1, 1}, {10, 10}}) do
    test.system("item_runtime", "mineral_properties", {
        json = '{"schemaVersion":1,"minerals":[' .. entry(levels[1], levels[2]) .. ']}'
    }, function(data)
        assert(data.accepted, "合法的1级或10级边界被拒绝")
    end, "矿物参考值合法边界")
end

local invalid = {
    '',
    'null',
    '{invalid json}',
    '{"schemaVersion":2,"minerals":[' .. entry(1, 1) .. ']}',
    '{"schemaVersion":1,"minerals":[]}',
    '{"schemaVersion":1,"minerals":[' .. entry(0, 1) .. ']}',
    '{"schemaVersion":1,"minerals":[' .. entry(11, 1) .. ']}',
    '{"schemaVersion":1,"minerals":[' .. entry(1, 0) .. ']}',
    '{"schemaVersion":1,"minerals":[' .. entry(1, 11) .. ']}',
    '{"schemaVersion":1,"minerals":[' .. entry("1.5", 1) .. ']}',
    '{"schemaVersion":1,"minerals":[' .. entry(1, "1.5") .. ']}',
    '{"schemaVersion":1,"minerals":[' .. entry(1, 1) .. ',' .. entry(2, 2) .. ']}',
    '{"schemaVersion":1,"minerals":[' .. entry(1, 1) .. ',' ..
        entry(2, 2):gsub('"probe"', '"other"') .. ']}',
    '{"schemaVersion":1,"minerals":[' .. entry(1, 1):gsub('"Probe_Ore"', '""') .. ']}',
    '{"schemaVersion":1,"minerals":[' .. entry(1, 1):gsub('"hardness":1,', '') .. ']}',
    '{"schemaVersion":1,"minerals":[' .. entry(1, 1):gsub('"toughness":1,', '') .. ']}',
    '{"schemaVersion":1,"typo":1,"minerals":[' .. entry(1, 1) .. ']}'
}
for _, json in ipairs(invalid) do
    test.system("item_runtime", "mineral_properties", {json = json}, function(data)
        assert(not data.accepted and data.error ~= "", "非法等级、重复绑定或错误配置没有被拒绝")
    end, "拒绝无效矿物参考配置")
end
