-- 逻辑世界身份由明确的管理器所有者决定，未绑定时沿用当前世界上下文。
test.system("world_streaming", "runtime_owner_identity", {}, function(data)
    assert(data.owner == "ship:probe" and data.worldId == "ship:probe",
        "区块世界模型没有保留已绑定的逻辑所有者身份")
    assert(data.context == "surface:probe" and data.fallback == "world",
        "未绑定所有者时没有沿用正式世界上下文或原有兜底身份")
end, "区块世界模型使用逻辑所有者身份")

test.system("world_streaming", "runtime_owner_handoff", {}, function(data)
    assert(data.committed and data.viewReleased and data.retentionCount == 1,
        "外部世界没有提交区块，或表现释放影响了数据保留租约")
    assert(data.sameWorld and data.sameChunk and data.terrainPreserved and data.newSchedulerTick,
        "旧调度器释放后，新调度器没有接管同一世界及其原始地形")
end, "外部世界模型在调度器和表现释放后保留")
