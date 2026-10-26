-- 电子材料目录和三条指定配方独立回归，避免被无关旧断言中断。
test.system("crafting", "electronic_material_catalog", {ids = {
    "CarbonFiber", "CarbonFiberPlate", "Ingot_Titanium", "Ingot_Aluminum", "Ingot_Molybdenum", "Ingot_Nickel",
    "Ore_Mica", "MicaSheet", "Phosphorus", "Fertilizer_Phosphate", "Boron", "Quartz", "Lithium", "Ingot_WroughtIron"
}}, function(data)
    assert(#data.items == 14, "电子材料或质量比较基准缺失")
    local items = {}
    for _, item in ipairs(data.items) do
        assert(not items[item.id], "电子材料身份重复")
        items[item.id] = item
        assert(item.stackable and item.canBePickedUp and item.weight > 0 and item.volume > 0
            and #item.gameName > 0 and #item.description > 0 and item.spriteExists,
            "材料缺少名称、实物贴图、质量体积或库存资格：" .. item.id)
        local state = item.state
        assert(state.added and state.addedAmount == 3 and state.amount == 5
            and state.savedId == item.id and state.savedAmount == 5 and state.savedTemperature == 1800,
            "材料堆叠或实例快照丢失数量、稳定身份和温度：" .. item.id)
        assert(state.serialized and state.identified and state.networkId == item.id and state.networkGuid == 9681,
            "电子材料没有复用正式网络物品身份：" .. item.id)
    end
    assert(items.Ingot_Titanium.weight / items.Ingot_Titanium.volume
        < items.Ingot_WroughtIron.weight / items.Ingot_WroughtIron.volume, "钛没有体现相同体积比铁轻")
    local mica = items.Ore_Mica.processing.cut
    assert(mica and mica.inputAmount == 1 and mica.work == 4 and #mica.outputs == 1
        and mica.outputs[1].itemId == "MicaSheet" and mica.outputs[1].amount == 2,
        "云母矿石未声明正式切割加工，或一矿两片的产出错误")
end, "电子材料：正式目录、独立贴图、轻质钛、堆叠、实例快照和网络身份")

test.system("crafting", "handmade_catalog", {package = "crafting/electronic_materials.json"}, function(data)
    local expected = {
        ["core:electronic_carbon_fiber_plate"] = {output = "CarbonFiberPlate", amount = 1,
            materials = {CarbonFiber = 1, Resin = 1}},
        ["core:electronic_phosphate_fertilizer"] = {output = "Fertilizer_Phosphate", amount = 2,
            materials = {Phosphorus = 1, Humus = 1}},
        ["core:electronic_lithium_iron_phosphate_battery"] = {output = "Battery_LithiumIronPhosphate_Summoner", amount = 1,
            materials = {Phosphorus = 1, Lithium = 1, Ingot_WroughtIron = 1, CopperPlate = 1, Rubber = 1}}
    }
    local function same(before, after)
        assert(#before == #after, "电子材料制作拒绝改变了槽数")
        for i, slot in ipairs(before) do
            local other = after[i]
            assert(slot.id == other.id and slot.guid == other.guid and slot.amount == other.amount
                and slot.durability == other.durability and slot.maxDurability == other.maxDurability,
                "电子材料制作拒绝改变了原库存")
        end
    end
    for _, recipe in ipairs(data.recipes) do
        local wanted = expected[recipe.id]
        assert(wanted and #recipe.warnings == 0, "电子材料配方缺失、引用无效或自主扩展了用途")
        assert(#recipe.expectedOutputs == 1 and recipe.expectedOutputs[1].id == wanted.output
            and recipe.expectedOutputs[1].amount == wanted.amount, "电子材料配方产物错误")
        local kinds, expectedKinds = 0, 0
        for _, _ in pairs(wanted.materials) do expectedKinds = expectedKinds + 1 end
        for _, ingredient in ipairs(recipe.ingredients) do
            assert(wanted.materials[ingredient.id] == ingredient.amount, "电子材料配方用料或数量错误")
            kinds = kinds + 1
        end
        assert(kinds == expectedKinds and #recipe.missingMaterials == expectedKinds, "未逐种覆盖电子材料缺料")
        assert(recipe.normal.matched and recipe.normal.prepared and recipe.normal.committed,
            "电子材料正式配方无法完成原子制作")
        for _, slot in ipairs(recipe.normal.inputAfter) do assert(slot.amount == 0, "电子材料制作没有精确扣料") end
        assert(recipe.normal.outputAfter[1].id == wanted.output
            and recipe.normal.outputAfter[1].amount == wanted.amount, "电子材料制作产物或数量错误")
        for _, attempt in ipairs(recipe.missingMaterials) do
            assert(attempt.removedOne and not attempt.matched and not attempt.prepared and not attempt.committed,
                "电子材料缺少任一种耗材仍能制作")
            same(attempt.inputBefore, attempt.inputAfter)
            same(attempt.outputBefore, attempt.outputAfter)
        end
        assert(recipe.full.matched and not recipe.full.prepared and not recipe.full.committed,
            "电子材料满输出仍提交制作")
        same(recipe.full.inputBefore, recipe.full.inputAfter)
        same(recipe.full.outputBefore, recipe.full.outputAfter)
        expected[recipe.id] = nil
    end
    assert(next(expected) == nil, "电子材料指定配方未全部登记")
end, "碳纤维树脂制板、磷肥和锂电池：正式配方、精确扣料、逐种缺料和满输出")
