-- 独立样本调用正式伤害与分裂接口，不创建世界；本主题没有需要截图的视觉变化。
local function near(actual, expected, label)
    assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) < 0.0000001, label)
end

local function by_id(parts, id)
    for _, part in ipairs(parts) do if part.id == id then return part end end
    error("缺少部件：" .. id)
end

local function scope(data, operation)
    assert(data.scope == "production-domain" and data.operation == operation, "没有采样正式船体规则")
    assert(type(data.limitations) == "string" and #data.limitations > 0, "采样必须说明现场覆盖缺口")
end

test.system("space", "ship_local_damage", {}, function(data)
    scope(data, "ship_local_damage")
    assert(data.firstAccepted and data.secondAccepted, "两发命中没有通过正式伤害入口")
    near(data.first.health, 81, "引擎第一发结构血量应为 81")
    near(data.first.disableHealth, 1, "引擎第一发后应还差 1 点才瘫痪")
    assert(data.first.alive and data.first.operational and not data.first.destroyed, "第一发后引擎应仍可工作")
    near(data.second.health, 62, "引擎第二发结构血量应为 62")
    near(data.second.disableHealth, -18, "引擎第二发瘫痪耐久应为 -18")
    assert(data.second.alive and not data.second.operational and not data.second.destroyed,
        "瘫痪应停工但不能当作结构摧毁")
    assert(not data.disabledEngineAvailable, "瘫痪引擎不能继续提供可用动力资格")
    near(data.floor.health, 100, "上层引擎挨打不能连带扣地板血")
    assert(#data.events == 2, "一次命中不能因两种耐久重复发出结构扣血")
    for _, event in ipairs(data.events) do
        assert(event.id == "local-engine", "局部伤害事件指向了其他部件")
        near(event.damage, 19, "引擎防御 1 应抵消伤害 20 中的 1 点")
    end
    assert(not data.disabledGeometryRebuilt and data.disabledGeometry.rebuilds == data.beforeGeometry.rebuilds,
        "瘫痪不能移除或重建船体碰撞形状")
    assert(#data.disabledGeometry.rectangles == 1 and data.disabledGeometry.rectangles[1].blocksPassage,
        "瘫痪引擎应仍占据原阻挡形状")
    assert(#data.invalidAccepted == 4, "无效伤害边界样本缺失")
    for _, accepted in ipairs(data.invalidAccepted) do assert(not accepted, "无效伤害应被正式入口拒绝") end
    assert(not data.armorAccepted, "伤害等于防御应不扣血")
    near(data.armor.health, 100, "抵消的伤害不能扣墙体血量")
    near(data.wallEffective, 10, "舱壁防御 10 应将伤害 20 抵消到 10")
    assert(data.destroyAccepted and not data.repeatDestroyAccepted and data.destroyCallbacks == 1,
        "真正摧毁必须只通知一次，重复命中死亡部件应拒绝")
    assert(data.destroyed.destroyed and not data.destroyed.alive, "结构血量耗尽后没有提交摧毁状态")
    near(data.destroyed.health, 0, "结构血量不能变成负数")
    assert(#data.destroyedGeometry.rectangles == 1 and not data.destroyedGeometry.rectangles[1].blocksPassage,
        "摧毁引擎后碰撞形状应恢复为下面的地板")
    assert(data.thresholdAccepted and data.threshold.alive and not data.threshold.operational,
        "瘫痪耐久恰好到零时必须停工并保留结构")
    near(data.threshold.disableHealth, 0, "零耐久边界错误")
    return "覆盖引擎弱点、两条耐久、瘫痪留形、结构摧毁、防御抵消、零耐久和无效伤害拒绝。"
end, "部位防御、结构血量与设备瘫痪")

test.system("space", "ship_hit_targeting", {}, function(data)
    scope(data, "ship_hit_targeting")
    assert(data.firstTarget == "target-engine" and data.firstAccepted,
        "同格设备必须先于地板受击，即使地板的 Layer 数值更高")
    assert(data.engineAfterFirst.destroyed, "第一发没有摧毁上层设备")
    near(data.floorAfterFirst.health, 100, "摧毁设备的同一发不能继续向地板溢出伤害")
    assert(data.nextTarget == "target-floor" and data.nextAccepted, "下一发才应命中暴露的地板")
    near(data.floorAfterNext.health, 81, "下一发地板伤害需要计算自己的防御")
    assert(data.emptyTarget == "" and data.invalidTarget == "", "空格或无效坐标不能命中部件")
    assert(data.rotatedInside == "rotated-engine" and data.rotatedOutside == "",
        "命中定位没有使用负坐标和旋转后的实际占地")
    assert(#data.overlays == 2, "电线/光纤上层遮挡样本不完整")
    for _, overlay in ipairs(data.overlays) do
        assert(overlay.firstTarget == "overlay-engine" and overlay.accepted and overlay.engineDestroyed,
            "线路的较大放置 Layer 不能盖住真实上层引擎：" .. overlay.kind)
        assert(overlay.nextTarget == "overlay-line", "引擎被毁后下一发才应命中下面线路：" .. overlay.kind)
        near(overlay.floorHealth, 100, "同一发不能穿透到地板")
        near(overlay.lineHealth, 100, "同一发不能穿透到线路")
    end
    assert(data.rightEdgeTarget == "edge-engine" and data.leftEdgeTarget == "edge-wall",
        "物理接触落在公共边线时必须按实际命中占地内缩，不能选中邻格较高 Layer 的部件")
    assert(data.invalidContact == "", "无效物理接触坐标必须拒绝")
    return "覆盖设备遮挡、设备优先于高 Layer 线路、一次只打一层、下一发地板、空格拒绝和旋转负坐标占地。"
end, "碰撞命中点定位与下一发地板")

test.system("space", "ship_damage_lines", {}, function(data)
    scope(data, "ship_damage_lines")
    assert(#data.selected == 10, "损伤线应该只选择十个实际活部件")
    local seen = {}
    for _, id in ipairs(data.selected) do
        assert(not seen[id] and id ~= "line-outside" and id ~= "line-dead", "损伤线重复或选中了线外/死亡部件")
        seen[id] = true
    end
    near(data.loss, 63, "100 点均分十份再扣防御，应只造成 63 点结构伤害")
    for x = 0, 9 do
        local part = by_id(data.parts, "line-" .. x)
        near(part.health, x >= 4 and x < 7 and 100 or 91, "损伤预算未先均分再抵消部位防御")
        if x >= 7 then near(part.disableHealth, 11, "引擎应同时损失 9 点瘫痪耐久") end
    end
    near(by_id(data.parts, "line-outside").health, 100, "线外部件不应分担或受到损伤")
    near(data.zeroLoss, 0, "零冲击预算不能扣血")
    near(data.filteredLoss, 76, "80 点仅分给四块地板再各扣 1 点防御，应损失 76")
    for x = 0, 9 do
        local part = by_id(data.filtered, "line-" .. x)
        near(part.health, x < 4 and 72 or x < 7 and 100 or 91, "筛选条件应在均分预算前应用")
    end
    near(data.fractionalLoss, 11, "13 点分给两块后扣防御不能舍入丢失小数")
    for _, part in ipairs(data.fractional) do near(part.health, 94.5, "小数损伤分担错误") end
    near(data.changingLoss, 51, "薄弱部件被毁后不能把过量伤害重新分给其他部位")
    near(data.changingSurvivor.health, 50, "摧毁回调移除部件不能放大后续部位的原始份额")
    assert(data.changingCallbacks == 1 and data.changingPieces == 1, "正式摧毁回调没有真正移除沿线部件")
    return "覆盖总伤害均分、防御分别抵消、线外和死亡排除、筛选先于均分、零/小数预算与摧毁回调份额冻结。"
end, "损伤线伤害预算与各部位防御")

test.system("space", "ship_damage_fragmentation", {}, function(data)
    scope(data, "ship_damage_fragmentation")
    assert(data.wallTarget == "long-waist-wall" and data.wallHit, "腰部攻击首先应命中舱壁")
    assert(#data.afterWall == 1 and not data.afterWall[1].wreck, "仅摧毁墙壁不能截断仍连着的地板")
    near(data.floorAfterWall.health, 40, "腰墙被毁的同一发不能伤害下面的地板")
    assert(data.floorTarget == "long-floor-3" and data.floorHit and #data.firstSplit == 2,
        "下一发摧毁腰部地板后必须截成两段")
    local head, tail
    for _, fragment in ipairs(data.firstSplit) do
        if fragment.wreck then tail = fragment else head = fragment end
    end
    assert(head and tail and head.id == data.originalId and head.console and head.engine,
        "保留可用控制台和引擎的船首必须保留身份并能继续作为飞船")
    assert(not tail.console and not tail.engine, "无控制台和动力的船尾应成为残骸")
    assert(#head.pieces == 5 and #tail.pieces == 6, "第一次截断后部件归属不守恒")
    near(by_id(head.pieces, "long-floor-2").health, 83, "船首分裂时不能重新分配或恢复血量")
    near(by_id(tail.pieces, "long-floor-5").health, 67, "船尾分裂时必须保留局部损伤")
    assert(by_id(tail.pieces, "long-cargo").snapshot == data.cargoSnapshot, "船尾物品快照在分裂时丢失")
    assert(data.tailTarget == "long-floor-6" and data.tailHit and #data.tailSplit == 2,
        "残骸继续挨打应能再次拆成两块")
    for _, fragments in ipairs({data.tailSplit, data.restoredTailSplit}) do
        assert(#fragments == 2, "残骸保存往返改变了碎块数量")
        local ids, count, cargo_count, wounded_count = {}, 0, 0, 0
        for _, fragment in ipairs(fragments) do
            assert(fragment.wreck and not ids[fragment.id], "残骸再次分裂不得复活为飞船或复制身份")
            ids[fragment.id] = true
            for _, part in ipairs(fragment.pieces) do
                count = count + 1
                assert(part.id ~= "long-floor-6", "已摧毁的船尾地板不能进入后续残骸")
                if part.id == "long-floor-5" then near(part.health, 67, "再次拆分必须保留原局部耐久"); wounded_count = wounded_count + 1 end
                if part.id == "long-cargo" then
                    near(part.health, 74, "货物承载部件的结构血量不能被重置")
                    assert(part.snapshot == data.cargoSnapshot, "再次拆分或保存往返丢失库存快照")
                    cargo_count = cargo_count + 1
                end
            end
        end
        assert(count == 5 and cargo_count == 1 and wounded_count == 1, "再次拆分后部件、库存或伤者归属不守恒")
    end
    assert(#data.diagonalSplit == 2, "只有对角接触的地板不能当作同一个结构")
    return "覆盖长船腰部墙/地板两次命中、船首可用、船尾残骸、递归分裂、部位耐久/库存快照保存和四邻边界。"
end, "长船腰部截断与残骸继续破碎")
