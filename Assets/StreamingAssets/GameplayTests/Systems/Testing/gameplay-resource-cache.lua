-- 读取本轮正式 GameRes 加载诊断；完整原始字段由 runtime_sample 自动写入报告，不以耗时推断缓存命中。
test.check("world_ready", "资源缓存测试的真实世界就绪")
test.check("player_ready", "资源缓存测试的真实玩家就绪")

local function assert_nonnegative(value, name, integer)
    assert(type(value) == "number" and value == value and value >= 0 and value < math.huge,
        name .. "必须是非负有限数")
    if integer then assert(value == math.floor(value), name .. "必须是实际整数计数") end
end

test.runtime("testing", "resource_cache", {}, function(data)
    assert(data.scope == "live-isolated-world", "资源缓存采样没有进入本轮真实隔离世界")
    assert(data.status == "cold" or data.status == "hit",
        "资源缓存没有完成可用的冷加载或热命中：" .. tostring(data.status))
    assert(data.ready == true, "缓存状态已完成，但正式 GameRes 尚未就绪")
    assert_nonnegative(data.items, "实际物品目录数量", true)
    assert(data.items > 0, "缓存加载没有重建本轮正式物品目录")
    assert_nonnegative(data.assetHits, "源资产命中数量", true)
    assert_nonnegative(data.assetMisses, "源资产新请求数量", true)
    assert_nonnegative(data.documentHits, "解析文档命中数量", true)
    assert_nonnegative(data.resourceSeconds, "本轮正式资源加载耗时", false)
    assert_nonnegative(data.cacheReadSeconds, "缓存读取和指纹核对耗时", false)
    assert_nonnegative(data.cacheWriteSeconds, "缓存发布耗时", false)
    assert(data.resourceSeconds >= data.cacheReadSeconds + data.cacheWriteSeconds,
        "资源耗时遗漏缓存读取或发布成本")
    if data.status == "hit" then
        assert(data.assetHits > 0, "报告热命中，但没有实际复用源资产")
        assert(data.documentHits > 0, "报告热命中，但没有实际复用解析文档")
    end
end, "Lua专用 GameRes 缓存：实际目录、冷热状态、资产及文档命中诊断")
