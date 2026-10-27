test.system("player", "wheelbarrow_push_rules", {}, function(data)
    assert(data.rear and data.rearTurn, "车尾推动或从后侧转向被拒绝")
    assert(not data.front and not data.side and not data.away and not data.idle and not data.invalid,
        "正面、侧面、反向、无输入或非法输入仍能推动")
    assert(math.abs(data.fullSpeed - 6.5) < .0001 and math.abs(data.partialSpeed - 3.25) < .0001,
        "推动没有保持玩家速度的 65%")
    assert(math.abs(data.partialX - 1.95) < .0001 and math.abs(data.partialY - 2.6) < .0001,
        "推动速度改变了输入方向")
end, "独轮车后侧准入、转向与推动速度")

test.system("player", "wheelbarrow_content", {}, function(data)
    assert(data.packageEnabled and #data.warnings == 0, "独轮车配方包未启用或解析有警告")
    assert(#data.pairs == 2, "缺少独轮车放置体或召唤器")
    for index, item in ipairs(data.pairs) do
        assert(item.id == (index == 1 and "Wheelbarrow" or "Wheelbarrow_Building"), "独轮车身份错误")
        assert(item.role == index - 1 and item.body == "Wheelbarrow_Building" and item.summoner == "Wheelbarrow",
            "独轮车放置配对错误")
        assert(#item.shared == 1 and item.shared[1] == "Mod_WheelbarrowInventory", "拆装没有声明共享货仓")
        assert(item.slots == 12 and item.weight == 120 and math.abs(item.volume - .3) < .0001,
            "独轮车货仓应为 12 格、120 公斤和 300 升")
        assert(item.sprite == "Assets/6_Art/Generated/Transport/Wheelbarrow/Wheelbarrow.png", "独轮车贴图地址错误")
        assert(item.driverCount == index - 1 and item.carrierCount == index - 1, "推车运动模块必须只存在于放置体")
    end
    assert(data.station == "workbench" and #data.inputs == 3 and #data.outputs == 1,
        "独轮车应在工作台用三种原始材料制作")
    local ingredients = {}
    for _, value in ipairs(data.inputs) do
        local key = value.id and value.id ~= "" and value.id or value.tag
        ingredients[key] = value.amount
    end
    assert(ingredients.Plank == 6 and ingredients.Stick_Wood == 4 and ingredients.Rope == 2,
        "木板、木棒或绳索配方数量不正确")
    assert(data.outputs[1].id == "Wheelbarrow" and data.outputs[1].amount == 1, "独轮车配方产物不正确")
    assert(data.normal.matched and data.normal.prepared and data.normal.committed, "材料齐全仍无法正式制作")
    for _, sample in ipairs(data.missing) do
        assert(sample.removedOne and not sample.matched and not sample.committed, "缺料时仍完成制作")
        for i, before in ipairs(sample.inputBefore) do
            assert(before.id == sample.inputAfter[i].id and before.amount == sample.inputAfter[i].amount,
                "缺料拒绝仍消耗输入")
        end
    end
    assert(data.full.matched and not data.full.prepared and not data.full.committed, "输出槽满时没有拒绝制作")
    for i, before in ipairs(data.full.inputBefore) do
        assert(before.id == data.full.inputAfter[i].id and before.amount == data.full.inputAfter[i].amount,
            "输出槽满时仍消耗输入")
    end
end, "独轮车配置、工作台配方、缺料及满仓回滚")
