for _, actor in ipairs({"Human", "Wolf"}) do
    test.runtime("ai", "actor_control", {actor = actor}, function(data)
        assert(data.genuineAI and data.idleRemainsManual and data.resumed, "空闲/输入锁恢复AI，或释放后没有恢复自主决策")
        assert(data.sameBodyAndContext and data.sameModules and data.profileRetained, "接管重建身体、能力或丢失档案")
        assert(data.excludedFromPopulation, "档案角色仍占生态种群预算，或重新注册回生态")
        assert(data.localModulesLoaded > 0 and data.localModulesAfterRelease == 0, "本地镜头/HUD未按租约收尾")
        assert(data.bodyHudsLoaded and data.bodyHudsReleased and data.bodyHudsRecreated, "身体HUD未随接管、释放和重新接管切换")
        if actor == "Wolf" then
            assert(data.nativeAttack and data.coolingDown, "原生攻击没有使用同一冷却")
            assert(not data.hasBag and not data.hasRoll, "接管动物补装了背包或翻滚")
        else
            assert(data.hasBag and data.hasRoll, "默认人形丢失已有能力")
        end
    end, actor .. "原身体接管、空闲和释放")
end
