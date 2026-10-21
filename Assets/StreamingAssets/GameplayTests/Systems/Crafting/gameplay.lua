-- 使用当前隔离玩家的实际手工制作库存、正式配方目录和面板按钮。
test.check("world_ready", "手工制作使用正式隔离玩家")
test.runtime("crafting", "handcraft_clicks", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.materialInserted and data.panelOpen and
        data.buttonInteractable and data.recipeId == "core:藤蔓=绳子", "正式手工面板、材料或生产配方未就绪")
    assert(data.requiredClicks >= 1 and data.inputBefore == 3 and data.outputBefore == 0,
        "自有制作前置材料错误")
    assert(data.viewBound and data.recipeButtonBound and data.selectedRecipeId == data.recipeId,
        "制作只读视图没有绑定当前库存、业务选择或正式候选按钮")
    assert(data.previewSucceeded and data.previewUnchanged and data.observedWork == data.requiredClicks - 1
        and data.observedRequiredWork == data.requiredClicks, "制作预检改变库存或只读进度与实际点击不同步")
    assert(data.inputBeforeFinalClick == 3 and data.outputBeforeFinalClick == 0,
        "最后一次制作点击前提前扣料或生成产物")
    assert(data.recipeInputAmount > 0 and data.recipeInputAmount <= data.inputBefore
        and data.inputAfterCraft == data.inputBefore - data.recipeInputAmount
        and data.outputAfterCraft == data.expectedOutput,
        "正式合成按钮没有按当前生产配方原子扣除藤蔓并产生绳子")
    assert(data.secondMaterialsInserted and data.fullOutputSlots > 0 and data.blockerBefore == data.fullOutputSlots,
        "没有真实填满全部输出槽")
    assert(data.inputAfterFull == data.inputBeforeFull and data.inputAfterFull == 3 and
        data.blockerAfter == data.blockerBefore and data.outputAfterFull == 0 and not data.fullButtonInteractable,
        "满输出槽位仍扣除材料、覆盖原物品或产生部分产物")

    -- 合成材料与实际产物始终属于随身物品，配方预览和面板开关不改变携带量。
    local baseline = data.carryBefore
    assert(baseline.available and baseline.inputWeight == 0 and baseline.outputWeight == 0
        and baseline.inputVolume == 0 and baseline.outputVolume == 0, "手工负重基线没有使用空制作库存")
    local stages = {data.carryWithInput, data.carryWhileClosed, data.carryBeforeFinalClick,
        data.carryAfterCraft, data.carryWithFullOutput}
    for _, carry in ipairs(stages) do
        assert(carry.available
            and math.abs(carry.weight - baseline.weight - carry.inputWeight - carry.outputWeight) < .001
            and math.abs(carry.volume - baseline.volume - carry.inputVolume - carry.outputVolume) < .001,
            "手工输入输出的质量或体积漏算、重复计算，或配方预览被算成实物")
    end
    assert(data.carryWithInput.inputWeight > 0 and data.carryWithInput.inputVolume > 0
        and data.carryAfterCraft.outputWeight > 0 and data.carryAfterCraft.outputVolume > 0,
        "没有实际覆盖手工材料及产物的质量和体积")
    assert(data.closedForCarrySample
        and math.abs(data.carryWhileClosed.weight - data.carryWithInput.weight) < .001
        and math.abs(data.carryWhileClosed.volume - data.carryWithInput.volume) < .001,
        "关闭手工面板后材料不再计入随身携带量")
    assert(data.cleanupRegistered, "制作自有材料没有登记失败时的收尾")
end, "真实手工按钮点击、产出扣料及满输出拒绝")
test.runtime("crafting", "cleanup", {}, function(data)
    assert(data.hadFixture and data.released and data.inputEmpty and data.outputEmpty,
        "自有制作材料或输出物未清理")
    assert(data.carryAfterCleanup.available
        and math.abs(data.carryAfterCleanup.weight - data.carryBefore.weight) < .001
        and math.abs(data.carryAfterCleanup.volume - data.carryBefore.volume) < .001,
        "手工栏位清空后随身质量或体积没有恢复")
end, "清理手工制作自有库存并恢复面板状态")
