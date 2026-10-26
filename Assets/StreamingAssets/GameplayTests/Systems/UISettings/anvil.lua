test.system("ui_settings", "anvil_layout", {}, function(data)
    local counts = {UI_HandCraftTable = {4, 0, 2}, UI_MakerTable = {6, 0, 2}, UI_Anvil = {1, 1, 1}}
    for _, panel in ipairs(data.panels) do
        assert(panel.inside, panel.id .. "所有槽位位于缩放内容内")
        local expected = counts[panel.id]
        assert(expected[1] == panel.inputs, panel.id .. "材料槽数")
        assert(expected[2] == panel.tools, panel.id .. "工具槽数")
        assert(expected[3] == panel.outputs, panel.id .. "产物槽数")
        if panel.id == "UI_Anvil" then
            assert(panel.dismantleBound, "砧拆除按钮和标题绑定完整")
            assert(panel.buttonGap >= 16, "拆除按钮在开始制作左侧留出间距")
            assert(panel.hintGap >= 8, "耐久提示不会与拆除按钮重叠")
        end
    end
end, "砧和工作台正式Prefab布局")

test.system("crafting", "manual_tool_configuration", {}, function(data)
    assert(#data.items == 4, "铜铁砧与载体均声明工具规则")
    for _, item in ipairs(data.items) do
        assert(item.parameters.RequiredToolKind == 4, item.id .. "要求锤类工具")
        assert(item.parameters.RequiredToolTag == "Metal", item.id .. "要求金属材质")
        assert(item.parameters.ToolDurabilityPerClick == 1, item.id .. "每击消耗一点工具耐久")
    end
end, "正式砧目录的金属锤配置")

for _, row in ipairs({
    {durability = 3, clicks = 2, required = 12, strokes = 2, progress = 2, remaining = 1, completions = 0},
    {durability = 1, clicks = 2, required = 1, strokes = 1, progress = 0, remaining = 0, completions = 1},
    {durability = 0, clicks = 1, required = 1, strokes = 0, progress = 0, remaining = 0, completions = 0},
    {durability = 3, clicks = 2, required = 1, blocked = true, strokes = 0, progress = 0, remaining = 3, completions = 0}
}) do
    test.system("crafting", "manual_tool_work", row, function(data)
        assert(row.strokes == data.strokes, "只对有效敲击扣锤子耐久")
        assert(row.progress == data.progress, "未完成批次也保留敲击进度")
        assert(row.remaining == data.durability, "锤子当前耐久")
        assert(row.completions == data.completions, "最后一点耐久仍完成当前击")
        assert(data.metalHammer, "金属锤符合工具种类和材质")
        assert(not data.woodHammer and not data.metalAxe, "木锤和金属斧均拒绝")
        assert(data.separateMaterialInventory, "工具不能被配方当作材料扣除")
        assert(1 == data.restoredToolCount, "工具独立库存随加工状态保存")
        assert(data.durability == data.restoredDurability, "锤耐久在状态封包往返后保留")
        assert(math.abs(data.coldDurabilityFraction - row.remaining / 100) < .000001,
            "冷快照应保存当前耐久相对定义上限的比例")
        assert(data.progress == data.restoredProgress, "加工进度在状态封包往返后保留")
    end, "砧工具资格、逐击磨损和状态保存")
end
