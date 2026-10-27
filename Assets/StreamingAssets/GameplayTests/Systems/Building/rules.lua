for _, sample in ipairs({
    { label = "前向距离优先", forward = .5, lateral = 10, distance = 10, better = true },
    { label = "更远前向不抢占", forward = 2, lateral = 0, distance = 0, better = false },
    { label = "横向距离优先", lateral = .5, distance = 10, better = true },
    { label = "更远横向不抢占", lateral = 2, distance = 0, better = false },
    { label = "攻击距离优先", distance = .5, better = true },
    { label = "更远攻击距离不抢占", distance = 2, better = false },
    { label = "前向误差内继续横向比较", forward = 1.00005, lateral = .5, better = true },
    { label = "前向误差外先拒绝", forward = 1.0002, lateral = .5, better = false },
    { label = "横向误差内继续攻击距离比较", lateral = 1.00005, distance = .5, better = true },
    { label = "攻击距离误差内继续坐标比较", distance = 1.00005, x = -1, better = true },
    { label = "等距离按X再Y稳定排序", x = -1, y = 100, better = true },
    { label = "较大X不能靠Y抢占", x = 1, y = -100, better = false },
    { label = "X相同时按Y稳定排序", y = -1, better = true },
    { label = "完全相同候选不替换", better = false }
}) do
    local expected = sample.better
    test.system("building", "hit_candidate_order", sample, function(data)
        assert(data.better == expected, "格子建筑命中候选排序改变")
    end, "格子命中排序：" .. sample.label)
end

