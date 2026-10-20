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

for _, amount in ipairs({1, 2}) do
    local expected = amount - 1
    test.runtime("building", "host_placement_receipt", {amount = amount}, function(data)
        assert(data.installed and data.afterServer == expected and data.remainingAmount == expected,
            "服务端建造没有实际落地或没有准确扣除一份材料")
        assert(data.afterReceipt == expected and data.receiptWrites == 0 and not data.pending,
            "Host 成功或重复回执再次写入共享库存，或没有结束等待状态")
        assert(#data.errors == 0, "Host 回执仍报告槽位解析或其他错误")
        assert(data.restored, "Host 回执采样没有撤除自有建筑并恢复地块")
    end, "Host 建造回执：材料初始数量 " .. amount)
end
