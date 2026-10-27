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

test.system("world_streaming", "runtime_scene_detach", {}, function(data)
    assert(data.committed and data.competingOwnerRejected and data.attachedReleaseRejected,
        "已附着世界允许第二个管理器接管，或允许会话提前销毁数据")
    assert(data.oldSchedulerDetached and data.sameData and data.epochPreserved and data.newOwnerIntact,
        "场景解绑后世界数据或新管理器归属丢失")
    assert(data.explicitlyReleased and data.disposed and data.staleReleaseRejected and
        data.replacementIntact and data.replacementReleased,
        "会话释放失败，或旧世界误删了替换后的模型")
end, "场景管理器解绑后会话世界接管、隔离和显式释放")
