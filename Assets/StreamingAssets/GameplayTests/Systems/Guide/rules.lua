-- 所有阶段用独立玩家进度往返检查，里程碑仍来自生产进度存储。
local stages = { "OpenInventory", "GatherSurvivalMaterials", "CraftSparkMaker", "PlaceSparkMaker", "CreateFireSeed", "CraftBonfire", "PlaceBonfire", "IgniteBonfire", "Completed" }
for index, stage in ipairs(stages) do
    local count = index - 1
    test.system("guide", "milestones", { count = count, eligible = true }, function(data)
        assert(data.accepted == count and data.allFirstAccepted and data.milestones == count, "合法里程碑推进或恢复丢失")
        assert(not data.duplicateAccepted and not data.unknownAccepted, "重复或未知里程碑被接受")
        assert(data.stage == stage and data.restoredStage == stage, "教程当前阶段或内存恢复错误")
        assert(data.completed == (count == 8) and data.eligible, "完成或本地资格状态错误")
        assert(data.other.keep == 42, "教程进度覆盖其他系统数据")
    end, "新手引导里程碑阶段：" .. stage)
end
test.system("guide", "milestones", { count = 0, eligible = false }, function(data)
    assert(not data.eligible and data.stage == "OpenInventory", "未建立资格的角色自动获得教程资格")
end, "新手引导资格必须显式建立")

test.system("guide", "milestones", { count = 0, extra = { "bonfire-ignited", "bonfire-ignited", "probe-unknown" } }, function(data)
    assert(data.extraAccepted[1] and not data.extraAccepted[2] and not data.extraAccepted[3], "额外里程碑不幂等或接受未知 ID")
    assert(data.milestones == 1 and data.stage == "OpenInventory" and not data.completed, "后续里程碑错误跳过前面的任务")
end, "乱序里程碑不得跳过尚未完成阶段")
