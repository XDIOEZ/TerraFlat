-- 使用正式放置/伤害/固定步，验证真实残骸 Item 与冷库存；没有直接调用分裂算法。
local function near(actual, expected, label)
    assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) < 0.0001, label)
end

local function part(fragments, id)
    for _, fragment in ipairs(fragments) do
        for _, value in ipairs(fragment.pieces) do if value.id == id then return value, fragment end end
    end
    error("真实会话缺少部件：" .. id)
end

local function check_stage(fragments, count, pieces)
    assert(#fragments == count, "真实分裂整体数量错误")
    local ship_ids, part_ids, total, head, wrecks = {}, {}, 0, nil, 0
    for _, fragment in ipairs(fragments) do
        assert(not ship_ids[fragment.id], "碎块船体身份重复")
        ship_ids[fragment.id] = true
        assert(fragment.phase == "Cruising" and fragment.flightCount == 1, "碎块丢失巡航状态或出现重复飞行记录")
        assert(fragment.presentationBodies == 1 and fragment.presentationColliders == 1,
            "每个整体应只有一个表现刚体与合并 CustomCollider，子部件不能增加刚体")
        assert(fragment.physicsBodies == 1 and fragment.physicsColliders == 2,
            "每个整体应只有一个原生模拟刚体及阻挡/地板两类合并形状")
        if fragment.wreck then
            wrecks = wrecks + 1
            assert(fragment.wreckViewCount == 1 and fragment.presentationItems == 1,
                "一整块真实残骸必须只有一个 Item，子图块不能重启物品与机器")
            assert(not fragment.ignited, "残骸不能继续点火")
        else
            assert(not head and fragment.console and fragment.engine and fragment.ignited,
                "新船首必须保留可用控制台、引擎及原点火状态")
            head = fragment
            assert(fragment.wreckViewCount == 0, "可用船首不应转成残骸 Item")
        end
        for _, value in ipairs(fragment.pieces) do
            assert(not part_ids[value.id] and value.health > 0, "部位被重复归属或死亡部件仍存在")
            part_ids[value.id], total = true, total + 1
        end
    end
    assert(total == pieces and head and wrecks == count - 1, "分裂后的部件与船首/残骸数量不守恒")
    return head
end

test.space("ship_damage_live", function(data)
    assert(data.scope == "live-ship-local-damage" and data.fixtureSetup == "production-piece-transactions-tail-anchor",
        "没有使用正式放置事务和尾部锚点的真实长船")
    assert(data.headDamageAccepted and data.tailDamageAccepted and data.cargoDamageAccepted and
        data.waistHitAccepted and data.tailHitAccepted and data.coldCargoDamageAccepted, "正式伤害入口没有接受自有夹具伤害")
    assert(data.afterTime - data.beforeTime >= .5 - .000001, "没有等待实际固定步完成两次分裂")
    local initial_head = check_stage(data.before, 1, 13)
    assert(initial_head.id == data.originalId, "原长船身份缺失")
    local head = check_stage(data.afterWaist, 2, 12)
    local final_head = check_stage(data.afterTail, 3, 11)
    assert(head.id ~= data.originalId and final_head.id == head.id,
        "尾部锚点应保留原身份，真正可用的新船首应连续存在")
    local source_tail = nil
    for _, fragment in ipairs(data.afterWaist) do
        if fragment.id == data.originalId then source_tail = fragment end
    end
    assert(source_tail and source_tail.wreck, "保留原身份的船尾没有成为真实残骸")
    assert(data.navigationPresent and data.navigationShipId == head.id,
        "船尾转冷残骸错误地清除了已迁移到船首的导航记录")
    local _, console_owner = part(data.afterTail, data.consoleId)
    local _, engine_owner = part(data.afterTail, data.engineId)
    local _, navigation_owner = part(data.afterTail, data.navigationId)
    assert(console_owner.id == head.id and engine_owner.id == head.id and navigation_owner.id == head.id,
        "船首可用设备在两次真实分裂后归属错误")
    local before_head = part(data.before, data.headWoundedId)
    local before_tail = part(data.before, data.tailWoundedId)
    local before_cargo = part(data.before, data.cargoId)
    assert(before_cargo.seeds > 0 and before_cargo.fertilizer > 0, "正式模板没有填入真实种子与肥料库存")
    assert(before_cargo.plantedPlots > 0, "当前真实基线没有记录培育器已播种的槽位")
    for index, stage in ipairs({data.afterWaist, data.afterTail, data.saved}) do
        near(part(stage, data.headWoundedId).health, before_head.health, "船首局部血量不能因分裂被重置")
        near(part(stage, data.tailWoundedId).health, before_tail.health, "船尾局部血量不能因继续打碎而被重置")
        local cargo, owner = part(stage, data.cargoId)
        assert(owner.wreck and cargo.snapshotPresent and cargo.decodedGuid == tonumber(data.cargoId) and cargo.decodedId ~= "",
            "冷残骸未保留真实设备快照和 GUID")
        near(cargo.health, before_cargo.health - (index == 1 and 0 or 3), "冷残骸设备继续掉血或分裂后的结构耐久错误")
        near(cargo.snapshotHealth, cargo.health, "冷设备快照血量与船部位映射不一致")
        near(cargo.seeds, before_cargo.seeds, "真实种子库存在冷移交/递归分裂/保存往返中丢失")
        near(cargo.fertilizer, before_cargo.fertilizer, "真实肥料库存在冷移交/递归分裂/保存往返中丢失")
        assert(cargo.plantedPlots == before_cargo.plantedPlots, "已播种槽位在冷移交/递归分裂/保存往返中丢失")
    end
    for _, stage in ipairs({data.afterWaist, data.afterTail}) do
        local cargo = part(stage, data.cargoId)
        assert(not cargo.machineActive and not cargo.deviceActive, "冷残骸中的设备仍在机器或飞船设备调度中运行")
    end
    assert(#data.saved == 3 and type(data.limitations) == "string" and #data.limitations > 0,
        "正式会话快照没有保存三个整体或未说明武器/联机覆盖缺口")
    return "正式固定步完成尾锚长船截断、新船首保留点火与导航、单 Item 船尾转冷、冷残骸再拆、真实库存和血量保存。"
end, "真实长船截断、单 Item 冷残骸与递归分裂")
test.screenshot("长船截断后的可用船首和两块真实残骸")
