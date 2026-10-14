-- 正式钓竿模块持久化两槽内容，经 FWI1 封包后重新加载，采样完成即回收自有物品。
test.check("player_ready", "钓具现场测试玩家就绪")
test.runtime("fishing", "rig_roundtrip", {}, function(data)
    assert(data.scope == "live-isolated-world" and data.saved and data.decoded and data.payloadBytes > 4,
        "正式钓具快照没有成功封包与恢复")
    assert(data.loaded and data.generationChanged, "恢复钓具未完成模块加载或复用了过期实例代际")
    assert(data.hookId == data.expectedHookId and data.baitId == data.expectedBaitId
        and data.hookAmount == 1 and data.baitAmount == 1 and data.hookGuidPreserved and data.baitGuidPreserved,
        "钓具恢复丢失鱼钩/鱼饵内容或实例身份")
    assert(data.hookCapacity == 1 and data.baitCapacity == 1, "恢复钓具丢失正式单份槽布局")
    assert(data.fixturesReleased, "自有钓竿实例没有完整卸载回收")
end, "真实钓具模块保存、网络快照和实例恢复")