test.system("building", "portable_building_health", {}, function(data)
    local expected = { Coconut_Shell_Building = 20, ClayJar_Building = 40, StoneMortar_Building = 50, WoodenBarrel_Building = 60 }
    assert(#data.buildings == 4, "可放置容器的正式定义覆盖不完整")
    for _, row in ipairs(data.buildings) do
        assert(row.validationError == "", row.id .. "没有通过正式机器受击校验：" .. row.validationError)
        assert(row.hasHp and row.maxHp == expected[row.id] and row.hp == row.maxHp,
            row.id .. "缺少满血初始状态或有效最大生命")
        assert(row.triggerEnabled and row.colliderType == "BoxCollider2D" and row.width > 0 and row.height > 0,
            row.id .. "没有配置有效的独立受击范围")
    end
    assert(data.missingHealth == "InvalidOperationException" and data.zeroHealth == "InvalidOperationException" and
            data.missingTrigger == "InvalidOperationException", "机器受击校验接受了缺少生命或受击范围的配置")
end, "落地椰壳、陶罐、石臼、木桶的正式受击配置与缺失拒绝")

test.system("building", "chunk_residency", {}, function(data)
    assert(data.scope == "production-registry-only", "必须调用正式建筑归属索引")
    assert(data.duplicateCount == 1 and data.isolatedCount == 1, "重复登记或同坐标跨世界串入区块")
    assert(data.oldChunkCount == 0 and data.newChunkCount == 1, "建筑迁移后仍留在旧区块")
    assert(data.destroyRemoved and data.reusedCount == 1, "建筑回收或对象池复用残留旧归属")
    assert(data.copyClearsPrevious and data.cleared, "区块采样或世界释放留下旧建筑引用")
end, "地面船体和普通建筑的区块归属、跨界、回收及复用")

test.system("building", "placement_feedback", {}, function(data)
    assert(#data.details == 2 and data.details[1] == "召唤器数量不足" and data.details[2] == "地块支撑能力不足",
        "建造拒绝原因被统一位置错误覆盖")
    assert(data.semanticCount == 2 and data.actorPreserved, "详细原因通道破坏既有音效语义或玩家归属")
end, "建造数量和支撑拒绝原因原样发布且保留语义事件")

test.system("building", "door_definitions", {}, function(data)
    assert(#data.doors == 4, "缺少木门石门的召唤器或本体定义")
    for _, door in ipairs(data.doors) do
        local isSummoner = string.sub(door.id, -9) == "_Summoner"
        local bodyId = isSummoner and string.sub(door.id, 1, -10) or door.id
        assert(door.buildingEnabled and door.quarterTurnAllowed and door.role == (isSummoner and 0 or 1),
            door.id .. "未启用正式四向放置或载体/本体角色配置错误")
        assert(door.stackable and door.sharedModuleCount == 0,
            door.id .. "堆叠门不能引入仅允许单件复制的共享模块")
        assert(door.bodyId == bodyId and door.summonerId == bodyId .. "_Summoner",
            door.id .. "放置后没有指向同一扇门")
        assert(door.spriteAddress == "Assets/6_Art/Building/" .. bodyId .. "_Close.png[" .. bodyId .. "_Close]"
            and door.spriteStateCount == 0, door.id .. "闭合图来源或双扇动画状态配置错误")
        if not isSummoner then
            assert(door.hasDoorModule and door.useItemSprite,
                door.id .. "没有通过正式门模块使用双扇滑动表现")
        end
    end
end, "木门石门：正式载体本体四向配置、配对与共用双扇表现")

test.system("building", "support_layer", {}, function(data)
    assert(data.surfaceGround == 90 and data.surfaceCost == 2 and data.surfaceWalkable,
        "覆盖支撑没有提供新的有效地面和通行成本")
    assert(data.baseGround == 17 and not data.baseWalkable, "覆盖支撑改写了真实底层地形")
    assert(math.abs(data.wetDepth - 0.6) < 0.00001, "铺支撑覆盖时消除了底下的真实液体")
    assert(data.coveredSurfaceDepth == 0, "有效支撑没有遮断表面浸水")
    assert(data.clearedGround == 17 and data.clearedCost == 25 and not data.clearedWalkable and data.supportId == 0,
        "撤支撑后没有恢复底层有效表面")
    assert(math.abs(data.clearedDepth - data.wetDepth) < 0.00001, "撤支撑破坏了独立液体层")
end, "地表支撑覆盖、底层保留与拆除恢复")

test.system("building", "shared_state", {}, function(data)
    assert(data.copiedFirst == 1 and data.sourceFirstAfterCopyMutation == 1, "目标模块修改污染了载体源状态")
    assert(data.distinctModule and data.distinctPayload, "共享建筑模块或载荷没有深拷贝")
    assert(data.copiedName == "placed-state" and data.copiedId == "system-probe:shared-module" and not data.copiedEnabled,
        "载体状态覆盖了建筑本体的模块元信息")
    assert(data.sourceName == "carrier-state" and data.sourceEnabled, "建筑状态转移修改了原始载体")
    assert(data.duplicate == "InvalidOperationException" and data.missing == "InvalidOperationException"
        and data.stacked == "InvalidOperationException", "重复/缺失共享模块或堆叠载体未拒绝")
end, "载体到建筑模块状态转移与深拷贝隔离")
test.system("building", "placement_distance", {}, function(data)
    assert(data.survival == 7.5 and data.fallback == 3.25, "生存放置半径未减半，或忽略玩家实际交互范围")
    assert(data.creativeUnlimited, "减半半径错误限制了创造放置")
end, "建筑距离：生存跟随实际交互半径、无玩家回退与创造无限")

local material_cases = {
    { label = "普通绳子", straps = 0, ropes = 4, quantity = 1 },
    { label = "纯皮带", straps = 4, ropes = 0, quantity = 1 },
    { label = "混合绳料", straps = 2, ropes = 2, quantity = 1 },
    { label = "增产分摊", straps = 2, ropes = 2, quantity = 2 }
}
for _, scenario in ipairs(material_cases) do
    test.system("building", "material_provenance", scenario, function(data)
        assert(data.matched and data.prepared and data.success, "木箱真实材料未通过制作事务")
        assert(data.previewUnchanged and not data.inputsUnchanged and data.spareUntouched,
            "材料预览提前扣料、制作未扣料或多余输入被算入材料")
        local quantity = scenario.quantity
        assert(data.materials.Plank == 6 / quantity, "制作增产没有按每件分摊木板成本")
        assert((data.materials.Rope or 0) == scenario.ropes / quantity,
            "普通绳子被替换成标签默认物品，或存储/放置/拆回丢失材料")
        assert((data.materials.LeatherStrap or 0) == scenario.straps / quantity,
            "实际皮带材料没有跨建筑移交保留")
        assert(data.materials.Stick_Wood == nil, "未消耗的额外材料进入回收清单")
        assert(data.zeroRecovery and data.fullRecovery.Plank == 6 / quantity,
            "零概率仍返料，或满概率没有使用真实木板成本")
        assert((data.fullRecovery.Rope or 0) == scenario.ropes / quantity and
            (data.fullRecovery.LeatherStrap or 0) == scenario.straps / quantity,
            "随机回收没有使用实际替代材料")
        assert(data.proportional.Plank == math.floor(3 / quantity) and
            (data.proportional.Rope or 0) == math.floor(scenario.ropes / quantity * 0.5) and
            (data.proportional.LeatherStrap or 0) == math.floor(scenario.straps / quantity * 0.5),
            "磨损固定返料没有使用实际材料并逐种向下取整")
        assert(data.reorderedIdentity and not data.differentMaterialsStack,
            "相同材料因扣料顺序无法合堆，或不同材料建筑被合堆")
        assert(data.isolated and data.invalidRejected, "材料记录共享了可变引用，或非法数量未拒绝")
    end, "木箱材料记录、保存恢复与两种回收：" .. scenario.label)
end

test.system("building", "material_provenance", { full = true }, function(data)
    assert(data.matched and data.prepared and not data.success, "满输出槽仍然完成制作")
    assert(data.previewUnchanged and data.inputsUnchanged, "输出被阻挡时制作材料被扣除")
end, "建筑材料记录不破坏满输出回滚")

test.system("building", "material_provenance", { ropes = 0, straps = 0 }, function(data)
    assert(not data.matched and not data.prepared and not data.success, "缺少绳料仍制作出带材料记录的木箱")
    assert(data.inputsUnchanged, "缺少绳料时扣除了木板")
end, "建筑缺料不生成虚假材料记录")


test.system("building", "bitumen_floor_definition", {}, function(d)
    assert(d.tileId == 35 and d.tileAsset == "Tile_BitumenFloor" and d.walkable and d.stackable,
        "沥青地板缺少稳定地块、深色Tile或可堆叠召唤器")
    assert(d.placement.RequiredSourceFlags == 1 and d.placement.LiquidRequirement == 2,
        "沥青地板未限制普通可行走干地")
    assert(d.placement.RefundItemId == "Floor_Bitumen_Summoner" and d.carrier == "Floor_Bitumen_Summoner"
        and d.placementTile == "Tile_Floor_Bitumen" and d.loadCapacity == 10,
        "沥青地板铺设、承重或拆回配对错误")
end, "沥青地板正式内容、地面支撑与拆回定义")
