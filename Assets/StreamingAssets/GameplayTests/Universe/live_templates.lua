-- 遍历全部正式船型及展开部件，不用固定船型数量判断通过。
test.space("ship_templates", function(data)
    assert(data.scope == "live-content" and #data.templates > 0, "没有正式飞船模板")
    local ids, count = {}, 0
    for _, ship in ipairs(data.templates) do
        assert(not ids[ship.id], "重复船型：" .. ship.id)
        ids[ship.id] = true
        assert(ship.width > 0 and ship.height > 0 and #ship.members > 0, "空船型：" .. ship.id)
        local members, floor, console, engine = {}, 0, 0, 0
        for _, part in ipairs(ship.members) do
            local label = ship.id .. "/" .. part.id
            assert(not members[part.id], "重复部件：" .. label)
            members[part.id] = true
            assert(part.registered and part.kind ~= nil, "模板引用未注册船体物品：" .. label)
            assert(part.x >= 0 and part.x < ship.width and part.y >= 0 and part.y < ship.height, "模板坐标越界：" .. label)
            assert(part.quarterTurns >= 0 and part.quarterTurns <= 3, "模板朝向错误：" .. label)
            if part.kind == "Floor" then floor = floor + 1 end
            if part.kind == "Console" then console = console + 1 end
            if part.kind == "Engine" then engine = engine + 1 end
            count = count + 1
        end
        assert(floor > 0 and console > 0 and engine > 0, "测试船型缺少地板、控制台或引擎：" .. ship.id)
    end
    return "遍历 " .. #data.templates .. " 个正式船型、" .. count .. " 个部件"
end, "全部飞船模板与资源引用")
