-- 使用当前隔离玩家的实际手工制作库存、正式配方目录和面板按钮。
test.check("world_ready", "手工制作使用正式隔离玩家")
test.runtime("crafting", "handcraft_clicks", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.materialInserted and data.panelOpen and
        data.buttonInteractable and data.recipeId == "core:藤蔓=绳子", "正式手工面板、材料或生产配方未就绪")
    assert(data.requiredClicks >= 1 and data.inputBefore == 3 and data.outputBefore == 0,
        "自有制作前置材料错误")
    assert(data.inputBeforeFinalClick == 3 and data.outputBeforeFinalClick == 0,
        "最后一次制作点击前提前扣料或生成产物")
    assert(data.inputAfterCraft == 0 and data.outputAfterCraft == data.expectedOutput,
        "正式合成按钮没有按生产配方原子扣除三份藤蔓并产生绳子")
    assert(data.secondMaterialsInserted and data.fullOutputSlots > 0 and data.blockerBefore == data.fullOutputSlots,
        "没有真实填满全部输出槽")
    assert(data.inputAfterFull == data.inputBeforeFull and data.inputAfterFull == 3 and
        data.blockerAfter == data.blockerBefore and data.outputAfterFull == 0 and not data.fullButtonInteractable,
        "满输出槽位仍扣除材料、覆盖原物品或产生部分产物")
    assert(data.cleanupRegistered, "制作自有材料没有登记失败时的收尾")
end, "真实手工按钮点击、产出扣料及满输出拒绝")
test.screenshot("手工制作真实面板在满输出槽时保留材料的现场")
test.runtime("crafting", "cleanup", {}, function(data)
    assert(data.hadFixture and data.released and data.inputEmpty and data.outputEmpty,
        "自有制作材料或输出物未清理")
end, "清理手工制作自有库存并恢复面板状态")
