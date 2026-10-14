-- 真实格子后端与独立材料槽执行放置、失败退料和冲突拒绝，采样返回前拆除自有墙。
test.check("player_ready", "建造现场测试玩家就绪")
test.runtime("building", "placement", {}, function(data)
    assert(data.scope == "live-isolated-world", "建造测试没有进入隔离世界")
    assert(not data.rejectedAccepted and data.writesAfterRejection == 1 and data.amountAtRejectedPublish == 1
        and data.expectedTile > 0 and data.tileAtRejectedPublish == data.expectedTile,
        "回滚测试没有实际进入世界写入和扣料后的发布阶段")
    assert(data.amountAfterRejection == 2 and data.terrainAfterRejection, "发布失败没有恢复真实材料和地形")
    assert(data.placed and data.installedTile == data.expectedTile and data.placedId == data.expectedBuildingId
        and data.amountAfterPlacement == 1 and data.remainingAmount == 1, "正式建筑放置和单份扣料不一致")
    assert(not data.overlapAccepted and data.amountAfterOverlap == 1 and data.placementWrites == 2,
        "重叠放置未拒绝或额外扣料/写入地形")
    assert(data.restored, "建造现场没有拆除自有墙并恢复地块")
end, "真实格子建筑放置、扣料、失败回滚与占地冲突")
