-- 检查真实会话时钟和正式快照入口，不手动推进当前宇宙。
test.space("session", function(data)
    assert(data.scope == "live-session", "没有采样真实太空会话")
    assert(data.before.version == 2 and data.after.version == 2, "会话版本不正确")
    assert(data.after.seconds > data.before.seconds, "真实固定步没有推进宇宙时钟")
    assert(data.after.playerProfile == data.before.playerProfile, "观察期间测试玩家改变")
    assert(data.savedVersion == data.after.version, "正式捕获的快照版本不一致")
    assert(data.savedTime >= data.before.seconds and data.savedTime <= data.after.seconds, "快照时钟不属于本次采样")
    assert(data.savedSectorCount == data.after.sectorCount and data.savedSectorCount > 0, "快照遗漏星区")
    return "真实固定步、会话身份和正式捕获快照已检查"
end, "真实宇宙会话与固定步时钟")
