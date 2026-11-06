for _, actor in ipairs({"Human", "Wolf"}) do
    test.runtime("ai", "actor_control", {actor = actor}, function(data)
        assert(data.rawShellAssembled and data.genuineAI and data.idleRemainsManual and data.resumed, "原始外壳装配失败、空闲/输入锁恢复AI，或释放后没有恢复自主决策")
        assert(data.sameBodyAndContext and data.sameModules and data.profileRetained, "接管重建身体、能力或丢失档案")
        assert(data.excludedFromPopulation, "档案角色仍占生态种群预算，或重新注册回生态")
        assert(data.localModulesLoaded > 0 and data.localModulesAfterRelease == 0, "本地镜头/HUD未按租约收尾")
        assert(data.bodyHudsLoaded and data.bodyHudsReleased and data.bodyHudsRecreated, "身体HUD未随接管、释放和重新接管切换")
        assert(data.singleSpriteMode and data.hasDefinitionSprite and data.hasBodySprite, "角色单图首帧或身体图片缺失")
        if actor == "Wolf" then
            assert(data.bodyUsesDefinitionSprite, "动物身体未使用正式定义的首帧")
            assert(data.nativeAttack and data.coolingDown, "原生攻击没有使用同一冷却")
            assert(not data.hasBag and not data.hasRoll, "接管动物补装了背包或翻滚")
        else
            assert(data.appearanceLoaded and not data.bodyUsesDefinitionSprite, "人形身体外观未完成合成")
            assert(data.bodyAbilitiesLoaded and data.bodyAbilitiesAfterRelease, "丢弃、装备或手工能力未随身体保留")
            assert(data.hasBag and data.hasRoll and data.hasEquip, "能力缺失")
        end
    end, actor .. "原身体接管、空闲和释放")
end

test.runtime("ai", "control_bound_inventory_input", {}, function(data)
    assert(data.equipmentBefore and data.equipmentAfter and data.craftingBefore and data.craftingAfter,
        "装备或手工面板快捷键未随控制权恢复")
    assert(data.afterDrop == data.start - 1 and data.afterControlLoss == data.afterDrop
        and data.afterReacquire == data.afterDrop - 1 and data.spawnCalls == 2,
        "丢弃快捷键在控制切换时误丢、失效或重复触发")
end, "真实键盘输入随控制权释放和恢复")
