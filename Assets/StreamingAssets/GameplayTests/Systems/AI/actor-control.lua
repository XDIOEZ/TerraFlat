test.system("ai", "actor_control_lease", {}, function(data)
    assert(data.acquired and data.exclusive and data.staleLeaseIgnored, "独占租约或过期租约释放错误")
    assert(data.released and data.profileRetained and data.sameData, "释放控制丢失档案或重建身体")
end, "独占接管租约与档案身份分离")

test.system("save", "legacy_actor_inventory", {}, function(data)
    assert(data.modulePriority and data.compatibilityKey and data.keyMatched and data.legacyRemoved,
        "旧库存未按键迁移、覆盖模块内容或缺失兼容内容")
    local ids = {}
    for _, id in ipairs(data.remainingIds) do
        assert(not ids[id], "迁移重复保留同一物品实例")
        ids[id] = true
    end
    assert(#data.remainingIds == 3 and ids[701] and ids[702] and ids[704] and not ids[703],
        "快捷栏实例重复进入背包，或剩余旧内容丢失")
end, "旧库存模块优先、快捷栏去重与完整保留")
